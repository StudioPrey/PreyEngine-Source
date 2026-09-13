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
        ImGui.Begin("Viewport", ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse | EditorLayout.PanelFlags);

        var requestedSwitch = DrawSceneTabs(state);

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

    /// <summary>Small strip listing scenes opened this session, for quick manual switching (right-click a
    /// tab to close it — this only removes it from the strip, it never touches the file).</summary>
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
}
