using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MyEngine.Core.ECS;
using MyEngine.Core.Rendering;
using MyEngine.Core.Physics;

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

    // Transient render state: deliberately not part of the public/serialized component contract.
    internal bool IsVisible = true;
    private bool _boundsValid;
    private Vector2 _boundsPosition, _boundsScale, _boundsFrame, _boundsOrigin;
    private float _boundsRotation;
    private bool _boundsTextured;
    private AABB2D _worldBounds;
    internal int BoundsRebuildCount { get; private set; }

    public override void OnAddedToScene()
    {
        _boundsValid = false;
        Owner.Scene!.SpriteVisibility.Register(this);
    }

    public override void OnRemovedFromScene()
    {
        Owner.Scene!.SpriteVisibility.Unregister(this);
        _boundsValid = false;
    }

    private Vector2 GetLocalFrameSize() => Texture != null
        ? new Vector2(SourceRect?.Width ?? Texture.Width, SourceRect?.Height ?? Texture.Height)
        : Size;

    /// <summary>Caches actual rendered geometry, not Transform.WorldMatrix. The renderer uses world
    /// position, summed rotation and multiplied scale; composing hierarchy matrices instead introduces
    /// shear with nonuniform parents, which the existing Draw does not render. Comparing live inputs
    /// detects parent edits/reparenting without changing Transform or exposing a new dirty API.</summary>
    internal AABB2D ComputeWorldBounds()
        => ComputeWorldBounds(Transform.Position, Transform.Rotation, Transform.Scale);

    private AABB2D ComputeWorldBounds(Vector2 position, float rotation, Vector2 scale)
    {
        var frame = GetLocalFrameSize();
        bool textured = Texture != null;
        if (_boundsValid && position == _boundsPosition && rotation == _boundsRotation &&
            scale == _boundsScale && frame == _boundsFrame && Origin == _boundsOrigin && textured == _boundsTextured)
            return _worldBounds;

        _boundsPosition = position; _boundsRotation = rotation; _boundsScale = scale;
        _boundsFrame = frame; _boundsOrigin = Origin; _boundsTextured = textured;
        _boundsValid = true;
        BoundsRebuildCount++;

        var size = frame * scale;
        // Preserve SpriteBatch's arithmetic order in both paths: textured origins are in frame pixels,
        // while a placeholder uses a 1x1 pixel scaled by Size * scale. Effects only flip UVs.
        var offset = textured ? (frame * Origin) * scale : Origin * size;
        float x = -offset.X, y = -offset.Y;
        float sin = MathF.Sin(rotation), cos = MathF.Cos(rotation);
        Vector2 Corner(float cx, float cy) => new(position.X + cx * cos - cy * sin,
            position.Y + cx * sin + cy * cos);
        var c0 = Corner(x, y); var c1 = Corner(x + size.X, y);
        var c2 = Corner(x, y + size.Y); var c3 = Corner(x + size.X, y + size.Y);
        _worldBounds = !AABB2D.IsFinite(c0) || !AABB2D.IsFinite(c1) || !AABB2D.IsFinite(c2) || !AABB2D.IsFinite(c3)
            ? AABB2D.Unbounded
            : new AABB2D(Vector2.Min(Vector2.Min(c0, c1), Vector2.Min(c2, c3)),
                Vector2.Max(Vector2.Max(c0, c1), Vector2.Max(c2, c3)));
        return _worldBounds;
    }

    public override void Draw(GameTime gameTime)
    {
        var position = Transform.Position;
        var rotation = Transform.Rotation;
        var scale = Transform.Scale;

        // A preceding custom Draw may have moved/enabled this sprite since the scene's refresh.
        // Revalidate the cheap cache key before rejecting it; no bounds/trigonometry is rebuilt
        // for unchanged geometry. Direct Draw and the legacy scene overload always fail open.
        var bounds = Owner.Scene?.SpriteVisibility.CurrentBounds;
        IsVisible = bounds == null || ComputeWorldBounds(position, rotation, scale).Overlaps(bounds.Value);
        if (!IsVisible) return;

        // SpriteBatch is started with SpriteSortMode.BackToFront in the host, which draws high
        // layerDepth first (background) and low layerDepth last (front). Map SortingOrder onto
        // that so a higher SortingOrder visually ends up on top.
        float layerDepth = MathHelper.Clamp(0.5f - SortingOrder * 0.0005f, 0f, 1f);

        if (Texture != null)
        {
            // Frame size, not sheet size: with SourceRect set (an animated sprite showing one frame of a
            // larger sheet), Texture.Width/Height is the *whole sheet* — using that here would compute a
            // wildly wrong pivot the instant sprite-sheet animation is in play.
            var frame = GetLocalFrameSize();
            var origin = new Vector2(frame.X * Origin.X, frame.Y * Origin.Y);
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
