using ImGuiNET;
using Microsoft.Xna.Framework;
using MyEngine.Core.Audio;
using MyEngine.Core.Components;
using MyEngine.Core.ECS;
using MyEngine.Core.SceneSystem;

namespace MyEngine.Editor;

/// <summary>Left-click-to-select for anything in the Viewport that isn't already covered by its own
/// dedicated icon (CameraIconRenderer/AudioIconRenderer) — i.e. ordinary SpriteRenderer sprites. Without
/// this, a GameObject with just a SpriteRenderer — the overwhelming majority of a typical scene — was never
/// clickable at all; only selectable via the Outline.</summary>
public static class ViewportSelector
{
    /// <summary>Called once per frame, after the gizmo has already had a chance to claim this same click for
    /// dragging a handle (see GizmoSystem.IsDragging) and after the Camera/Audio icon renderers have already
    /// had a chance to claim it for their own icons — this only runs the general hit-test if nothing
    /// higher-priority already handled the click, and only reacts to a fresh click (IsMouseClicked), never a
    /// held button, so it can't fight a drag in progress. <paramref name="viewMatrix"/>/<paramref name="zoom"/>
    /// are the Viewport camera's — the same pair every other overlay (gizmo, icons) is positioned with.</summary>
    public static void Update(EditorState state, Matrix viewMatrix, float zoom, System.Numerics.Vector2 imageMin,
        System.Numerics.Vector2 imageMax, bool viewportHovered, bool gizmoIsDragging)
    {
        if (state.IsPlaying || gizmoIsDragging || !viewportHovered) return;
        if (!ImGui.IsMouseClicked(ImGuiMouseButton.Left)) return;

        var mouse = ImGui.GetMousePos();
        if (mouse.X < imageMin.X || mouse.X > imageMax.X || mouse.Y < imageMin.Y || mouse.Y > imageMax.Y) return;

        var hit = HitTest(state, viewMatrix, zoom, mouse, imageMin);
        state.SelectGameObject(hit); // null when nothing was hit — clicking empty space deselects,
                                      // matching Unity's own Scene view
    }

    private static GameObject? HitTest(EditorState state, Matrix cameraMatrix, float cameraZoom,
        System.Numerics.Vector2 mouse, System.Numerics.Vector2 imageMin)
    {
        GameObject? topmost = null;
        int topmostOrder = int.MinValue;

        foreach (var go in state.ActiveScene.GameObjects)
        {
            // Already has its own dedicated icon + click handling (CameraIconRenderer/AudioIconRenderer) —
            // skip here so the two systems can't disagree about which one owns a given click.
            if (go.GetComponent<Camera2D>() != null || go.GetComponent<AudioSource>() != null) continue;

            if (!TryGetHitBox(go, cameraMatrix, cameraZoom, out var center, out var halfSize, out var order))
                continue;

            var min = imageMin + new System.Numerics.Vector2(center.X - halfSize.X, center.Y - halfSize.Y);
            var max = imageMin + new System.Numerics.Vector2(center.X + halfSize.X, center.Y + halfSize.Y);

            bool inside = mouse.X >= min.X && mouse.X <= max.X && mouse.Y >= min.Y && mouse.Y <= max.Y;
            if (!inside) continue;

            // Highest SortingOrder wins when things overlap — matches the same "higher draws on top"
            // convention SortingOrder already has for actual rendering, so the one you'd see on top is the
            // one clicking selects.
            if (order >= topmostOrder)
            {
                topmostOrder = order;
                topmost = go;
            }
        }

        return topmost;
    }

    /// <summary>Computes a screen-space (Viewport-image-relative, before the imageMin offset) center and
    /// half-size for <paramref name="go"/>. The half-size is scaled by the camera's Zoom as well as the
    /// GameObject's own Transform.Scale — Zoom visibly changes how big everything renders on screen, so
    /// leaving it out would silently throw off click detection at any zoom other than 1.</summary>
    private static bool TryGetHitBox(GameObject go, Matrix cameraMatrix, float cameraZoom,
        out Vector2 center, out Vector2 halfSize, out int sortingOrder)
    {
        var sr = go.GetComponent<SpriteRenderer>();
        if (sr != null)
        {
            center = Vector2.Transform(go.Transform.Position, cameraMatrix);
            halfSize = new Vector2(sr.Size.X, sr.Size.Y) * 0.5f * MathF.Max(go.Transform.Scale.X, 0.0001f) * cameraZoom;
            sortingOrder = sr.SortingOrder;
            return true;
        }

        center = default;
        halfSize = default;
        sortingOrder = 0;
        return false;
    }
}
