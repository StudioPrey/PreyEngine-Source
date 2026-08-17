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
        _components.Add(component);
        component.OnAttach();
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
        if (!_components.Remove(component)) return false;
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

    internal void UpdateAll(GameTime gameTime)
    {
        if (!ActiveInHierarchy) return;
        foreach (var c in _components)
            if (c.Enabled) c.Update(gameTime);
    }

    internal void DrawAll(GameTime gameTime)
    {
        if (!ActiveInHierarchy) return;
        foreach (var c in _components)
            if (c.Enabled) c.Draw(gameTime);
    }
}
