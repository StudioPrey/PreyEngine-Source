using System.Numerics;
using ImGuiNET;
using MyEngine.Core.Animation;
using MyEngine.Core.Audio;
using MyEngine.Core.Components;

namespace MyEngine.Editor.Panels;

/// <summary>
/// The "Blocks" panel (bottom-center): everything you build a scene FROM, in Phaser Editor's tab layout —
///   Assets   → the current folder's files as a thumbnail grid (the old Content Browser's grid)
///   Built-in → click a tile to create that kind of GameObject at the scene root (the same things the
///              GameObject menu and the Outline's create menu offer, plus Camera, as one palette)
///   Prefabs  → every prefab in the project; drag into the scene or double-click to instantiate
///   Console  → the log
/// One panel, one tab bar, each tab getting the panel's full width while it's active — same idea as the old
/// combined "Console / Content Browser" panel, which this replaces.
/// </summary>
public static class BlocksPanel
{
    private const float TileSize = 64f;
    private const float CellPadding = 12f;

    public static ContentBrowserResult Draw(EditorState state)
    {
        // ProcessConfirmedDelete is NOT called here — it has to run before EVERY panel this frame, including
        // InspectorPanel, which draws before this one. EditorApp.BuildUI calls it as the very first thing it
        // does. See ProcessConfirmedDelete's own doc comment for the full story.

        EditorLayout.PinBlocks();
        ImGui.Begin("Blocks", EditorLayout.PanelFlags);

        var result = new ContentBrowserResult();

        if (ImGui.BeginTabBar("BlocksTabs"))
        {
            // Assets first: ImGui opens a tab bar on its first tab, and the project's files are the most-used
            // tab, so it should be the one that's showing when the editor starts. (ImGui.NET has no
            // BeginTabItem overload taking flags without also taking a "ref bool open", which would add a
            // close button to the tab — so the order, not SetSelected, is what picks the starting tab.)
            if (ImGui.BeginTabItem("Assets"))
            {
                result = ContentBrowserPanel.DrawAssetsTab(state);
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Built-in"))
            {
                DrawBuiltInTab(state);
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Prefabs"))
            {
                ContentBrowserPanel.DrawPrefabsTab(state);
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Console"))
            {
                ConsolePanel.DrawContents(state);
                ImGui.EndTabItem();
            }
            ImGui.EndTabBar();
        }

        ImGui.End();

        // Its own top-level window, not part of any tab's content — has to run after End(), not nested
        // inside a tab item, or it would be clipped to that tab's content region and only usable while that
        // one tab happens to be active. (Deleting can be requested from the Files panel too.)
        ContentBrowserPanel.DrawDeleteConfirmModal(state);

        return result;
    }

    // ---------------------------------------------------------------- Built-in tab

    private sealed record Block(string Label, string Tooltip, Action<ImDrawListPtr, Vector2, Vector2, uint> Icon, Action<EditorState> Create);

    private static readonly Block[] BuiltInBlocks =
    {
        new("Empty", "Empty GameObject", BlockIcons.Empty, AssetDropHandler.CreateEmptyGameObject),
        new("Square", "Square sprite", BlockIcons.Square, s => AssetDropHandler.CreatePrimitiveShape(s, PrimitiveShape.Square)),
        new("Circle", "Circle sprite", BlockIcons.Circle, s => AssetDropHandler.CreatePrimitiveShape(s, PrimitiveShape.Circle)),
        new("Triangle", "Triangle sprite", BlockIcons.Triangle, s => AssetDropHandler.CreatePrimitiveShape(s, PrimitiveShape.Triangle)),
        new("Capsule", "Capsule sprite", BlockIcons.Capsule, s => AssetDropHandler.CreatePrimitiveShape(s, PrimitiveShape.Capsule)),
        new("Star", "Star sprite", BlockIcons.Star, s => AssetDropHandler.CreatePrimitiveShape(s, PrimitiveShape.Star)),
        new("Camera", "Camera 2D", BlockIcons.Camera, s => AssetDropHandler.CreateComponentAsChild<Camera2D>(s, null, "Camera")),
        new("Audio", "Audio Source", EditorIcons.Audio, s => AssetDropHandler.CreateComponentAsChild<AudioSource>(s, null, "Audio Source")),
        new("Animation", "Sprite Animation", BlockIcons.Animation, s => AssetDropHandler.CreateComponentAsChild<SpriteAnimation>(s, null, "Sprite Animation")),
    };

    /// <summary>One click on a tile creates that GameObject at the scene root (undoable, selected — the same
    /// AssetDropHandler paths the menus use). Inert during Play Mode, exactly like those menus: the scene
    /// being edited is a throwaway clone then.</summary>
    private static void DrawBuiltInTab(EditorState state)
    {
        ImGui.BeginChild("BuiltInGrid", Vector2.Zero, ImGuiChildFlags.None, ImGuiWindowFlags.None);
        ImGui.BeginDisabled(state.IsPlaying);

        float cellSize = TileSize + CellPadding;
        int columns = Math.Max(1, (int)(ImGui.GetContentRegionAvail().X / cellSize));
        int i = 0;
        foreach (var block in BuiltInBlocks)
        {
            ImGui.BeginGroup();
            if (EditorIcons.Button("block_" + block.Label, new Vector2(TileSize, TileSize), false, block.Icon))
                block.Create(state);
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip(state.IsPlaying ? "Stop Play Mode to add objects to the scene" : "Add: " + block.Tooltip);
            ImGui.TextUnformatted(block.Label);
            ImGui.EndGroup();

            i++;
            if (i % columns != 0) ImGui.SameLine();
        }

        ImGui.EndDisabled();
        ImGui.EndChild();
    }
}
