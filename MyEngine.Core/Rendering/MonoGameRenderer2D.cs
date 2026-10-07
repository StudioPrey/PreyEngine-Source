using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace MyEngine.Core.Rendering;

/// <summary>
/// The engine's only IRenderer2D implementation today: a thin, direct wrapper around a MonoGame SpriteBatch.
/// BeginScene hardcodes the same Begin() parameters both EditorApp and RuntimeGame already used identically
/// before this abstraction existed (SpriteSortMode.BackToFront so SpriteRenderer.SortingOrder keeps working,
/// BlendState.AlphaBlend, SamplerState.PointClamp for crisp pixel art) — behavior for every existing caller
/// is unchanged, only where the SpriteBatch calls live has moved. The sampler specifically is a default, not
/// a hardcoded constant: a caller batching non-pixel-art content (see IRenderer2D.BeginScene) can override it
/// per-call without this class needing to know anything about who's asking or why.
/// </summary>
public sealed class MonoGameRenderer2D : IRenderer2D
{
    private readonly SpriteBatch _spriteBatch;
    private readonly Texture2D _pixel;

    public MonoGameRenderer2D(GraphicsDevice graphicsDevice)
    {
        _spriteBatch = new SpriteBatch(graphicsDevice);
        _pixel = new Texture2D(graphicsDevice, 1, 1);
        _pixel.SetData(new[] { Color.White });
    }

    public void BeginScene(Matrix viewMatrix, SamplerState? samplerState = null)
    {
        _spriteBatch.Begin(
            sortMode: SpriteSortMode.BackToFront,
            blendState: BlendState.AlphaBlend,
            samplerState: samplerState ?? SamplerState.PointClamp,
            transformMatrix: viewMatrix);
    }

    public void DrawSprite(Texture2D texture, Vector2 position, Rectangle? sourceRect, Color color,
        float rotation, Vector2 origin, Vector2 scale, SpriteEffects effects, float layerDepth)
        => _spriteBatch.Draw(texture, position, sourceRect, color, rotation, origin, scale, effects, layerDepth);

    public void DrawFilledRect(Vector2 position, Color color, float rotation, Vector2 origin, Vector2 size,
        SpriteEffects effects, float layerDepth)
        => _spriteBatch.Draw(_pixel, position, null, color, rotation, origin, size, effects, layerDepth);

    public void EndScene() => _spriteBatch.End();

    /// <summary>Escape hatch for editor-only overlays (viewport grid, gizmos) that intentionally keep
    /// drawing with a raw SpriteBatch/pixel rather than through IRenderer2D — those aren't part of the
    /// swappable game-content render path this interface exists for (they're editor chrome tied to
    /// whatever the Editor itself runs on), so there's no value in routing them through the abstraction.
    /// Exposed here, not on IRenderer2D itself, precisely so nothing that depends only on IRenderer2D can
    /// reach for these. See RenderContext.SpriteBatch/Pixel, which is the only place this is used from.</summary>
    public SpriteBatch SpriteBatch => _spriteBatch;
    public Texture2D Pixel => _pixel;
}
