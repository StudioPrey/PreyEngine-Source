using System.Numerics;
using ImGuiNET;
using Microsoft.Xna.Framework.Graphics;
using MyEngine.EditorFramework;

namespace MyEngine.Editor;

/// <summary>
/// The Editor's icons as real images: one texture atlas (<c>EditorAssets/Icons/icons.png</c>) baked from the
/// vector masters in <c>EditorAssets/Icons/svg</c> by <c>tools/IconBake/bake_icons.py</c>. Replaces drawing every
/// icon frame-by-frame out of AddLine/AddTriangleFilled calls, whose hairlines and polygon-approximated curves
/// looked rough at any size the code hadn't been hand-tuned for.
///
/// <para>The atlas stores every icon as a WHITE mask (alpha = shape), so <see cref="Draw"/> tints it with the
/// same <c>color</c> the old draw-list icons received — hover/active/accent colors keep working unchanged, and
/// all <c>EditorIcons</c>/<c>BlockIcons</c> call sites (delegates with the same signature) are untouched.</para>
///
/// <para>Failure is never fatal: if the atlas is missing or invalid, <see cref="Draw"/> returns false and each
/// icon method falls back to its original procedural drawing, so the Editor is exactly as usable as before.</para>
/// </summary>
public static class EditorIconAtlas
{
    private static EditorIconAtlasLayout? _layout;
    private static IntPtr _textureId;

    public static bool IsLoaded => _layout != null;

    /// <summary>Loads the atlas once at startup (after the ImGui renderer exists). Problems are logged and leave
    /// <see cref="IsLoaded"/> false.</summary>
    public static void Load(GraphicsDevice graphicsDevice, ImGuiRenderer imGui, Action<string> log)
    {
        var folder = Path.Combine(AppContext.BaseDirectory, "EditorAssets", "Icons");
        var manifestPath = Path.Combine(folder, "icons.json");
        var imagePath = Path.Combine(folder, "icons.png");

        if (!File.Exists(manifestPath) || !File.Exists(imagePath))
        {
            log("WARNING: icon atlas not found in EditorAssets/Icons — using the built-in drawn icons instead.");
            return;
        }

        try
        {
            var layout = EditorIconAtlasLayout.Parse(File.ReadAllText(manifestPath));

            using var stream = File.OpenRead(imagePath);
            // KeepWhite instead of the default loader: MonoGame's default zeroes the color of fully transparent
            // texels, and bilinear filtering across an edge would then blend white toward black — a dark fringe
            // around every icon. With white kept everywhere, only alpha varies, which is exactly a clean mask.
            var texture = Texture2D.FromStream(graphicsDevice, stream, KeepWhite);

            if (texture.Width != layout.Width || texture.Height != layout.Height)
            {
                log($"WARNING: icons.png is {texture.Width}x{texture.Height} but icons.json describes {layout.Width}x{layout.Height} — " +
                    "using the built-in drawn icons instead. Re-run tools/IconBake/bake_icons.py.");
                return;
            }

            _textureId = imGui.BindTexture(texture);
            _layout = layout;
        }
        catch (Exception ex) when (ex is IOException or FormatException or InvalidOperationException or UnauthorizedAccessException)
        {
            log($"WARNING: could not load the icon atlas ({ex.Message}) — using the built-in drawn icons instead.");
        }
    }

    private static void KeepWhite(byte[] rgba)
    {
        for (int i = 0; i < rgba.Length; i += 4)
            rgba[i] = rgba[i + 1] = rgba[i + 2] = 255;
    }

    /// <summary>Draws <paramref name="iconName"/> into the box (<paramref name="min"/>,<paramref name="max"/>),
    /// tinted with <paramref name="color"/>. Returns false — drawing nothing — when the atlas isn't loaded or has
    /// no such icon, so the caller can fall back. Icon boxes are square everywhere in the Editor; a non-square
    /// box is fitted by its short side and centered.</summary>
    public static bool Draw(ImDrawListPtr drawList, string iconName, Vector2 min, Vector2 max, uint color)
    {
        var layout = _layout;
        if (layout == null) return false;

        Vector2 size = max - min;
        var (sizeIndex, drawSize) = layout.PickSize(MathF.Min(size.X, size.Y));
        if (!layout.TryGetCell(iconName, sizeIndex, out var cell)) return false;

        // Whole-pixel top-left: with drawSize == the baked size (the usual case) every texel then lands on
        // exactly one screen pixel.
        Vector2 center = (min + max) * 0.5f;
        var topLeft = new Vector2(MathF.Round(center.X - drawSize * 0.5f), MathF.Round(center.Y - drawSize * 0.5f));

        int baked = layout.Sizes[sizeIndex];
        var uv0 = new Vector2(cell.X / (float)layout.Width, cell.Y / (float)layout.Height);
        var uv1 = new Vector2((cell.X + baked) / (float)layout.Width, (cell.Y + baked) / (float)layout.Height);

        drawList.AddImage(_textureId, topLeft, topLeft + new Vector2(drawSize, drawSize), uv0, uv1, color);
        return true;
    }
}
