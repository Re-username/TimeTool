#if WINDOWS
using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using WinRT.Interop;

namespace TimeTool.Platforms.Windows;

/// <summary>
/// Закрытие окна → в трей (не выход). Сворачивание кнопкой «—» — как обычно в панели задач.
/// Клик по значку — вернуть окно; правая кнопка — меню (Открыть / Выход).
/// Значок живёт на СВОЕМ потоке с собственным циклом сообщений: скрытие/состояние
/// UI-потока приложения его не убивает (иначе Explorer убирал значок через секунду).
/// </summary>
public static class TrayService
{
    private const int WM_CLOSE = 0x0010;
    private const int WM_QUIT = 0x0012;
    private const int WM_COMMAND = 0x0111;
    private const int WM_CONTEXTMENU = 0x007B;
    private const uint WM_TRAYCALLBACK = 0x8001;
    private const uint WM_LBUTTONUP = 0x0202;
    private const uint WM_LBUTTONDBLCLK = 0x0203;
    private const uint WM_RBUTTONUP = 0x0205;
    private const uint NIN_SELECT = 0x0400;
    private const uint NIN_KEYSELECT = 0x0401;
    private const uint NIF_MESSAGE = 0x01;
    private const uint NIF_ICON = 0x02;
    private const uint NIF_TIP = 0x04;
    private const uint NIF_INFO = 0x10;
    private const uint NIM_ADD = 0x00;
    private const uint NIM_MODIFY = 0x01;
    private const uint NIM_DELETE = 0x02;
    private const uint NIM_SETVERSION = 0x04;
    private const uint NOTIFYICON_VERSION_4 = 4;
    private const int ID_ICON = 5000;
    private const int ID_MENU_OPEN = 1001;
    private const int ID_MENU_EXIT = 1002;
    private const int GWL_WNDPROC = -4;
    private const int SW_HIDE = 0;
    private const int SW_SHOW = 5;

    private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    private static WndProcDelegate? _mainProc;
    private static WndProcDelegate? _trayProc; // держим ссылку, иначе GC собьёт WndProc
    private static IntPtr _mainHwnd;
    private static IntPtr _oldMainProc;
    private static volatile IntPtr _trayHwnd;
    private static Thread? _trayThread;
    private static readonly ManualResetEventSlim _trayReady = new(false);
    private static volatile bool _iconAdded;
    private static bool _exitRequested;
    private static bool _hintShown;
    private static IntPtr _hIcon;
    private static IntPtr _hModule;
    private static Microsoft.Maui.Controls.Window? _window;

    public static void Attach(Microsoft.Maui.Controls.Window window)
    {
        try
        {
            var native = window.Handler?.PlatformView as Microsoft.UI.Xaml.Window
                ?? throw new InvalidOperationException("Нативное окно недоступно");
            var hwnd = WindowNative.GetWindowHandle(native);
            _window = window;
            _mainHwnd = hwnd;
            _mainProc = MainWndProc;
            _oldMainProc = SetWindowLongPtr(hwnd, GWL_WNDPROC, Marshal.GetFunctionPointerForDelegate(_mainProc));
            StartTrayThread();
        }
        catch { /* трей — необязателен, не должно ронять приложение */ }
    }

    private static void StartTrayThread()
    {
        if (_trayThread is not null)
            return;
        _trayThread = new Thread(TrayThreadProc)
        {
            IsBackground = true,
            Name = "TimeToolTray",
        };
        _trayThread.SetApartmentState(ApartmentState.STA);
        _trayThread.Start();
        _trayReady.Wait(TimeSpan.FromSeconds(3));
    }

    private const string TrayClassName = "TimeToolTrayWnd";

