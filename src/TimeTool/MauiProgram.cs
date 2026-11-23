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
        builder.Services.AddSingleton<Services.TimerEngine>();
        builder.Services.AddSingleton<Services.UpdateChecker>();

#if ANDROID
        // Звук таймера срабатывает без жеста пользователя (сработка по прошествии времени)
        Microsoft.Maui.Handlers.WebViewHandler.Mapper.AppendToMapping("MediaPlayback", (handler, view) =>
        {
            handler.PlatformView.Settings.MediaPlaybackRequiresUserGesture = false;
        });
#endif

        var app = builder.Build();

        // Локальный сервер для Rainmeter (127.0.0.1, работает и в трее)
        try
        {
            RainmeterServerHolder.Instance = new Services.RainmeterServer(
                app.Services.GetRequiredService<Services.EventsStore>(),
                app.Services.GetRequiredService<Services.TimerEngine>());
            RainmeterServerHolder.Instance.Start();
            AppDomain.CurrentDomain.ProcessExit += (_, _) => RainmeterServerHolder.Instance?.Stop();

            // тестовый сидер событий для проверки интеграций (Rainmeter и пр.)
            if (Environment.GetEnvironmentVariable("TIMETOOL_TEST_EVENTS") == "1")
            {
                var store = app.Services.GetRequiredService<Services.EventsStore>();
                _ = store.AddAsync(new Services.EventItem { Name = "Зарплата", Date = DateTime.Now.AddDays(6).Date.AddHours(9), Repeat = "monthly", WeekendShift = "friday" });
                _ = store.AddAsync(new Services.EventItem { Name = "День рождения", Date = DateTime.Now.AddDays(9).Date });
                _ = store.AddAsync(new Services.EventItem { Name = "Тренировка", Date = DateTime.Now.AddDays(1).Date.AddHours(19), Repeat = "weekly" });
            }
        }
        catch { }

        return app;
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

/// <summary>Доступ к серверу Rainmeter из платформенного кода (трей и т.п.).</summary>
public static class RainmeterServerHolder
{
    public static Services.RainmeterServer? Instance { get; set; }
}
