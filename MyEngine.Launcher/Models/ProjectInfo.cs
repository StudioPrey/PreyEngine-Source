namespace MyEngine.Launcher.Models;

/// <summary>One entry in the "recent projects" list. Persisted to projects.json.</summary>
public sealed class ProjectInfo
{
    public string Name { get; set; } = "New Project";
    public string Path { get; set; } = "";
    public DateTime LastOpened { get; set; } = DateTime.Now;

    /// <summary>
    /// When the project folder was created, captured the first time the launcher registers the project.
    /// Null for projects registered before this field existed — the Projects page then falls back to the
    /// folder's own creation time.
    /// </summary>
    public DateTime? CreatedAt { get; set; }

    /// <summary>Pinned projects always sort above the rest of the list.</summary>
    public bool IsPinned { get; set; }

    /// <summary>
    /// Which installed Editor version this project should open with (see EditorVersionInfo.Id).
    /// Null means "use whatever is currently marked Default" — this is what every project created
    /// before version-pinning existed will have, and it's also the sensible default for new projects.
    /// </summary>
    public string? EditorVersionId { get; set; }
}
