using Microsoft.Xna.Framework;
using MyEngine.Core.ECS;
using MyEngine.Core.Physics;

namespace MyEngine.Core.Scripting;

/// <summary>
/// Base class every user-written game script inherits from. This exists as a thin translation layer on
/// top of <see cref="Component"/> — deliberately a SEPARATE class from Component, not a modification of
/// it, for two reasons:
///   1. The engine-internal API (Component, GameTime, the OnAttach/Update lifecycle names) can keep
///      evolving without breaking every script ever written against it.
///   2. Every "friendly name" a script author sees lives in exactly one file. Adding another
///      Unity-familiar convenience later (another lifecycle hook, another helper method) means adding
///      one member here — nowhere else in the engine needs to change, and nothing scripts already wrote
///      needs to change either.
///
/// ## Writing a script
/// Override Start() for one-time setup and OnUpdate() for per-frame logic:
/// <code>
/// public class PlayerController : Script
/// {
///     public float speed = 200f;
///
///     protected override void Start() { }
///
///     protected override void OnUpdate(float deltaTime)
///     {
///         if (Input.IsKeyDown(Keys.Right)) transform.Position += Vector2.UnitX * speed * deltaTime;
///     }
/// }
/// </code>
///
/// ## Adding a new translated member (for engine maintainers)
/// Add it to the "Unity-style aliases" or "Convenience methods" region below with an XML doc comment.
/// Keep the implementation a one-line forward to the real Component/GameObject/Scene API — this class
/// should never contain actual engine logic, only naming/shape translation.
/// </summary>
public abstract class Script : Component
{
    private bool _hasAwoken;
    private bool _hasStarted;

    // ---------------------------------------------------------------- Unity-style aliases
    // Every one of these forwards to something that already exists on Component/GameObject — the value
    // is purely in the familiar name, not new behavior. Add more here as needed; never duplicate logic.

    /// <summary>The GameObject this script is attached to. Same as <see cref="Component.Owner"/>.</summary>
    public GameObject gameObject => Owner;

    /// <summary>This GameObject's Transform. Same as the inherited <see cref="Component.Transform"/> (capital T).</summary>
    public Transform transform => Transform;

    /// <summary>Shortcut for gameObject.Name.</summary>
    public string name
    {
        get => Owner.Name;
        set => Owner.Name = value;
    }

    /// <summary>Shortcut for this component's Enabled flag (lowercase, Unity-style).</summary>
    public bool enabled
    {
        get => Enabled;
        set => Enabled = value;
    }

    /// <summary>This script's Scene's physics world — raycasts, overlap queries, gravity. Unity exposes the
    /// equivalent as a global static (Physics2D); here it's reached through the owning Scene instead, since
    /// an Edit scene and a Play scene can exist at the same time and each has its own simulation. Safe to
    /// use from anywhere a script's lifecycle methods run — by then Owner is always in a scene.</summary>
    public PhysicsWorld2D physics => Owner.Scene!.Physics;

    // ---------------------------------------------------------------- lifecycle (override these, not Component's)
    // Update(GameTime) from Component is sealed below specifically so a script can't accidentally override
    // the "wrong" (engine-facing) lifecycle method and wonder why Start/OnUpdate never fire — there is
    // exactly one correct set of hooks to override, and the compiler enforces it.
    //
    // Awake/Start are driven by Scene.Update's two-phase sweep (RunAwakeIfNeeded/RunStartIfNeeded below),
    // not lazily triggered from this class's own Update() — this is what guarantees every script active in
    // the scene gets Awake called before any of them get Start called, matching Unity's real cross-scene
    // ordering guarantee (not just "this script's own Awake before its own Start", which a purely
    // self-triggered approach could only ever offer).

    /// <summary>Called once, right before this script's first Start/OnUpdate.</summary>
    protected virtual void Awake() { }

    /// <summary>Called once, right after Awake and before this script's first OnUpdate.</summary>
    protected virtual void Start() { }

    /// <summary>Called every frame while Play Mode is running and this script is enabled.</summary>
    /// <param name="deltaTime">Seconds since the last frame — same value as Time.DeltaTime.</param>
    protected virtual void OnUpdate(float deltaTime) { }

    /// <summary>Runs Awake() exactly once. Called by Scene.Update's sweep before any script in the scene
    /// gets RunStartIfNeeded — see that method. No-op on every call after the first.</summary>
    internal void RunAwakeIfNeeded()
    {
        if (_hasAwoken) return;
        _hasAwoken = true;
        Awake();
    }

    /// <summary>Runs Start() exactly once, guaranteeing Awake() has already run first (calling
    /// RunAwakeIfNeeded defensively, in case something ever calls this out of Scene.Update's normal
    /// Awake-sweep-then-Start-sweep order). No-op on every call after the first.</summary>
    internal void RunStartIfNeeded()
    {
        if (_hasStarted) return;
        RunAwakeIfNeeded();
        _hasStarted = true;
        Start();
    }

    public sealed override void Update(GameTime gameTime)
    {
        OnUpdate((float)gameTime.ElapsedGameTime.TotalSeconds);
    }

    // ---------------------------------------------------------------- convenience methods

    /// <summary>Shortcut for gameObject.GetComponent&lt;T&gt;().</summary>
    public T? GetComponent<T>() where T : Component => Owner.GetComponent<T>();

    /// <summary>Shortcut for gameObject.AddComponent&lt;T&gt;().</summary>
    public T AddComponent<T>() where T : Component, new() => Owner.AddComponent<T>();

    /// <summary>Removes <paramref name="target"/> from the scene entirely (equivalent to Unity's Destroy(gameObject)).</summary>
    public void Destroy(GameObject target) => target.Scene?.Destroy(target);

    /// <summary>Removes this script's own GameObject from the scene.</summary>
    public void DestroySelf() => Destroy(gameObject);
}
