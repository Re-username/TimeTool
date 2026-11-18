#if WINDOWS
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;

namespace TimeTool.Platforms.Windows;

/// <summary>Системные toast-уведомления Windows (Windows App SDK, работает для unpackaged).</summary>
public static class ToastNotifier
{
    private static bool _registered;

    public static void Init()
    {
        try
        {
            if (_registered)
                return;
            AppNotificationManager.Default.Register();
            _registered = true;
            TrayService.LogTray("toast: Register ok, setting=" + AppNotificationManager.Default.Setting);
        }
        catch (Exception ex)
        {
            TrayService.LogTray("toast Register: " + ex.GetType().Name + ": " + ex.Message);
        }
    }

    public static void Show(string title, string body)
    {
        try
        {
            Init();
            var toast = new AppNotificationBuilder()
                .AddText(title)
                .AddText(body)
                .BuildNotification();
            AppNotificationManager.Default.Show(toast);
            TrayService.LogTray("toast: Show отправлен, setting=" + AppNotificationManager.Default.Setting);
        }
        catch (Exception ex)
        {
            TrayService.LogTray("toast Show: " + ex.GetType().Name + ": " + ex.Message);
        }
    }
}
#endif
