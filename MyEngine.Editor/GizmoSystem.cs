using ImGuiNET;
using MyEngine.Core.ECS;
using MyEngine.Editor.UndoSystem;
using XnaVector2 = Microsoft.Xna.Framework.Vector2;
using XnaMatrix = Microsoft.Xna.Framework.Matrix;
using ImVec2 = System.Numerics.Vector2;
using ImVec4 = System.Numerics.Vector4;

namespace MyEngine.Editor;

/// <summary>
/// A 2D transform gizmo (Move / Rotate / Scale), drawn on ImGui's foreground draw list and hit-tested
/// in screen space. Supports World and Local axis space for Move (toggle in EditorState.GizmoSpace);
/// Scale always uses local axes since Transform.LocalScale is inherently along the object's own,
/// possibly-rotated axes — a "world space" non-uniform scale wouldn't mean anything without shearing.
/// Rotate doesn't have a space concept in 2D (there's only one rotation axis, Z).
///
/// Every finished drag (mouse-down to mouse-up) is pushed as a single undo command, so tiny mouse
/// jitter during a drag doesn't spam the undo stack — only the net change from start to release does.
///
/// Known simplification: axis directions ignore the *camera's* rotation (only the object's own rotation,
/// for Local mode). Fine as long as the scene camera itself isn't rotated — see README for future work.
/// </summary>
public sealed class GizmoSystem
{
    private const float AxisLength = 60f;
    private const float HandleHitRadius = 9f;
    private const float RotateRadius = 55f;
    private const float ScaleHandleOffset = 60f;

    private enum Drag { None, Center, AxisX, AxisY, Rotate, ScaleUniform, ScaleX, ScaleY }

    private Drag _drag = Drag.None;
    private GameObject? _dragTarget;
    private ImVec2 _dragStartMouse;
    private XnaVector2 _dragStartWorldPos;
    private float _dragStartRotation;
    private XnaVector2 _dragStartScale;
    private float _rotateDragStartAngle;

    /// <summary>Call once per frame after the Viewport panel has been drawn, passing the Viewport window's
    /// own draw list (ViewportResult.DrawList) — drawing to ImGui's global foreground list here instead
    /// used to make the gizmo paint over popups/modals opened above the Viewport, since that list ignores
    /// window order entirely.</summary>
    public void Update(EditorState state, XnaMatrix viewMatrix, float zoom, ImVec2 imageMin, bool viewportHovered, ImDrawListPtr drawList)
    {
        bool down = ImGui.IsMouseDown(ImGuiMouseButton.Left);

        // Finish (and record the undo for) any drag in progress *before* looking at the current
        // selection — if something else changed the selection mid-drag (e.g. an Undo triggered by a
        // keyboard shortcut while the mouse is still held down on a handle), the in-progress change
        // must still be recorded against the object that was actually being dragged, not silently lost.
        if (!down && _drag != Drag.None)
        {
            if (_dragTarget != null) PushUndoForFinishedDrag(state, _dragTarget, _drag);
            _drag = Drag.None;
            _dragTarget = null;
        }

        var go = state.Selected;
        if (go == null || state.SelectedAsset != null) return;

        ImVec2 WorldToScreen(XnaVector2 world) => imageMin + ToIm(XnaVector2.Transform(world, viewMatrix));

        var mouse = ImGui.GetMousePos();
        bool clicked = ImGui.IsMouseClicked(ImGuiMouseButton.Left);

        var origin = WorldToScreen(go.Transform.Position);

        // Only allow starting a *new* drag while the mouse is over the viewport image. Once a drag has
        // started, it keeps going (via _dragTarget) even if the mouse strays outside the image, or —
        // per the comment above — even if the selection itself changes before mouse-up.
        bool canStartDrag = viewportHovered && _drag == Drag.None;

        bool isLocal = state.GizmoSpace == GizmoSpace.Local;

        switch (state.Gizmo)
        {
            case GizmoMode.Move:
                UpdateMove(go, origin, mouse, clicked, down, zoom, canStartDrag, isLocal, drawList);
                break;
            case GizmoMode.Rotate:
                UpdateRotate(go, origin, mouse, clicked, down, canStartDrag, drawList);
                break;
            case GizmoMode.Scale:
                UpdateScale(go, origin, mouse, clicked, down, zoom, canStartDrag, drawList);
                break;
        }
    }

    // ---------------------------------------------------------------- move

