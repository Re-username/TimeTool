using System.Reflection;
using System.Text.Json;

namespace TimeTool.Services;

/// <summary>
/// Проверка обновлений на GitHub (releases/latest). Не блокирует запуск:
/// вызывать CheckAsync fire-and-forget, результат — через Changed.
/// </summary>
public sealed class UpdateChecker
{
    private const string LatestReleaseApi = "https://api.github.com/repos/Re-username/TimeTool/releases/latest";
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(6) };

    public event Action? Changed;

    public string? LatestVersion { get; private set; }
    public string? LatestUrl { get; private set; }
    public bool CheckFinished { get; private set; }

    /// <summary>Текущая версия приложения (из AssemblyInformationalVersion — бампить в csproj).</summary>
    public static string CurrentVersion =>
        typeof(UpdateChecker).Assembly
            .GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>()?
            .InformationalVersion?.Split('+')[0] ?? "dev";

    public bool HasUpdate =>
        CheckFinished && LatestVersion is not null &&
        TryParseVersion(LatestVersion, out var latest) &&
        TryParseVersion(CurrentVersion, out var current) &&
        latest > current;

    public async Task CheckAsync()
    {
        if (CheckFinished)
            return;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, LatestReleaseApi);
            request.Headers.UserAgent.ParseAdd("TimeTool-UpdateChecker");
            using var response = await Http.SendAsync(request);
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("tag_name", out var tag))
                    LatestVersion = tag.GetString()?.TrimStart('v', 'V');
                if (doc.RootElement.TryGetProperty("html_url", out var url))
                    LatestUrl = url.GetString();
            }
        }
        catch { }
        finally
        {
            CheckFinished = true;
            Changed?.Invoke();
        }
    }

    private static bool TryParseVersion(string s, out Version v)
    {
        v = new Version(0, 0);
        var parts = s.Split('.');
        if (parts.Length < 2 || !int.TryParse(parts[0], out var major) || !int.TryParse(parts[1], out var minor))
            return false;
        var build = 0;
        if (parts.Length > 2)
            int.TryParse(parts[2], out build);
        v = new Version(major, minor, build);
        return true;
    }
}
