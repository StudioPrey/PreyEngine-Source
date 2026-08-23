using Microsoft.Xna.Framework;
using MyEngine.Core.ECS;

namespace MyEngine.Core.Physics;

/// <summary>
/// Makes a GameObject participate in the physics simulation as a moving body. Add one or more
/// <see cref="Collider2D"/> components alongside it to give it a shape — without any collider, a
/// Rigidbody2D still integrates gravity/velocity but never collides with anything.
///
/// Mirrors Unity's Rigidbody2D API surface (AddForce, MovePosition, ...) since scripts already lean on
/// Unity-familiar naming elsewhere (see Script.cs); the underlying body-type split (Dynamic/Kinematic/
/// Static) is the same three-way distinction Godot expresses as separate node types.
/// </summary>
public sealed class Rigidbody2D : Component
{
    public BodyType2D BodyType { get; set; } = BodyType2D.Dynamic;

    /// <summary>Total mass in arbitrary units. Only meaningful for <see cref="BodyType2D.Dynamic"/> bodies —
    /// ignored (treated as infinite) for Kinematic and Static. When several colliders share this
    /// GameObject, mass is distributed between them in proportion to each collider's area * Density.</summary>
    public float Mass { get; set; } = 1f;

    /// <summary>Multiplies the world's gravity for this body specifically — 0 to ignore gravity entirely,
    /// negative to float upward.</summary>
    public float GravityScale { get; set; } = 1f;

    /// <summary>Fraction of linear velocity lost per second, exponentially — Unity calls this "drag".</summary>
    public float LinearDamping { get; set; }

    /// <summary>Fraction of angular velocity lost per second, exponentially — Unity's "angular drag".</summary>
    public float AngularDamping { get; set; } = 0.05f;

    /// <summary>When true, this body never rotates from physics (collisions, torque) — useful for
    /// characters that should stay upright. It can still be rotated directly via script.</summary>
    public bool FreezeRotation { get; set; }

    /// <summary>World-space linear velocity in units/second. Free to read or set directly from a script.</summary>
    public Vector2 LinearVelocity { get; set; }

    /// <summary>Angular velocity in radians/second.</summary>
    public float AngularVelocity { get; set; }

    private Vector2 _pendingForce;
    private float _pendingTorque;

    /// <summary>Queues a force (applied at the center of mass) to be integrated on the next physics step.
    /// Has no effect on Kinematic or Static bodies.</summary>
    public void AddForce(Vector2 force) => _pendingForce += force;

    /// <summary>Queues a force applied at a specific world-space point, contributing both linear
    /// acceleration and torque (the further the point is from the center, the more it spins the body).</summary>
    public void AddForceAtPosition(Vector2 force, Vector2 worldPosition)
    {
        _pendingForce += force;
        _pendingTorque += Vec2.Cross(worldPosition - Transform.Position, force);
    }

    /// <summary>Queues a pure rotational force for the next physics step.</summary>
    public void AddTorque(float torque) => _pendingTorque += torque;

    /// <summary>Moves this body to a world position immediately. Intended for Kinematic bodies driven by
    /// script logic (a moving platform, an elevator) — for Dynamic bodies, prefer AddForce/LinearVelocity
    /// so collisions keep working correctly.</summary>
    public void MovePosition(Vector2 worldPosition) => Transform.Position = worldPosition;

    /// <summary>Rotates this body to a world rotation (radians) immediately. See MovePosition.</summary>
    public void MoveRotation(float radians) => Transform.Rotation = radians;

    /// <summary>Backend-facing: reads and clears the force/torque accumulated via AddForce/AddForceAtPosition/
    /// AddTorque since the last physics step. Internal — only physics backends in this assembly call this.</summary>
    internal (Vector2 Force, float Torque) ConsumePendingForces()
    {
        var result = (_pendingForce, _pendingTorque);
        _pendingForce = Vector2.Zero;
        _pendingTorque = 0f;
        return result;
    }

    public override void OnAttach()
    {
        base.OnAttach();
        Owner.Scene?.Physics.RegisterBody(this);
    }

    public override void OnDetach()
    {
        Owner.Scene?.Physics.UnregisterBody(this);
        base.OnDetach();
    }
}
