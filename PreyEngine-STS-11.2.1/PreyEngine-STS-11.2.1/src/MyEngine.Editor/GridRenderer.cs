using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace MyEngine.Editor;

/// <summary>
/// Draws a world-space reference grid into the currently-bound render target, using the same SpriteBatch
/// pass as the scene itself (stretched 1x1-pixel lines, same technique SpriteRenderer uses for its
/// placeholder-rectangle mode). Must be called between SpriteBatch.Begin/End, before the scene's own
/// GameObjects draw — since we render with BackToFront sorting, layerDepth alone (not draw order) is
/// what actually guarantees the grid stays behind everything, but drawing it first keeps the code readable.
/// </summary>
public static class GridRenderer
{
    private const float MinScreenSpacing = 16f;   // world-grid step is doubled/halved until on-screen spacing is in this range
    private const float MaxScreenSpacing = 128f;
    private const float BaseWorldSpacing = 32f;
    private const int MajorEvery = 4;             // every 4th line is drawn brighter

    public static void Draw(SpriteBatch spriteBatch, Texture2D pixel, Matrix viewMatrix, Vector2 viewportSize, float zoom)
    {
        if (viewportSize.X <= 0 || viewportSize.Y <= 0) return;

        Matrix inverse;
        try { inverse = Matrix.Invert(viewMatrix); }
        catch { return; }

        // visible world-space bounds = the inverse-transformed corners of the render target
        var a = Vector2.Transform(Vector2.Zero, inverse);
        var b = Vector2.Transform(new Vector2(viewportSize.X, 0), inverse);
        var c = Vector2.Transform(new Vector2(0, viewportSize.Y), inverse);
        var d = Vector2.Transform(viewportSize, inverse);

        float minX = Min4(a.X, b.X, c.X, d.X), maxX = Max4(a.X, b.X, c.X, d.X);
        float minY = Min4(a.Y, b.Y, c.Y, d.Y), maxY = Max4(a.Y, b.Y, c.Y, d.Y);

        float safeZoom = MathF.Max(zoom, 0.0001f);
        float spacing = BaseWorldSpacing;
        while (spacing * safeZoom < MinScreenSpacing) spacing *= 2f;
        while (spacing * safeZoom > MaxScreenSpacing) spacing /= 2f;

        float thickness = 1f / safeZoom; // keep grid lines ~1px on screen regardless of zoom

        var minorColor = new Color(255, 255, 255, 22);
        var majorColor = new Color(255, 255, 255, 50);
        var xAxisColor = new Color(220, 90, 90, 190);
        var yAxisColor = new Color(100, 200, 110, 190);

        int startCol = (int)MathF.Floor(minX / spacing);
        int endCol = (int)MathF.Ceiling(maxX / spacing);
        for (int i = startCol; i <= endCol; i++)
        {
            float x = i * spacing;
            var color = i == 0 ? yAxisColor : (i % MajorEvery == 0 ? majorColor : minorColor);
            DrawLine(spriteBatch, pixel, new Vector2(x, minY), new Vector2(x, maxY), color, thickness);
        }

        int startRow = (int)MathF.Floor(minY / spacing);
        int endRow = (int)MathF.Ceiling(maxY / spacing);
        for (int j = startRow; j <= endRow; j++)
        {
            float y = j * spacing;
            var color = j == 0 ? xAxisColor : (j % MajorEvery == 0 ? majorColor : minorColor);
            DrawLine(spriteBatch, pixel, new Vector2(minX, y), new Vector2(maxX, y), color, thickness);
        }
    }

    private static void DrawLine(SpriteBatch spriteBatch, Texture2D pixel, Vector2 from, Vector2 to, Color color, float thickness)
    {
        var delta = to - from;
        float length = delta.Length();
        if (length < 0.0001f) return;
        float angle = MathF.Atan2(delta.Y, delta.X);

        // grid is drawn at layerDepth 1 (the maximum), guaranteeing it renders behind every GameObject
        // regardless of SortingOrder, since the scene is drawn with SpriteSortMode.BackToFront.
        // origin.Y = 0.5 centers the line's thickness on the from->to centerline rather than offsetting it.
        spriteBatch.Draw(pixel, from, null, color, angle, new Vector2(0f, 0.5f), new Vector2(length, thickness), SpriteEffects.None, 1f);
    }

    private static float Min4(float a, float b, float c, float d) => MathF.Min(MathF.Min(a, b), MathF.Min(c, d));
    private static float Max4(float a, float b, float c, float d) => MathF.Max(MathF.Max(a, b), MathF.Max(c, d));
}
