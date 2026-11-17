#if WINDOWS
using System.Runtime.InteropServices;
using Microsoft.UI.Windowing;
using WinRT.Interop;

namespace TimeTool.Platforms.Windows;

/// <summary>Открывает компактное окно-виджет (всегда сверху, без кнопки на панели задач).</summary>
public static class WidgetWindows
{
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TOOLWINDOW = 0x80;
    private const int WS_EX_TOPMOST = 0x8;
    private const int SWP_NOMOVE = 0x2;
    private const int SWP_NOSIZE = 0x1;
    private const int HWND_TOPMOST = -1;
    private const int CW_USEDEFAULT = unchecked((int)0x80000000);

    public static void Open(string tab)
    {
        Services.WidgetContext.Tab = tab;
        var window = new Window(new WidgetPage()) { Title = "TimeTool — виджет" };
        Application.Current?.OpenWindow(window);

        window.HandlerChanged += (_, _) =>
        {
            if (window.Handler?.PlatformView is not Microsoft.UI.Xaml.Window native)
                return;
            var hwnd = WindowNative.GetWindowHandle(native);

            var ex = GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64();
            SetWindowLongPtr(hwnd, GWL_EXSTYLE, new IntPtr(ex | WS_EX_TOOLWINDOW | WS_EX_TOPMOST));

            var width = 320;
            var height = tab switch
            {
                "events" => 210,
                "timer" or "stopwatch" => 150,
                "calc" => 190,
                _ => 120,
            };
            SetWindowPos(hwnd, HWND_TOPMOST, CW_USEDEFAULT, CW_USEDEFAULT, width, height, SWP_NOMOVE);
        };
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, int hWndInsertAfter, int x, int y, int cx, int cy, uint flags);
}
#endif
