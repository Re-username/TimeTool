#if WINDOWS
using Microsoft.Win32;

namespace TimeTool.Platforms.Windows;

/// <summary>Автозапуск с Windows (реестр, HKCU Run — без прав администратора).</summary>
public static class Autostart
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string Name = "TimeTool";

    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return string.Equals(key?.GetValue(Name) as string, ExePath(), StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    public static void Set(bool enable)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true)
                ?? Registry.CurrentUser.CreateSubKey(RunKey);
            if (enable)
                key.SetValue(Name, ExePath());
            else
                key.DeleteValue(Name, throwOnMissingValue: false);
        }
        catch { }
    }

    private static string ExePath() => $"\"{Environment.ProcessPath}\"";
}
#endif
