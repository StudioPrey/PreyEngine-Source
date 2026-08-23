using ImGuiNET;
using MyEngine.Core.Physics;
using XnaVector2 = Microsoft.Xna.Framework.Vector2;
using XnaMatrix = Microsoft.Xna.Framework.Matrix;
using ImVec2 = System.Numerics.Vector2;
using ImVec4 = System.Numerics.Vector4;

namespace MyEngine.Editor;

/// <summary>
/// Draws an outline over every enabled Collider2D in the active scene — a box as its (possibly rotated)
/// rectangle, a circle as a ring — on ImGui's foreground draw list, the same screen-space-overlay technique
/// GizmoSystem and CameraIconRenderer already use. Purely visual: this never touches simulation state.
///
/// Deliberately recomputes each collider's world-space shape from public Transform/Collider2D data rather
/// than reusing the physics backend's internal shape math (BoxShape2D etc. are assembly-internal to
/// MyEngine.Core), so the Editor stays decoupled from the physics backend's private implementation details —
/// the same boundary SceneSerializer and the Inspector already respect.
///
/// Toggle with EditorState.ShowColliders (the Viewport toolbar's "Colliders" checkbox).
/// </summary>
public static class ColliderGizmoRenderer
{
    private const float Thickness = 1.5f;
    private const int CircleSegments = 32;

    /// <summary>Call once per frame after the Viewport panel has been drawn, with the same view matrix/zoom
    /// used to render the scene (EditorApp._currentViewMatrix / _currentZoom) and the Viewport image's
    /// screen rect (outlines are clipped to stay inside it).</summary>
    public static void Draw(EditorState state, XnaMatrix viewMatrix, float zoom, ImVec2 imageMin, ImVec2 imageMax)
    {
        if (!state.ShowColliders) return;

        var drawList = ImGui.GetForegroundDrawList();
        drawList.PushClipRect(imageMin, imageMax, true);

        ImVec2 WorldToScreen(XnaVector2 world) => imageMin + ToIm(XnaVector2.Transform(world, viewMatrix));

        foreach (var go in state.ActiveScene.GameObjects)
        {
            foreach (var collider in go.GetComponents<Collider2D>())
            {
                if (!collider.Enabled) continue;
                uint color = collider.IsTrigger ? Col(0.35f, 0.7f, 1f, 0.9f) : Col(0.35f, 1f, 0.55f, 0.9f);

                switch (collider)
                {
                    case BoxCollider2D box:
                        DrawBoxOutline(drawList, box, color, WorldToScreen);
                        break;
                    case CircleCollider2D circle:
                        DrawCircleOutline(drawList, circle, zoom, color, WorldToScreen);
                        break;
                }
            }
        }

        drawList.PopClipRect();
    }

    private static void DrawBoxOutline(ImDrawListPtr drawList, BoxCollider2D box, uint color, Func<XnaVector2, ImVec2> worldToScreen)
    {
        var t = box.Transform;
        var worldScale = t.Scale;
        var center = t.Position + Rotate(box.Offset * worldScale, t.Rotation);
        var halfExtents = box.Size * worldScale * 0.5f;

        var right = Rotate(new XnaVector2(halfExtents.X, 0f), t.Rotation);
        var up = Rotate(new XnaVector2(0f, halfExtents.Y), t.Rotation);

        var c0 = worldToScreen(center - right - up);
        var c1 = worldToScreen(center + right - up);
        var c2 = worldToScreen(center + right + up);
        var c3 = worldToScreen(center - right + up);

        drawList.AddLine(c0, c1, color, Thickness);
        drawList.AddLine(c1, c2, color, Thickness);
        drawList.AddLine(c2, c3, color, Thickness);
        drawList.AddLine(c3, c0, color, Thickness);
    }

    private static void DrawCircleOutline(ImDrawListPtr drawList, CircleCollider2D circle, float zoom, uint color, Func<XnaVector2, ImVec2> worldToScreen)
    {
        var t = circle.Transform;
        var worldScale = t.Scale;
        var center = t.Position + Rotate(circle.Offset * worldScale, t.Rotation);

        // The world-space radius is a scalar, not a point, so — unlike the box corners above, which go
        // through the full view matrix — it needs an explicit *zoom to become a screen-space pixel radius.
        float uniformScale = (MathF.Abs(worldScale.X) + MathF.Abs(worldScale.Y)) * 0.5f;
        float screenRadius = circle.Radius * uniformScale * zoom;

        drawList.AddCircle(worldToScreen(center), screenRadius, color, CircleSegments, Thickness);
    }

    private static uint Col(float r, float g, float b, float a = 1f)
        => ImGui.ColorConvertFloat4ToU32(new ImVec4(r, g, b, a));

    private static ImVec2 ToIm(XnaVector2 v) => new(v.X, v.Y);

    /// <summary>Same rotation convention as Transform.Rotation / Matrix.CreateRotationZ.</summary>
    private static XnaVector2 Rotate(XnaVector2 v, float radians)
    {
        float cos = MathF.Cos(radians), sin = MathF.Sin(radians);
        return new XnaVector2(v.X * cos - v.Y * sin, v.X * sin + v.Y * cos);
    }
}
