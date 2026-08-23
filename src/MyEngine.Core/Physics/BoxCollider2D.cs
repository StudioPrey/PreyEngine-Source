using Microsoft.Xna.Framework;

namespace MyEngine.Core.Physics;

/// <summary>
/// A rectangular collision shape, aligned with the GameObject's rotated Transform (an oriented box, not
/// just an AABB). Size is in the same local/unscaled units as SpriteRenderer.Size — the world-space
/// footprint is Size * Transform.Scale, so a collider sized to match its sprite stays matched even if the
/// GameObject is later rescaled.
/// </summary>
public sealed class BoxCollider2D : Collider2D
{
    /// <summary>Local, unscaled width/height. Defaults to a common sprite size so a freshly-added box
    /// roughly matches a default SpriteRenderer without any tweaking.</summary>
    public Vector2 Size { get; set; } = new(64f, 64f);
}
