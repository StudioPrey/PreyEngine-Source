using MyEngine.Core.Components;
using MyEngine.Core.ECS;
using MyEngine.Core.SceneSystem;

namespace MyEngine.Editor.UndoSystem;

/// <summary>
/// Generic "some value changed" command — covers Transform fields, colors, sizes, checkboxes, anything
/// exposed through a plain get/set pair. Works with any type T (float, XNA Vector2, Color, bool, ...).
/// </summary>
public sealed class PropertyChangeCommand<T> : IEditorCommand
{
    private readonly Action<T> _setter;
    private readonly T _oldValue;
    private readonly T _newValue;

    public string Description { get; }

    public PropertyChangeCommand(string description, Action<T> setter, T oldValue, T newValue)
    {
        Description = description;
        _setter = setter;
        _oldValue = oldValue;
        _newValue = newValue;
    }

    public void Execute() => _setter(_newValue);
    public void Undo() => _setter(_oldValue);
}

/// <summary>
/// Create or delete a GameObject (and its whole subtree). Both directions are implemented the same way —
/// snapshot the subtree once via SceneSerializer, then toggle between "destroyed" and "restored" — because
/// Scene.Destroy tears down components (calling OnDetach), so simply keeping a C# reference around isn't
/// enough to bring an object back exactly as it was.
/// </summary>
public sealed class CreateDeleteGameObjectCommand : IEditorCommand
{
    private readonly Scene _scene;
    private readonly Action<Scene> _resolveTextures;
    private readonly List<GameObjectData> _snapshot;
    private readonly Guid _parentId;
    private readonly bool _hasParent;
    private readonly bool _deleteIsTheAction; // true = Execute deletes, Undo restores. false = the reverse.

    private CreateDeleteGameObjectCommand(string description, Scene scene, Action<Scene> resolveTextures,
        List<GameObjectData> snapshot, GameObject? parent, bool deleteIsTheAction)
    {
        Description = description;
        _scene = scene;
        _resolveTextures = resolveTextures;
        _snapshot = snapshot;
        _hasParent = parent != null;
        _parentId = parent?.Id ?? Guid.Empty;
        _deleteIsTheAction = deleteIsTheAction;
    }

    /// <summary>Call right after creating <paramref name="root"/> — captures it so Undo can delete it and Redo can restore it.</summary>
    public static CreateDeleteGameObjectCommand ForCreate(string description, Scene scene, Action<Scene> resolveTextures, GameObject root)
        => new(description, scene, resolveTextures, Capture(root), root.Parent, deleteIsTheAction: false);

    /// <summary>Call right *before* destroying <paramref name="root"/> — captures it so Execute can delete it and Undo can restore it.</summary>
    public static CreateDeleteGameObjectCommand ForDelete(string description, Scene scene, Action<Scene> resolveTextures, GameObject root)
        => new(description, scene, resolveTextures, Capture(root), root.Parent, deleteIsTheAction: true);

    private static List<GameObjectData> Capture(GameObject root)
    {
        var list = new List<GameObjectData>();
        void Visit(GameObject go)
        {
            list.Add(SceneSerializer.ToGameObjectData(go));
            foreach (var child in go.Transform.Children)
                Visit(child.Owner);
        }
        Visit(root);
        return list;
    }

    public string Description { get; }

    public void Execute()
    {
        if (_deleteIsTheAction) Delete();
        else Restore();
    }

    public void Undo()
    {
        if (_deleteIsTheAction) Restore();
        else Delete();
    }

    private void Delete()
    {
        var root = _scene.FindById(_snapshot[0].Id);
        if (root != null) _scene.Destroy(root);
    }

    private void Restore()
    {
        var lookup = SceneSerializer.BuildIntoScene(_snapshot, _scene, preserveIds: true);
        _resolveTextures(_scene);

        if (_hasParent)
        {
            var root = lookup[_snapshot[0].Id];
            var parent = _scene.FindById(_parentId);
            if (parent != null) root.SetParent(parent, keepWorldPosition: false);
        }

        RelinkExternalReferences(lookup);
    }

    /// <summary>
    /// After restoring objects with their original Ids, any OTHER GameObject in the scene that was
    /// pointing at one of them by direct reference now holds a stale reference (same Id, but a different,
    /// orphaned C# instance) — e.g. a Camera2D following something that got deleted and then un-deleted.
    /// Re-point every such reference at the freshly restored instance.
    /// If a future component adds another cross-reference field like FollowTarget, add a line here too.
    /// </summary>
    private void RelinkExternalReferences(Dictionary<Guid, GameObject> lookup)
    {
        foreach (var go in _scene.GameObjects)
        {
            var camera = go.GetComponent<Camera2D>();
            if (camera?.FollowTarget != null
                && lookup.TryGetValue(camera.FollowTarget.Id, out var restored)
                && !ReferenceEquals(camera.FollowTarget, restored))
            {
                camera.FollowTarget = restored;
            }
        }
    }
}

/// <summary>Add or remove a component with no constructor arguments (covers every current component type).</summary>
public sealed class AddRemoveComponentCommand<TComponent> : IEditorCommand where TComponent : Component, new()
{
    private readonly GameObject _owner;
    private readonly bool _addIsTheAction;

    private AddRemoveComponentCommand(string description, GameObject owner, bool addIsTheAction)
    {
        Description = description;
        _owner = owner;
        _addIsTheAction = addIsTheAction;
    }

    /// <summary>Adds TComponent to <paramref name="owner"/> immediately, and returns a command that can undo/redo that.</summary>
    public static AddRemoveComponentCommand<TComponent> AddNow(string description, GameObject owner)
    {
        owner.AddComponent<TComponent>();
        return new(description, owner, addIsTheAction: true);
    }

    public string Description { get; }

    public void Execute()
    {
        if (_addIsTheAction) _owner.AddComponent<TComponent>();
        else _owner.RemoveComponent<TComponent>();
    }

    public void Undo()
    {
        if (_addIsTheAction) _owner.RemoveComponent<TComponent>();
        else _owner.AddComponent<TComponent>();
    }
}
