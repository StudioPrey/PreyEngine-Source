using Microsoft.Xna.Framework;

namespace MyEngine.Core.Physics;

/// <summary>
/// A handful of 2D vector operations MonoGame's Vector2 doesn't provide directly, used throughout the
/// collision/solver code. Kept separate from CollisionMath2D so the "pure vector algebra" stays easy to
/// scan independently of the shape-specific geometry that builds on top of it.
/// </summary>
internal static class Vec2
{
    /// <summary>2D "cross product" of two vectors — really the Z component of their 3D cross product.
    /// Positive when <paramref name="b"/> is counter-clockwise from <paramref name="a"/>.</summary>
    public static float Cross(Vector2 a, Vector2 b) => a.X * b.Y - a.Y * b.X;

    /// <summary>Cross product of a scalar (angular velocity) and a vector (position offset) — the linear
    /// velocity contribution of a rotation, as used when integrating torque and applying impulses at a point.</summary>
    public static Vector2 Cross(float scalar, Vector2 v) => new(-scalar * v.Y, scalar * v.X);

    /// <summary>Rotates <paramref name="v"/> by <paramref name="radians"/> using the same rotation
    /// direction as Transform (Matrix.CreateRotationZ / Transform.Forward): positive angles turn +X toward +Y.</summary>
    public static Vector2 Rotate(Vector2 v, float radians)
    {
        float cos = MathF.Cos(radians);
        float sin = MathF.Sin(radians);
        return new Vector2(v.X * cos - v.Y * sin, v.X * sin + v.Y * cos);
    }
}
