using System.Numerics;
using ImGuiNET;

namespace MyEngine.Editor;

/// <summary>
/// Small flat-style icons drawn directly with ImDrawList primitives (lines, filled triangles, filled
/// rects, circles) rather than loaded from image files — this project has no bundled icon assets, and
/// generating them procedurally avoids needing to create/manage Texture2D objects for something this
/// simple. Every icon is drawn proportionally to whatever (min,max) box it's given, so the same function
/// works for both the small toolbar buttons and the larger Content Browser tiles.
///
/// Deliberately restricted to draw-list calls already proven to compile in this project (AddLine,
/// AddTriangleFilled, AddRectFilled, AddCircleFilled, AddCircle) rather than reaching for less-common
/// ImDrawList methods (PathArcTo, the rounding overload of AddRectFilled, etc.) this project hasn't
/// exercised yet.
/// </summary>
public static class EditorIcons
{
    // ---------------------------------------------------------------- shared button widget

    /// <summary>An icon-only button: draws a normal ImGui button (so it gets the theme's hover/active/
    /// background colors and correct hit-testing) with no visible label, then draws the icon centered on
    /// top of it. Returns true the frame it's clicked, exactly like ImGui.Button.</summary>
    public static bool Button(string id, Vector2 size, bool active, Action<ImDrawListPtr, Vector2, Vector2, uint> drawIcon)
    {
        Vector2 min = ImGui.GetCursorScreenPos();

        if (active) ImGui.PushStyleColor(ImGuiCol.Button, ImGui.GetStyle().Colors[(int)ImGuiCol.HeaderActive]);
        bool clicked = ImGui.Button("##" + id, size);
        if (active) ImGui.PopStyleColor();

        Vector2 max = min + size;
        var drawList = ImGui.GetForegroundDrawList();
        uint color = ImGui.ColorConvertFloat4ToU32(ImGui.GetStyle().Colors[(int)ImGuiCol.Text]);
        drawIcon(drawList, min, max, color);

        return clicked;
    }

    // ---------------------------------------------------------------- geometry helpers

    private static Vector2 Center(Vector2 min, Vector2 max) => (min + max) * 0.5f;
    private static float ShortSide(Vector2 min, Vector2 max) => MathF.Min(max.X - min.X, max.Y - min.Y);

    /// <summary>A curved arrow swept between two angles (radians) around <paramref name="center"/>, with an
    /// arrowhead at the end of the sweep — the shared shape behind Undo/Redo/Rotate, which only differ in
    /// how far and which direction the sweep goes.</summary>
    private static void CurvedArrow(ImDrawListPtr dl, Vector2 center, float radius, float startAngle, float endAngle, uint color, float thickness = 2f)
    {
        const int segments = 14;
        Vector2 previous = center + new Vector2(MathF.Cos(startAngle), MathF.Sin(startAngle)) * radius;
        for (int i = 1; i <= segments; i++)
        {
            float t = startAngle + (endAngle - startAngle) * i / segments;
            Vector2 point = center + new Vector2(MathF.Cos(t), MathF.Sin(t)) * radius;
            dl.AddLine(previous, point, color, thickness);
            previous = point;
        }

        // Arrowhead tangent to the circle at the end of the sweep.
        Vector2 tip = center + new Vector2(MathF.Cos(endAngle), MathF.Sin(endAngle)) * radius;
        Vector2 tangent = new Vector2(-MathF.Sin(endAngle), MathF.Cos(endAngle));
        if (endAngle < startAngle) tangent = -tangent;
        Vector2 perp = new Vector2(-tangent.Y, tangent.X);

        float arrowLength = radius * 0.55f;
        float arrowWidth = radius * 0.42f;
        Vector2 back = tip - tangent * arrowLength * 0.3f;
        dl.AddTriangleFilled(tip + tangent * arrowLength * 0.7f, back + perp * arrowWidth, back - perp * arrowWidth, color);
    }

