using Microsoft.Xna.Framework;
using MyEngine.Core.ECS;
using MyEngine.Core.Physics;
using MyEngine.Core.Scripting;

namespace MyEngine.Core.SceneSystem;

/// <summary>
/// Holds a flat list of root-level and nested GameObjects and drives their lifecycle.
/// One Scene is active (edited/played) at a time; a project can have many scene files.
/// </summary>
public sealed class Scene
{
    public string Name { get; set; }
    private readonly List<GameObject> _gameObjects = new();
    public IReadOnlyList<GameObject> GameObjects => _gameObjects;

    /// <summary>This scene's own physics simulation — every Rigidbody2D/Collider2D created within this
    /// scene registers itself here. Each Scene gets a fresh, independent one (an Edit scene and a cloned
    /// Play scene never share simulation state), matching Godot's one-World2D-per-viewport model.</summary>
    public PhysicsWorld2D Physics { get; } = new();

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

        // ToArray so components can safely add/destroy objects mid-update
        foreach (var go in _gameObjects.ToArray())
            go.UpdateAll(gameTime);
    }

    /// <summary>Two full passes — not one interleaved pass — over every currently-eligible script, so that
    /// a script whose Awake() creates or enables another object still sees every pre-existing script's own
    /// Awake already done first, matching Unity's real "every Awake before any Start" guarantee for a
    /// scene's starting contents. Eligibility mirrors GameObject.UpdateAll's own gating (Owner.ActiveInHierarchy
    /// and Component.Enabled) exactly, since Awake/Start are meant to fire for precisely the same scripts
    /// that would otherwise have OnUpdate called on them — this only changes *when* Awake/Start run
    /// relative to each other, not which scripts get them.
    ///
    /// Cheap to call unconditionally every frame: RunAwakeIfNeeded/RunStartIfNeeded are no-ops after the
    /// first time for any given script, so in steady state this is just an O(scripts) scan of already-set
    /// flags. Objects created mid-frame, after this sweep already ran, still get a correct Awake-before-Start
    /// pairing of their own the moment their Update is reached — see RunStartIfNeeded's defensive call to
    /// RunAwakeIfNeeded — the same way Unity's Instantiate() doesn't retroactively re-run already-finished
    /// objects' lifecycles either.</summary>
    private void RunAwakeStartSweep()
    {
        var scripts = _gameObjects.ToArray()
            .Where(go => go.ActiveInHierarchy)
            .SelectMany(go => go.GetComponents<Script>())
            .Where(s => s.Enabled)
            .ToArray();

        foreach (var script in scripts) script.RunAwakeIfNeeded();
        foreach (var script in scripts) script.RunStartIfNeeded();
    }

    public void Draw(GameTime gameTime)
    {
        foreach (var go in _gameObjects.ToArray())
            go.DrawAll(gameTime);
    }

    public GameObject? Find(string name) => _gameObjects.FirstOrDefault(g => g.Name == name);
    public GameObject? FindById(Guid id) => _gameObjects.FirstOrDefault(g => g.Id == id);
}
