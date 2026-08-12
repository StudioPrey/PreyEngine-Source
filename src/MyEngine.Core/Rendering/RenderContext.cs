using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace MyEngine.Core.Rendering;

/// <summary>
/// Holds the objects a Component needs to draw itself this frame.
/// The host (Editor viewport or standalone Runtime) sets this once per frame before calling Scene.Draw.
/// </summary>
public static class RenderContext
{
    public static GraphicsDevice GraphicsDevice { get; private set; } = null!;
    public static SpriteBatch SpriteBatch { get; private set; } = null!;

    /// <summary>A 1x1 white pixel, handy for drawing untextured colored rectangles (placeholder sprites).</summary>
    public static Texture2D Pixel { get; private set; } = null!;

    private static bool _initialized;

    public static void Initialize(GraphicsDevice graphicsDevice)
    {
        if (_initialized) return;
        GraphicsDevice = graphicsDevice;
        SpriteBatch = new SpriteBatch(graphicsDevice);
        Pixel = new Texture2D(graphicsDevice, 1, 1);
        Pixel.SetData(new[] { Color.White });
        _initialized = true;
    }

    public static void BeginFrame() { }
}
