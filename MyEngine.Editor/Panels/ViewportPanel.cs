using System.Numerics;
using ImGuiNET;

namespace MyEngine.Editor.Panels;

public readonly struct ViewportResult
{
    public Vector2 Size { get; init; }
    public Vector2 ImageScreenMin { get; init; }
    public Vector2 ImageScreenMax { get; init; }
    public bool IsHovered { get; init; }

    /// <summary>The Viewport window's own draw list, captured while it was still the current ImGui window
    /// (right before End()). Gizmos/camera icons/collider outlines are drawn from EditorApp *after*
    /// ViewportPanel.Draw() returns — i.e. outside this window's Begin/End — so they must be handed this
    /// draw list explicitly rather than calling ImGui.GetForegroundDrawList() themselves. The foreground
    /// list paints above every window in the app, popups and modals included, which used to make these
    /// overlays bleed on top of things like the Build popup; this draw list instead stays part of the
    /// Viewport window's own paint order, so anything opened above the Viewport correctly covers it.</summary>
    public ImDrawListPtr DrawList { get; init; }

    /// <summary>Relative asset path of a texture dropped this frame, or null.</summary>
    public string? DroppedTexturePath { get; init; }

    /// <summary>Relative asset path of a prefab dropped this frame, or null.</summary>
    public string? DroppedPrefabPath { get; init; }

    /// <summary>Relative asset path of an audio clip dropped this frame, or null.</summary>
    public string? DroppedAudioPath { get; init; }

    /// <summary>Screen-space mouse position at the moment of the drop (only meaningful if a drop happened).</summary>
    public Vector2 DropScreenPosition { get; init; }

    /// <summary>Full path of a scene tab the user clicked to switch to, or null.</summary>
    public string? RequestedSceneSwitch { get; init; }
}

public static class ViewportPanel
{
    public static ViewportResult Draw(EditorState state, IntPtr sceneTextureId)
    {
        EditorLayout.PinViewport();
        // No title bar of its own: the scene-tab strip right below IS this panel's header, the same way Phaser
        // Editor's scene tabs are the header of its scene editor. Zero padding (pushed only for Begin, so the
        // window keeps it — child windows and later widgets take the normal style) so the scene picture and
        // the tab strip run flush to the panel's edges instead of leaving a gray margin around the scene.
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
        ImGui.Begin("Scene", ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse | EditorLayout.PanelFlags);
        ImGui.PopStyleVar();

        var requestedSwitch = DrawSceneTabs(state);

        var avail = ImGui.GetContentRegionAvail();
        avail.Y = MathF.Max(avail.Y, 1);
        avail.X = MathF.Max(avail.X, 1);

        var imageMin = ImGui.GetCursorScreenPos();
        if (sceneTextureId != IntPtr.Zero)
            ImGui.Image(sceneTextureId, avail);
        var imageMax = imageMin + avail;

        bool imageHovered = ImGui.IsItemHovered();

        string? droppedTexture = null;
        string? droppedPrefab = null;
        string? droppedAudio = null;
        var dropPos = Vector2.Zero;

        if (ImGui.BeginDragDropTarget())
        {
            var tex = DragDropPayloads.AcceptTarget(DragDropPayloads.Texture);
            if (tex != null) { droppedTexture = tex; dropPos = ImGui.GetMousePos(); }

            var prefab = DragDropPayloads.AcceptTarget(DragDropPayloads.Prefab);
            if (prefab != null) { droppedPrefab = prefab; dropPos = ImGui.GetMousePos(); }

            var audio = DragDropPayloads.AcceptTarget(DragDropPayloads.Audio);
            if (audio != null) { droppedAudio = audio; dropPos = ImGui.GetMousePos(); }

            ImGui.EndDragDropTarget();
        }

        // Drawn AFTER the drop target block above — BeginDragDropTarget attaches to the most recently
        // submitted item, which must still be the scene Image, not one of the overlay's buttons.
        bool overOverlay = ViewportToolsOverlay.Draw(state, imageMin, imageMax);

        // "Hovering the scene" must exclude the tool palette floating over its corner, or a click on a tool
        // button would also fall through to ViewportSelector/GizmoSystem and select or deselect whatever
        // is behind it.
        bool hovered = imageHovered && !overOverlay;

        var drawList = ImGui.GetWindowDrawList();
        ImGui.End();

        return new ViewportResult
        {
            Size = avail,
            ImageScreenMin = imageMin,
            ImageScreenMax = imageMax,
            IsHovered = hovered,
            DrawList = drawList,
            DroppedTexturePath = droppedTexture,
            DroppedPrefabPath = droppedPrefab,
            DroppedAudioPath = droppedAudio,
            DropScreenPosition = dropPos,
            RequestedSceneSwitch = requestedSwitch,
        };
    }

    private const float TabStripHeight = 30f;

    /// <summary>The strip of scenes opened this session, drawn as flat editor tabs (like Phaser Editor's scene
    /// tabs) for quick manual switching: the active one is raised to the panel's lighter color with an amber
    /// underline, a "*" marks unsaved changes to it, and right-click → Close Tab removes a tab from the strip
    /// (this only removes the tab — it never touches the file, and never unloads the open scene).</summary>
    private static string? DrawSceneTabs(EditorState state)
    {
        if (state.SceneTabs.Count == 0) return null;

        string? requestedSwitch = null;
        SceneTab? tabToClose = null;

        var style = ImGui.GetStyle();
        var raised = style.Colors[(int)ImGuiCol.FrameBg];
        var hover = style.Colors[(int)ImGuiCol.FrameBgHovered];
        var flat = new Vector4(raised.X, raised.Y, raised.Z, 0f);
        uint accent = ImGui.ColorConvertFloat4ToU32(style.Colors[(int)ImGuiCol.CheckMark]);

        ImGui.BeginChild("SceneTabs", new Vector2(0, TabStripHeight), ImGuiChildFlags.None, ImGuiWindowFlags.HorizontalScrollbar);
        for (int idx = 0; idx < state.SceneTabs.Count; idx++)
        {
            var tab = state.SceneTabs[idx];
            bool active = tab.Path == state.CurrentScenePath;
            if (idx > 0) ImGui.SameLine(0f, 2f);

            ImGui.PushID(idx);
            ImGui.PushStyleColor(ImGuiCol.Button, active ? raised : flat);
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, hover);
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, hover);

            string label = active && state.IsDirty ? tab.Name + " *" : tab.Name;
            if (ImGui.Button(label) && !active) requestedSwitch = tab.Path;

            ImGui.PopStyleColor(3);

            if (active)
            {
                var min = ImGui.GetItemRectMin();
                var max = ImGui.GetItemRectMax();
                ImGui.GetWindowDrawList().AddLine(new Vector2(min.X, max.Y), new Vector2(max.X, max.Y), accent, 2f);
            }

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
}
