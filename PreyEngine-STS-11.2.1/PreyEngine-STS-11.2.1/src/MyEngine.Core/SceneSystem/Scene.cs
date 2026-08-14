using Microsoft.Xna.Framework;
using MyEngine.Core.ECS;

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
