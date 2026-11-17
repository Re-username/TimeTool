#if WINDOWS
namespace TimeTool.Platforms.Windows;

/// <summary>Нативный выбор файла (проводник) — BlazorWebView не умеет showOpenFilePicker.</summary>
public static class AudioPick
{
    public static async Task<string?> PickAudioAsync()
    {
        try
        {
            var result = await FilePicker.Default.PickAsync(new PickOptions
            {
                PickerTitle = "Выберите звуковой файл",
                FileTypes = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
                {
                    [DevicePlatform.WinUI] = new[] { ".wav", ".mp3", ".ogg" },
                }),
            });
            return result?.FullPath;
        }
        catch
        {
            return null;
        }
    }
}
#endif
