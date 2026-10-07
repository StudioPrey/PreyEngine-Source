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

    /// <summary>Wait for the monitor's refresh before presenting each frame (no screen tearing). On by default.</summary>
    public bool VSync { get; set; } = true;

    /// <summary>Let the player drag the window edges to resize it.</summary>
    public bool Resizable { get; set; }

    /// <summary>Frame rate the game loop runs at (update AND draw). 60 matches Play Mode in the Editor, so
    /// physics and timing behave exactly as they did while testing. Read it through
    /// <see cref="NormalizeTargetFps"/> — a hand-edited manifest may hold anything.</summary>
    public int TargetFps { get; set; } = DefaultTargetFps;

    public const int DefaultTargetFps = 60;
    public const int MinTargetFps = 15;
    public const int MaxTargetFps = 360;

    /// <summary>A usable frame rate for any input: values below 1 (missing/invalid) become
    /// <see cref="DefaultTargetFps"/>, everything else is limited to [<see cref="MinTargetFps"/>,
    /// <see cref="MaxTargetFps"/>]. Shared by the Editor's settings UI and the Runtime so they can't disagree.</summary>
    public static int NormalizeTargetFps(int fps) => fps < 1 ? DefaultTargetFps : Math.Clamp(fps, MinTargetFps, MaxTargetFps);

    /// <summary>Show the PreyEngine splash before the game starts. On by default — see ProjectSettings.</summary>
    public bool ShowEngineSplash { get; set; } = true;

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
