using ImGuiNET;
using MyEngine.Core.Animation;
using MyEngine.Core.Audio;
using MyEngine.Core.Components;
using MyEngine.Core.ECS;
using MyEngine.Core.SceneSystem;
using MyEngine.Editor.UndoSystem;

namespace MyEngine.Editor.Panels;

/// <summary>
/// The "Outline": the tree of every GameObject in the scene (Phaser Editor's name for what Unity calls the
/// Hierarchy). Same behavior as before — click to select, right-click for the create/prefab/delete menus,
/// drop an asset on the strip to add it — with a compact header row (+ create menu, Delete) in place of the
/// old full-width Delete button.
/// </summary>
public static class OutlinePanel
{
    private static readonly System.Numerics.Vector4 PrefabInstanceColor = new(0.55f, 0.75f, 1f, 1f);

    public static void Draw(EditorState state)
    {
        EditorLayout.PinOutline();
        ImGui.Begin("Outline", EditorLayout.PanelFlags);

        if (state.IsPlaying)
        {
            ImGui.TextColored(new System.Numerics.Vector4(1f, 0.8f, 0.3f, 1f), "Play Mode — changes are discarded on Stop");
            ImGui.Separator();
        }

        DrawHeaderRow(state);

        DrawDropZone(state);

        foreach (var root in state.ActiveScene.RootObjects.ToArray())
            DrawNode(root, state);

        // Right-clicking empty space (as opposed to an existing node — see DrawNode's own
        // BeginPopupContextItem) creates root-level GameObjects instead of children of anything: right-click
        // a GameObject to add a child under it, right-click empty space to add a new independent one.
        // NoOpenOverItems is what keeps this from ALSO firing (as well as DrawNode's own popup) when you
        // right-click directly on a node.
        if (!state.IsPlaying && ImGui.BeginPopupContextWindow("OutlineEmptySpace",
                ImGuiPopupFlags.MouseButtonRight | ImGuiPopupFlags.NoOpenOverItems))
        {
            DrawCreateMenuItems(state);
            ImGui.EndPopup();
        }

        ImGui.End();
    }

    /// <summary>The compact strip above the tree: "+ Add" opens the same create menu as right-clicking empty
    /// space (inert during Play Mode, exactly like that menu — see Draw), "Delete" removes the selected
    /// GameObject (available in Play Mode too, as the old full-width Delete button always was: it acts on
    /// the throwaway play clone then).</summary>
    private static void DrawHeaderRow(EditorState state)
    {
        ImGui.BeginDisabled(state.IsPlaying);
        if (ImGui.SmallButton("+ Add"))
            ImGui.OpenPopup("OutlineAddPopup");
        ImGui.EndDisabled();
        if (ImGui.IsItemHovered() && !state.IsPlaying) ImGui.SetTooltip("Create a new GameObject at the scene root");

        if (!state.IsPlaying && ImGui.BeginPopup("OutlineAddPopup"))
        {
            DrawCreateMenuItems(state);
            ImGui.EndPopup();
        }

        ImGui.SameLine();
        ImGui.BeginDisabled(state.Selected == null);
        if (ImGui.SmallButton("Delete") && state.Selected != null)
            DeleteGameObject(state, state.Selected);
        ImGui.EndDisabled();

        ImGui.Separator();
    }

