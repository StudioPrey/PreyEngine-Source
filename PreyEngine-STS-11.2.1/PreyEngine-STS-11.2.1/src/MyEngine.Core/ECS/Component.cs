using Microsoft.Xna.Framework;

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
}
