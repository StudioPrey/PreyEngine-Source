using System.Numerics;
using ImGuiNET;

namespace MyEngine.Editor.Panels;

public readonly struct ViewportResult
{
    public Vector2 Size { get; init; }
    public Vector2 ImageScreenMin { get; init; }
    public Vector2 ImageScreenMax { get; init; }
    public bool IsHovered { get; init; }

    /// <summary>Relative asset path of a texture dropped this frame, or null.</summary>
    public string? DroppedTexturePath { get; init; }

    /// <summary>Relative asset path of a prefab dropped this frame, or null.</summary>
    public string? DroppedPrefabPath { get; init; }

    /// <summary>Screen-space mouse position at the moment of the drop (only meaningful if a drop happened).</summary>
    public Vector2 DropScreenPosition { get; init; }

    /// <summary>Full path of a scene tab the user clicked to switch to, or null.</summary>
    public string? RequestedSceneSwitch { get; init; }
}

public static class ViewportPanel
{
    public static ViewportResult Draw(EditorState state, IntPtr sceneTextureId)
    {
        ImGui.Begin("Viewport", ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);

        var requestedSwitch = DrawSceneTabs(state);
        DrawToolbar(state);

        var avail = ImGui.GetContentRegionAvail();
        avail.Y = MathF.Max(avail.Y, 1);
        avail.X = MathF.Max(avail.X, 1);

        var imageMin = ImGui.GetCursorScreenPos();
        if (sceneTextureId != IntPtr.Zero)
            ImGui.Image(sceneTextureId, avail);
        var imageMax = imageMin + avail;

        bool hovered = ImGui.IsItemHovered();

        string? droppedTexture = null;
        string? droppedPrefab = null;
        var dropPos = Vector2.Zero;

        if (ImGui.BeginDragDropTarget())
        {
            var tex = DragDropPayloads.AcceptTarget(DragDropPayloads.Texture);
            if (tex != null) { droppedTexture = tex; dropPos = ImGui.GetMousePos(); }

            var prefab = DragDropPayloads.AcceptTarget(DragDropPayloads.Prefab);
            if (prefab != null) { droppedPrefab = prefab; dropPos = ImGui.GetMousePos(); }

            ImGui.EndDragDropTarget();
        }

        ImGui.End();

        return new ViewportResult
        {
            Size = avail,
            ImageScreenMin = imageMin,
            ImageScreenMax = imageMax,
            IsHovered = hovered,
            DroppedTexturePath = droppedTexture,
            DroppedPrefabPath = droppedPrefab,
            DropScreenPosition = dropPos,
            RequestedSceneSwitch = requestedSwitch,
        };
    }

    /// <summary>Small strip above the toolbar listing scenes opened this session, for quick manual switching
    /// (right-click a tab to close it — this only removes it from the strip, it never touches the file).</summary>
    private static string? DrawSceneTabs(EditorState state)
    {
        if (state.SceneTabs.Count == 0) return null;

        string? requestedSwitch = null;
        SceneTab? tabToClose = null;

        ImGui.BeginChild("SceneTabs", new Vector2(0, 30), ImGuiChildFlags.None, ImGuiWindowFlags.HorizontalScrollbar);
        for (int idx = 0; idx < state.SceneTabs.Count; idx++)
        {
            var tab = state.SceneTabs[idx];
            bool active = tab.Path == state.CurrentScenePath;
            if (idx > 0) ImGui.SameLine();

            ImGui.PushID(idx);
            if (active) ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.26f, 0.45f, 0.75f, 0.9f));
            if (ImGui.Button(tab.Name) && !active) requestedSwitch = tab.Path;
            if (active) ImGui.PopStyleColor();

            if (ImGui.BeginPopupContextItem())
            {
                if (ImGui.MenuItem("Close Tab")) tabToClose = tab;
                ImGui.EndPopup();
            }
            ImGui.PopID();
        }
        ImGui.EndChild();

        if (tabToClose != null) state.CloseSceneTab(tabToClose);
        return requestedSwitch;
    }

    private static void DrawToolbar(EditorState state)
    {
        if (ImGui.Button(state.IsPlaying ? "Stop" : "Play"))
            state.TogglePlay();

        ImGui.SameLine();
        DrawUndoRedoButtons(state);

        ImGui.SameLine();
        ImGui.TextDisabled("|");
        ImGui.SameLine();

        DrawGizmoModeButton(state, GizmoMode.Move, "Move (W)");
        ImGui.SameLine();
        DrawGizmoModeButton(state, GizmoMode.Rotate, "Rotate (E)");
        ImGui.SameLine();
        DrawGizmoModeButton(state, GizmoMode.Scale, "Scale (R)");

        ImGui.SameLine();
        ImGui.TextDisabled("|");
        ImGui.SameLine();
        DrawGizmoSpaceToggle(state);

        ImGui.SameLine();
        ImGui.TextDisabled("|");
        ImGui.SameLine();
        bool showGrid = state.ShowGrid;
        if (ImGui.Checkbox("Grid", ref showGrid)) state.ShowGrid = showGrid;

        if (!ImGui.GetIO().WantTextInput)
        {
            if (ImGui.IsKeyPressed(ImGuiKey.W)) state.Gizmo = GizmoMode.Move;
            if (ImGui.IsKeyPressed(ImGuiKey.E)) state.Gizmo = GizmoMode.Rotate;
            if (ImGui.IsKeyPressed(ImGuiKey.R)) state.Gizmo = GizmoMode.Scale;
        }
    }

    private static void DrawUndoRedoButtons(EditorState state)
    {
        ImGui.BeginDisabled(!state.Undo.CanUndo);
        if (ImGui.Button("Undo")) state.PerformUndo();
        if (ImGui.IsItemHovered() && state.Undo.NextUndoDescription != null)
            ImGui.SetTooltip(state.Undo.NextUndoDescription);
        ImGui.EndDisabled();

        ImGui.SameLine();

        ImGui.BeginDisabled(!state.Undo.CanRedo);
        if (ImGui.Button("Redo")) state.PerformRedo();
        if (ImGui.IsItemHovered() && state.Undo.NextRedoDescription != null)
            ImGui.SetTooltip(state.Undo.NextRedoDescription);
        ImGui.EndDisabled();
    }

    private static void DrawGizmoModeButton(EditorState state, GizmoMode mode, string label)
    {
        bool active = state.Gizmo == mode;
        if (active) ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.26f, 0.45f, 0.75f, 0.9f));
        if (ImGui.Button(label)) state.Gizmo = mode;
        if (active) ImGui.PopStyleColor();
    }

    private static void DrawGizmoSpaceToggle(EditorState state)
    {
        string label = state.GizmoSpace == GizmoSpace.World ? "World" : "Local";
        if (ImGui.Button(label))
            state.GizmoSpace = state.GizmoSpace == GizmoSpace.World ? GizmoSpace.Local : GizmoSpace.World;
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Click to toggle Move-gizmo axis space (Scale is always local).");
    }
}
