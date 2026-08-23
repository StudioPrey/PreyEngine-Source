using Microsoft.Xna.Framework;
using MyEngine.Core.Physics;

namespace MyEngine.Core.ECS;

/// <summary>
/// Base class for every piece of data/behavior that can be attached to a GameObject.
/// Override the lifecycle hooks you need; leave the rest alone.
/// </summary>
public abstract class Component
{
    /// <summary>The GameObject this component is attached to. Set automatically by GameObject.AddComponent.</summary>
    public GameObject Owner { get; internal set; } = null!;

    /// <summary>Shortcut to the owning GameObject's Transform.</summary>
    public Transform Transform => Owner.Transform;

    /// <summary>Whether this component participates in Update/Draw.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Called once, right after the component is added to a GameObject.</summary>
    public virtual void OnAttach() { }

    /// <summary>Called once when the component/owner is removed from the scene.</summary>
    public virtual void OnDetach() { }

    /// <summary>Called every frame before Draw, if Enabled and Owner.ActiveInHierarchy.</summary>
    public virtual void Update(GameTime gameTime) { }

    /// <summary>Called every frame after Update, if Enabled and Owner.ActiveInHierarchy.</summary>
    public virtual void Draw(GameTime gameTime) { }

    // ---------------------------------------------------------------- physics callbacks
    // Delivered to every component on a GameObject whose Collider2D took part in a collision/trigger this
    // physics step — not just Script subclasses — the same way Update/Draw above are available to any
    // Component, not only scripted behavior. See PhysicsWorld2D for how these get raised. A GameObject only
    // ever receives these if it has a Collider2D; a Rigidbody2D alone (no collider) never triggers them.

    /// <summary>Called on the first physics step where this object's collider touches another (non-trigger) collider.</summary>
    public virtual void OnCollisionEnter2D(Collision2D collision) { }

    /// <summary>Called on every physics step after OnCollisionEnter2D, for as long as the two colliders stay touching.</summary>
    public virtual void OnCollisionStay2D(Collision2D collision) { }

    /// <summary>Called on the first physics step where two previously-touching colliders stop touching.</summary>
    public virtual void OnCollisionExit2D(Collision2D collision) { }

    /// <summary>Called on the first physics step where this object's collider overlaps a trigger collider
    /// (or this object's own collider is the trigger). See Collider2D.IsTrigger.</summary>
    public virtual void OnTriggerEnter2D(Collider2D other) { }

    /// <summary>Called on every physics step after OnTriggerEnter2D, for as long as the two colliders keep overlapping.</summary>
    public virtual void OnTriggerStay2D(Collider2D other) { }

    /// <summary>Called on the first physics step where two previously-overlapping trigger colliders stop overlapping.</summary>
    public virtual void OnTriggerExit2D(Collider2D other) { }
}
