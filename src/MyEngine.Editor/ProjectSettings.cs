using System.Text.Json;

namespace MyEngine.Editor;

/// <summary>
/// Small per-project editor state that isn't part of the game itself. Right now that's just "which
/// scene was open last" — stored at the project root (sibling to Assets/), not inside Assets/, since
/// it's editor bookkeeping rather than a game asset a build should ship.
/// </summary>
public sealed class ProjectSettings
{
    /// <summary>Path to the last-opened scene, relative to the project root (e.g. "Assets/Scenes/Main.scene"). Null if none yet.</summary>
    public string? LastOpenedScenePath { get; set; }

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    private static string GetFilePath(string projectPath) => Path.Combine(projectPath, "ProjectSettings.json");

    public static ProjectSettings Load(string projectPath)
    {
        try
        {
            var path = GetFilePath(projectPath);
            if (File.Exists(path))
            {
                var loaded = JsonSerializer.Deserialize<ProjectSettings>(File.ReadAllText(path), Options);
                if (loaded != null) return loaded;
            }
        }
        catch
        {
            // corrupt or unreadable settings file — fall through to a fresh one rather than block startup
        }
        return new ProjectSettings();
    }

    public void Save(string projectPath)
    {
        try
        {
            File.WriteAllText(GetFilePath(projectPath), JsonSerializer.Serialize(this, Options));
        }
        catch
        {
            // best-effort — losing "last opened scene" bookkeeping isn't worth crashing the editor over
        }
    }
}
