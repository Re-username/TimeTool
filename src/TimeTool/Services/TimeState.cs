using System.Net;
using System.Net.Sockets;

namespace TimeTool.Services;

/// <summary>Общие настройки часов/времени и помощники (пояс, формат, NTP).</summary>
public sealed class TimeState
{
    public string TimeZoneId { get; set; } = TimeZoneInfo.Local.Id;
    public bool Use24Hours { get; set; } = true;
    public bool ShowSeconds { get; set; } = true;

    private TimeZoneInfo? _tzCache;

    public TimeZoneInfo TimeZone
    {
        get
        {
            _tzCache ??= Find(TimeZoneId);
            return _tzCache;
        }
    }

    public event Action? Changed;

    public void NotifyChanged()
    {
        _tzCache = null;
        Changed?.Invoke();
    }

    public DateTime NowInZone() => TimeZoneInfo.ConvertTime(DateTime.Now, TimeZone);

    public string FormatTime(DateTime dt)
    {
        var pattern = Use24Hours
            ? (ShowSeconds ? "HH:mm:ss" : "HH:mm")
            : (ShowSeconds ? "hh:mm:ss tt" : "hh:mm tt");
        return dt.ToString(pattern);
    }

    private static TimeZoneInfo Find(string id)
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(id);
        }
        catch
        {
            return TimeZoneInfo.Local;
        }
    }

    /// <summary>Список часовых поясов для выбора (пусто — fallback на UTC±N).</summary>
    public static IReadOnlyList<TimeZoneInfo> GetZones()
    {
        try
        {
            return TimeZoneInfo.GetSystemTimeZones();
        }
        catch
        {
            return Array.Empty<TimeZoneInfo>();
        }
    }

    /// <summary>NTP-запрос: расхождение локальных часов с эталоном.</summary>
    public static async Task<(bool Ok, TimeSpan Offset, string Message)> QueryNtpAsync(string host = "pool.ntp.org")
    {
        try
        {
            var request = new byte[48];
            request[0] = 0x1B; // LI=0, VN=3, Mode=3 (client)

            using var udp = new UdpClient();
            udp.Connect(host, 123);
            var t1 = DateTime.UtcNow;
            await udp.SendAsync(request, request.Length);
            var result = await udp.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
            var t4 = DateTime.UtcNow;

            // Transmit Timestamp: байты 40..47, секунды с 1900-01-01, big-endian
            var data = result.Buffer;
            if (data.Length < 48)
                return (false, TimeSpan.Zero, "Короткий ответ NTP-сервера.");

            ulong secs = ((ulong)data[40] << 24) | ((ulong)data[41] << 16) | ((ulong)data[42] << 8) | data[43];
            ulong frac = ((ulong)data[44] << 24) | ((ulong)data[45] << 16) | ((ulong)data[46] << 8) | data[47];
            var serverTime = new DateTime(1900, 1, 1, 0, 0, 0, DateTimeKind.Utc)
                .AddSeconds(secs)
                .AddTicks((long)(frac * 10_000_000 / 0x1_0000_0000));

            // offset ≈ serverTime − t4 (+ пол-RTT уже не выделишь без t2/t3; для показа хватает)
            var rtt = t4 - t1;
            var offset = serverTime - t4 + TimeSpan.FromTicks(rtt.Ticks / 2);
            return (true, offset,
                $"расхождение с NTP: {(offset >= TimeSpan.Zero ? "+" : "−")}{Math.Abs(offset.TotalSeconds):0.00} с (RTT {rtt.TotalMilliseconds:0} мс)");
        }
        catch (Exception ex)
        {
            return (false, TimeSpan.Zero, "NTP недоступен: " + ex.Message);
        }
    }
}
