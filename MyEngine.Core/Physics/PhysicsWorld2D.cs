using Microsoft.Xna.Framework;
using MyEngine.Core.ECS;

namespace MyEngine.Core.Physics;

/// <summary>
/// One Scene's physics simulation — gravity, every registered Rigidbody2D/Collider2D, and the fixed-
/// timestep loop that steps them. Every Scene owns exactly one of these (see Scene.Physics), the same way
/// Godot gives each World2D its own physics space.
///
/// This class never simulates anything itself — it forwards to whichever <see cref="IPhysicsBackend2D"/>
/// it was built with (the built-in <see cref="ManagedPhysicsBackend2D"/> by default) and translates that
/// backend's raw events into the OnCollisionEnter2D/OnTriggerEnter2D/etc. callbacks components see. Coordinates
/// use this engine's screen-space convention: +Y points down, matching Godot 2D (and unlike Unity 2D/3D,
/// where +Y is up) — the default gravity below falls "down the screen".
/// </summary>
public sealed class PhysicsWorld2D
{
    /// <summary>Simulation steps happen at this fixed rate regardless of frame rate, same as Godot's
    /// default physics tick rate.</summary>
    public const float FixedTimestep = 1f / 60f;

    /// <summary>Caps how many fixed steps run in a single frame — protects against a "spiral of death"
    /// where a slow frame produces more physics work, which makes the next frame slower still. Time beyond
    /// this many steps' worth is simply dropped rather than simulated in a burst.</summary>
    private const int MaxStepsPerFrame = 4;

    private readonly IPhysicsBackend2D _backend;
    private float _accumulator;

    /// <summary>World gravity, applied every step to every Dynamic body (scaled by that body's own
    /// GravityScale). Defaults to a modest downward pull matching Godot 2D's own default.</summary>
    public Vector2 Gravity { get; set; } = new(0f, 980f);

    public PhysicsWorld2D() : this(new ManagedPhysicsBackend2D()) { }

    /// <summary>Accepts a custom backend — this is the extension point for swapping in a native physics
    /// engine (Bullet, PhysX, ...) later without changing anything else in Core or the Editor.</summary>
    public PhysicsWorld2D(IPhysicsBackend2D backend) => _backend = backend;

    internal void RegisterBody(Rigidbody2D body) => _backend.RegisterBody(body);
    internal void UnregisterBody(Rigidbody2D body) => _backend.UnregisterBody(body);
    internal void RegisterCollider(Collider2D collider) => _backend.RegisterCollider(collider);
    internal void UnregisterCollider(Collider2D collider) => _backend.UnregisterCollider(collider);

    /// <summary>Advances the simulation by <paramref name="frameDeltaTime"/> seconds' worth of frame time,
    /// internally broken into zero or more FixedTimestep-sized steps via an accumulator — the standard
    /// "decouple physics from frame rate" pattern both Unity's FixedUpdate and Godot's _physics_process
    /// are built on. Called once per frame from Scene.Update, before any component's regular Update.</summary>
    public void Step(float frameDeltaTime)
    {
        _accumulator += frameDeltaTime;
        int steps = 0;

        while (_accumulator >= FixedTimestep && steps < MaxStepsPerFrame)
        {
            _backend.Step(FixedTimestep, Gravity);
            DispatchEvents();
            _accumulator -= FixedTimestep;
            steps++;
        }

        if (steps == MaxStepsPerFrame)
            _accumulator = 0f; // dragged too far behind — drop the remainder instead of ever catching up in one burst
    }

    private void DispatchEvents()
    {
        foreach (var evt in _backend.ConsumeEvents())
        {
            switch (evt.Kind)
            {
                case PhysicsEventKind.CollisionEnter:
                    DispatchCollision(evt, static (c, collision) => c.OnCollisionEnter2D(collision));
                    break;
                case PhysicsEventKind.CollisionStay:
                    DispatchCollision(evt, static (c, collision) => c.OnCollisionStay2D(collision));
                    break;
                case PhysicsEventKind.CollisionExit:
                    DispatchCollision(evt, static (c, collision) => c.OnCollisionExit2D(collision));
                    break;
                case PhysicsEventKind.TriggerEnter:
                    DispatchTrigger(evt, static (c, other) => c.OnTriggerEnter2D(other));
                    break;
                case PhysicsEventKind.TriggerStay:
                    DispatchTrigger(evt, static (c, other) => c.OnTriggerStay2D(other));
                    break;
                case PhysicsEventKind.TriggerExit:
                    DispatchTrigger(evt, static (c, other) => c.OnTriggerExit2D(other));
                    break;
            }
        }
    }

    private static void DispatchCollision(PhysicsEvent2D evt, Action<Component, Collision2D> invoke)
    {
        var fromA = new Collision2D(evt.ColliderB, evt.Normal, evt.ContactPoint, evt.RelativeVelocity);
        NotifyAllComponents(evt.ColliderA.Owner, fromA, invoke);

        var fromB = new Collision2D(evt.ColliderA, -evt.Normal, evt.ContactPoint, -evt.RelativeVelocity);
        NotifyAllComponents(evt.ColliderB.Owner, fromB, invoke);
    }

    private static void DispatchTrigger(PhysicsEvent2D evt, Action<Component, Collider2D> invoke)
    {
        NotifyAllComponents(evt.ColliderA.Owner, evt.ColliderB, invoke);
        NotifyAllComponents(evt.ColliderB.Owner, evt.ColliderA, invoke);
    }

    private static void NotifyAllComponents<TPayload>(GameObject owner, TPayload payload, Action<Component, TPayload> invoke)
    {
        // Matches Unity's behavior of delivering collision messages to every component on the GameObject,
        // not just a single designated "physics listener" — a Script, an audio-on-impact component, etc.
        // can all react independently. ToArray() so a handler adding/removing components mid-callback
        // (e.g. AddComponent, DestroySelf) can't invalidate this enumeration.
        foreach (var component in owner.Components.ToArray())
            if (component.Enabled) invoke(component, payload);
    }

    // ---------------------------------------------------------------- queries

    /// <summary>Casts a ray and returns the closest hit, if any. Triggers are ignored by default, matching
    /// Unity's default raycast behavior. (The `out` parameter has to come before the optional ones here —
    /// C# doesn't allow a required parameter after an optional one — which is also exactly the order
    /// Unity's own Physics.Raycast(origin, direction, out hitInfo, maxDistance, ...) uses.)</summary>
    public bool Raycast(Vector2 origin, Vector2 direction, out RaycastHit2D hit, float maxDistance = float.MaxValue, bool includeTriggers = false)
        => _backend.Raycast(origin, direction, maxDistance, includeTriggers, out hit);

    /// <summary>Casts a ray and returns every hit along it, nearest first.</summary>
    public IReadOnlyList<RaycastHit2D> RaycastAll(Vector2 origin, Vector2 direction, float maxDistance = float.MaxValue, bool includeTriggers = false)
        => _backend.RaycastAll(origin, direction, maxDistance, includeTriggers);

    /// <summary>Every collider whose shape contains <paramref name="point"/>.</summary>
    public IReadOnlyList<Collider2D> OverlapPoint(Vector2 point, bool includeTriggers = true)
        => _backend.OverlapPoint(point, includeTriggers);

    /// <summary>Every collider overlapping a circle at <paramref name="center"/> — handy for "what's near
    /// this position" gameplay checks (explosion radius, pickup range, ...).</summary>
    public IReadOnlyList<Collider2D> OverlapCircle(Vector2 center, float radius, bool includeTriggers = true)
        => _backend.OverlapCircle(center, radius, includeTriggers);
}
