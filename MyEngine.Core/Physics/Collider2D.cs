using Microsoft.Xna.Framework;
using MyEngine.Core.ECS;

namespace MyEngine.Core.Physics;

/// <summary>
/// Base class for a 2D collision shape. A GameObject with a Collider2D but no <see cref="Rigidbody2D"/>
/// behaves as an implicit Static body — exactly like Unity's "static collider" convention, and equivalent
/// to Godot's StaticBody2D + CollisionShape2D pair. Add a Rigidbody2D alongside it once you want the
/// GameObject to move or receive forces.
///
/// This class only ever holds plain data — the actual shape (box, circle, ...) lives on the concrete
/// subclass, and the physics backend reads it directly by pattern-matching the concrete type, the same way
/// SceneSerializer switches on concrete component types rather than asking components to serialize themselves.
/// </summary>
public abstract class Collider2D : Component
{
    /// <summary>Local-space offset from the GameObject's origin, rotated and scaled along with the
    /// Transform — the same way a child GameObject's local position would be.</summary>
    public Vector2 Offset { get; set; } = Vector2.Zero;

    /// <summary>When true, this collider detects overlaps (OnTriggerEnter2D/Stay2D/Exit2D) but never
    /// physically pushes anything — Unity's IsTrigger, equivalent to routing it through Godot's Area2D.</summary>
    public bool IsTrigger { get; set; }

    /// <summary>Surface friction used when resolving a collision against another collider. The pair's
    /// effective friction is the two colliders' values multiplied together.</summary>
    public float Friction { get; set; } = 0.4f;

    /// <summary>Bounciness: 0 absorbs all relative velocity along the contact normal, 1 conserves it
    /// (perfectly elastic). The pair's effective restitution is the larger of the two colliders' values.</summary>
    public float Restitution { get; set; }

    /// <summary>Mass per unit area, used only when the owning Rigidbody2D needs to distribute an explicit
    /// total mass across several colliders on the same GameObject (see Rigidbody2D.Mass).</summary>
    public float Density { get; set; } = 1f;

    public override void OnAddedToScene() => Owner.Scene!.Physics.RegisterCollider(this);

    public override void OnRemovedFromScene() => Owner.Scene!.Physics.UnregisterCollider(this);
}
