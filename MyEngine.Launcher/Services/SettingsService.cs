using System.Text.Json;
using MyEngine.Launcher.Models;

namespace MyEngine.Launcher.Services;

public sealed class SettingsService
{
    private static readonly string SettingsPath = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "MyEngine", "launcher-settings.json");

    public LauncherSettings Current { get; private set; } = new();

    public void Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var json = File.ReadAllText(SettingsPath);
                Current = JsonSerializer.Deserialize<LauncherSettings>(json) ?? new LauncherSettings();
            }
        }
        catch
        {
            // A damaged settings file must never stop the launcher from starting — fall back to defaults.
            Current = new LauncherSettings();
        }
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
            var json = JsonSerializer.Serialize(Current, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(SettingsPath, json);
        }
        catch
        {
            // Preferences are a convenience; failing to write them shouldn't interrupt the user.
        }
    }
}
