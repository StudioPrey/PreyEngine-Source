using System.Text.Json;

namespace MyEngine.Editor;

/// <summary>
/// The pure-data half of the Editor's icon atlas: parses <c>icons.json</c> (written by
/// <c>tools/IconBake/bake_icons.py</c>) and decides which pre-rendered pixel size to draw an icon at. No
/// MonoGame or ImGui dependency on purpose, so the rules below can be tested without a GPU — the texture and
/// draw-list half lives in <see cref="EditorIconAtlas"/>.
///
/// <para><b>The pixel-size rule.</b> The atlas holds each icon rasterized straight from its vector master at a
/// ladder of sizes (12, 14, 16 … 32, then a few larger steps). At draw time the icon's box is matched to the
/// nearest ladder size and, when that is within <see cref="SnapTolerancePixels"/>, the icon is drawn at EXACTLY
/// that size, so every texel lands on one screen pixel — no GPU resampling, which is what made small icons look
/// soft. Below 32px the ladder step is 2, so any size the UI draws there always snaps; above it, an icon is
/// drawn from the next size up and only shrunk slightly.</para>
/// </summary>
public sealed class EditorIconAtlasLayout
{
    /// <summary>Largest gap, in pixels, between a requested icon size and a ladder size for which the icon is
    /// drawn at the ladder size itself instead of being scaled to the requested one.</summary>
    public const float SnapTolerancePixels = 1f;

    private readonly Dictionary<string, (int X, int Y)[]> _cells;

    public int Width { get; }
    public int Height { get; }
    public int[] Sizes { get; }

    private EditorIconAtlasLayout(int width, int height, int[] sizes, Dictionary<string, (int X, int Y)[]> cells)
    {
        Width = width;
        Height = height;
        Sizes = sizes;
        _cells = cells;
    }

    public bool Contains(string iconName) => _cells.ContainsKey(iconName);

    public IEnumerable<string> IconNames => _cells.Keys;

    /// <summary>Top-left texel of <paramref name="iconName"/>'s <c>Sizes[sizeIndex]</c> square in the atlas.</summary>
    public bool TryGetCell(string iconName, int sizeIndex, out (int X, int Y) cell)
    {
        cell = default;
        if (!_cells.TryGetValue(iconName, out var cells) || sizeIndex < 0 || sizeIndex >= cells.Length) return false;
        cell = cells[sizeIndex];
        return true;
    }

    /// <summary>Which atlas size to draw an icon from, and how many pixels wide to draw it, for an icon box whose
    /// short side is <paramref name="targetPixels"/>.</summary>
    public (int SizeIndex, float DrawSize) PickSize(float targetPixels)
    {
        int nearest = 0;
        for (int i = 1; i < Sizes.Length; i++)
            if (MathF.Abs(Sizes[i] - targetPixels) < MathF.Abs(Sizes[nearest] - targetPixels)) nearest = i;

        if (MathF.Abs(Sizes[nearest] - targetPixels) <= SnapTolerancePixels)
            return (nearest, Sizes[nearest]);

        // Off the ladder: draw from the smallest size that is still >= the target (a small shrink looks far
        // better than any upscale); past the largest size there is nothing bigger, so upscale that one.
        for (int i = 0; i < Sizes.Length; i++)
            if (Sizes[i] >= targetPixels) return (i, targetPixels);
        return (Sizes.Length - 1, targetPixels);
    }

    /// <summary>Parses and validates the manifest. Throws <see cref="FormatException"/> with a readable message
    /// on any problem — a half-valid manifest would draw wrong icons rather than fail, so it is all-or-nothing.</summary>
    public static EditorIconAtlasLayout Parse(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            int version = root.GetProperty("version").GetInt32();
            if (version != 1) throw new FormatException($"unsupported icon atlas version {version} (this Editor reads version 1)");

            int width = root.GetProperty("width").GetInt32();
            int height = root.GetProperty("height").GetInt32();
            if (width <= 0 || height <= 0) throw new FormatException("atlas width/height must be positive");

            var sizes = root.GetProperty("sizes").EnumerateArray().Select(e => e.GetInt32()).ToArray();
            if (sizes.Length == 0) throw new FormatException("atlas lists no sizes");
            for (int i = 0; i < sizes.Length; i++)
                if (sizes[i] <= 0 || (i > 0 && sizes[i] <= sizes[i - 1]))
                    throw new FormatException("atlas sizes must be positive and strictly ascending");

            var cells = new Dictionary<string, (int X, int Y)[]>();
            foreach (var icon in root.GetProperty("icons").EnumerateObject())
            {
                var entries = icon.Value.EnumerateArray().ToArray();
                if (entries.Length != sizes.Length)
                    throw new FormatException($"icon '{icon.Name}' has {entries.Length} cells but the atlas lists {sizes.Length} sizes");

                var list = new (int X, int Y)[sizes.Length];
                for (int i = 0; i < sizes.Length; i++)
                {
                    int x = entries[i][0].GetInt32(), y = entries[i][1].GetInt32();
                    if (x < 0 || y < 0 || x + sizes[i] > width || y + sizes[i] > height)
                        throw new FormatException($"icon '{icon.Name}' size {sizes[i]} lies outside the {width}x{height} atlas");
                    list[i] = (x, y);
                }
                cells[icon.Name] = list;
            }

            if (cells.Count == 0) throw new FormatException("atlas contains no icons");
            return new EditorIconAtlasLayout(width, height, sizes, cells);
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or IndexOutOfRangeException)
        {
            throw new FormatException("icon atlas manifest is malformed: " + ex.Message, ex);
        }
    }
}