    private void UpdateMove(GameObject go, ImVec2 origin, ImVec2 mouse, bool clicked, bool down,
        float zoom, bool canStartDrag, bool isLocal, ImDrawListPtr drawList)
    {
        float axisAngle = isLocal ? go.Transform.Rotation : 0f;
        var xDir = Rotate(new ImVec2(1, 0), axisAngle);
        var yDir = Rotate(new ImVec2(0, 1), axisAngle);
        var xEnd = origin + xDir * AxisLength;
        var yEnd = origin + yDir * AxisLength;

        uint colX = Col(1f, 0.3f, 0.3f);
        uint colY = Col(0.3f, 1f, 0.3f);
        uint colCenter = Col(0.9f, 0.9f, 0.3f);
        uint white = Col(1f, 1f, 1f);

        drawList.AddLine(origin, xEnd, colX, 3f);
        drawList.AddTriangleFilled(xEnd, xEnd - xDir * 10 + PerpCw(xDir) * 5, xEnd - xDir * 10 - PerpCw(xDir) * 5, colX);
        drawList.AddLine(origin, yEnd, colY, 3f);
        drawList.AddTriangleFilled(yEnd, yEnd - yDir * 10 + PerpCw(yDir) * 5, yEnd - yDir * 10 - PerpCw(yDir) * 5, colY);
        drawList.AddCircleFilled(origin, 6f, colCenter);
        drawList.AddCircle(origin, 6f, white, 12, 1.5f);

        if (canStartDrag && clicked)
        {
            if (ImVec2.Distance(mouse, origin) < HandleHitRadius)
                BeginDrag(Drag.Center, mouse, go);
            else if (DistanceToSegment(mouse, origin, xEnd) < HandleHitRadius)
                BeginDrag(Drag.AxisX, mouse, go);
            else if (DistanceToSegment(mouse, origin, yEnd) < HandleHitRadius)
                BeginDrag(Drag.AxisY, mouse, go);
        }

        if (down && _drag is Drag.Center or Drag.AxisX or Drag.AxisY)
        {
            var screenDelta = mouse - _dragStartMouse;
            var newPos = _dragStartWorldPos;

            if (_drag == Drag.Center)
            {
                newPos += new XnaVector2(screenDelta.X / zoom, screenDelta.Y / zoom);
            }
            else
            {
                // project the mouse delta onto the (possibly rotated) axis direction, then move along it
                var axisDir = _drag == Drag.AxisX ? xDir : yDir;
                float amountScreen = ImVec2.Dot(screenDelta, axisDir);
                newPos += ToXna(axisDir) * (amountScreen / zoom);
            }

            go.Transform.Position = newPos;
        }
    }

    // ---------------------------------------------------------------- rotate

    private void UpdateRotate(GameObject go, ImVec2 origin, ImVec2 mouse, bool clicked, bool down,
        bool canStartDrag, ImDrawListPtr drawList)
    {
        uint col = Col(0.3f, 0.6f, 1f);
        drawList.AddCircle(origin, RotateRadius, col, 48, 2.5f);
        drawList.AddCircleFilled(origin, 5f, col);

        float dist = ImVec2.Distance(mouse, origin);

        if (canStartDrag && clicked && MathF.Abs(dist - RotateRadius) < HandleHitRadius)
        {
            _drag = Drag.Rotate;
            _dragTarget = go;
            _dragStartMouse = mouse;
            _dragStartRotation = go.Transform.LocalRotation;
            _rotateDragStartAngle = MathF.Atan2(mouse.Y - origin.Y, mouse.X - origin.X);
        }

        if (down && _drag == Drag.Rotate)
        {
            float currentAngle = MathF.Atan2(mouse.Y - origin.Y, mouse.X - origin.X);
            float delta = currentAngle - _rotateDragStartAngle;
            go.Transform.LocalRotation = _dragStartRotation + delta;
        }
    }

    // ---------------------------------------------------------------- scale

