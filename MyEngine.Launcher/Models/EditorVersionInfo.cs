using System.Text.Json.Serialization;

namespace MyEngine.Launcher.Models;

/// <summary>
/// One installed build of the Editor. Points at a *folder* (containing MyEngine.Editor.exe and all its
/// dependencies) rather than a single exe path — so shipping a new Editor version later is just "copy
/// the new build's output into this folder" (or add a new EditorVersionInfo pointing at a new folder),
/// no re-linking needed anywhere else.
/// </summary>
public sealed class EditorVersionInfo
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Label { get; set; } = "Untitled Version";
    public string FolderPath { get; set; } = "";
    public bool IsDefault { get; set; }
    public DateTime AddedAt { get; set; } = DateTime.Now;

    private static string ExecutableFileName =>
        OperatingSystem.IsWindows() ? "MyEngine.Editor.exe" : "MyEngine.Editor";

    [JsonIgnore]
    public string ExecutablePath => System.IO.Path.Combine(FolderPath, ExecutableFileName);

    [JsonIgnore]
    public bool IsValid => System.IO.File.Exists(ExecutablePath);
}
