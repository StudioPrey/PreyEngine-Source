using Microsoft.Xna.Framework;
using MyEngine.Core.Components;
using MyEngine.Core.SceneSystem;
using MyEngine.Editor.UndoSystem;

namespace MyEngine.Editor;

public static class AssetDropHandler
{
    /// <summary>Creates a new GameObject with a SpriteRenderer pointing at <paramref name="texturePath"/>, at
    /// <paramref name="worldPosition"/> (defaults to the origin when the drop target has no spatial context,
    /// e.g. the Hierarchy panel). Selects it and records an undo entry.</summary>
    public static void CreateSpriteFromTexture(EditorState state, string texturePath, Vector2? worldPosition = null)
    {
        var go = state.ActiveScene.CreateGameObject(Path.GetFileNameWithoutExtension(texturePath));
        var sr = go.AddComponent<SpriteRenderer>();
        sr.TexturePath = texturePath;
        sr.Texture = state.Assets.GetTexture(texturePath);
        go.Transform.Position = worldPosition ?? Vector2.Zero;

        state.SelectGameObject(go);
        state.LogMessage($"Placed sprite '{go.Name}'.");

        var command = CreateDeleteGameObjectCommand.ForCreate(
            $"Create {go.Name}", state.ActiveScene, state.Assets.ResolveSceneTextures, go);
        state.Undo.Push(command);
    }

    /// <summary>Instantiates the prefab at <paramref name="prefabRelativePath"/> (a Content Browser asset path)
    /// into the active scene, optionally at a specific world position. Selects it and records an undo entry.</summary>
    public static void InstantiatePrefab(EditorState state, string prefabRelativePath, Vector2? worldPosition = null)
    {
        var asset = state.Assets.Find(prefabRelativePath);
        if (asset == null)
        {
            state.LogMessage($"ERROR: prefab asset not found: {prefabRelativePath}");
            return;
        }

        try
        {
            var root = PrefabSerializer.Instantiate(asset.FullPath, state.ActiveScene, worldPosition: worldPosition);
            state.SelectGameObject(root);
            state.LogMessage($"Instantiated prefab '{root.Name}'.");

            var command = CreateDeleteGameObjectCommand.ForCreate(
                $"Instantiate {root.Name}", state.ActiveScene, state.Assets.ResolveSceneTextures, root);
            state.Undo.Push(command);
        }
        catch (Exception ex)
        {
            state.LogMessage($"ERROR instantiating prefab: {ex.Message}");
        }
    }
}
