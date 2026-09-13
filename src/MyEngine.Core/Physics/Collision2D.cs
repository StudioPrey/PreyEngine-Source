using Microsoft.Xna.Framework;
using MyEngine.Core.ECS;

namespace MyEngine.Core.Physics;

/// <summary>
/// Describes the other object involved in a non-trigger collision — passed to OnCollisionEnter2D/Stay2D/
/// Exit2D. Mirrors Unity's Collision2D, simplified to a single averaged contact point + normal rather than
/// a full per-manifold contact array, which is plenty for the gameplay code these callbacks are typically
/// used for (impact sounds/particles, damage, knockback).
/// </summary>
public sealed class Collision2D
{
    /// <summary>The other object's collider.</summary>
    public Collider2D Collider { get; }

    /// <summary>Shortcut for Collider.Owner.</summary>
    public GameObject GameObject => Collider.Owner;

    /// <summary>The other object's Rigidbody2D, or null if it's an implicit-Static collider with none.</summary>
    public Rigidbody2D? Rigidbody { get; }

    /// <summary>World-space contact normal, pointing away from this object and toward the other one.</summary>
    public Vector2 Normal { get; }

    /// <summary>An approximate world-space contact point (averaged if the collision has more than one).</summary>
    public Vector2 ContactPoint { get; }

    /// <summary>This object's velocity relative to the other object's, at the moment of collision.</summary>
    public Vector2 RelativeVelocity { get; }

    internal Collision2D(Collider2D collider, Vector2 normal, Vector2 contactPoint, Vector2 relativeVelocity)
    {
        Collider = collider;
        Rigidbody = collider.Owner.GetComponent<Rigidbody2D>();
        Normal = normal;
        ContactPoint = contactPoint;
        RelativeVelocity = relativeVelocity;
    }
}
