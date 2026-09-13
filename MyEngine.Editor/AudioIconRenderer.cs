using ImGuiNET;
using MyEngine.Core.Audio;
using XnaVector2 = Microsoft.Xna.Framework.Vector2;
using XnaMatrix = Microsoft.Xna.Framework.Matrix;
using ImVec2 = System.Numerics.Vector2;

namespace MyEngine.Editor;

/// <summary>
/// Draws a speaker icon over every AudioSource in the active scene, so a sound — which has nothing to show
/// on its own, unlike a SpriteRenderer — is still visible and selectable in the Viewport. Reuses
/// EditorIcons.Audio (the same glyph AssetType.Audio tiles use in the Content Browser) rather than
/// bundling a separate texture the way CameraIconRenderer does — the shape is simple enough that a
/// texture would just be extra asset/lifecycle overhead for no visual benefit.
/// </summary>
public static class AudioIconRenderer
{
    private const float IconScreenSize = 32f;

    /// <summary>Call once per frame after the Viewport panel has been drawn — same call shape as
    /// CameraIconRenderer.Draw and ColliderGizmoRenderer.Draw, using the Viewport window's own draw list
    /// (ViewportResult.DrawList) so these icons stay part of that window's normal paint order instead of
    /// bleeding on top of popups/modals opened above the Viewport.</summary>
    public static void Draw(EditorState state, XnaMatrix viewMatrix, ImVec2 imageMin, ImVec2 imageMax, ImDrawListPtr drawList)
    {
        drawList.PushClipRect(imageMin, imageMax, true);

        var mouse = ImGui.GetMousePos();
        bool clicked = ImGui.IsMouseClicked(ImGuiMouseButton.Left);
        uint iconColor = ImGui.ColorConvertFloat4ToU32(new System.Numerics.Vector4(1f, 1f, 1f, 0.95f));
        uint badgeColor = ImGui.ColorConvertFloat4ToU32(new System.Numerics.Vector4(0.08f, 0.09f, 0.11f, 0.75f));

        foreach (var go in state.ActiveScene.GameObjects)
        {
            var audio = go.GetComponent<AudioSource>();
            if (audio == null) continue;

            var local = XnaVector2.Transform(go.Transform.Position, viewMatrix);
            var screenPos = imageMin + new ImVec2(local.X, local.Y);

            const float half = IconScreenSize * 0.5f;
            var min = screenPos - new ImVec2(half, half);
            var max = screenPos + new ImVec2(half, half);

            drawList.AddCircleFilled(screenPos, half, badgeColor);
            EditorIcons.Audio(drawList, min, max, iconColor);

            var labelPos = new ImVec2(screenPos.X - ImGui.CalcTextSize(go.Name).X * 0.5f, max.Y + 2);
            drawList.AddText(labelPos, iconColor, go.Name);

            bool hovered = mouse.X >= min.X && mouse.X <= max.X && mouse.Y >= min.Y && mouse.Y <= max.Y;
            if (hovered && clicked)
                state.SelectGameObject(go);
        }

        drawList.PopClipRect();
    }
}
