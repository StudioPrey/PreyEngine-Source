using Microsoft.Xna.Framework;
using MyEngine.Core.Animation;
using MyEngine.Core.Audio;
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

    /// <summary>Called once when this component's GameObject becomes part of a live Scene — either because
    /// the GameObject was just added to one (Scene.AddExisting, or freshly created via
    /// Scene.CreateGameObject), or because this component was added to a GameObject that was already in a
    /// scene. Unlike OnAttach, Owner.Scene is guaranteed non-null here, which is precisely the guarantee
    /// OnAttach can't make: a GameObject can be constructed and have components added to it entirely
    /// before ever being added to a Scene (`new GameObject()` + AddComponent, added to a scene only
    /// later), so anything that needs to register with scene-level systems (Rigidbody2D/Collider2D
    /// registering with Scene.Physics, for example) belongs here, not in OnAttach.</summary>
    public virtual void OnAddedToScene() { }

    /// <summary>Called once when this component's GameObject stops being part of a live Scene — removed via
    /// Scene.Destroy, or this component removed via RemoveComponent while its GameObject was in a scene.
    /// Mirrors OnAddedToScene; anything registered there should be unregistered here.</summary>
    public virtual void OnRemovedFromScene() { }

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

    // ---------------------------------------------------------------- animation callbacks
    // Same delivery model as the physics callbacks above: raised on every component of the GameObject an
    // IAnimationSource (SpriteAnimation today; see that interface's doc comment for why it's not typed as
    // SpriteAnimation directly) lives on, not just Script subclasses. The source itself is passed through
    // so a GameObject with more than one animation source attached (e.g. a body sprite and a separate hat
    // overlay, each independently animated) can tell them apart.

    /// <summary>Called once per discrete frame change while an IAnimationSource on this GameObject is
    /// playing — e.g. to spawn a hitbox on an exact "Attack" frame. Fires at the clip's frame rate, not
    /// every Update() tick.</summary>
    public virtual void OnAnimationFrame(IAnimationSource animation, string clipName, int frameIndex) { }

    /// <summary>Called once when a non-looping clip finishes on its last frame, and once per full cycle for
    /// a looping/ping-pong clip (see AnimationLoopMode / FrameAdvanceResult.Completed for the exact
    /// definition per mode).</summary>
    public virtual void OnAnimationComplete(IAnimationSource animation, string clipName) { }

    // ---------------------------------------------------------------- audio callbacks
    // Same delivery model as the physics/animation callbacks above. Unlike the animation callbacks, this
    // takes a concrete AudioSource rather than an interface — audio playback (unlike sprite-frame vs. a
    // future skeletal/mesh animation) isn't expected to grow multiple, fundamentally different
    // implementations the way animation was, so there's no present need for that extra seam; see
    // IAnimationSource's own doc comment for the contrasting case where one was warranted.

    /// <summary>Called once when an AudioSource on this GameObject naturally finishes playing its main
    /// (Play()-started) clip. Never fires for a looping AudioSource, or for one stopped explicitly via
    /// Stop()/StopAllSounds, or for PlayOneShot instances — see AudioSource.Update's doc comment.</summary>
    public virtual void OnAudioFinished(AudioSource source) { }
}