    // ---------------------------------------------------------------- toolbar icons

    public static void Play(ImDrawListPtr dl, Vector2 min, Vector2 max, uint color)
    {
        Vector2 c = Center(min, max);
        float s = ShortSide(min, max) * 0.30f;
        dl.AddTriangleFilled(c + new Vector2(-s * 0.6f, -s), c + new Vector2(-s * 0.6f, s), c + new Vector2(s * 0.9f, 0f), color);
    }

    public static void Stop(ImDrawListPtr dl, Vector2 min, Vector2 max, uint color)
    {
        Vector2 c = Center(min, max);
        float s = ShortSide(min, max) * 0.22f;
        dl.AddRectFilled(c - new Vector2(s, s), c + new Vector2(s, s), color);
    }

    public static void Undo(ImDrawListPtr dl, Vector2 min, Vector2 max, uint color)
    {
        Vector2 c = Center(min, max);
        float r = ShortSide(min, max) * 0.28f;
        CurvedArrow(dl, c, r, MathF.PI * 0.15f, MathF.PI * 1.65f, color);
    }

    public static void Redo(ImDrawListPtr dl, Vector2 min, Vector2 max, uint color)
    {
        Vector2 c = Center(min, max);
        float r = ShortSide(min, max) * 0.28f;
        CurvedArrow(dl, c, r, MathF.PI * 0.85f, MathF.PI * -0.65f, color);
    }

    public static void Rotate(ImDrawListPtr dl, Vector2 min, Vector2 max, uint color)
    {
        Vector2 c = Center(min, max);
        float r = ShortSide(min, max) * 0.30f;
        CurvedArrow(dl, c, r, -MathF.PI * 0.35f, MathF.PI * 1.05f, color, thickness: 2.2f);
    }

    public static void Move(ImDrawListPtr dl, Vector2 min, Vector2 max, uint color)
    {
        Vector2 c = Center(min, max);
        float s = ShortSide(min, max) * 0.32f;
        float a = s * 0.30f;   // arrowhead length
        float half = s * 0.18f; // arrowhead half-width

        dl.AddLine(c + new Vector2(-s, 0f), c + new Vector2(s, 0f), color, 2f);
        dl.AddLine(c + new Vector2(0f, -s), c + new Vector2(0f, s), color, 2f);

        dl.AddTriangleFilled(c + new Vector2(s, 0f), c + new Vector2(s - a, -half), c + new Vector2(s - a, half), color);
        dl.AddTriangleFilled(c + new Vector2(-s, 0f), c + new Vector2(-s + a, -half), c + new Vector2(-s + a, half), color);
        dl.AddTriangleFilled(c + new Vector2(0f, -s), c + new Vector2(-half, -s + a), c + new Vector2(half, -s + a), color);
        dl.AddTriangleFilled(c + new Vector2(0f, s), c + new Vector2(-half, s - a), c + new Vector2(half, s - a), color);
    }

    public static void Scale(ImDrawListPtr dl, Vector2 min, Vector2 max, uint color)
    {
        Vector2 c = Center(min, max);
        float half = ShortSide(min, max) * 0.30f;
        float handle = half * 0.32f;

        Vector2 tl = c + new Vector2(-half, -half);
        Vector2 tr = c + new Vector2(half, -half);
        Vector2 bl = c + new Vector2(-half, half);
        Vector2 br = c + new Vector2(half, half);

        dl.AddLine(tl, tr, color, 1.5f);
        dl.AddLine(tr, br, color, 1.5f);
        dl.AddLine(br, bl, color, 1.5f);
        dl.AddLine(bl, tl, color, 1.5f);

        dl.AddRectFilled(tl - new Vector2(handle, handle), tl + new Vector2(handle, handle), color);
        dl.AddRectFilled(tr - new Vector2(handle, handle), tr + new Vector2(handle, handle), color);
        dl.AddRectFilled(bl - new Vector2(handle, handle), bl + new Vector2(handle, handle), color);
        dl.AddRectFilled(br - new Vector2(handle, handle), br + new Vector2(handle, handle), color);
    }

