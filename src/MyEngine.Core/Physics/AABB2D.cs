using Microsoft.Xna.Framework;

namespace MyEngine.Core.Physics;

/// <summary>
/// A world-space axis-aligned bounding box. Used for the broadphase overlap test (a cheap first pass
/// before the exact narrowphase shape test) and to expose collider bounds to the editor for gizmo drawing.
/// </summary>
public readonly struct AABB2D
{
    public Vector2 Min { get; }
    public Vector2 Max { get; }

    public AABB2D(Vector2 min, Vector2 max)
    {
        Min = min;
        Max = max;
    }

    public Vector2 Center => (Min + Max) * 0.5f;
    public Vector2 Size => Max - Min;

    public bool Overlaps(in AABB2D other) =>
        Min.X <= other.Max.X && Max.X >= other.Min.X &&
        Min.Y <= other.Max.Y && Max.Y >= other.Min.Y;

    public bool Contains(Vector2 point) =>
        point.X >= Min.X && point.X <= Max.X &&
        point.Y >= Min.Y && point.Y <= Max.Y;

    /// <summary>Returns a copy of this box grown outward by <paramref name="margin"/> on every side —
    /// used for the broadphase pass so fast-moving shapes are less likely to skip past each other between
    /// AABB updates.</summary>
    public AABB2D Expanded(float margin) =>
        new(new Vector2(Min.X - margin, Min.Y - margin), new Vector2(Max.X + margin, Max.Y + margin));

    public static AABB2D FromCenterHalfExtents(Vector2 center, Vector2 halfExtents) =>
        new(center - halfExtents, center + halfExtents);
}
