using Microsoft.Xna.Framework;
using MyEngine.Core.Audio;
using MyEngine.Core.ECS;
using MyEngine.Core.Physics;
using MyEngine.Core.Scripting;
using MyEngine.Core.Rendering;

namespace MyEngine.Core.SceneSystem;

/// <summary>
/// Holds a flat list of root-level and nested GameObjects and drives their lifecycle.
/// One Scene is active (edited/played) at a time; a project can have many scene files.
/// </summary>
public sealed class Scene
{
    public string Name { get; set; }
    private readonly List<GameObject> _gameObjects = new();

    /// <summary>Reusable per-frame copy of <see cref="_gameObjects"/> — see TakeSnapshot. Update and Draw run
    /// one after the other (never nested or concurrently), so a single buffer serves both.</summary>
    private GameObject[] _snapshot = new GameObject[64];
    public IReadOnlyList<GameObject> GameObjects => _gameObjects;

    /// <summary>This scene's own physics simulation — every Rigidbody2D/Collider2D created within this
    /// scene registers itself here. Each Scene gets a fresh, independent one (an Edit scene and a cloned
    /// Play scene never share simulation state), matching Godot's one-World2D-per-viewport model.</summary>
    public PhysicsWorld2D Physics { get; } = new();

    /// <summary>Independent render visibility registry, including for cloned Edit/Play scenes.</summary>
    public SpriteVisibilitySystem SpriteVisibility { get; } = new();

    public Scene(string name = "Untitled Scene")
    {
        Name = name;
    }

    public GameObject CreateGameObject(string name = "GameObject")
        => CreateGameObject(name, Guid.NewGuid());

    /// <summary>Creates a GameObject with a specific Id. Used by the scene/prefab serializers.</summary>
    public GameObject CreateGameObject(string name, Guid id)
    {
        // Constructed bare, then routed through AddExisting rather than setting go.Scene directly — a
        // GameObject's constructor already adds its own mandatory Transform via AddComponentInternal
        // before an object-initializer-style `{ Scene = this }` would ever run, so that Transform would
        // otherwise never get OnAddedToScene called on it. Going through AddExisting uniformly (its
        // recursion is a harmless no-op here — a freshly constructed GameObject has no children yet) keeps
        // that guarantee — "every component of a GameObject that's part of a live scene has had
        // OnAddedToScene called" — true regardless of which of the two ways a GameObject entered the scene.
        var go = new GameObject(name, id);
        AddExisting(go);
        return go;
    }

    /// <summary>Adds an already-constructed GameObject (and, recursively, every existing child already
    /// parented under it) to this scene. This is the one path where a component can legitimately have been
    /// added to a GameObject *before* that GameObject ever reached a scene — e.g.
    /// `new GameObject()` + `AddComponent&lt;Rigidbody2D&gt;()` + `scene.AddExisting(go)` — so it's also the
    /// one path responsible for firing the OnAddedToScene every one of those already-attached components
    /// missed at the time they were actually added (see Component.OnAddedToScene's doc comment). Recurses
    /// into Transform.Children so a whole pre-built hierarchy (e.g. an instantiated prefab) is registered
    /// in one call, not just its root. Safe to call again on something already in this scene — the
    /// notification only fires for objects genuinely new to it, since OnAddedToScene/OnRemovedFromScene are
    /// meant to pair up exactly once each, even though the underlying physics registration they typically
    /// drive happens to tolerate being called more than once.</summary>
    public void AddExisting(GameObject go)
    {
        bool alreadyPresent = _gameObjects.Contains(go);
        go.Scene = this;
        if (!alreadyPresent)
        {
            _gameObjects.Add(go);
            foreach (var component in go.Components)
                component.OnAddedToScene();
        }

        foreach (var child in go.Transform.Children)
            AddExisting(child.Owner);
    }

    public void Destroy(GameObject go)
    {
        // detach children first so we don't leave dangling parent refs
        foreach (var child in go.Transform.Children.ToArray())
            Destroy(child.Owner);

        go.Transform.SetParent(null, keepWorldPosition: false);
        foreach (var c in go.Components.ToArray())
            go.RemoveComponent(c);

        _gameObjects.Remove(go);
        go.Scene = null;
    }

    /// <summary>Call this right before dropping a Scene reference outright — e.g. EditorState.StopPlay
    /// discarding PlayScene — as opposed to destroying an individual GameObject within a Scene that's
    /// otherwise still running, which already cleans up correctly on its own via
    /// RemoveComponent/OnDetach/OnRemovedFromScene.
    ///
    /// Losing every reference to a Scene doesn't walk its hierarchy calling Destroy(), and it doesn't get
    /// a "next frame" for Update/Step to run either — so anything that assumes one of those two would
    /// eventually reach it needs an explicit chance to finish up here instead:
    ///   - An AudioSource still playing keeps its SoundEffectInstance alive (and audible) until the .NET
    ///     GC eventually finalizes it, with no idea the Scene it belonged to is already gone.
    ///   - A Collider2D/Rigidbody2D destroyed earlier this same frame (after Physics.Step already ran)
    ///     left an Exit event queued in the physics backend that only the next Step would have dispatched.
    ///
    /// Add to this list as new subsystems pick up similar "needs Update or Destroy to eventually run"
    /// assumptions — that's the whole reason this lives here as one place, instead of each caller that
    /// drops a Scene needing to remember every subsystem individually.</summary>
    public void Teardown()
    {
        foreach (var go in _gameObjects)
            foreach (var audio in go.GetComponents<AudioSource>())
                audio.StopAllSounds();

        Physics.FlushPendingEvents();
    }

