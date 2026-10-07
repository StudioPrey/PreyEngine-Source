using Microsoft.Xna.Framework;

namespace MyEngine.Core.ECS;

/// <summary>
/// The basic entity of the engine. A GameObject is just a name + Transform + a bag of Components.
/// Create instances through Scene.CreateGameObject so the scene can track them.
/// </summary>
public sealed class GameObject
{
    public Guid Id { get; }
    public string Name { get; set; }
    public bool Enabled { get; set; } = true;
    public Transform Transform { get; }
    public SceneSystem.Scene? Scene { get; internal set; }

    /// <summary>
    /// If this GameObject is the root of a prefab instance, the path (relative to the project) of the
    /// .prefab file it was created from. Null for anything not created from a prefab. Purely metadata —
    /// Core doesn't do anything with it beyond carrying it through serialization; the editor uses it to
    /// offer "Apply to Prefab" / "Revert" actions.
    /// </summary>
    public string? SourcePrefabPath { get; set; }

    private readonly List<Component> _components = new();
    public IReadOnlyList<Component> Components => _components;

    public GameObject(string name = "GameObject") : this(name, Guid.NewGuid()) { }

    /// <summary>Used by the scene/prefab serializers so a loaded or cloned object can keep (or intentionally not keep) its original Id.</summary>
    public GameObject(string name, Guid id)
    {
        Id = id;
        Name = name;
        Transform = new Transform();
        AddComponentInternal(Transform);
    }

    /// <summary>Whether this object and every ancestor up the parent chain is enabled.</summary>
    public bool ActiveInHierarchy
    {
        get
        {
            if (!Enabled) return false;
            var parent = Transform.Parent;
            while (parent != null)
            {
                if (!parent.Owner.Enabled) return false;
                parent = parent.Parent;
            }
            return true;
        }
    }

    public T AddComponent<T>() where T : Component, new()
    {
        var component = new T();
        AddComponentInternal(component);
        return component;
    }

    /// <summary>
    /// Non-generic counterpart to AddComponent&lt;T&gt;(), for when the component type is only known at
    /// runtime — e.g. a user script compiled dynamically via Reflection, whose Type can't satisfy a
    /// compile-time generic constraint. <paramref name="componentType"/> must be a concrete (non-abstract)
    /// Component subclass with a public parameterless constructor.
    /// </summary>
    public Component AddComponent(Type componentType)
    {
        if (!typeof(Component).IsAssignableFrom(componentType))
            throw new ArgumentException($"'{componentType.Name}' is not a Component.", nameof(componentType));

        var component = (Component)Activator.CreateInstance(componentType)!;
        AddComponentInternal(component);
        return component;
    }

    private void AddComponentInternal(Component component)
    {
        component.Owner = this;
        BeginMutation();
        _components.Add(component);
        component.OnAttach();
        // Only if this GameObject is already in a scene at the moment the component is added — e.g.
        // Scene.CreateGameObject() then AddComponent<T>() immediately after. If it isn't yet (a bare
        // `new GameObject()` with components still being added before ever reaching a scene),
        // OnAddedToScene fires later instead, when Scene.AddExisting actually puts it in one — see there.
        if (Scene != null) component.OnAddedToScene();
    }

    public T? GetComponent<T>() where T : Component
    {
        foreach (var c in _components)
            if (c is T typed) return typed;
        return null;
    }

    public IEnumerable<T> GetComponents<T>() where T : Component
    {
        foreach (var c in _components)
            if (c is T typed) yield return typed;
    }

    public bool RemoveComponent(Component component)
    {
        if (component is Transform) return false; // Transform is mandatory
        if (!_components.Contains(component)) return false;
        BeginMutation();
        _components.Remove(component);
        // Scene != null check mirrors AddComponentInternal's: only notify if this GameObject was actually
        // registered with a scene's systems in the first place (see OnAddedToScene's doc comment).
        if (Scene != null) component.OnRemovedFromScene();
        component.OnDetach();
        return true;
    }

    public bool RemoveComponent<T>() where T : Component
    {
        var c = GetComponent<T>();
        return c != null && RemoveComponent(c);
    }

    public GameObject? Parent => Transform.Parent?.Owner;

    public void SetParent(GameObject? parent, bool keepWorldPosition = true)
        => Transform.SetParent(parent?.Transform, keepWorldPosition);

    // ---------------------------------------------------------------- per-frame component loops
    //
    // A component's Update/Draw is allowed to change its own GameObject's component list — the classic case
    // is a script calling DestroySelf() from OnUpdate, which removes every component of the object while the
    // loop over them is still running. The loops used to be a plain `foreach` over the live list, which threw
    // "Collection was modified" the instant that happened (a hard crash of the whole game for one of the most
    // common things a script does).
    //
    // The fix has to keep the fast path free: allocating a copy of the list for every object every frame
    // would cost far more than the crash ever risked. So the loop walks the live list by index, and the
    // list itself makes a copy of its PRE-mutation contents (copy-on-write, see BeginMutation) only in the
    // rare frame something actually adds/removes a component mid-loop; the loop then carries on over that
    // copy. Every component that was present when the loop started is still visited exactly once, in order —
    // except one that has since been removed, which is skipped (a removed component must not receive
    // callbacks after OnDetach). Components added mid-loop start with the next frame's loop.

    private int _iterationDepth;
    private Component[]? _iterationSnapshot;

    /// <summary>Called by every method that structurally changes <see cref="_components"/>, BEFORE it does so.</summary>
    private void BeginMutation()
    {
        if (_iterationDepth > 0 && _iterationSnapshot == null)
            _iterationSnapshot = _components.ToArray();
    }

    private void RunComponentLoop(GameTime gameTime, bool draw)
    {
        _iterationDepth++;
        try
        {
            int count = _components.Count;
            for (int i = 0; i < count; i++)
            {
                Component c;
                if (_iterationSnapshot == null)
                {
                    c = _components[i];
                }
                else
                {
                    c = _iterationSnapshot[i];
                    if (!_components.Contains(c)) continue; // removed since the loop began — no more callbacks
                }

                if (!c.Enabled) continue;
                if (draw) c.Draw(gameTime); else c.Update(gameTime);
            }
        }
        finally
        {
            if (--_iterationDepth == 0) _iterationSnapshot = null;
        }
    }

    internal void UpdateAll(GameTime gameTime)
    {
        if (!ActiveInHierarchy) return;
        RunComponentLoop(gameTime, draw: false);
    }

    internal void DrawAll(GameTime gameTime)
    {
        if (!ActiveInHierarchy) return;
        RunComponentLoop(gameTime, draw: true);
    }
}
