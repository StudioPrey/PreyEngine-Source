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
    /// camera near the edge doesn't visually bleed onto the Hierarchy/Inspector panels), and the Viewport
    /// window's own draw list (ViewportResult.DrawList) — using ImGui's global foreground draw list here
    /// instead used to make the camera icon paint over popups/modals opened above the Viewport, since that
    /// list ignores window order entirely.</summary>
    public void Draw(EditorState state, XnaMatrix viewMatrix, ImVec2 imageMin, ImVec2 imageMax, ImDrawListPtr drawList)
    {
        drawList.PushClipRect(imageMin, imageMax, true);

        var mouse = ImGui.GetMousePos();
        bool clicked = ImGui.IsMouseClicked(ImGuiMouseButton.Left);

        foreach (var go in state.ActiveScene.GameObjects)
        {
            var camera = go.GetComponent<Camera2D>();
            if (camera == null) continue;

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

    public void Dispose()
    {
        _imGui.UnbindTexture(_iconId);
        _icon.Dispose();
    }
}