    /// <summary>Only root objects (no parent) — useful for hierarchy panels.</summary>
    public IEnumerable<GameObject> RootObjects => _gameObjects.Where(g => g.Parent == null);

    public void Update(GameTime gameTime)
    {
        // Guarantees every script active in the scene has had Awake called before any of them have Start
        // called — see RunAwakeStartSweep. Runs before physics/the update loop so Awake/Start can safely
        // set up state (initial velocity, event subscriptions) this same frame's Step/Update will use.
        RunAwakeStartSweep();

        // Physics steps first so scripts' Update sees this frame's already-resolved positions/collisions —
        // the same ordering Unity uses (FixedUpdate before Update) and Godot uses (_physics_process
        // interleaved ahead of _process within a frame).
        Physics.Step((float)gameTime.ElapsedGameTime.TotalSeconds);

        // A snapshot, so components can safely add/destroy objects mid-update (an object created this frame
        // isn't updated until the next; one destroyed this frame that's already in the snapshot is still visited,
        // but a destroyed object has no components left to run — as with the ToArray() this replaced). The
        // snapshot buffer is reused frame
        // after frame instead of allocating a fresh array of every object in the scene, sixty times a second.
        int count = TakeSnapshot();
        for (int i = 0; i < count; i++)
            _snapshot[i].UpdateAll(gameTime);
        ClearSnapshot(count);
    }

    /// <summary>Copies the current object list into the reusable <see cref="_snapshot"/> buffer (growing it
    /// only when the scene has grown past its capacity) and returns how many entries are valid.</summary>
    private int TakeSnapshot()
    {
        int count = _gameObjects.Count;
        if (_snapshot.Length < count)
            _snapshot = new GameObject[Math.Max(count, _snapshot.Length * 2)];
        _gameObjects.CopyTo(_snapshot, 0);
        return count;
    }

    /// <summary>Drops the snapshot's references so a buffer that outlives a big scene doesn't keep destroyed
    /// GameObjects (and everything they reference) alive.</summary>
    private void ClearSnapshot(int count) => Array.Clear(_snapshot, 0, count);

    /// <summary>Scripts that still owe an Awake/Start call, gathered fresh by RunAwakeStartSweep each frame.
    /// Kept as a field only so the list's storage is reused; it is always empty between sweeps.</summary>
    private readonly List<Script> _pendingScripts = new();

    private void RunAwakeStartSweep()
    {
        // Two phases, exactly as before: EVERY pending script's Awake runs before ANY script's Start (the
        // cross-script ordering guarantee — see Script's doc comment). The difference is only that the
        // gathering pass no longer builds LINQ pipelines and arrays over the whole scene every frame: in
        // steady state nothing is pending, and finding that out is now one allocation-free scan.
        //
        // Scripts that have already started are skipped up front — both RunAwakeIfNeeded and
        // RunStartIfNeeded are no-ops for them, so leaving them out changes nothing but the work done.
        int count = TakeSnapshot();
        for (int i = 0; i < count; i++)
        {
            var go = _snapshot[i];
            if (!go.ActiveInHierarchy) continue;

            var components = go.Components;
            for (int c = 0; c < components.Count; c++)
                if (components[c] is Script { Enabled: true, NeedsLifecycleSweep: true } script)
                    _pendingScripts.Add(script);
        }
        ClearSnapshot(count);

        if (_pendingScripts.Count == 0) return;

        // Snapshot the pending list itself too: an Awake/Start can add scripts to the scene, and those (like
        // before) wait for the next frame's sweep rather than joining this one mid-iteration.
        var pending = _pendingScripts.ToArray();
        _pendingScripts.Clear();

        foreach (var script in pending) script.RunAwakeIfNeeded();
        foreach (var script in pending) script.RunStartIfNeeded();
    }

    /// <summary>Draws every GameObject once, in the order the scene lists them — the caller (EditorApp or
    /// RuntimeGame) has already opened the renderer's batch with the right view matrix; per-sprite depth
    /// ordering comes from SpriteRenderer.SortingOrder (encoded as layerDepth), not from this call order.</summary>
    public void Draw(GameTime gameTime) => Draw(gameTime, null);

    /// <summary>Draws through the unchanged component loop, rejecting only sprites outside the supplied
    /// world bounds. Null preserves the legacy path. Each call refreshes visibility for its own camera.</summary>
    public void Draw(GameTime gameTime, AABB2D? visibleWorldBounds)
    {
        SpriteVisibility.Refresh(visibleWorldBounds);
        int count = TakeSnapshot();
        try
        {
            for (int i = 0; i < count; i++)
                _snapshot[i].DrawAll(gameTime);
        }
        finally
        {
            ClearSnapshot(count);
            SpriteVisibility.EndDraw();
        }
    }

    public GameObject? Find(string name) => _gameObjects.FirstOrDefault(g => g.Name == name);
    public GameObject? FindById(Guid id) => _gameObjects.FirstOrDefault(g => g.Id == id);
}
