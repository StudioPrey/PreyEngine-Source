using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MyEngine.Core.ECS;
using MyEngine.Core.Rendering;

namespace MyEngine.Core.Components;

/// <summary>
/// Draws a sprite at the owning GameObject's world transform.
/// If Texture is null, draws a solid colored rectangle sized by Size — useful as a placeholder
/// before any art assets exist, and it means the engine never needs to ship a "missing texture" sprite.
/// </summary>
public sealed class SpriteRenderer : Component
{
    public Texture2D? Texture { get; set; }

    /// <summary>
    /// Path to the source image, relative to the project's Assets folder (e.g. "Sprites/player.png").
    /// Core doesn't know how to load images — this is just metadata the scene serializer writes out; the
    /// Editor's AssetDatabase or the standalone Runtime's RuntimeAssets (see RenderContext.TextureResolver)
    /// resolves it back into an actual Texture after loading a scene, or whenever SpriteAnimation swaps it.
    /// </summary>
    public string? TexturePath { get; set; }

    /// <summary>Region of Texture to draw, in pixel coordinates; null draws the whole texture (the only
    /// behavior that existed before sprite-sheet animation). Set by SpriteAnimation to show one frame of a
    /// sheet — a plain, non-animated sprite never needs to touch this. Origin/pivot math in Draw() below
    /// always sizes itself off THIS rect when it's set, not the whole texture, so a single 32x32 frame cut
    /// from a much larger sheet still pivots/positions correctly.</summary>
    public Rectangle? SourceRect { get; set; }

    public Color Color { get; set; } = Color.White;

    /// <summary>Size in world units used only when Texture is null (placeholder rectangle mode).</summary>
    public Vector2 Size { get; set; } = new Vector2(64, 64);

    /// <summary>Pivot, in normalized 0..1 texture space. (0.5, 0.5) = centered.</summary>
    public Vector2 Origin { get; set; } = new Vector2(0.5f, 0.5f);

    /// <summary>Higher values draw on top of lower ones (within the same camera).</summary>
    public int SortingOrder { get; set; } = 0;
    public SpriteEffects Effects { get; set; } = SpriteEffects.None;

    public override void Draw(GameTime gameTime)
    {
        var position = Transform.Position;
        var rotation = Transform.Rotation;
        var scale = Transform.Scale;

        // SpriteBatch is started with SpriteSortMode.BackToFront in the host, which draws high
        // layerDepth first (background) and low layerDepth last (front). Map SortingOrder onto
        // that so a higher SortingOrder visually ends up on top.
        float layerDepth = MathHelper.Clamp(0.5f - SortingOrder * 0.0005f, 0f, 1f);

        if (Texture != null)
        {
            // Frame size, not sheet size: with SourceRect set (an animated sprite showing one frame of a
            // larger sheet), Texture.Width/Height is the *whole sheet* — using that here would compute a
            // wildly wrong pivot the instant sprite-sheet animation is in play.
            int frameWidth = SourceRect?.Width ?? Texture.Width;
            int frameHeight = SourceRect?.Height ?? Texture.Height;
            var origin = new Vector2(frameWidth * Origin.X, frameHeight * Origin.Y);
            RenderContext.Renderer2D.DrawSprite(
                Texture, position, SourceRect, Color, rotation, origin, scale, Effects, layerDepth);
        }
        else
        {
            var size = Size * scale;
            var origin = new Vector2(Origin.X, Origin.Y); // pixel is 1x1, origin is in 0..1 space already
            RenderContext.Renderer2D.DrawFilledRect(
                position, Color, rotation, origin, size, Effects, layerDepth);
        }
    }
}
