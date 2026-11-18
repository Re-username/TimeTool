namespace TimeTool.Services;

/// <summary>
/// Движок обратного отсчёта. Живёт в памяти приложения (singleton) — таймер продолжает
/// идти при переходе между вкладками и срабатывает, даже когда страница таймера закрыта.
/// Звук/уведомления выполняет подписчик (MainLayout), у него есть JS-интероп и трей.
/// </summary>
public sealed class TimerEngine
{
    private static readonly TimeSpan MaxTotal = new(999, 59, 59);

    public TimeSpan Total { get; private set; } = TimeSpan.FromMinutes(5);
    public TimeSpan Remaining { get; private set; } = TimeSpan.FromMinutes(5);
    public bool IsRunning { get; private set; }
    public bool IsPaused { get; private set; }
    public bool Finished { get; private set; }
    public string SoundPath { get; set; } = "";

    /// <summary>Периодическое обновление Remaining (100 мс) — подписывается страница для перерисовки.</summary>
    public event Action? Changed;

    /// <summary>Отсчёт дошёл до нуля (один раз на запуск).</summary>
    public event Action? Elapsed;

    private DateTime _endAt;
    private bool _loopActive;

    public void Arm(TimeSpan total)
    {
        if (IsRunning)
            return;
        Total = total < TimeSpan.Zero ? TimeSpan.Zero : total > MaxTotal ? MaxTotal : total;
        Remaining = Total;
        Finished = false;
        Changed?.Invoke();
    }

    public void Start()
    {
        if (IsRunning || Total == TimeSpan.Zero)
            return;
        Remaining = Total;
        IsPaused = false;
        Finished = false;
        IsRunning = true;
        _endAt = DateTime.Now + Total;
        EnsureLoop();
        Changed?.Invoke();
    }

    public void Pause()
    {
        if (!IsRunning)
            return;
        Remaining = _endAt - DateTime.Now;
        if (Remaining < TimeSpan.Zero)
            Remaining = TimeSpan.Zero;
        IsRunning = false;
        IsPaused = true;
        Changed?.Invoke();
    }

    public void Resume()
    {
        if (!IsPaused || Remaining <= TimeSpan.Zero)
            return;
        IsPaused = false;
        IsRunning = true;
        _endAt = DateTime.Now + Remaining;
        EnsureLoop();
        Changed?.Invoke();
    }

    public void Reset()
    {
        IsRunning = false;
        IsPaused = false;
        Finished = false;
        Remaining = Total;
        Changed?.Invoke();
    }

    private void EnsureLoop()
    {
        if (_loopActive)
            return;
        _loopActive = true;
        _ = Task.Run(async () =>
        {
            try
            {
                while (IsRunning)
                {
                    var left = _endAt - DateTime.Now;
                    Remaining = left < TimeSpan.Zero ? TimeSpan.Zero : left;
                    if (left <= TimeSpan.Zero)
                    {
                        IsRunning = false;
                        Finished = true;
                        try { Elapsed?.Invoke(); } catch { }
                    }
                    Changed?.Invoke();
                    if (IsRunning)
                        await Task.Delay(100);
                }
            }
            finally
            {
                _loopActive = false;
                Changed?.Invoke();
            }
        });
    }
}
