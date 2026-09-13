using Microsoft.Xna.Framework.Graphics;

namespace MyEngine.Core.Rendering;

/// <summary>
/// Holds the objects a Component needs to draw/resolve assets for itself this frame.
/// The host (Editor viewport or standalone Runtime) sets this once at startup before the first Scene.Draw.
/// </summary>
public static class RenderContext
{
    public static GraphicsDevice GraphicsDevice { get; private set; } = null!;

    /// <summary>The active 2D renderer backend — MonoGame-backed today (MonoGameRenderer2D). Swap what's
    /// assigned here for a different implementation of IRenderer2D in the future without touching
    /// SpriteRenderer, SpriteAnimation, or either host's Draw loop; they only ever see this through the
    /// interface. See IRenderer2D's doc comment for exactly what is and isn't abstracted.</summary>
    public static IRenderer2D Renderer2D { get; private set; } = null!;

    /// <summary>Resolves a project-relative Assets path (e.g. "Sprites/player.png") to a loaded Texture2D,
    /// or null if it can't be found/loaded. Core doesn't know how to read an image file off disk — see
    /// SpriteRenderer.TexturePath's doc comment — so whichever host is running wires this up once at
    /// startup: the Editor points it at AssetDatabase.GetTexture (which already tracks/caches every project
    /// texture), the standalone Runtime points it at RuntimeAssets.ResolveTexture (which loads + caches on demand).
    /// SpriteAnimation is the only thing that currently calls this directly — needed because it swaps
    /// TexturePath at animation-frame rate, faster than either host's own once-per-load or once-per-frame
    /// texture resync would ever pick up.</summary>
    public static Func<string?, Texture2D?>? TextureResolver { get; set; }

    /// <summary>Escape hatch for editor-only overlays (viewport grid, gizmos) that intentionally still draw
    /// with a raw SpriteBatch rather than through IRenderer2D — see MonoGameRenderer2D.SpriteBatch's doc
    /// comment. Game-content code (SpriteRenderer, SpriteAnimation) must use Renderer2D instead, never this.</summary>
    public static SpriteBatch SpriteBatch => ((MonoGameRenderer2D)Renderer2D).SpriteBatch;

    /// <summary>Same escape hatch as SpriteBatch above, for the shared 1x1 white pixel.</summary>
    public static Texture2D Pixel => ((MonoGameRenderer2D)Renderer2D).Pixel;

    private static bool _initialized;

    public static void Initialize(GraphicsDevice graphicsDevice)
    {
        if (_initialized) return;
        GraphicsDevice = graphicsDevice;
        Renderer2D = new MonoGameRenderer2D(graphicsDevice);
        _initialized = true;
    }
}
