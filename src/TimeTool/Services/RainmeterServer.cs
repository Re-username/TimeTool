using System.Net;
using System.Text;
using System.Text.Json;

namespace TimeTool.Services;

/// <summary>
/// Локальный HTTP-сервер для интеграции с Rainmeter и прочих виджетов.
/// Слушает только 127.0.0.1. Эндпоинты:
///   /            — HTML-страница с отсчётами (для плагина WebView2)
///   /events.json — список событий с ближайшими датами
///   /events.txt  — плоский текст (для WebParser): имя|дд.мм.гггг чч:мм|осталось
///   /timer.json  — состояние таймера
///   /timer.txt   — «Таймер|00:05:00» одной строкой для WebParser
/// </summary>
public sealed class RainmeterServer
{
    public const int BasePort = 47777;
    public const int MaxPortTries = 10;

    private readonly EventsStore _store;
    private readonly TimerEngine _timer;
    private HttpListener? _listener;
    private CancellationTokenSource? _cts;

    public int Port { get; private set; }
    public bool Running { get; private set; }
    public string Url => $"http://127.0.0.1:{Port}/";

    public RainmeterServer(EventsStore store, TimerEngine timer)
    {
        _store = store;
        _timer = timer;
    }

    public void Start()
    {
        if (Running)
            return;
        for (var port = BasePort; port < BasePort + MaxPortTries; port++)
        {
            try
            {
                var l = new HttpListener();
                l.Prefixes.Add($"http://127.0.0.1:{port}/");
                l.Start();
                _listener = l;
                Port = port;
                break;
            }
            catch
            {
                // порт занят — пробуем следующий
            }
        }
        if (_listener is null)
            return;
        Running = true;
        _cts = new CancellationTokenSource();
        _ = Task.Run(() => LoopAsync(_cts.Token));
        Log($"rainmeter: сервер запущен на {Url}");
    }

    public void Stop()
    {
        Running = false;
        try { _cts?.Cancel(); } catch { }
        try { _listener?.Stop(); } catch { }
        try { _listener?.Close(); } catch { }
        _listener = null;
    }

    private async Task LoopAsync(CancellationToken ct)
    {
        while (Running && _listener is not null)
        {
            HttpListenerContext ctx;
            try { ctx = await _listener.GetContextAsync(); }
            catch { break; } // listener остановлен
            if (ct.IsCancellationRequested)
                break;
            _ = Task.Run(() => HandleAsync(ctx));
        }
    }

    private async Task HandleAsync(HttpListenerContext ctx)
    {
        try
        {
            var path = ctx.Request.Url?.AbsolutePath ?? "/";
            var (content, mime) = path switch
            {
                "/" or "/index.html" => (HtmlPage(), "text/html; charset=utf-8"),
                "/events.json" => (JsonPayload(), "application/json; charset=utf-8"),
                "/events.txt" => (TextPayload(), "text/plain; charset=utf-8"),
                "/timer.json" => (TimerJson(), "application/json; charset=utf-8"),
                "/timer.txt" => (TimerText(), "text/plain; charset=utf-8"),
                _ => ("not found", "text/plain; charset=utf-8"),
            };
            var bytes = Encoding.UTF8.GetBytes(content);
            // BOM помогает Rainmeter (WebParser) определить UTF-8 — иначе кириллицу
            // он декодирует по системной ANSI-кодировке и данные «теряются»
            if (path is "/events.txt" or "/timer.txt")
                bytes = Encoding.UTF8.GetPreamble().Concat(bytes).ToArray();
            ctx.Response.ContentType = mime;
            ctx.Response.ContentLength64 = bytes.Length;
            if (path is not "/" and not "/index.html" and not "/events.json" and not "/events.txt" and not "/timer.json" and not "/timer.txt")
                ctx.Response.StatusCode = 404;
            await ctx.Response.OutputStream.WriteAsync(bytes);
        }
        catch { }
        finally
        {
            try { ctx.Response.Close(); } catch { }
        }
    }

    private sealed record EventDto(string Name, string Next, double DaysLeft, string Left, string Repeat, string WeekendShift, string? Note, bool Happening, bool Overdue);

