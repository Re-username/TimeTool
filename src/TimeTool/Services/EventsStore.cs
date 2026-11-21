using Microsoft.JSInterop;

namespace TimeTool.Services;

public sealed class EventItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public DateTime Date { get; set; } = DateTime.Today.AddDays(7);
    public string Repeat { get; set; } = "none"; // none | monthly | yearly
    public int RemindLeadMinutes { get; set; } // 0 — без предварительного напоминания, -1 — вовремя
    public string? SoundPath { get; set; } // кастомный звук напоминания (wav/mp3/ogg), пусто — стандартный
    public string WeekendShift { get; set; } = "none"; // none | friday | monday — перенос выпадения на выходные
    public string? Note { get; set; } // заметка к событию (редактируется)
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
    /// <summary>Предварительное напоминание (lead) — только если настроено у события.</summary>
    public event Action<EventItem, DateTime>? Remind;
    /// <summary>Событие наступило — сигналит ВСЕГДА, независимо от настройки напоминания.</summary>
    public event Action<EventItem, DateTime>? Occurred;

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

    /// <summary>Обновляет поля события (редактирование, включая заметку).</summary>
    public async Task UpdateAsync(string id, Action<EventItem> mutate)
    {
        var e = _events.FirstOrDefault(x => x.Id == id);
        if (e is null)
            return;
        mutate(e);
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

    /// <summary>
    /// Событие «наступило сейчас»: момент ближайшего наступления не более
    /// <paramref name="window"/> назад. Для повторяющихся событий учитывается
    /// предыдущее наступление (текущее уже «перекатилось» в будущее).
    /// </summary>
    public static bool IsHappening(EventItem e, DateTime now, TimeSpan? window = null)
    {
        window ??= TimeSpan.FromMinutes(2);
        var prev = NextOccurrence(e, now - window.Value - TimeSpan.FromSeconds(1));
        return prev <= now && now - prev <= window.Value;
    }

    /// <summary>Однократное событие, время которого прошло (для красного «просрочено»).</summary>
    public static bool IsOverdue(EventItem e, DateTime now)
        => e.Repeat == "none" && e.Date < now;

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

    /// <summary>Формат «осталось» без отрицательных значений: просроченное — «прошло … назад».</summary>
    public static string FormatLeft(TimeSpan left, out bool overdue)
    {
        overdue = left < TimeSpan.Zero;
        if (overdue)
            left = left.Negate();
        string s;
        if (left.TotalDays >= 1)
            s = $"{(int)left.TotalDays} д {(int)left.Hours} ч {left.Minutes} мин";
        else if (left.TotalHours >= 1)
            s = $"{(int)left.TotalHours} ч {left.Minutes} мин";
        else
            s = $"{(int)left.TotalMinutes} мин {left.Seconds} с";
        return overdue ? $"прошло {s} назад" : s;
    }

    /// <summary>
    /// Цикл напоминаний. Два независимых срабатывания на каждое наступление:
    /// «due» — в момент события (ВСЕГДА, вне зависимости от настроек) и
    /// «lead» — заранее (только если RemindLeadMinutes != 0).
    /// Пропущенные (приложение было закрыто) показываются при запуске — окно сутки.
    /// </summary>
    private async Task ReminderLoop()
    {
        while (true)
        {
            try
            {
                var now = DateTime.Now;
                foreach (var e in _events.ToList())
                {
                    var next = NextOccurrence(e, now);
                    var key = $"{e.Id}:{next.Ticks}";

                    // наступление — сигналим всегда, даже без настроенного напоминания
                    if (now >= next && now - next <= TimeSpan.FromHours(24) && _fired.Add(key + ":due"))
                    {
                        TrimFired();
                        _ = SaveFiredAsync();
                        Occurred?.Invoke(e, next);
                    }

                    if (e.RemindLeadMinutes == 0)
                        continue;
                    if (_fired.Contains(key + ":lead"))
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
                        _fired.Add(key + ":lead");
                        TrimFired();
                        _ = SaveFiredAsync();
                        Remind?.Invoke(e, next);
                    }
                }
            }
            catch { }
            await Task.Delay(20_000);
        }
    }

    private void TrimFired()
    {
        while (_fired.Count > 500)
            _fired.Remove(_fired.First());
    }
}