    private static void TrayThreadProc()
    {
        try
        {
            _trayProc = TrayWndProc;
            // свой класс окна: системные классы (STATIC) на новом потоке недоступны —
            // CreateWindowExW падал с ERROR_CANNOT_FIND_WND_CLASS (1407).
            // ВАЖНО: класс регистрируется под hInstance модуля — CreateWindowExW должен
            // получить ТОТ ЖЕ hInstance, иначе класс «не находится».
            _hModule = GetModuleHandleW(IntPtr.Zero);
            var wc = new WNDCLASSW
            {
                lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_trayProc),
                hInstance = _hModule,
                lpszClassName = TrayClassName,
            };
            var atom = RegisterClassW(ref wc);
            if (atom == 0)
            {
                LogTray("RegisterClassW вернул 0, err=" + Marshal.GetLastWin32Error());
                return;
            }

            // цепочка fallback'ов: диагностируем, что именно «не находит» Win32
            // окно-трафарет — ОБЫЧНОЕ скрытое top-level (не message-only!): message-only окно
            // нельзя сделать foreground, и TrackPopupMenu молча не показывает меню (опыт filechat)
            const int WS_EX_TOOLWINDOW = 0x80;
            _trayHwnd = CreateWindowExW(WS_EX_TOOLWINDOW, TrayClassName, "TimeToolTray", 0, 0, 0, 0, 0,
                IntPtr.Zero, IntPtr.Zero, _hModule, IntPtr.Zero);
            var errName = Marshal.GetLastWin32Error();
            var errAtom = 0;
            var errStatic = 0;
            if (_trayHwnd == IntPtr.Zero && atom != 0)
            {
                // поиск по атому класса в обход таблицы имён (MAKEINTATOM)
                _trayHwnd = CreateWindowExWAtom(WS_EX_TOOLWINDOW, new IntPtr(atom), "TimeToolTray", 0, 0, 0, 0, 0,
                    IntPtr.Zero, IntPtr.Zero, _hModule, IntPtr.Zero);
                errAtom = Marshal.GetLastWin32Error();
            }
            if (_trayHwnd == IntPtr.Zero)
            {
                // глобальный системный класс user32 (CS_GLOBALCLASS) — доступен на любом потоке
                _trayHwnd = CreateWindowExW(WS_EX_TOOLWINDOW, "STATIC", "TimeToolTray", 0, 0, 0, 0, 0,
                    IntPtr.Zero, IntPtr.Zero, _hModule, IntPtr.Zero);
                errStatic = Marshal.GetLastWin32Error();
            }
            if (_trayHwnd == IntPtr.Zero)
            {
                LogTray($"CreateWindowExW: hModule={_hModule} atom={atom} errs name={errName} atom={errAtom} static={errStatic}");
                return;
            }
            if (errName != 0)
                LogTray($"CreateWindowExW: hwnd={_trayHwnd}, сработал fallback (errs name={errName} atom={errAtom} static={errStatic})");
            SetWindowLongPtr(_trayHwnd, GWL_WNDPROC, Marshal.GetFunctionPointerForDelegate(_trayProc));
            _trayReady.Set();

            while (GetMessageW(out var msg, IntPtr.Zero, 0, 0))
            {
                if (msg.message == WM_QUIT)
                    break;
                TranslateMessage(ref msg);
                DispatchMessageW(ref msg);
            }
        }
        catch (Exception ex)
        {
            LogTray("TrayThreadProc: " + ex);
        }
    }

    private static IntPtr MainWndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == WM_CLOSE && !_exitRequested)
        {
            // закрытие → трей, а не выход
            EnsureTrayIcon();
            ShowWindow(hWnd, SW_HIDE);
            if (!_hintShown)
            {
                _hintShown = true;
                ShowBalloon("TimeTool свёрнут в трей",
                    "Значок в скрытых значках (стрелка ^ рядом с треем). Перетащите его на панель, чтобы закрепить.");
            }
            return IntPtr.Zero;
        }
        return CallWindowProc(_oldMainProc, hWnd, msg, wParam, lParam);
    }

    private static void EnsureTrayIcon()
    {
        if (_iconAdded)
            return;
        StartTrayThread();
        if (_trayHwnd == IntPtr.Zero)
            return;

        // иконка — из exe приложения (а не системная стрелка): на ней приложение узнаваемо
        try
        {
            var exe = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(exe) &&
                ExtractIconExW(exe, 0, out _, out var small, 1) > 0)
                _hIcon = small;
        }
        catch { }

        var data = new NOTIFYICONDATAW
        {
            cbSize = Marshal.SizeOf<NOTIFYICONDATAW>(),
            hWnd = _trayHwnd,
            uID = ID_ICON,
            uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP,
            uCallbackMessage = WM_TRAYCALLBACK,
            hIcon = _hIcon != IntPtr.Zero ? _hIcon : LoadIcon(IntPtr.Zero, new IntPtr(0x7F00)),
            szTip = "TimeTool — таймер и расчёт времени",
        };
        if (Shell_NotifyIconW(NIM_ADD, ref data))
        {
            _iconAdded = true;
            // без SETVERSION оболочка трактует значок как «version 0» и NIF_INFO-баллуны
            // на Win10/11 молча теряются (это и была причина «уведомление не пришло»)
            var ver = new NOTIFYICONDATAW { cbSize = Marshal.SizeOf<NOTIFYICONDATAW>(), hWnd = _trayHwnd, uID = ID_ICON, uTimeoutOrVersion = NOTIFYICON_VERSION_4 };
            Shell_NotifyIconW(NIM_SETVERSION, ref ver);

            if (Environment.GetEnvironmentVariable("TIMETOOL_TEST_BALLOON") == "1")
                Task.Delay(2000).ContinueWith(_ =>
                {
                    ShowBalloon("Тест TimeTool", "Проверка уведомления из трея");
                    ToastNotifier.Show("TimeTool: тест", "Проверка toast-уведомления");
                });
        }
        else
            LogTray("Shell_NotifyIconW(NIM_ADD) вернул false, err=" + Marshal.GetLastWin32Error());
    }

    private static IntPtr TrayWndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == WM_TRAYCALLBACK)
        {
            // ВАЖНО: младшее слово lParam — это сообщение мыши (в старшем бите бывает мусор,
            // поэтому маскировать ОБЯЗАТЕЛЬНО — иначе клики не распознаются, см. filechat).
            var mouse = (uint)lParam.ToInt64() & 0xffff;
            if (mouse == WM_LBUTTONDBLCLK)
                RestoreWindow();
            else if (mouse == WM_RBUTTONUP || mouse == WM_CONTEXTMENU)
                ShowTrayMenu();
        }
        else if (msg == WM_COMMAND)
        {
            var id = (int)(wParam.ToInt64() & 0xFFFF);
            if (id == ID_MENU_OPEN)
                RestoreWindow();
            else if (id == ID_MENU_EXIT)
                ExitApp();
        }
        return DefWindowProc(hWnd, msg, wParam, lParam);
    }

    private static void ShowTrayMenu()
    {
        var menu = CreatePopupMenu();
        AppendMenuW(menu, 0, ID_MENU_OPEN, "Открыть");
        AppendMenuW(menu, 0, ID_MENU_EXIT, "Выход");
        GetCursorPos(out var pt);
        SetForegroundWindow(_trayHwnd);
        TrackPopupMenu(menu, 0, pt.X, pt.Y, 0, _trayHwnd, IntPtr.Zero);
        DestroyMenu(menu);
    }

    public static void RestoreWindow()
    {
        if (_mainHwnd == IntPtr.Zero)
            return;
        // манипуляции с окном — на UI-потоке (как в filechat: DispatcherQueue.TryEnqueue)
        var w = _window;
        if (w is not null)
        {
            w.Dispatcher.Dispatch(() =>
            {
                try
                {
                    ShowWindow(_mainHwnd, SW_SHOW);
                    SetForegroundWindow(_mainHwnd);
                }
                catch { }
            });
        }
        else
        {
            ShowWindow(_mainHwnd, SW_SHOW);
            SetForegroundWindow(_mainHwnd);
        }
    }

    /// <summary>Всплывающее уведомление у значка в трее — видно всегда, в отличие от toast API.</summary>
    public static void ShowBalloon(string title, string text)
    {
        try
        {
            EnsureTrayIcon();
            if (_trayHwnd == IntPtr.Zero || !_iconAdded)
                return;
            var data = new NOTIFYICONDATAW
            {
                cbSize = Marshal.SizeOf<NOTIFYICONDATAW>(),
                hWnd = _trayHwnd,
                uID = ID_ICON,
                uFlags = NIF_INFO,
                szInfo = text.Length > 255 ? text[..255] : text,
                szInfoTitle = title.Length > 63 ? title[..63] : title,
                dwInfoFlags = 0x01, // NIIF_INFO
                uTimeoutOrVersion = 15_000, // таймаут показа баллуна, мс
            };
            if (!Shell_NotifyIconW(NIM_MODIFY, ref data))
                LogTray("ShowBalloon: NIM_MODIFY вернул false, err=" + Marshal.GetLastWin32Error());
        }
        catch { }
    }

    private static void ExitApp()
    {
        _exitRequested = true;
        try
        {
            if (_trayHwnd != IntPtr.Zero && _iconAdded)
            {
                var data = new NOTIFYICONDATAW { cbSize = Marshal.SizeOf<NOTIFYICONDATAW>(), hWnd = _trayHwnd, uID = ID_ICON };
                Shell_NotifyIconW(NIM_DELETE, ref data);
            }
            PostMessageW(_trayHwnd, WM_QUIT, IntPtr.Zero, IntPtr.Zero);
        }
        catch { }
        // Quit — через диспетчер UI-потока (вызов с потока трея нестабилен)
        var w = _window;
        if (w is not null)
            w.Dispatcher.Dispatch(() => Microsoft.Maui.Controls.Application.Current?.Quit());
        else
            Microsoft.Maui.Controls.Application.Current?.Quit();
    }

    public static void LogTray(string m)
    {
        try
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TimeTool");
            Directory.CreateDirectory(dir);
            File.AppendAllText(Path.Combine(dir, "tray.log"),
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {m}{Environment.NewLine}");
        }
        catch { }
    }

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", ExactSpelling = true)]
    private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll")] private static extern IntPtr CallWindowProc(IntPtr lpPrevWndFunc, IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern IntPtr DefWindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll", EntryPoint = "CreateWindowExW", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)] private static extern IntPtr CreateWindowExW(int exStyle, string className, string? windowName, int style, int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);
    [DllImport("user32.dll", EntryPoint = "CreateWindowExW", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)] private static extern IntPtr CreateWindowExWAtom(int exStyle, IntPtr atomClassName, string? windowName, int style, int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out POINT pt);
    [DllImport("user32.dll")] private static extern IntPtr CreatePopupMenu();
    [DllImport("user32.dll", EntryPoint = "AppendMenuW", CharSet = CharSet.Unicode, ExactSpelling = true)] private static extern bool AppendMenuW(IntPtr menu, uint flags, int id, string text);
    [DllImport("user32.dll")] private static extern bool TrackPopupMenu(IntPtr menu, uint flags, int x, int y, int reserved, IntPtr hwnd, IntPtr rect);
    [DllImport("user32.dll")] private static extern bool DestroyMenu(IntPtr menu);
    [DllImport("user32.dll")] private static extern IntPtr LoadIcon(IntPtr instance, IntPtr name);
    [DllImport("user32.dll")] private static extern bool PostMessageW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool GetMessageW(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);
    [DllImport("user32.dll")] private static extern bool PeekMessageW(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax, uint wRemoveMsg);
    [DllImport("user32.dll")] private static extern bool TranslateMessage(ref MSG lpMsg);
    [DllImport("user32.dll")] private static extern IntPtr DispatchMessageW(ref MSG lpMsg);

    [DllImport("shell32.dll", EntryPoint = "Shell_NotifyIconW", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern bool Shell_NotifyIconW(uint message, ref NOTIFYICONDATAW data);

    [DllImport("shell32.dll", EntryPoint = "ExtractIconExW", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern uint ExtractIconExW(string lpszFile, int nIconIndex, out IntPtr phiconLarge, out IntPtr phiconSmall, uint nIcons);

    [DllImport("user32.dll", EntryPoint = "RegisterClassW", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    private static extern ushort RegisterClassW(ref WNDCLASSW lpWndClass);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandleW(IntPtr lpModuleName);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WNDCLASSW
    {
        public uint style;
        public IntPtr lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        public string lpszMenuName;
        public string lpszClassName;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X; public int Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG { public IntPtr hwnd; public uint message; public IntPtr wParam; public IntPtr lParam; public uint time; public POINT pt; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NOTIFYICONDATAW
    {
        public int cbSize;
        public IntPtr hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szTip;
        public int dwState;
        public int dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string szInfo;
        public uint uTimeoutOrVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string szInfoTitle;
        public uint dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }
}
#endif
