using System.Text.Json;

namespace MyEngine.Editor;

/// <summary>
/// Small per-project editor state that isn't part of the game itself — stored at the project root
/// (sibling to Assets/), not inside Assets/, since it's editor/build bookkeeping rather than a game asset
/// a build should ship. This covers two things: which scene was open last (pure editor convenience), and
/// the Build Settings a "File > Build" uses to produce a standalone game (name, version, icon, window size).
/// </summary>
public sealed class ProjectSettings
{
    /// <summary>Path to the last-opened scene, relative to the project root (e.g. "Assets/Scenes/Main.scene"). Null if none yet.</summary>
    public string? LastOpenedScenePath { get; set; }

    // ---------------------------------------------------------------- build settings

    public string GameName { get; set; } = "MyGame";
    public string GameVersion { get; set; } = "1.0.0";

    /// <summary>Path to a custom .ico file, relative to the project root. Empty means "use MyEngine's
    /// bundled default icon" rather than pointing at a file that may not exist.</summary>
    public string IconPath { get; set; } = "";

    /// <summary>Scene to boot into, relative to Assets/ (e.g. "Scenes/Main.scene"). Empty means "use
    /// whichever scene is open when Build is run" — resolved at build time, not stored as a stale default.</summary>
    public string BootScenePath { get; set; } = "";

    public int WindowWidth { get; set; } = 1280;
    public int WindowHeight { get; set; } = 720;
    public bool Fullscreen { get; set; }

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
