using Microsoft.JSInterop;

namespace TimeTool.Services;

public sealed class EventItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public DateTime Date { get; set; } = DateTime.Today.AddDays(7);
    public string Repeat { get; set; } = "none"; // none | monthly | yearly
    public int RemindLeadMinutes { get; set; } // 0 — без напоминания, -1 — вовремя
    public string? SoundPath { get; set; } // кастомный звук напоминания (wav/mp3/ogg), пусто — стандартный
    public string WeekendShift { get; set; } = "none"; // none | friday | monday — перенос выпадения на выходные
}

/// <summary>События с повторяемостью + движок «умных» напоминаний.</summary>
public sealed class EventsStore
{
    private const string StorageKey = "timetool-events";
    private const string FiredKey = "timetool-events-fired";

    private readonly List<EventItem> _events = new();
    private readonly HashSet<string> _fired = new();
    private IJSRuntime? _js;
    private bool _initialized;

    public IReadOnlyList<EventItem> Events => _events;

    public event Action? Changed;
    public event Action<EventItem, DateTime>? Remind;

    public async Task InitAsync(IJSRuntime js)
    {
        if (_initialized)
            return;
        _initialized = true;
        _js = js;
        try
        {
            var json = await js.InvokeAsync<string?>("localStorage.getItem", StorageKey);
            if (!string.IsNullOrEmpty(json))
            {
                var list = System.Text.Json.JsonSerializer.Deserialize<List<EventItem>>(json);
                if (list is not null)
                    _events.AddRange(list);
            }
            var fired = await js.InvokeAsync<string?>("localStorage.getItem", FiredKey);
            if (!string.IsNullOrEmpty(fired))
                foreach (var key in fired.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                    _fired.Add(key);
        }
        catch { }

        _ = ReminderLoop();
    }

    public async Task AddAsync(EventItem item)
    {
        _events.Add(item);
        await SaveAsync();
        Changed?.Invoke();
    }

    public async Task RemoveAsync(string id)
    {
        _events.RemoveAll(e => e.Id == id);
        await SaveAsync();
        Changed?.Invoke();
    }

    private async Task SaveAsync()
    {
        try
        {
            if (_js is not null)
                await _js.InvokeVoidAsync("localStorage.setItem", StorageKey,
                    System.Text.Json.JsonSerializer.Serialize(_events));
        }
        catch { }
    }

    private async Task SaveFiredAsync()
    {
        try
        {
            if (_js is not null)
                await _js.InvokeVoidAsync("localStorage.setItem", FiredKey, string.Join('\n', _fired));
        }
        catch { }
    }

    /// <summary>Ближайшее наступление события относительно момента from.</summary>
    private static DateTime NextOccurrenceCore(EventItem e, DateTime from)
    {
        if (e.Repeat == "daily")
        {
            var c = from.Date + e.Date.TimeOfDay;
            return c <= from ? c.AddDays(1) : c;
        }
        if (e.Repeat == "weekly")
        {
            var c = from.Date + e.Date.TimeOfDay;
            while (c <= from || c.DayOfWeek != e.Date.DayOfWeek)
                c = c.AddDays(1);
            return c;
        }
        if (e.Repeat == "monthly")
        {
            var day = Math.Clamp(e.Date.Day, 1, 28);
            var c = new DateTime(from.Year, from.Month, day, e.Date.Hour, e.Date.Minute, 0);
            if (c <= from)
                c = c.AddMonths(1);
            return ClampDay(c, e.Date.Day);
        }
        if (e.Repeat == "yearly")
        {
            var day = Math.Clamp(e.Date.Day, 1, 28);
            var c = new DateTime(from.Year, e.Date.Month, day, e.Date.Hour, e.Date.Minute, 0);
            if (c <= from)
                c = c.AddYears(1);
            return ClampDay(c, e.Date.Day);
        }
        return e.Date;
    }

    /// <summary>Ближайшее наступление с учётом переноса выходных (зп в сб/вс → пятница и т.п.).</summary>
    public static DateTime NextOccurrence(EventItem e, DateTime from)
    {
        var d = NextOccurrenceCore(e, from);
        if (e.WeekendShift == "none")
            return d;
        var shifted = ApplyWeekendShift(d, e.WeekendShift);
        // перенос назад (на пятницу) имеет смысл только если пятница ещё впереди;
        // иначе оставляем исходную дату (интерпретация: переносили уже нечего)
        return shifted > from ? shifted : d;
    }

    private static DateTime ApplyWeekendShift(DateTime d, string mode) => mode switch
    {
        "friday" => d.DayOfWeek == DayOfWeek.Saturday ? d.AddDays(-1)
                  : d.DayOfWeek == DayOfWeek.Sunday ? d.AddDays(-2) : d,
        "monday" => d.DayOfWeek == DayOfWeek.Saturday ? d.AddDays(2)
                  : d.DayOfWeek == DayOfWeek.Sunday ? d.AddDays(1) : d,
        _ => d,
    };

    private static DateTime ClampDay(DateTime month, int preferredDay)
    {
        var day = Math.Min(preferredDay, DateTime.DaysInMonth(month.Year, month.Month));
        return new DateTime(month.Year, month.Month, day, month.Hour, month.Minute, 0);
    }

    /// <summary>Цикл напоминаний: срабатывает заранее (lead) и показывает пропущенные при запуске.</summary>
    private async Task ReminderLoop()
    {
        while (true)
        {
            try
            {
                var now = DateTime.Now;
                foreach (var e in _events.ToList())
                {
                    if (e.RemindLeadMinutes == 0)
                        continue;
                    var next = NextOccurrence(e, now);
                    var key = $"{e.Id}:{next.Ticks}";
                    if (_fired.Contains(key))
                        continue;

                    bool due;
                    if (e.RemindLeadMinutes < 0)
                    {
                        // «вовремя»: в момент события и окно сутки после (если приложение было закрыто)
                        due = now >= next && now - next <= TimeSpan.FromHours(24);
                    }
                    else
                    {
                        // заранее (lead); если приложение было закрыто — показать и только что прошедшее
                        var lead = TimeSpan.FromMinutes(e.RemindLeadMinutes);
                        due = next - now <= lead && now - next <= TimeSpan.FromHours(24);
                    }

                    if (due)
                    {
                        _fired.Add(key);
                        if (_fired.Count > 500)
                            _fired.Remove(_fired.First());
                        _ = SaveFiredAsync();
                        Remind?.Invoke(e, next);
                    }
                }
            }
            catch { }
            await Task.Delay(20_000);
        }
    }
}
