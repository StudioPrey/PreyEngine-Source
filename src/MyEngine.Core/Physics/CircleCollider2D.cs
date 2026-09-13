namespace MyEngine.Core.Physics;

/// <summary>
/// A circular collision shape. World-space radius is Radius * the average of Transform.Scale's X and Y —
/// circles assume roughly-uniform scale, since a non-uniformly-scaled circle is really an ellipse, which
/// this collider doesn't model. For heavily stretched objects, use a BoxCollider2D instead.
/// </summary>
public sealed class CircleCollider2D : Collider2D
{
    /// <summary>Local, unscaled radius.</summary>
    public float Radius { get; set; } = 32f;
}
