namespace MyEngine.Launcher.Services;

/// <summary>
/// Scaffolds a new project's folder structure on disk. This is the exact same layout the previous
/// launcher created — Scenes and Prefabs live inside Assets/, matching the convention the Editor itself
/// expects (see MyEngine.Editor.AssetDatabase) — only moved here unchanged as its own service.
/// </summary>
public static class ProjectFactory
{
    public static string CreateProjectFolders(string parentFolder, string name)
    {
        var projectPath = Path.Combine(parentFolder, name);
        Directory.CreateDirectory(projectPath);
        Directory.CreateDirectory(Path.Combine(projectPath, "Assets"));
        Directory.CreateDirectory(Path.Combine(projectPath, "Assets", "Scenes"));
        Directory.CreateDirectory(Path.Combine(projectPath, "Assets", "Prefabs"));
        return projectPath;
    }
}
