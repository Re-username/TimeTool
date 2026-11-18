using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Reflection;

// Однофайловый лаунчер TimeTool: извлекает встроенный zip в %LOCALAPPDATA%\TimeTool\App
// (один раз на версию) и запускает приложение. Требует только .NET Framework 4.7.2+ (встроен в Windows 10/11).
namespace SfxStub;

internal static class Program
{
    private const string Version = "1.4.5";

    [STAThread]
    private static void Main()
    {
        try
        {
            var root = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "TimeTool", "App");
            var marker = Path.Combine(root, "version.txt");

            if (!File.Exists(marker) || File.ReadAllText(marker).Trim() != Version)
            {
                Extract(root);
                Directory.CreateDirectory(root);
                File.WriteAllText(marker, Version);
            }

            var app = Path.Combine(root, "TimeTool.exe");
            Process.Start(new ProcessStartInfo(app) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log(ex);
            System.Windows.Forms.MessageBox.Show(
                "Не удалось запустить TimeTool:\n" + ex.Message,
                "TimeTool", System.Windows.Forms.MessageBoxButtons.OK,
                System.Windows.Forms.MessageBoxIcon.Error);
        }
    }

    private static void Log(Exception ex)
    {
        try
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TimeTool");
            Directory.CreateDirectory(dir);
            File.AppendAllText(Path.Combine(dir, "sfx.log"),
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {ex}{Environment.NewLine}");
        }
        catch { }
    }

    private static void Extract(string root)
    {
        var asm = Assembly.GetExecutingAssembly();
        using var rs = asm.GetManifestResourceStream("app.zip")
            ?? throw new InvalidOperationException("app.zip не найден в ресурсах");
        using var zip = new ZipArchive(rs, ZipArchiveMode.Read);

        foreach (var entry in zip.Entries)
        {
            if (string.IsNullOrEmpty(entry.Name))
                continue; // каталог
            var dest = Path.GetFullPath(Path.Combine(root, entry.FullName.Replace('/', Path.DirectorySeparatorChar)));
            if (!dest.StartsWith(Path.GetFullPath(root), StringComparison.OrdinalIgnoreCase))
                continue; // защита от zip-slip
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            entry.ExtractToFile(dest, overwrite: true);
        }
    }
}
