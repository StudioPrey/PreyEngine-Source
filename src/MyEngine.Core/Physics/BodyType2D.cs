namespace MyEngine.Core.Physics;

/// <summary>
/// How a Rigidbody2D participates in the simulation. This single enum on one component covers the same
/// ground Godot splits across three node types (StaticBody2D / RigidBody2D / CharacterBody2D) — Unity's
/// approach, which fits this engine's one-GameObject-many-components model better than separate node types.
/// A Collider2D with no Rigidbody2D at all behaves exactly like <see cref="Static"/> — you only need to add
/// a Rigidbody2D when something should move or receive forces.
/// </summary>
public enum BodyType2D
{
    /// <summary>Fully simulated: gravity and forces move it, collisions push it around. Most game objects.</summary>
    Dynamic,

    /// <summary>Never moved by collisions or forces, but you can still move it yourself — via script
    /// (Transform, or Rigidbody2D.MovePosition/MoveRotation) — and it will correctly push Dynamic bodies
    /// out of the way. Godot's CharacterBody2D covers this same role.</summary>
    Kinematic,

    /// <summary>Never moves and has infinite mass — ground, walls, platforms. Godot's StaticBody2D.
    /// Equivalent to leaving Rigidbody2D off entirely; the explicit type is here for clarity when a
    /// GameObject already needs a Rigidbody2D for some other reason.</summary>
    Static,
}
