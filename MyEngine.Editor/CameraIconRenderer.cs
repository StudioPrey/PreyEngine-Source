using ImGuiNET;
using Microsoft.Xna.Framework.Graphics;
using MyEngine.Core.Components;
using MyEngine.EditorFramework;
using XnaVector2 = Microsoft.Xna.Framework.Vector2;
using XnaMatrix = Microsoft.Xna.Framework.Matrix;
using ImVec2 = System.Numerics.Vector2;

namespace MyEngine.Editor;

public sealed class CameraIconRenderer : IDisposable
{
    private const float IconScreenSize = 40f;
    private const float BoundsThickness = 2f;

    // Bold, unmistakably-a-gizmo white — matches Unity's own default camera gizmo color (see the
    // architecture research this was built from) rather than something that could pass for actual scene
    // content.
    private static readonly System.Numerics.Vector4 BoundsColor = new(1f, 1f, 1f, 0.95f);

    private readonly ImGuiRenderer _imGui;
    private readonly Texture2D _icon;
    private readonly IntPtr _iconId;

    public CameraIconRenderer(GraphicsDevice graphicsDevice, ImGuiRenderer imGui)
    {
        _imGui = imGui;

        var iconPath = Path.Combine(AppContext.BaseDirectory, "EditorAssets", "camera_icon.png");
        using var stream = File.OpenRead(iconPath);
        _icon = Texture2D.FromStream(graphicsDevice, stream);
        _iconId = imGui.BindTexture(_icon);
    }

    /// <summary>Call once per frame after the Viewport panel has been drawn, with the same view matrix used
    /// to render the scene, the Viewport image's screen rect (icons are clipped to stay inside it, so a
    /// camera near the edge doesn't visually bleed onto the Outline/Inspector panels), the Viewport
    /// window's own draw list (ViewportResult.DrawList) — using ImGui's global foreground draw list here
    /// instead used to make the camera icon paint over popups/modals opened above the Viewport, since that
    /// list ignores window order entirely — and <paramref name="targetResolution"/>, the project's actual
    /// configured output size (ProjectSettings.WindowWidth/Height — the same numbers GameManifest ships and
    /// RuntimeGame's window opens at). That last one is what makes the bounds rectangle drawn below
    /// accurate: it has to match what the exported game will actually show, not an arbitrary shape sized to
    /// however wide the Editor's own Viewport panel happens to be right now.</summary>
    public void Draw(EditorState state, XnaMatrix viewMatrix, ImVec2 imageMin, ImVec2 imageMax, ImDrawListPtr drawList,
        XnaVector2 targetResolution)
    {
        drawList.PushClipRect(imageMin, imageMax, true);

        var mouse = ImGui.GetMousePos();
        bool clicked = ImGui.IsMouseClicked(ImGuiMouseButton.Left);

        foreach (var go in state.ActiveScene.GameObjects)
        {
            var camera = go.GetComponent<Camera2D>();
            if (camera == null) continue;

            DrawBounds(camera, viewMatrix, imageMin, drawList, targetResolution);

            var local = XnaVector2.Transform(go.Transform.Position, viewMatrix);
            var screenPos = imageMin + new ImVec2(local.X, local.Y);

            const float half = IconScreenSize * 0.5f;
            var min = screenPos - new ImVec2(half, half);
            var max = screenPos + new ImVec2(half, half);

            drawList.AddImage(_iconId, min, max);

            var labelPos = new ImVec2(screenPos.X - ImGui.CalcTextSize(go.Name).X * 0.5f, max.Y + 2);
            drawList.AddText(labelPos, ImGui.ColorConvertFloat4ToU32(new System.Numerics.Vector4(1, 1, 1, 0.85f)), go.Name);

            bool hovered = mouse.X >= min.X && mouse.X <= max.X && mouse.Y >= min.Y && mouse.Y <= max.Y;
            if (hovered && clicked)
                state.SelectGameObject(go);
        }

        drawList.PopClipRect();
    }

    /// <summary>The rectangle <paramref name="camera"/> actually captures at its current Zoom, in world
    /// units, is <paramref name="targetResolution"/> / Zoom on each axis (Zoom scales world→screen the same
    /// way GetViewMatrix does, so this is exactly its inverse) — bigger Zoom means a smaller visible slice
    /// of the world, shrinking these lines to match, the same relationship Unity's own Camera "Size" gizmo
    /// has to its orthographic size. Rotated by the camera's own Transform.Rotation (positive, not negated
    /// the way GetViewMatrix's view-space rotation is — these four corners are being placed IN world space
    /// here, not transformed OUT of it) so a rotated camera's gizmo tilts to match what it actually frames.</summary>
    private static void DrawBounds(Camera2D camera, XnaMatrix viewMatrix, ImVec2 imageMin, ImDrawListPtr drawList,
        XnaVector2 targetResolution)
    {
        if (camera.Zoom <= 0f) return;

        var visibleSize = targetResolution / camera.Zoom;
        var center = camera.Transform.Position;
        float rot = camera.Transform.Rotation;
        float cos = MathF.Cos(rot), sin = MathF.Sin(rot);

        XnaVector2 Corner(float sx, float sy)
        {
            var local = new XnaVector2(sx * visibleSize.X * 0.5f, sy * visibleSize.Y * 0.5f);
            var rotated = new XnaVector2(local.X * cos - local.Y * sin, local.X * sin + local.Y * cos);
            return center + rotated;
        }

        ImVec2 ToScreen(XnaVector2 worldPos)
        {
            var local = XnaVector2.Transform(worldPos, viewMatrix);
            return imageMin + new ImVec2(local.X, local.Y);
        }

        var c0 = ToScreen(Corner(-1, -1));
        var c1 = ToScreen(Corner(1, -1));
        var c2 = ToScreen(Corner(1, 1));
        var c3 = ToScreen(Corner(-1, 1));

        uint col = ImGui.ColorConvertFloat4ToU32(BoundsColor);
        drawList.AddLine(c0, c1, col, BoundsThickness);
        drawList.AddLine(c1, c2, col, BoundsThickness);
        drawList.AddLine(c2, c3, col, BoundsThickness);
        drawList.AddLine(c3, c0, col, BoundsThickness);
    }

    public void Dispose()
    {
        _imGui.UnbindTexture(_iconId);
        _icon.Dispose();
    }
}