    public static void Grid(ImDrawListPtr dl, Vector2 min, Vector2 max, uint color)
    {
        Vector2 c = Center(min, max);
        float s = ShortSide(min, max) * 0.32f;
        float x0 = c.X - s, x1 = c.X - s / 3f, x2 = c.X + s / 3f, x3 = c.X + s;
        float y0 = c.Y - s, y1 = c.Y - s / 3f, y2 = c.Y + s / 3f, y3 = c.Y + s;

        dl.AddLine(new Vector2(x0, y0), new Vector2(x3, y0), color, 1f);
        dl.AddLine(new Vector2(x3, y0), new Vector2(x3, y3), color, 1f);
        dl.AddLine(new Vector2(x3, y3), new Vector2(x0, y3), color, 1f);
        dl.AddLine(new Vector2(x0, y3), new Vector2(x0, y0), color, 1f);

        dl.AddLine(new Vector2(x1, y0), new Vector2(x1, y3), color, 1f);
        dl.AddLine(new Vector2(x2, y0), new Vector2(x2, y3), color, 1f);
        dl.AddLine(new Vector2(x0, y1), new Vector2(x3, y1), color, 1f);
        dl.AddLine(new Vector2(x0, y2), new Vector2(x3, y2), color, 1f);
    }

    public static void Colliders(ImDrawListPtr dl, Vector2 min, Vector2 max, uint color)
    {
        Vector2 c = Center(min, max);
        float s = ShortSide(min, max) * 0.30f;

        Vector2 circleCenter = c + new Vector2(-s * 0.28f, 0f);
        dl.AddCircle(circleCenter, s * 0.5f, color, 16, 1.5f);

        Vector2 boxCenter = c + new Vector2(s * 0.32f, 0f);
        float half = s * 0.42f;
        Vector2 tl = boxCenter + new Vector2(-half, -half);
        Vector2 tr = boxCenter + new Vector2(half, -half);
        Vector2 bl = boxCenter + new Vector2(-half, half);
        Vector2 br = boxCenter + new Vector2(half, half);
        dl.AddLine(tl, tr, color, 1.5f);
        dl.AddLine(tr, br, color, 1.5f);
        dl.AddLine(br, bl, color, 1.5f);
        dl.AddLine(bl, tl, color, 1.5f);
    }

    // ---------------------------------------------------------------- content browser icons

    public static void Folder(ImDrawListPtr dl, Vector2 min, Vector2 max, uint color)
    {
        float w = max.X - min.X;
        float h = max.Y - min.Y;
        float tabHeight = h * 0.16f;
        float tabWidth = w * 0.55f;
        float bodyTop = min.Y + tabHeight;

        dl.AddRectFilled(new Vector2(min.X, min.Y + tabHeight * 0.25f), new Vector2(min.X + tabWidth, bodyTop + 1f), color);
        dl.AddRectFilled(new Vector2(min.X, bodyTop), new Vector2(max.X, max.Y), color);
    }

    public static void FolderUp(ImDrawListPtr dl, Vector2 min, Vector2 max, uint color)
    {
        Folder(dl, min, max, (color & 0x00FFFFFFu) | 0x60000000u); // same color, much more transparent (dimmed backdrop)

        Vector2 c = Center(min, max) + new Vector2(0f, ShortSide(min, max) * 0.08f);
        float s = ShortSide(min, max) * 0.16f;
        Vector2 apex = c + new Vector2(0f, -s);
        Vector2 baseLeft = c + new Vector2(-s * 0.9f, s * 0.5f);
        Vector2 baseRight = c + new Vector2(s * 0.9f, s * 0.5f);
        dl.AddTriangleFilled(apex, baseLeft, baseRight, color);
    }

