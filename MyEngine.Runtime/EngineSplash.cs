using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace MyEngine.Runtime;

/// <summary>
/// The "Made with PreyEngine" splash shown for a couple of seconds when a built game starts, before the game
/// itself runs. The artwork is embedded in Game.dll (Splash/EngineSplash.png, produced by
/// tools/SplashBake/bake_splash.py) rather than shipped as a loose file, so it can't go missing from a build.
///
/// <para>The game is fully loaded BEFORE this is shown — MonoGame's desktop window only becomes visible when the
/// game loop starts, after LoadContent — so nothing loads (or hitches) while it plays, and no game code has run
/// yet: scripts' Awake/Start, physics and audio all begin on the first frame after it finishes.</para>
///
/// <para>It draws with its own SpriteBatch and sets every GPU state it needs in Begin, and the game's renderer
/// does the same in its BeginScene, so neither leaves state behind for the other.</para>
/// </summary>
internal sealed class EngineSplash : IDisposable
{
    /// <summary>The flat color the artwork's edges fade into. MUST match the color printed by
    /// tools/SplashBake/bake_splash.py, or the image's border becomes visible.</summary>
    public static readonly Color BackgroundColor = new(18, 18, 18);

    private const string ResourceName = "EngineSplash.png";

    private readonly SplashTimeline _timeline = new();
    private readonly SpriteBatch _batch;
    private readonly Texture2D _texture;

    private EngineSplash(SpriteBatch batch, Texture2D texture)
    {
        _batch = batch;
        _texture = texture;
    }

    /// <summary>Loads the embedded artwork. Throws if the resource is missing or can't be decoded; the caller
    /// (RuntimeGame) decides that a failed splash must not stop the game, and records the reason.</summary>
    public static EngineSplash Create(GraphicsDevice graphicsDevice)
    {
        using var stream = typeof(EngineSplash).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"The embedded engine splash '{ResourceName}' is missing from this build.");
        return new EngineSplash(new SpriteBatch(graphicsDevice), Texture2D.FromStream(graphicsDevice, stream));
    }

    public bool IsFinished => _timeline.IsFinished;

    public void Update(GameTime gameTime) => _timeline.Advance((float)gameTime.ElapsedGameTime.TotalSeconds);

    public void Draw(GraphicsDevice graphicsDevice)
    {
        graphicsDevice.Clear(BackgroundColor);

        var (x, y, width, height) = SplashLayout.Fit(_texture.Width, _texture.Height,
            graphicsDevice.PresentationParameters.BackBufferWidth, graphicsDevice.PresentationParameters.BackBufferHeight);
        if (width <= 0) return;

        int alpha = Math.Clamp((int)MathF.Round(_timeline.Alpha * 255f), 0, 255);

        // The artwork is opaque, so fading is just the tint's alpha over the cleared background. NonPremultiplied
        // because the tint is straight (255,255,255,alpha), not a premultiplied color.
        _batch.Begin(SpriteSortMode.Deferred, BlendState.NonPremultiplied, SamplerState.LinearClamp,
            DepthStencilState.None, RasterizerState.CullCounterClockwise);
        _batch.Draw(_texture, new Rectangle(x, y, width, height), new Color(255, 255, 255, alpha));
        _batch.End();
    }

    public void Dispose()
    {
        _texture.Dispose();
        _batch.Dispose();
    }
}
