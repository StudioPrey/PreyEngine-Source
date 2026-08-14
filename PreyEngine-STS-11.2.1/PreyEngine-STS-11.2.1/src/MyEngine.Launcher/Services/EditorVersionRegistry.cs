using System.Text.Json;
using MyEngine.Launcher.Models;

namespace MyEngine.Launcher.Services;

public sealed class EditorVersionRegistry
{
    private static readonly string RegistryPath = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "MyEngine", "EditorVersions.json");

    public List<EditorVersionInfo> Versions { get; private set; } = new();

    public void Load()
    {
        try
        {
            if (File.Exists(RegistryPath))
            {
                var json = File.ReadAllText(RegistryPath);
                Versions = JsonSerializer.Deserialize<List<EditorVersionInfo>>(json) ?? new List<EditorVersionInfo>();
            }
        }
        catch
        {
            Versions = new List<EditorVersionInfo>();
        }
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(RegistryPath)!);
        var json = JsonSerializer.Serialize(Versions, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(RegistryPath, json);
    }

    public EditorVersionInfo? GetDefault() => Versions.FirstOrDefault(v => v.IsDefault) ?? Versions.FirstOrDefault();

    public EditorVersionInfo? Find(string? id) => id == null ? null : Versions.FirstOrDefault(v => v.Id == id);

    public EditorVersionInfo Add(string label, string folderPath, bool makeDefault = false)
    {
        var version = new EditorVersionInfo { Label = label, FolderPath = folderPath };
        Versions.Add(version);
        if (makeDefault || Versions.Count == 1) SetDefault(version);
        else Save();
        return version;
    }

    public void SetDefault(EditorVersionInfo version)
    {
        foreach (var v in Versions) v.IsDefault = false;
        version.IsDefault = true;
        Save();
    }

    public void Remove(EditorVersionInfo version)
    {
        bool wasDefault = version.IsDefault;
        Versions.Remove(version);
        if (wasDefault && Versions.Count > 0) SetDefault(Versions[0]);
        else Save();
    }

    /// <summary>
    /// If no versions are registered yet (fresh install, or an existing dev checkout of the solution),
    /// look for the Editor's own build output near the Launcher's executable — covers the common
    /// "everything built from the same solution, running from Visual Studio / dotnet run" setup so the
    /// launcher isn't useless before anyone has manually added a version.
    /// </summary>
    public void EnsureDefaultDiscovered()
    {
        if (Versions.Count > 0) return;

        var baseDir = AppContext.BaseDirectory;
        var candidates = new[]
        {
            baseDir,
            Path.Combine(baseDir, "..", "..", "..", "..", "MyEngine.Editor", "bin", "Debug", "net8.0"),
            Path.Combine(baseDir, "..", "..", "..", "..", "MyEngine.Editor", "bin", "Release", "net8.0"),
        };

        string exeName = OperatingSystem.IsWindows() ? "MyEngine.Editor.exe" : "MyEngine.Editor";

        foreach (var candidate in candidates)
        {
            var full = Path.GetFullPath(candidate);
            if (File.Exists(Path.Combine(full, exeName)))
            {
                Add("Default (Dev Build)", full, makeDefault: true);
                return;
            }
        }
    }
}
