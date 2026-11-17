#if WINDOWS
using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using WinRT.Interop;

namespace TimeTool.Platforms.Windows;

/// <summary>
/// Закрытие окна → в трей (не выход). Сворачивание кнопкой «—» — как обычно в панели задач.
/// Клик по значку — вернуть окно; правая кнопка — меню (Открыть / Выход).
/// </summary>
public static class TrayService
{
    private const int WM_CLOSE = 0x0010;
    private const int WM_COMMAND = 0x0111;
    private const uint WM_TRAYCALLBACK = 0x8001;
    private const int WM_LBUTTONUP = 0x0202;
    private const int WM_RBUTTONUP = 0x0205;
    private const uint NIF_MESSAGE = 0x01;
    private const uint NIF_ICON = 0x02;
    private const uint NIF_TIP = 0x04;
    private const uint NIF_INFO = 0x10;
    private const uint NIM_ADD = 0x00;
    private const uint NIM_MODIFY = 0x01;
    private const uint NIM_DELETE = 0x02;
    private const int ID_ICON = 5000;
    private const int ID_MENU_OPEN = 1001;
    private const int ID_MENU_EXIT = 1002;
    private const int GWL_WNDPROC = -4;
    private const int SW_HIDE = 0;
    private const int SW_SHOW = 5;

    private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    private static WndProcDelegate? _mainProc;
    private static WndProcDelegate? _trayProc;
    private static IntPtr _mainHwnd;
    private static IntPtr _oldMainProc;
    private static IntPtr _trayHwnd;
    private static bool _exitRequested;
    private static Microsoft.Maui.Controls.Window? _window;

    public static void Attach(Microsoft.Maui.Controls.Window window)
    {
        try
        {
            _window = window;
            var native = window.Handler?.PlatformView as Microsoft.UI.Xaml.Window
                ?? throw new InvalidOperationException("Нативное окно недоступно");
            var hwnd = WindowNative.GetWindowHandle(native);
            _mainHwnd = hwnd;
            _mainProc = MainWndProc;
            _oldMainProc = SetWindowLongPtr(hwnd, GWL_WNDPROC, Marshal.GetFunctionPointerForDelegate(_mainProc));
        }
        catch { /* трей — необязателен, не должно ронять приложение */ }
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
        if (_trayHwnd != IntPtr.Zero)
            return;

        _trayProc = TrayWndProc;
        _trayHwnd = CreateWindowExW(0, "STATIC", "TimeToolTray", 0, 0, 0, 0, 0,
            new IntPtr(-3), IntPtr.Zero, IntPtr.Zero, IntPtr.Zero); // HWND_MESSAGE
        SetWindowLongPtr(_trayHwnd, GWL_WNDPROC, Marshal.GetFunctionPointerForDelegate(_trayProc));

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
        if (!Shell_NotifyIconW(NIM_ADD, ref data))
            LogTray("Shell_NotifyIconW(NIM_ADD) вернул false");
    }

    private static IntPtr _hIcon;
    private static bool _hintShown;

    private static void LogTray(string msg)
    {
        try
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TimeTool");
            Directory.CreateDirectory(dir);
            File.AppendAllText(Path.Combine(dir, "tray.log"),
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {msg}{Environment.NewLine}");
        }
        catch { }
    }

    private static IntPtr TrayWndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == WM_TRAYCALLBACK)
        {
            var mouse = (uint)lParam;
            if (mouse == WM_LBUTTONUP)
                RestoreWindow();
            else if (mouse == WM_RBUTTONUP)
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
        TrackPopupMenu(menu, 0x0100, pt.X, pt.Y, 0, _trayHwnd, IntPtr.Zero); // TPM_RIGHTBUTTON... 0x0100 = TPM_RETURNCMD? нет — выбор через WM_COMMAND
        DestroyMenu(menu);
    }

    public static void RestoreWindow()
    {
        if (_mainHwnd == IntPtr.Zero)
            return;
        ShowWindow(_mainHwnd, SW_SHOW);
        SetForegroundWindow(_mainHwnd);
    }

    /// <summary>Всплывающее уведомление у значка в трее — видно всегда, в отличие от toast API.</summary>
    public static void ShowBalloon(string title, string text)
    {
        try
        {
            EnsureTrayIcon();
            if (_trayHwnd == IntPtr.Zero)
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
            };
            Shell_NotifyIconW(NIM_MODIFY, ref data);
        }
        catch { }
    }

    private static void ExitApp()
    {
        _exitRequested = true;
        if (_trayHwnd != IntPtr.Zero)
        {
            var data = new NOTIFYICONDATAW { cbSize = Marshal.SizeOf<NOTIFYICONDATAW>(), hWnd = _trayHwnd, uID = ID_ICON };
            Shell_NotifyIconW(NIM_DELETE, ref data);
        }
        Microsoft.Maui.Controls.Application.Current?.Quit();
    }

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll")] private static extern IntPtr CallWindowProc(IntPtr lpPrevWndFunc, IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern IntPtr DefWindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern IntPtr CreateWindowExW(int exStyle, string className, string? windowName, int style, int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out POINT pt);
    [DllImport("user32.dll")] private static extern IntPtr CreatePopupMenu();
    [DllImport("user32.dll")] private static extern bool AppendMenuW(IntPtr menu, uint flags, int id, string text);
    [DllImport("user32.dll")] private static extern bool TrackPopupMenu(IntPtr menu, uint flags, int x, int y, int reserved, IntPtr hwnd, IntPtr rect);
    [DllImport("user32.dll")] private static extern bool DestroyMenu(IntPtr menu);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern bool Shell_NotifyIconW(uint message, ref NOTIFYICONDATAW data);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern uint ExtractIconExW(string lpszFile, int nIconIndex, out IntPtr phiconLarge, out IntPtr phiconSmall, uint nIcons);

    [DllImport("user32.dll")] private static extern IntPtr LoadIcon(IntPtr instance, IntPtr name);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X; public int Y; }

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
