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

    /// <summary>Conservative world bounds of the viewport, using the same matrix as BeginScene.
    /// A rotated view includes some extra space: keeping an extra sprite is preferable to hiding a
    /// visible one. Invalid/singular views fail open; they must never silently cull the entire scene.</summary>
    public static AABB2D FromViewMatrix(Matrix viewMatrix, Vector2 viewportSize)
    {
        if (!float.IsFinite(viewportSize.X) || !float.IsFinite(viewportSize.Y) ||
            viewportSize.X <= 0f || viewportSize.Y <= 0f)
            return Unbounded;

        float determinant2D = viewMatrix.M11 * viewMatrix.M22 - viewMatrix.M12 * viewMatrix.M21;
        if (!float.IsFinite(determinant2D) || determinant2D == 0f) return Unbounded;

        var inverse = Matrix.Invert(viewMatrix);
        Vector2 c0 = Vector2.Transform(Vector2.Zero, inverse);
        Vector2 c1 = Vector2.Transform(new Vector2(viewportSize.X, 0f), inverse);
        Vector2 c2 = Vector2.Transform(new Vector2(0f, viewportSize.Y), inverse);
        Vector2 c3 = Vector2.Transform(viewportSize, inverse);
        if (!IsFinite(c0) || !IsFinite(c1) || !IsFinite(c2) || !IsFinite(c3)) return Unbounded;
        return new AABB2D(Vector2.Min(Vector2.Min(c0, c1), Vector2.Min(c2, c3)),
            Vector2.Max(Vector2.Max(c0, c1), Vector2.Max(c2, c3)));
    }

    internal static AABB2D Unbounded => new(new Vector2(float.NegativeInfinity), new Vector2(float.PositiveInfinity));
    internal static bool IsFinite(Vector2 value) => float.IsFinite(value.X) && float.IsFinite(value.Y);
}
