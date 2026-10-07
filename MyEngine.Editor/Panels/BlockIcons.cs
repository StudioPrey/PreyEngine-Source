using System.Numerics;
using ImGuiNET;

namespace MyEngine.Editor.Panels;

/// <summary>
/// Icons for the Blocks panel's "Built-in" tiles (see BlocksPanel) and the Outline's per-GameObject row icons, in
/// the same style as EditorIcons: drawn from the baked image atlas (EditorIconAtlas) and, only if it isn't
/// available, from the original procedural fallback below — ImDrawList primitives already proven in this
/// codebase (AddLine, AddTriangleFilled, AddRectFilled). In that fallback, round shapes are a fan of thin
/// triangles instead of AddCircleFilled / PathArcTo. Each method has the same signature EditorIcons.Button
/// expects for its drawIcon parameter.
/// </summary>
public static class BlockIcons
{
    private const int FanSegments = 20;

    private static Vector2 Center(Vector2 min, Vector2 max) => (min + max) * 0.5f;
    private static float ShortSide(Vector2 min, Vector2 max) => MathF.Min(max.X - min.X, max.Y - min.Y);

    /// <summary>A filled disc as <see cref="FanSegments"/> triangles all sharing the center point.</summary>
    private static void FillDisc(ImDrawListPtr dl, Vector2 c, float r, uint color)
    {
        for (int i = 0; i < FanSegments; i++)
        {
            float a0 = MathF.Tau * i / FanSegments;
            float a1 = MathF.Tau * (i + 1) / FanSegments;
            dl.AddTriangleFilled(c,
                c + new Vector2(MathF.Cos(a0), MathF.Sin(a0)) * r,
                c + new Vector2(MathF.Cos(a1), MathF.Sin(a1)) * r,
                color);
        }
    }

    private static void Outline(ImDrawListPtr dl, Vector2 tl, Vector2 br, uint color, float thickness)
    {
        var tr = new Vector2(br.X, tl.Y);
        var bl = new Vector2(tl.X, br.Y);
        dl.AddLine(tl, tr, color, thickness);
        dl.AddLine(tr, br, color, thickness);
        dl.AddLine(br, bl, color, thickness);
        dl.AddLine(bl, tl, color, thickness);
    }

    /// <summary>An empty GameObject: just an outlined box with a small filled pivot dot.</summary>
    public static void Empty(ImDrawListPtr dl, Vector2 min, Vector2 max, uint color)
    {
        if (EditorIconAtlas.Draw(dl, "Empty", min, max, color)) return;
        Vector2 c = Center(min, max);
        float half = ShortSide(min, max) * 0.28f;
        Outline(dl, c - new Vector2(half, half), c + new Vector2(half, half), color, 1.5f);
        FillDisc(dl, c, half * 0.22f, color);
    }

    public static void Square(ImDrawListPtr dl, Vector2 min, Vector2 max, uint color)
    {
        if (EditorIconAtlas.Draw(dl, "Square", min, max, color)) return;
        Vector2 c = Center(min, max);
        float half = ShortSide(min, max) * 0.28f;
        dl.AddRectFilled(c - new Vector2(half, half), c + new Vector2(half, half), color);
    }

    public static void Circle(ImDrawListPtr dl, Vector2 min, Vector2 max, uint color)
    {
        if (EditorIconAtlas.Draw(dl, "Circle", min, max, color)) return;
        FillDisc(dl, Center(min, max), ShortSide(min, max) * 0.30f, color);
    }

    public static void Triangle(ImDrawListPtr dl, Vector2 min, Vector2 max, uint color)
    {
        if (EditorIconAtlas.Draw(dl, "Triangle", min, max, color)) return;
        Vector2 c = Center(min, max);
        float s = ShortSide(min, max) * 0.30f;
        dl.AddTriangleFilled(c + new Vector2(0f, -s), c + new Vector2(s, s * 0.8f), c + new Vector2(-s, s * 0.8f), color);
    }

    public static void Capsule(ImDrawListPtr dl, Vector2 min, Vector2 max, uint color)
    {
        if (EditorIconAtlas.Draw(dl, "Capsule", min, max, color)) return;
        Vector2 c = Center(min, max);
        float r = ShortSide(min, max) * 0.16f;
        float halfBody = ShortSide(min, max) * 0.14f;
        dl.AddRectFilled(c + new Vector2(-r, -halfBody), c + new Vector2(r, halfBody), color);
        FillDisc(dl, c + new Vector2(0f, -halfBody), r, color);
        FillDisc(dl, c + new Vector2(0f, halfBody), r, color);
    }

    /// <summary>A five-point star: a triangle fan over alternating outer/inner vertices (a star is
    /// star-shaped about its center, so a fan from the center covers it exactly).</summary>
    public static void Star(ImDrawListPtr dl, Vector2 min, Vector2 max, uint color)
    {
        if (EditorIconAtlas.Draw(dl, "Star", min, max, color)) return;
        Vector2 c = Center(min, max);
        float outer = ShortSide(min, max) * 0.32f;
        float inner = outer * 0.42f;
        const int points = 5;
        for (int i = 0; i < points * 2; i++)
        {
            float a0 = -MathF.PI / 2f + MathF.PI * i / points;
            float a1 = -MathF.PI / 2f + MathF.PI * (i + 1) / points;
            float r0 = i % 2 == 0 ? outer : inner;
            float r1 = i % 2 == 0 ? inner : outer;
            dl.AddTriangleFilled(c,
                c + new Vector2(MathF.Cos(a0), MathF.Sin(a0)) * r0,
                c + new Vector2(MathF.Cos(a1), MathF.Sin(a1)) * r1,
                color);
        }
    }

    /// <summary>Camera2D: a body box with a lens wedge on the right.</summary>
    public static void Camera(ImDrawListPtr dl, Vector2 min, Vector2 max, uint color)
    {
        if (EditorIconAtlas.Draw(dl, "Camera", min, max, color)) return;
        Vector2 c = Center(min, max);
        float s = ShortSide(min, max);
        Vector2 tl = c + new Vector2(-s * 0.30f, -s * 0.16f);
        Vector2 br = c + new Vector2(s * 0.10f, s * 0.16f);
        Outline(dl, tl, br, color, 1.5f);
        dl.AddTriangleFilled(new Vector2(br.X + s * 0.02f, c.Y), new Vector2(br.X + s * 0.20f, c.Y - s * 0.14f), new Vector2(br.X + s * 0.20f, c.Y + s * 0.14f), color);
    }

    /// <summary>SpriteAnimation: three film-strip frames in a row.</summary>
    public static void Animation(ImDrawListPtr dl, Vector2 min, Vector2 max, uint color)
    {
        if (EditorIconAtlas.Draw(dl, "Animation", min, max, color)) return;
        Vector2 c = Center(min, max);
        float s = ShortSide(min, max);
        float frame = s * 0.16f;
        float gap = s * 0.05f;
        for (int i = -1; i <= 1; i++)
        {
            float cx = c.X + i * (frame + gap);
            var tl = new Vector2(cx - frame * 0.5f, c.Y - frame * 0.7f);
            var br = new Vector2(cx + frame * 0.5f, c.Y + frame * 0.7f);
            if (i == 0) dl.AddRectFilled(tl, br, color);
            else Outline(dl, tl, br, color, 1.5f);
        }
    }
}