    public static void Scene(ImDrawListPtr dl, Vector2 min, Vector2 max, uint color)
    {
        float margin = (max.X - min.X) * 0.18f;
        Vector2 fMin = min + new Vector2(margin, margin);
        Vector2 fMax = max - new Vector2(margin, margin);

        dl.AddLine(new Vector2(fMin.X, fMin.Y), new Vector2(fMax.X, fMin.Y), color, 1f);
        dl.AddLine(new Vector2(fMax.X, fMin.Y), new Vector2(fMax.X, fMax.Y), color, 1f);
        dl.AddLine(new Vector2(fMax.X, fMax.Y), new Vector2(fMin.X, fMax.Y), color, 1f);
        dl.AddLine(new Vector2(fMin.X, fMax.Y), new Vector2(fMin.X, fMin.Y), color, 1f);

        Vector2 size = fMax - fMin;
        dl.AddCircleFilled(fMin + new Vector2(size.X * 0.28f, size.Y * 0.3f), MathF.Min(size.X, size.Y) * 0.12f, color);

        Vector2 p0 = new Vector2(fMin.X + 1f, fMax.Y - 1f);
        Vector2 p1 = new Vector2(fMin.X + size.X * 0.5f, fMin.Y + size.Y * 0.35f);
        Vector2 p2 = new Vector2(fMax.X - 1f, fMax.Y - 1f);
        dl.AddTriangleFilled(p0, p1, p2, color);
    }

    public static void Prefab(ImDrawListPtr dl, Vector2 min, Vector2 max, uint color)
    {
        Vector2 c = Center(min, max);
        float r = ShortSide(min, max) * 0.34f;

        Vector2 HexPoint(int i)
        {
            float angle = -MathF.PI / 2f + i * (MathF.PI / 3f);
            return c + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * r;
        }

        Vector2 p0 = HexPoint(0), p1 = HexPoint(1), p2 = HexPoint(2);
        Vector2 p3 = HexPoint(3), p4 = HexPoint(4), p5 = HexPoint(5);

        dl.AddLine(p0, p1, color, 1.5f);
        dl.AddLine(p1, p2, color, 1.5f);
        dl.AddLine(p2, p3, color, 1.5f);
        dl.AddLine(p3, p4, color, 1.5f);
        dl.AddLine(p4, p5, color, 1.5f);
        dl.AddLine(p5, p0, color, 1.5f);

        dl.AddLine(c, p0, color, 1.5f);
        dl.AddLine(c, p2, color, 1.5f);
        dl.AddLine(c, p4, color, 1.5f);
    }

    public static void Script(ImDrawListPtr dl, Vector2 min, Vector2 max, uint color)
    {
        float marginX = (max.X - min.X) * 0.24f;
        float marginY = (max.Y - min.Y) * 0.12f;
        Vector2 pMin = min + new Vector2(marginX, marginY);
        Vector2 pMax = max - new Vector2(marginX, marginY);

        dl.AddLine(new Vector2(pMin.X, pMin.Y), new Vector2(pMax.X, pMin.Y), color, 1f);
        dl.AddLine(new Vector2(pMax.X, pMin.Y), new Vector2(pMax.X, pMax.Y), color, 1f);
        dl.AddLine(new Vector2(pMax.X, pMax.Y), new Vector2(pMin.X, pMax.Y), color, 1f);
        dl.AddLine(new Vector2(pMin.X, pMax.Y), new Vector2(pMin.X, pMin.Y), color, 1f);

        Vector2 size = pMax - pMin;
        for (int i = 1; i <= 3; i++)
        {
            float y = pMin.Y + size.Y * i / 4f;
            dl.AddLine(new Vector2(pMin.X + size.X * 0.18f, y), new Vector2(pMax.X - size.X * 0.18f, y), color, 1f);
        }
    }
}
