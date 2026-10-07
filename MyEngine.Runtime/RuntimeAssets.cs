using Microsoft.Xna.Framework.Audio;
using Microsoft.Xna.Framework.Graphics;
using MyEngine.Core.Audio;
using MyEngine.Core.Components;
using MyEngine.Core.SceneSystem;

namespace MyEngine.Runtime;

/// <summary>
/// A deliberately tiny counterpart to the Editor's AssetDatabase: the Editor's version tracks every asset
/// in the project, generates thumbnails, and binds textures for ImGui — none of which a shipped game needs.
/// This just loads the specific assets actually referenced, straight from the shipped Assets/ folder with
/// Texture2D.FromStream / SoundEffect.FromStream (no content pipeline / .xnb involved, so there's nothing
/// extra to build or ship beyond the raw PNGs/WAVs already sitting in Assets/), and keeps persistent caches
/// so repeated lookups of the same path — including the many-times-a-second lookups a playing
/// SpriteAnimation makes for textures — are free after the first.
/// </summary>
internal static class RuntimeAssets
{
    private static string _assetsRoot = "";
    private static GraphicsDevice _graphicsDevice = null!;
    private static readonly Dictionary<string, Texture2D> TextureCache = new();
    private static readonly Dictionary<string, SoundEffect> SoundCache = new();

    /// <summary>Call once at startup, before Resolve/ResolveSceneAssets. Mirrors RenderContext.Initialize's
    /// shape deliberately — both are "host hands Core-facing statics what they need, once" setup calls.</summary>
    public static void Initialize(string assetsRoot, GraphicsDevice graphicsDevice)
    {
        _assetsRoot = assetsRoot;
        _graphicsDevice = graphicsDevice;
    }

    /// <summary>Resolves every SpriteRenderer.TexturePath and AudioSource.ClipPath in the scene, once, right
    /// after loading it. Anything resolved afterward — most notably SpriteAnimation swapping frames — goes
    /// through RenderContext.TextureResolver/AudioContext.ClipResolver instead, which RuntimeGame points at
    /// ResolveTexture/ResolveSound below; all three share the same caches, so there's no duplicate loading
    /// either way. Named generically (not ResolveSceneTextures) since it covers every asset-referencing
    /// component type — mirrors AssetDatabase.ResolveSceneAssets on the Editor side exactly.</summary>
    public static void ResolveSceneAssets(Scene scene)
    {
        foreach (var go in scene.GameObjects)
        {
            foreach (var sr in go.GetComponents<SpriteRenderer>())
                if (!string.IsNullOrEmpty(sr.TexturePath))
                    sr.Texture = ResolveTexture(sr.TexturePath);
            foreach (var audio in go.GetComponents<AudioSource>())
                if (!string.IsNullOrEmpty(audio.ClipPath))
                    audio.Clip = ResolveSound(audio.ClipPath);
        }
    }

    /// <summary>Resolves one project-relative Assets path to a Texture2D, loading and caching it on first
    /// use. Returns null (never throws) for a missing/unreadable file, so one bad asset reference can't
    /// crash the whole game — the sprite referencing it just stays blank.</summary>
    public static Texture2D? ResolveTexture(string? relativePath)
    {
        if (string.IsNullOrEmpty(relativePath)) return null;
        if (TextureCache.TryGetValue(relativePath, out var cached)) return cached;

        var fullPath = Path.Combine(_assetsRoot, relativePath);
        if (!File.Exists(fullPath)) return null;

        using var stream = File.OpenRead(fullPath);
        var texture = Texture2D.FromStream(_graphicsDevice, stream);
        TextureCache[relativePath] = texture;
        return texture;
    }

    /// <summary>Same as ResolveTexture, for WAV audio clips. Returns null (never throws) for a missing/
    /// unreadable file — the AudioSource referencing it just stays silent instead of crashing the game.</summary>
    public static SoundEffect? ResolveSound(string? relativePath)
    {
        if (string.IsNullOrEmpty(relativePath)) return null;
        if (SoundCache.TryGetValue(relativePath, out var cached)) return cached;

        var fullPath = Path.Combine(_assetsRoot, relativePath);
        if (!File.Exists(fullPath)) return null;

        using var stream = File.OpenRead(fullPath);
        var sound = SoundEffect.FromStream(stream);
        SoundCache[relativePath] = sound;
        return sound;
    }
}