    private List<EventDto> Snapshot()
    {
        var now = DateTime.Now;
        return _store.Events
            .Select(e => (e, next: EventsStore.NextOccurrence(e, now)))
            .OrderBy(x => x.next)
            .Select(x => new EventDto(
                x.e.Name,
                x.next.ToString("dd.MM.yyyy HH:mm"),
                Math.Round((x.next - now).TotalDays, 1),
                EventsStore.FormatLeft(x.next - now, out _),
                x.e.Repeat,
                x.e.WeekendShift,
                x.e.Note,
                EventsStore.IsHappening(x.e, now),
                EventsStore.IsOverdue(x.e, now)))
            .ToList();
    }

    private string JsonPayload() => JsonSerializer.Serialize(Snapshot());

    private string TimerText()
    {
        // одна строка «Таймер|значение»: виджет показывает правую часть
        string value;
        if (_timer.Finished)
            value = "Время вышло!";
        else if (_timer.IsRunning || _timer.IsPaused)
            value = (_timer.Remaining < TimeSpan.Zero ? TimeSpan.Zero : _timer.Remaining).ToString(@"hh\:mm\:ss");
        else
            value = "—";
        return "Таймер|" + value + "\n";
    }

    private string TimerJson() => JsonSerializer.Serialize(new
    {
        _timer.IsRunning,
        _timer.IsPaused,
        _timer.Finished,
        Remaining = (_timer.Remaining < TimeSpan.Zero ? TimeSpan.Zero : _timer.Remaining).ToString(@"hh\:mm\:ss"),
        Total = _timer.Total.ToString(@"hh\:mm\:ss"),
    });

    private string TextPayload()
    {
        var sb = new StringBuilder();
        foreach (var e in Snapshot())
            sb.Append(e.Name.Replace('|', '/')).Append('|').Append(e.Next).Append('|').Append(e.Left).Append('\n');
        return sb.ToString();
    }

    private static string HtmlPage() => """
<!DOCTYPE html>
<html lang="ru">
<head>
<meta charset="utf-8">
<title>TimeTool — отсчёты</title>
<style>
  body { background:#0d1117; color:#e6edf3; font-family:Segoe UI,system-ui,sans-serif; margin:0; padding:12px; }
  h1 { font-size:15px; margin:0 0 10px; color:#58a6ff; font-weight:600; }
  .ev { display:flex; justify-content:space-between; align-items:baseline; gap:12px; padding:6px 8px; border-bottom:1px solid #21262d; }
  .name { font-size:13px; overflow:hidden; text-overflow:ellipsis; white-space:nowrap; }
  .when { font-size:11px; color:#8b949e; white-space:nowrap; }
  .left { font-size:13px; color:#3fb950; font-weight:600; white-space:nowrap; }
  .empty { color:#8b949e; font-size:13px; }
</style>
</head>
<body>
<h1>TimeTool — события</h1>
<div id="list"><div class="empty">Загрузка…</div></div>
<script>
async function refresh() {
  try {
    const r = await fetch('/events.json', { cache: 'no-store' });
    const data = await r.json();
    const el = document.getElementById('list');
    if (!data.length) { el.innerHTML = '<div class="empty">Событий нет</div>'; return; }
    el.innerHTML = '';
    for (const e of data) {
      const div = document.createElement('div');
      div.className = 'ev';
      const name = document.createElement('div'); name.className = 'name'; name.textContent = e.Name;
      const right = document.createElement('div'); right.style.textAlign = 'right';
      const left = document.createElement('div'); left.className = 'left'; left.textContent = e.Left;
      const when = document.createElement('div'); when.className = 'when'; when.textContent = e.Next;
      right.appendChild(left); right.appendChild(when);
      div.appendChild(name); div.appendChild(right);
      el.appendChild(div);
    }
  } catch { }
}
refresh();
setInterval(refresh, 1000);
</script>
</body>
</html>
""";

    private static void Log(string message)
    {
#if WINDOWS
        Platforms.Windows.TrayService.LogTray(message);
#else
        System.Diagnostics.Debug.WriteLine(message);
#endif
    }
}
