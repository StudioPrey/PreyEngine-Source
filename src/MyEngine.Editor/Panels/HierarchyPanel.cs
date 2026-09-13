using ImGuiNET;
using MyEngine.Core.ECS;
using MyEngine.Core.SceneSystem;
using MyEngine.Editor.UndoSystem;

namespace MyEngine.Editor.Panels;

public static class HierarchyPanel
{
    private static readonly System.Numerics.Vector4 PrefabInstanceColor = new(0.55f, 0.75f, 1f, 1f);

    public static void Draw(EditorState state)
    {
        EditorLayout.PinHierarchy();
        ImGui.Begin("Hierarchy", EditorLayout.PanelFlags);

        if (state.IsPlaying)
        {
            ImGui.TextColored(new System.Numerics.Vector4(1f, 0.8f, 0.3f, 1f), "Play Mode — changes will be discarded on Stop");
            ImGui.Separator();
        }

        if (ImGui.Button("Delete") && state.Selected != null)
            DeleteGameObject(state, state.Selected);

        ImGui.Separator();

        DrawDropZone(state);

        foreach (var root in state.ActiveScene.RootObjects.ToArray())
            DrawNode(root, state);

        ImGui.End();
    }

    /// <summary>
    /// A dedicated, always-visible strip that accepts a texture/prefab/audio clip dragged from the Content
    /// Browser — deliberately a separate fixed-size widget (rather than trying to make the whole,
    /// content-filled tree area a drop target) so the hit-test area is unambiguous regardless of how many
    /// rows are in the tree.
    /// </summary>
    private static void DrawDropZone(EditorState state)
    {
        var avail = ImGui.GetContentRegionAvail();
        ImGui.PushStyleColor(ImGuiCol.Button, new System.Numerics.Vector4(0.16f, 0.18f, 0.23f, 1f));
        ImGui.Button("Drop a texture, prefab, or audio clip here to add it to the scene", new System.Numerics.Vector2(avail.X, 30));
        ImGui.PopStyleColor();

        if (ImGui.BeginDragDropTarget())
        {
            var texturePath = DragDropPayloads.AcceptTarget(DragDropPayloads.Texture);
            if (texturePath != null) AssetDropHandler.CreateSpriteFromTexture(state, texturePath);

            var prefabPath = DragDropPayloads.AcceptTarget(DragDropPayloads.Prefab);
            if (prefabPath != null) AssetDropHandler.InstantiatePrefab(state, prefabPath);

            var audioPath = DragDropPayloads.AcceptTarget(DragDropPayloads.Audio);
            if (audioPath != null) AssetDropHandler.CreateAudioSourceFromClip(state, audioPath);

            ImGui.EndDragDropTarget();
        }

        ImGui.Separator();
    }

    private static void DeleteGameObject(EditorState state, GameObject go)
    {
        var command = CreateDeleteGameObjectCommand.ForDelete(
            $"Delete {go.Name}", state.ActiveScene, state.Assets.ResolveSceneAssets, go);

        state.LogMessage($"Deleted '{go.Name}'");
        state.ActiveScene.Destroy(go);
        if (state.Selected == go) state.SelectGameObject(null);

        state.Undo.Push(command);
    }

    private static void DrawNode(GameObject go, EditorState state)
    {
        var flags = ImGuiTreeNodeFlags.OpenOnArrow | ImGuiTreeNodeFlags.SpanAvailWidth;
        if (go.Transform.Children.Count == 0) flags |= ImGuiTreeNodeFlags.Leaf;
        if (state.Selected == go) flags |= ImGuiTreeNodeFlags.Selected;

        bool isPrefabInstance = go.SourcePrefabPath != null;
        if (isPrefabInstance) ImGui.PushStyleColor(ImGuiCol.Text, PrefabInstanceColor);

        bool open = ImGui.TreeNodeEx(go.Name + "##" + go.Id, flags);

        if (isPrefabInstance) ImGui.PopStyleColor();

        if (ImGui.IsItemClicked()) state.SelectGameObject(go);

        if (!state.IsPlaying && ImGui.BeginPopupContextItem())
        {
            if (ImGui.MenuItem("Save As Prefab..."))
                SaveAsPrefab(go, state);
            if (ImGui.MenuItem("Delete"))
                DeleteGameObject(state, go);
            ImGui.EndPopup();
        }

        if (open)
        {
            foreach (var child in go.Transform.Children.ToArray())
                DrawNode(child.Owner, state);
            ImGui.TreePop();
        }
    }

    private static void SaveAsPrefab(GameObject go, EditorState state)
    {
        try
        {
            var path = Path.Combine(state.PrefabsFolder, $"{go.Name}.prefab");
            PrefabSerializer.Save(go, path);
            state.Assets.Refresh();
            state.LogMessage($"Saved prefab '{go.Name}' to {path}");
        }
        catch (Exception ex)
        {
            state.LogMessage($"ERROR saving prefab: {ex.Message}");
        }
    }
}
