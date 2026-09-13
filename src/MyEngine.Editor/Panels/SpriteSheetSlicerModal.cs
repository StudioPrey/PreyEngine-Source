using System.Numerics;
using ImGuiNET;
using Microsoft.Xna.Framework.Graphics;
using MyEngine.Core.Animation;

namespace MyEngine.Editor.Panels;

/// <summary>
/// "Slice Sheet..." — the grid-slicing tool InspectorPanel's Sprite Animation section opens for a
/// SpriteSheet-mode clip. Same flag-driven plain-window approach as EditorApp.DrawUnsavedChangesModal and
/// ContentBrowserPanel's delete confirmation, for the same reason: no dependency on whatever ImGui popup
/// ID-stack context happened to be active in the Inspector when "Slice Sheet..." was clicked. _targetClip
/// being non-null IS the "is this open" flag, the same way EditorState.PendingDelete works for that modal.
///
/// Rows x Columns is the only slicing mode for v1, matching exactly what was asked for — cutting a sheet
/// into equal cells is by far the most common sprite-sheet layout, and it's also the one Unity/Godot both
/// lead with in their own equivalent tools. A cell picker (excluding specific cells from an otherwise
/// regular grid, for sheets with gaps) is a natural v2 addition, not built here.
/// </summary>
public static class SpriteSheetSlicerModal
{
    private static SpriteAnimationClip? _targetClip;
    private static AssetInfo? _sheetAsset;
    private static int _rows = 1;
    private static int _columns = 1;

    /// <summary>Opens the modal for slicing <paramref name="sheet"/>'s frames into <paramref name="clip"/>.
    /// Rows/Columns always reset to 1/1 on open rather than remembering the last sheet's values — different
    /// sheets rarely share a grid layout, and 1x1 is obviously-incomplete so it can't be mistaken for a real
    /// answer if Apply is hit by accident before adjusting them.</summary>
    public static void Open(SpriteAnimationClip clip, AssetInfo? sheet)
    {
        _targetClip = clip;
        _sheetAsset = sheet;
        _rows = 1;
        _columns = 1;
    }

    public static void Draw(EditorState state)
    {
        if (_targetClip == null) return;

        // The sheet could have been deleted/moved out from under us while this was open (e.g. via the
        // Content Browser's delete, in a separate part of the UI) — bail out cleanly rather than show a
        // slicer for a texture that no longer resolves to anything.
        if (_sheetAsset?.Texture == null) { Close(); return; }

        var viewport = ImGui.GetMainViewport();
        var center = new Vector2(viewport.Pos.X + viewport.Size.X * 0.5f, viewport.Pos.Y + viewport.Size.Y * 0.5f);
        ImGui.SetNextWindowPos(center, ImGuiCond.Appearing, new Vector2(0.5f, 0.5f));
        ImGui.SetNextWindowSize(new Vector2(460, 0), ImGuiCond.Appearing);

        bool open = true;
        const ImGuiWindowFlags flags = ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoDocking | ImGuiWindowFlags.AlwaysAutoResize;
        ImGui.Begin("Slice Sprite Sheet", ref open, flags);

        var texture = _sheetAsset.Texture;
        ImGui.TextUnformatted(_sheetAsset.RelativePath);
        ImGui.TextDisabled($"{texture.Width} x {texture.Height} px");
        ImGui.Spacing();

        ImGui.SetNextItemWidth(120);
        ImGui.DragInt("Rows", ref _rows, 0.1f, 1, 64);
        ImGui.SameLine();
        ImGui.SetNextItemWidth(120);
        ImGui.DragInt("Columns", ref _columns, 0.1f, 1, 64);
        _rows = Math.Max(1, _rows);
        _columns = Math.Max(1, _columns);

        int cellWidth = texture.Width / _columns;
        int cellHeight = texture.Height / _rows;
        ImGui.TextDisabled($"Cell size: {cellWidth} x {cellHeight} px  ({_rows * _columns} frames total)");
        if (texture.Width % _columns != 0 || texture.Height % _rows != 0)
        {
            ImGui.TextColored(new Vector4(1f, 0.75f, 0.3f, 1f),
                "Sheet size doesn't divide evenly by this grid — right/bottom-edge cells will be slightly cropped.");
        }

        ImGui.Spacing();
        DrawGridPreview(texture, cellWidth, cellHeight);
        ImGui.Spacing();

        bool canApply = cellWidth > 0 && cellHeight > 0;
        if (!canApply) ImGui.BeginDisabled();
        if (ImGui.Button("Apply", new Vector2(120, 0)))
        {
            // Slicing can shrink the frame list out from under an in-progress preview's frame index — if
            // this exact clip happens to be actively previewing right now, stop it first so
            // FrameSequencePlayer resets cleanly instead of the next Tick() indexing past the end of the
            // just-replaced Frames list.
            if (state.PreviewingAnimation != null && state.PreviewingAnimation.CurrentClipName == _targetClip.Name)
            {
                state.PreviewingAnimation.Stop();
                state.PreviewingAnimation = null;
            }
            ApplySlice(cellWidth, cellHeight, state);
            Close();
        }
        if (!canApply) ImGui.EndDisabled();

        ImGui.SameLine();
        if (ImGui.Button("Cancel", new Vector2(100, 0)) || !open)
            Close();

        ImGui.End();
    }

    private static void DrawGridPreview(Texture2D texture, int cellWidth, int cellHeight)
    {
        const float maxPreviewWidth = 400f;
        float scale = MathF.Min(1f, maxPreviewWidth / texture.Width);
        var previewSize = new Vector2(texture.Width * scale, texture.Height * scale);

        var previewMin = ImGui.GetCursorScreenPos();
        ImGui.Image(_sheetAsset!.ThumbnailId, previewSize);

        var drawList = ImGui.GetWindowDrawList();
        uint lineColor = ImGui.ColorConvertFloat4ToU32(new Vector4(0.3f, 0.85f, 1f, 0.9f));
        for (int c = 1; c < _columns; c++)
        {
            float x = previewMin.X + c * cellWidth * scale;
            drawList.AddLine(new Vector2(x, previewMin.Y), new Vector2(x, previewMin.Y + previewSize.Y), lineColor);
        }
        for (int r = 1; r < _rows; r++)
        {
            float y = previewMin.Y + r * cellHeight * scale;
            drawList.AddLine(new Vector2(previewMin.X, y), new Vector2(previewMin.X + previewSize.X, y), lineColor);
        }
    }

    private static void ApplySlice(int cellWidth, int cellHeight, EditorState state)
    {
        _targetClip!.SheetTexturePath = _sheetAsset!.RelativePath;
        _targetClip.Frames.Clear();

        // Row-major order (left-to-right, then top-to-bottom) — the standard reading order for a sheet,
        // and what Unity's equivalent grid-slice defaults to as well.
        for (int row = 0; row < _rows; row++)
        {
            for (int col = 0; col < _columns; col++)
            {
                _targetClip.Frames.Add(new SpriteAnimationFrame
                {
                    SourceRect = new Microsoft.Xna.Framework.Rectangle(col * cellWidth, row * cellHeight, cellWidth, cellHeight),
                });
            }
        }

        state.LogMessage($"Sliced '{_sheetAsset.RelativePath}' into {_targetClip.Frames.Count} frames for clip '{_targetClip.Name}'.");
    }

    private static void Close()
    {
        _targetClip = null;
        _sheetAsset = null;
    }
}