    private void UpdateScale(GameObject go, ImVec2 origin, ImVec2 mouse, bool clicked, bool down,
        float zoom, bool canStartDrag, ImDrawListPtr drawList)
    {
        // Scale is always along the object's own (possibly rotated) local axes — LocalScale.X/Y are
        // defined in local space regardless of the Move gizmo's World/Local toggle.
        var xDir = Rotate(new ImVec2(1, 0), go.Transform.Rotation);
        var yDir = Rotate(new ImVec2(0, 1), go.Transform.Rotation);
        var xEnd = origin + xDir * ScaleHandleOffset;
        var yEnd = origin + yDir * ScaleHandleOffset;

        uint colX = Col(1f, 0.3f, 0.3f);
        uint colY = Col(0.3f, 1f, 0.3f);
        uint colCenter = Col(0.9f, 0.9f, 0.3f);

        drawList.AddLine(origin, xEnd, colX, 3f);
        drawList.AddRectFilled(xEnd - new ImVec2(5, 5), xEnd + new ImVec2(5, 5), colX);
        drawList.AddLine(origin, yEnd, colY, 3f);
        drawList.AddRectFilled(yEnd - new ImVec2(5, 5), yEnd + new ImVec2(5, 5), colY);
        drawList.AddRectFilled(origin - new ImVec2(6, 6), origin + new ImVec2(6, 6), colCenter);

        if (canStartDrag && clicked)
        {
            if (ImVec2.Distance(mouse, origin) < HandleHitRadius)
                BeginScaleDrag(Drag.ScaleUniform, mouse, go);
            else if (ImVec2.Distance(mouse, xEnd) < HandleHitRadius)
                BeginScaleDrag(Drag.ScaleX, mouse, go);
            else if (ImVec2.Distance(mouse, yEnd) < HandleHitRadius)
                BeginScaleDrag(Drag.ScaleY, mouse, go);
        }

        if (down && _drag is Drag.ScaleUniform or Drag.ScaleX or Drag.ScaleY)
        {
            var screenDelta = mouse - _dragStartMouse;
            float sensitivity = 1f / (100f * MathF.Max(zoom, 0.01f));

            var newScale = _dragStartScale;
            if (_drag == Drag.ScaleUniform)
            {
                float amount = (screenDelta.X - screenDelta.Y) * sensitivity;
                newScale = _dragStartScale + new XnaVector2(amount, amount);
            }
            else if (_drag == Drag.ScaleX)
            {
                newScale.X = _dragStartScale.X + ImVec2.Dot(screenDelta, xDir) * sensitivity;
            }
            else if (_drag == Drag.ScaleY)
            {
                newScale.Y = _dragStartScale.Y + ImVec2.Dot(screenDelta, yDir) * sensitivity;
            }

            const float minScale = 0.05f;
            go.Transform.LocalScale = new XnaVector2(MathF.Max(newScale.X, minScale), MathF.Max(newScale.Y, minScale));
        }
    }

    // ---------------------------------------------------------------- undo

    private void PushUndoForFinishedDrag(EditorState state, GameObject go, Drag finished)
    {
        switch (finished)
        {
            case Drag.Center or Drag.AxisX or Drag.AxisY:
                PushIfChanged(state, $"Move {go.Name}", v => go.Transform.Position = v, _dragStartWorldPos, go.Transform.Position);
                break;
            case Drag.Rotate:
                PushIfChanged(state, $"Rotate {go.Name}", v => go.Transform.LocalRotation = v, _dragStartRotation, go.Transform.LocalRotation);
                break;
            case Drag.ScaleUniform or Drag.ScaleX or Drag.ScaleY:
                PushIfChanged(state, $"Scale {go.Name}", v => go.Transform.LocalScale = v, _dragStartScale, go.Transform.LocalScale);
                break;
        }
    }

    private static void PushIfChanged<T>(EditorState state, string description, Action<T> setter, T oldValue, T newValue)
        where T : IEquatable<T>
    {
        if (oldValue.Equals(newValue)) return; // clicked without actually dragging - nothing to record
        state.Undo.Push(new PropertyChangeCommand<T>(description, setter, oldValue, newValue));
    }

    // ---------------------------------------------------------------- helpers

    private void BeginDrag(Drag kind, ImVec2 mouse, GameObject go)
    {
        _drag = kind;
        _dragTarget = go;
        _dragStartMouse = mouse;
        _dragStartWorldPos = go.Transform.Position;
    }

    private void BeginScaleDrag(Drag kind, ImVec2 mouse, GameObject go)
    {
        _drag = kind;
        _dragTarget = go;
        _dragStartMouse = mouse;
        _dragStartScale = go.Transform.LocalScale;
    }

    private static float DistanceToSegment(ImVec2 p, ImVec2 a, ImVec2 b)
    {
        var ab = b - a;
        float lenSq = ImVec2.Dot(ab, ab);
        float t = lenSq > 0.0001f ? Math.Clamp(ImVec2.Dot(p - a, ab) / lenSq, 0f, 1f) : 0f;
        var proj = a + ab * t;
        return ImVec2.Distance(p, proj);
    }

    private static uint Col(float r, float g, float b, float a = 1f)
        => ImGui.ColorConvertFloat4ToU32(new ImVec4(r, g, b, a));

    private static ImVec2 ToIm(XnaVector2 v) => new(v.X, v.Y);
    private static XnaVector2 ToXna(ImVec2 v) => new(v.X, v.Y);

    /// <summary>Rotates a screen-space direction vector by an angle in radians (same convention as Transform.Rotation).</summary>
    private static ImVec2 Rotate(ImVec2 v, float radians)
    {
        float cos = MathF.Cos(radians), sin = MathF.Sin(radians);
        return new ImVec2(v.X * cos - v.Y * sin, v.X * sin + v.Y * cos);
    }

    /// <summary>90-degree clockwise perpendicular, used to build arrowhead wings without extra trig.</summary>
    private static ImVec2 PerpCw(ImVec2 v) => new(-v.Y, v.X);
}
