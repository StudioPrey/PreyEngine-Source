namespace MyEngine.Launcher.Models;

/// <summary>One entry in the "recent projects" list. Persisted to projects.json.</summary>
public sealed class ProjectInfo
{
    public string Name { get; set; } = "New Project";
    public string Path { get; set; } = "";
    public DateTime LastOpened { get; set; } = DateTime.Now;

    /// <summary>
    /// Which installed Editor version this project should open with (see EditorVersionInfo.Id).
    /// Null means "use whatever is currently marked Default" — this is what every project created
    /// before version-pinning existed will have, and it's also the sensible default for new projects.
    /// </summary>
    public string? EditorVersionId { get; set; }
}
