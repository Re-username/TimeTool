using Microsoft.Extensions.Logging;

namespace TimeTool;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) => LogCrash("AppDomain", e.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            LogCrash("TaskScheduler", e.Exception);
            e.SetObserved();
        };

        var builder = MauiApp.CreateBuilder();
        builder.UseMauiApp<App>();

        builder.Services.AddMauiBlazorWebView();
        builder.Services.AddSingleton<Services.TimeState>();
        builder.Services.AddSingleton<Services.EventsStore>();

#if ANDROID
        // Звук таймера срабатывает без жеста пользователя (сработка по прошествии времени)
        Microsoft.Maui.Handlers.WebViewHandler.Mapper.AppendToMapping("MediaPlayback", (handler, view) =>
        {
            handler.PlatformView.Settings.MediaPlaybackRequiresUserGesture = false;
        });
#endif

        return builder.Build();
    }

    private static void LogCrash(string source, Exception? ex)
    {
        try
        {
            var dir = FileSystem.AppDataDirectory;
            File.AppendAllText(Path.Combine(dir, "crashes.log"),
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{source}] {ex}{Environment.NewLine}");
        }
        catch { }
    }

}
