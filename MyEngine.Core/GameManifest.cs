using System.Text.Json;

namespace MyEngine.Core;

/// <summary>
/// Everything a built game needs to know about itself at startup, other than the scene/asset/script data
/// itself: what to call the window, what size to open at, and which scene to boot into. Written once by
/// the Editor's Build process (see MyEngine.Editor's ProjectBuilder) and read once by MyEngine.Runtime on
/// launch — living here in Core rather than in either project individually so both read/write the exact
/// same shape without any risk of the two drifting apart.
/// </summary>
public sealed class GameManifest
{
    public string GameName { get; set; } = "MyGame";
    public string Version { get; set; } = "1.0.0";

    /// <summary>Path to the boot scene, relative to the shipped Assets/ folder (e.g. "Scenes/Main.scene").
    /// v1 is single-scene: this is the only scene a build ever loads. The field is already a path rather
    /// than an index or an embedded scene list specifically so a future multi-scene loader can reuse it
    /// unchanged as "the first scene" while adding its own way to reference the rest.</summary>
    public string BootScenePath { get; set; } = "";

    public int WindowWidth { get; set; } = 1280;
    public int WindowHeight { get; set; } = 720;
    public bool Fullscreen { get; set; }

    /// <summary>File name of the compiled scripts assembly, sitting next to the Runtime executable
    /// (e.g. "GameScripts.dll"). Empty means the project had no scripts to compile.</summary>
    public string ScriptsAssemblyName { get; set; } = "";

    private const string FileName = "game.manifest.json";
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public void Save(string directory) =>
        File.WriteAllText(Path.Combine(directory, FileName), JsonSerializer.Serialize(this, Options));

    public static GameManifest Load(string directory)
    {
        var path = Path.Combine(directory, FileName);
        var manifest = JsonSerializer.Deserialize<GameManifest>(File.ReadAllText(path), Options);
        return manifest ?? throw new InvalidDataException($"'{path}' did not contain a valid game manifest.");
    }
}