    /// <summary>The "create at the scene root" entries shared by the "+ Add" button and the empty-space
    /// right-click menu — one list so the two can never drift apart.</summary>
    private static void DrawCreateMenuItems(EditorState state)
    {
        if (ImGui.MenuItem("Create Empty"))
            AssetDropHandler.CreateEmptyGameObject(state);

        ImGui.Separator();
        if (ImGui.MenuItem("Audio Source"))
            AssetDropHandler.CreateComponentAsChild<AudioSource>(state, null, "Audio Source");
        if (ImGui.MenuItem("Sprite Animation"))
            AssetDropHandler.CreateComponentAsChild<SpriteAnimation>(state, null, "Sprite Animation");
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
        ImGui.PushStyleColor(ImGuiCol.Button, new System.Numerics.Vector4(0f, 0f, 0f, 0.18f));
        ImGui.PushStyleColor(ImGuiCol.Text, ImGui.GetStyle().Colors[(int)ImGuiCol.TextDisabled]);
        ImGui.Button("Drop a texture, prefab or audio clip here", new System.Numerics.Vector2(avail.X, 22));
        ImGui.PopStyleColor(2);

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

        // Two leading spaces reserve the small gutter DrawTreeRowIcon paints into, right after the arrow —
        // see that method's doc comment for why this has to be baked into the label instead of drawn some
        // other way.
        bool open = ImGui.TreeNodeEx("  " + go.Name + "##" + go.Id, flags);

        if (isPrefabInstance) ImGui.PopStyleColor();

        EditorIcons.DrawTreeRowIcon(GetGameObjectIcon(go));

        if (ImGui.IsItemClicked()) state.SelectGameObject(go);

        if (!state.IsPlaying && ImGui.BeginPopupContextItem())
        {
            if (ImGui.MenuItem("Save As Prefab..."))
                SaveAsPrefab(go, state);
            if (ImGui.MenuItem("Delete"))
                DeleteGameObject(state, go);

            ImGui.Separator();

            // Dual-path component addition. Every system below offers two entries: a "direct" one — the
            // exact same undo-tracked InspectorPanel.AddComponent<T> the Inspector's own "+ Add Component"
            // button uses, just reachable without switching panels — and an "as Child" one that creates a
            // new, separately-named child instead (see AssetDropHandler.CreateComponentAsChild's doc
            // comment for why: so e.g. three AudioSources meant for different sounds can be told apart by
            // name, instead of sitting unlabeled and indistinguishable on one GameObject). Both act on the
            // right-clicked node itself, matching Save As Prefab/Delete above rather than introducing a
            // second notion of "target" alongside the Outline's existing one. Adding a future component
            // type here is one more BeginMenu block following this exact shape — no new machinery needed.
            if (ImGui.MenuItem("Create Empty"))
                AssetDropHandler.CreateEmptyGameObject(state);
            if (ImGui.MenuItem("Create Empty Child"))
                AssetDropHandler.CreateEmptyChild(state, go);

            ImGui.Separator();
            if (ImGui.BeginMenu("Audio"))
            {
                if (go.GetComponent<AudioSource>() == null && ImGui.MenuItem("Audio Source"))
                {
                    InspectorPanel.AddComponent<AudioSource>(go, state, "Add Audio Source");
                    state.SelectGameObject(go);
                }
                if (ImGui.MenuItem("Audio Source as Child"))
                    AssetDropHandler.CreateComponentAsChild<AudioSource>(state, go, "Audio Source");
                ImGui.EndMenu();
            }
            if (ImGui.BeginMenu("Animation"))
            {
                if (go.GetComponent<SpriteAnimation>() == null && ImGui.MenuItem("Sprite Animation"))
                {
                    InspectorPanel.AddComponent<SpriteAnimation>(go, state, "Add Sprite Animation");
                    state.SelectGameObject(go);
                }
                if (ImGui.MenuItem("Sprite Animation as Child"))
                    AssetDropHandler.CreateComponentAsChild<SpriteAnimation>(state, go, "Sprite Animation");
                ImGui.EndMenu();
            }

            ImGui.EndPopup();
        }

        if (open)
        {
            foreach (var child in go.Transform.Children.ToArray())
                DrawNode(child.Owner, state);
            ImGui.TreePop();
        }
    }

    /// <summary>Which icon represents a GameObject in the Outline — the per-type row icon Unity's Hierarchy
    /// has and this project's tree didn't (every row rendered as plain text, with no visual cue for what
    /// kind of object it actually is). Checked in a fixed, most-specific-first order: a GameObject with
    /// several of these components only shows the first match below, same as Unity showing exactly one icon
    /// per row rather than stacking several. BlockIcons.Empty (an outlined box with a pivot dot) is the
    /// fallback for a GameObject with none of these — the same shape the Blocks panel's own "Empty" tile
    /// uses, so the same glyph means "empty GameObject" everywhere in the editor.</summary>
    private static Action<ImDrawListPtr, System.Numerics.Vector2, System.Numerics.Vector2, uint> GetGameObjectIcon(GameObject go)
    {
        if (go.GetComponent<Camera2D>() != null) return BlockIcons.Camera;
        if (go.GetComponent<AudioSource>() != null) return EditorIcons.Audio;
        if (go.GetComponent<SpriteAnimation>() != null) return BlockIcons.Animation;
        if (go.GetComponent<SpriteRenderer>() != null) return BlockIcons.Square;
        return BlockIcons.Empty;
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
