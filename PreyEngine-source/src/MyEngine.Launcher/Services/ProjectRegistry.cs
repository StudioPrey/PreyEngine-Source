using System.Text.Json;
using MyEngine.Launcher.Models;

namespace MyEngine.Launcher.Services;

public sealed class ProjectRegistry
{
    private static readonly string RegistryPath = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "MyEngine", "projects.json");

    public List<ProjectInfo> Projects { get; private set; } = new();

    public void Load()
    {
        try
        {
            if (File.Exists(RegistryPath))
            {
                var json = File.ReadAllText(RegistryPath);
                Projects = JsonSerializer.Deserialize<List<ProjectInfo>>(json) ?? new List<ProjectInfo>();
            }
        }
        catch
        {
            Projects = new List<ProjectInfo>();
        }
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(RegistryPath)!);
        var json = JsonSerializer.Serialize(Projects, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(RegistryPath, json);
    }

    public ProjectInfo AddOrUpdate(string name, string path, string? editorVersionId)
    {
        var existing = Projects.FirstOrDefault(p => p.Path == path);
        if (existing != null)
        {
            existing.LastOpened = DateTime.Now;
            existing.Name = name;
        }
        else
        {
            existing = new ProjectInfo { Name = name, Path = path, LastOpened = DateTime.Now, EditorVersionId = editorVersionId };
            Projects.Add(existing);
        }
        Save();
        return existing;
    }

    public void Remove(ProjectInfo project)
    {
        Projects.Remove(project);
        Save();
    }
}
