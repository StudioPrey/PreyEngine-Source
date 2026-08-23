using Microsoft.Xna.Framework;
using MyEngine.Core.ECS;
using MyEngine.Core.Physics;

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
        var go = new GameObject(name, id) { Scene = this };
        _gameObjects.Add(go);
        return go;
    }

    public void AddExisting(GameObject go)
    {
        go.Scene = this;
        if (!_gameObjects.Contains(go)) _gameObjects.Add(go);
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
        // Physics steps first so scripts' Update sees this frame's already-resolved positions/collisions —
        // the same ordering Unity uses (FixedUpdate before Update) and Godot uses (_physics_process
        // interleaved ahead of _process within a frame).
        Physics.Step((float)gameTime.ElapsedGameTime.TotalSeconds);

        // ToArray so components can safely add/destroy objects mid-update
        foreach (var go in _gameObjects.ToArray())
            go.UpdateAll(gameTime);
    }

    public void Draw(GameTime gameTime)
    {
        foreach (var go in _gameObjects.ToArray())
            go.DrawAll(gameTime);
    }

    public GameObject? Find(string name) => _gameObjects.FirstOrDefault(g => g.Name == name);
    public GameObject? FindById(Guid id) => _gameObjects.FirstOrDefault(g => g.Id == id);
}
