namespace MyEngine.Launcher.Models;

/// <summary>The launcher's own preferences. Persisted to launcher-settings.json next to projects.json.</summary>
public sealed class LauncherSettings
{
    /// <summary>"Dark" or "Light".</summary>
    public string Theme { get; set; } = "Dark";

    /// <summary>Where the New project wizard starts. Empty = Documents\MyEngine Projects.</summary>
    public string DefaultProjectLocation { get; set; } = "";

    /// <summary>Minimise the launcher once an editor window has opened.</summary>
    public bool MinimizeOnOpen { get; set; }
}
