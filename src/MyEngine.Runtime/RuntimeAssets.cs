using Microsoft.Xna.Framework.Graphics;
using MyEngine.Core.Components;
using MyEngine.Core.SceneSystem;

namespace MyEngine.Runtime;

/// <summary>
/// A deliberately tiny counterpart to the Editor's AssetDatabase: the Editor's version tracks every asset
/// in the project, generates thumbnails, and binds textures for ImGui — none of which a shipped game
/// needs. This just walks the loaded scene once and loads the specific textures it actually references,
/// straight from the shipped Assets/ folder with Texture2D.FromStream (no content pipeline / .xnb
/// involved, so there's nothing extra to build or ship beyond the raw PNGs already sitting in Assets/).
/// </summary>
internal static class RuntimeAssets
{
    public static void ResolveSceneTextures(Scene scene, string assetsRoot, GraphicsDevice graphicsDevice)
    {
        var cache = new Dictionary<string, Texture2D>();

        foreach (var go in scene.GameObjects)
        {
            foreach (var sr in go.GetComponents<SpriteRenderer>())
            {
                if (string.IsNullOrEmpty(sr.TexturePath)) continue;
                sr.Texture = LoadCached(sr.TexturePath, assetsRoot, graphicsDevice, cache);
            }
        }
    }

    private static Texture2D? LoadCached(string relativePath, string assetsRoot, GraphicsDevice graphicsDevice, Dictionary<string, Texture2D> cache)
    {
        if (cache.TryGetValue(relativePath, out var cached)) return cached;

        var fullPath = Path.Combine(assetsRoot, relativePath);
        if (!File.Exists(fullPath)) return null; // missing asset — leave the sprite blank rather than crash the game

        using var stream = File.OpenRead(fullPath);
        var texture = Texture2D.FromStream(graphicsDevice, stream);
        cache[relativePath] = texture;
        return texture;
    }
}
