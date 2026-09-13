using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace MyEngine.Editor;

public enum PrimitiveShape { Square, Circle, Triangle, Capsule, Star }

/// <summary>
/// Generates the actual PNG texture assets behind the GameObject menu's 5 basic shapes (see
/// EditorApp.DrawGameObjectMenu). Each shape becomes a real, file-backed texture under
/// Assets/Primitives/&lt;Shape&gt;.png the first time it's requested, rather than a texture-less
/// SpriteRenderer using the placeholder-rectangle draw path — a placeholder-mode sprite is
/// indistinguishable in the Inspector from "nobody assigned a texture yet", which would be a confusing
/// thing for an intentional primitive to look like. Every shape after the first request for it reuses the
/// same file, exactly as if the user had created one sprite asset by hand and dragged it onto several
/// GameObjects.
///
/// All 5 shapes go through the same white-RGB/shaped-alpha convention every other sprite in the engine
/// uses, so SpriteRenderer.Color tinting works on them exactly like it would on hand-authored art. Adding a
/// 6th shape later means one more enum value, one more branch in Coverage, and one more menu item — nothing
/// else in the pipeline (Content Browser, Inspector, serialization, RuntimeAssets) needs to change, since
/// as far as the rest of the engine is concerned this is just a normal PNG asset.
/// </summary>
public static class PrimitiveShapeGenerator
{
    private const int Size = 128;

    /// <summary>Returns the project-relative Assets path for <paramref name="shape"/>'s texture, generating
    /// Assets/Primitives/&lt;Shape&gt;.png (and the Primitives folder itself) first if it doesn't already
    /// exist. Safe to call repeatedly — an existing file is left untouched and its path just returned.</summary>
    public static string GetOrCreateTexturePath(PrimitiveShape shape, string assetsRoot, GraphicsDevice graphicsDevice)
    {
        var relativePath = $"Primitives/{shape}.png";
        var fullPath = Path.Combine(assetsRoot, "Primitives", $"{shape}.png");

        if (!File.Exists(fullPath))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            var pixels = Rasterize(shape);

            using var texture = new Texture2D(graphicsDevice, Size, Size);
            texture.SetData(pixels);
            using var stream = File.Create(fullPath);
            texture.SaveAsPng(stream, Size, Size);
        }

        return relativePath;
    }

    private static Color[] Rasterize(PrimitiveShape shape)
    {
        var pixels = new Color[Size * Size];
        for (int y = 0; y < Size; y++)
        {
            for (int x = 0; x < Size; x++)
            {
                // Sampled at the pixel center, matching standard rasterization convention.
                float coverage = Coverage(shape, x + 0.5f, y + 0.5f);
                pixels[y * Size + x] = new Color(255, 255, 255, (int)(Clamp01(coverage) * 255f));
            }
        }
        return pixels;
    }

    /// <summary>0 (fully outside) to 1 (fully inside), with a roughly 1px-wide antialiased edge — analytic
    /// distance falloff for the round shapes (Circle/Capsule), 4x4 supersampling for the polygon shapes
    /// (Triangle/Star), since a straight-edged polygon has no one single "distance to edge" formula as
    /// simple as a circle's.</summary>
    private static float Coverage(PrimitiveShape shape, float px, float py)
    {
        const float margin = Size * 0.08f;

        switch (shape)
        {
            case PrimitiveShape.Square:
                return 1f; // fills the whole texture edge-to-edge — a square needs no margin or AA

            case PrimitiveShape.Circle:
            {
                float radius = Size * 0.5f - margin;
                float dist = Distance(px, py, Size * 0.5f, Size * 0.5f);
                return radius - dist + 0.5f;
            }

            case PrimitiveShape.Capsule:
            {
                float radius = Size * 0.28f;
                float halfSpan = Size * 0.5f - margin - radius;
                float cy = Size * 0.5f;
                float dist = DistanceToSegment(px, py, Size * 0.5f - halfSpan, cy, Size * 0.5f + halfSpan, cy);
                return radius - dist + 0.5f;
            }

            case PrimitiveShape.Triangle:
                return Supersample(px, py, TrianglePoints());

            case PrimitiveShape.Star:
                return Supersample(px, py, StarPoints());

            default:
                return 0f;
        }
    }

    private static (float x, float y)[] TrianglePoints()
    {
        const float margin = Size * 0.08f;
        float top = margin, bottom = Size - margin, left = margin, right = Size - margin;
        float midX = Size * 0.5f;
        return new[] { (midX, top), (right, bottom), (left, bottom) };
    }

    private static (float x, float y)[] StarPoints()
    {
        const int points = 5;
        const float margin = Size * 0.06f;
        float centerX = Size * 0.5f, centerY = Size * 0.5f;
        float outerR = Size * 0.5f - margin;
        float innerR = outerR * 0.42f;

        var verts = new (float x, float y)[points * 2];
        for (int i = 0; i < points * 2; i++)
        {
            // starts pointing straight up, matching the usual orientation for a "star" icon
            float angle = -MathF.PI / 2f + i * MathF.PI / points;
            float r = i % 2 == 0 ? outerR : innerR;
            verts[i] = (centerX + MathF.Cos(angle) * r, centerY + MathF.Sin(angle) * r);
        }
        return verts;
    }

    /// <summary>4x4 sub-samples per pixel, averaged into a 0..1 coverage value — a simple, general
    /// antialiasing approach that works the same for any polygon, convex or concave (a star's points make
    /// it concave, which rules out the simpler per-edge analytic approach used for Circle/Capsule above).</summary>
    private static float Supersample(float px, float py, (float x, float y)[] polygon)
    {
        const int sub = 4;
        int inside = 0;
        for (int sy = 0; sy < sub; sy++)
        {
            for (int sx = 0; sx < sub; sx++)
            {
                float sampleX = px - 0.5f + (sx + 0.5f) / sub;
                float sampleY = py - 0.5f + (sy + 0.5f) / sub;
                if (PointInPolygon(sampleX, sampleY, polygon)) inside++;
            }
        }
        return inside / (float)(sub * sub);
    }

    /// <summary>Standard even-odd ray-casting point-in-polygon test. Works for both convex (triangle) and
    /// concave (star) polygons, unlike a per-edge "same side of every edge" test, which only works for
    /// convex shapes.</summary>
    private static bool PointInPolygon(float px, float py, (float x, float y)[] polygon)
    {
        bool inside = false;
        int j = polygon.Length - 1;
        for (int i = 0; i < polygon.Length; i++)
        {
            var (xi, yi) = polygon[i];
            var (xj, yj) = polygon[j];
            bool crosses = yi > py != yj > py;
            if (crosses && px < (xj - xi) * (py - yi) / (yj - yi) + xi)
                inside = !inside;
            j = i;
        }
        return inside;
    }

    private static float Distance(float x1, float y1, float x2, float y2) =>
        MathF.Sqrt((x2 - x1) * (x2 - x1) + (y2 - y1) * (y2 - y1));

    private static float DistanceToSegment(float px, float py, float ax, float ay, float bx, float by)
    {
        float dx = bx - ax, dy = by - ay;
        float lengthSq = dx * dx + dy * dy;
        float t = lengthSq > 0f ? Math.Clamp(((px - ax) * dx + (py - ay) * dy) / lengthSq, 0f, 1f) : 0f;
        float closestX = ax + t * dx, closestY = ay + t * dy;
        return Distance(px, py, closestX, closestY);
    }

    private static float Clamp01(float v) => Math.Clamp(v, 0f, 1f);
}
