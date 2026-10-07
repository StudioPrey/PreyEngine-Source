using System.Numerics;
using ImGuiNET;

namespace MyEngine.Editor;

/// <summary>
/// The Editor's visual theme: a dark, flat, compact look modeled on Phaser Editor's — square-ish panels,
/// tight padding, a smaller UI font, neutral gray section headers, and a warm amber accent reserved for
/// selection / active state. Purely cosmetic — sets ImGuiStyle colors/sizes once at startup and never
/// touches any engine or editor logic.
///
/// The base grays carry a slight, deliberate cool-blue lean (not perfectly neutral R=G=B) rather than a
/// flat gray — a small, standard touch several professional dark editors use so the palette reads as
/// designed rather than a stock/default gray. This is a nudge, not a repaint: still the same panel
/// arrangement, still the same amber accent, just less flatly neutral.
///
/// Sticks to ImGuiCol/ImGuiStyleVar members confirmed present in this project's exact ImGui.NET build.
/// Two renames to be aware of if this file needs editing later: ImGuiCol_NavHighlight became NavCursor in
/// Dear ImGui 1.91.4, and ImGuiCol_TabActive/TabUnfocused/TabUnfocusedActive became TabSelected/TabDimmed/
/// TabDimmedSelected in 1.90.9. This project's ImGui.NET (1.91.6.1) is newer than both renames — it already
/// uses NavCursor and ImGuiChildFlags.Borders (renamed in 1.91.1) — so the NEW tab spellings are the ones
/// used below.
/// </summary>
public static class EditorTheme
{
    /// <summary>The UI font that ships with the Editor (EditorAssets/Fonts, copied next to the executable like the
    /// other Editor assets). Bundled rather than looked up in the OS so the Editor looks identical on every
    /// machine. Swap the file name here to change the font; its license text must ship beside it in the same
    /// folder (see Vazirmatn-OFL.txt).</summary>
    private const string BundledFontFile = "Vazirmatn-Medium.ttf";

    /// <summary>Only used when the bundled font file is missing (e.g. someone cleaned EditorAssets): common
    /// Windows system UI fonts, tried in order, loaded from the OS font directory. If none exist either
    /// (a non-Windows OS), ImGui's built-in bitmap font is used instead of failing.</summary>
    private static readonly string[] FallbackSystemFonts = { "segoeui.ttf", "calibri.ttf", "tahoma.ttf", "arial.ttf" };

    /// <summary>The UI text size in pixels. The atlas is baked at EXACTLY this size and displayed 1:1 — no
    /// FontGlobalScale. The previous approach (bake at 20px, scale the result down to 15px) made the GPU
    /// minify the glyph atlas with plain bilinear filtering, which is what left text soft; baking at the
    /// displayed size lets every glyph texel land on a screen pixel.</summary>
    private const float FontSize = 19f;

    /// <summary>Brightens the baked glyph coverage (scaled up, clamped at full opacity) so thin strokes keep
    /// some presence at UI sizes, without changing glyph shapes or spacing. 1.0 = untouched. Lower it toward 1.0
    /// if the text looks too heavy, raise it if a lighter font looks faint.</summary>
    private const float FontRasterizerMultiply = 1.5f;

    /// <summary>Call once at startup, before ImGuiRenderer.RebuildFontAtlas() — fonts must be added to the
    /// atlas before it's baked and uploaded to the GPU, so this can't happen afterward like the color/style
    /// changes in Apply() below can.
    ///
    /// <para>The method is <c>unsafe</c> because ImFontConfigPtr is a wrapper over a native pointer, and the
    /// constructor that creates it (ImGuiNative.ImFontConfig_ImFontConfig) returns one — that is the CS0214 an
    /// earlier attempt hit without an unsafe context. The project already allows unsafe code
    /// (AllowUnsafeBlocks).</para></summary>
    public static unsafe void ApplyFont()
    {
        var io = ImGui.GetIO();

        string bundledPath = Path.Combine(AppContext.BaseDirectory, "EditorAssets", "Fonts", BundledFontFile);
        if (File.Exists(bundledPath))
        {
            // AddFontFromFileTTF copies the config, so it can (and must) be destroyed right after. Everything
            // not set here keeps the ImFontConfig defaults: OversampleH=2, OversampleV=1, no pixel snapping.
            var config = new ImFontConfigPtr(ImGuiNative.ImFontConfig_ImFontConfig());
            config.RasterizerMultiply = FontRasterizerMultiply;
            io.Fonts.AddFontFromFileTTF(bundledPath, FontSize, config, GetGlyphRanges());
            config.Destroy();
            return;
        }

        string fontsDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);
        foreach (var fileName in FallbackSystemFonts)
        {
            string path = Path.Combine(fontsDirectory, fileName);
            if (File.Exists(path))
            {
                io.Fonts.AddFontFromFileTTF(path, FontSize);
                return;
            }
        }

        io.Fonts.AddFontDefault(); // no font file found at all — explicit fallback to ImGui's built-in bitmap font
    }

    /// <summary>Glyph ranges to bake: Basic Latin + Latin-1 (ImGui's own default set) plus General Punctuation
    /// U+2010–U+2027 (dashes, curly quotes, bullet, ellipsis) — the default set omits those, so text like the
    /// "—" and "…" in menu and log strings was drawing as "?". Returned as unmanaged memory because ImGui keeps
    /// the pointer until the atlas is built; it's allocated once, is 10 bytes, and is intentionally never freed.</summary>
    private static unsafe IntPtr GetGlyphRanges()
    {
        if (_glyphRanges != IntPtr.Zero) return _glyphRanges;

        // ImGui's format: pairs of (first, last) codepoints, terminated by a single 0.
        ushort[] ranges = { 0x0020, 0x00FF, 0x2010, 0x2027, 0 };
        var memory = System.Runtime.InteropServices.Marshal.AllocHGlobal(ranges.Length * sizeof(ushort));
        var target = (ushort*)memory;
        for (int i = 0; i < ranges.Length; i++) target[i] = ranges[i];

        _glyphRanges = memory;
        return _glyphRanges;
    }

    private static IntPtr _glyphRanges;

    public static void Apply()
    {
        // Establishes a complete, internally-consistent baseline for every ImGuiCol/style slot first —
        // including the many we never touch below — so nothing is left at ImGuiStyle's raw, unthemed
        // default. (This is also the fix for panel title bars going blank on defocus: that came from
        // TitleBg and TitleBgActive being set to the exact same color, which left focused/unfocused
        // panels visually indistinguishable in a way that read as the title "disappearing" — see the
        // explicit, distinct values given to both below.)
        ImGui.StyleColorsDark();

        var style = ImGui.GetStyle();
        style.Alpha = 1f;
        style.DisabledAlpha = 0.6f;

        // ---------------------------------------------------------------- shape

        style.WindowPadding = new Vector2(8f, 6f);
        style.FramePadding = new Vector2(6f, 3f);
        style.ItemSpacing = new Vector2(6f, 4f);
        style.ItemInnerSpacing = new Vector2(6f, 4f);
        style.IndentSpacing = 14f;
        style.ScrollbarSize = 12f;
        style.GrabMinSize = 10f;

        style.WindowBorderSize = 1f;
        style.ChildBorderSize = 1f;
        style.PopupBorderSize = 1f;
        style.FrameBorderSize = 0f;
        style.TabBorderSize = 0f;

        // Near-square corners throughout — panels butt up against each other edge to edge (see EditorLayout)
        // like Phaser Editor's, rather than floating as rounded cards.
        style.WindowRounding = 0f;
        style.ChildRounding = 0f;
        style.FrameRounding = 2f;
        style.PopupRounding = 2f;
        style.ScrollbarRounding = 4f;
        style.GrabRounding = 2f;
        style.TabRounding = 0f;

        // NOT a WindowMinSize floor — that clamps every window equally, including the Toolbar's own
        // deliberately-short forced height, which was quietly stretching it tall enough to cover the
        // panels underneath (reading as "panel title bars disappeared" / "panel goes see-through"). Left
        // at ImGui's own tiny floor instead; nothing here actually needs a bigger one since every fixed
        // window's size is already forced exactly, every frame, via EditorLayout.
        style.WindowMinSize = new Vector2(1f, 1f);

        style.WindowTitleAlign = new Vector2(0f, 0.5f);
        style.SeparatorTextBorderSize = 1f;

        // ---------------------------------------------------------------- palette
        // A dark charcoal base with a warm amber accent for selection/checked/active state, flat panels,
        // thin low-contrast borders — the overall direction asked for as a reference (a clean, purpose-built
        // dark editor rather than ImGui's stock purple-blue default).

        Vector4 bg0 = Rgb(18, 19, 23);    // deepest background (behind everything)
        Vector4 bg1 = Rgb(30, 31, 37);    // window/panel background
        Vector4 bg2 = Rgb(39, 40, 47);    // panel header / menu bar / title bar
        Vector4 bg3 = Rgb(49, 50, 59);    // frame background (inputs, checkboxes, buttons)
        Vector4 bg4 = Rgb(61, 63, 73);    // hovered frame
        Vector4 bg5 = Rgb(75, 77, 89);    // active/pressed frame

        Vector4 border = Rgb(8, 8, 9);
        Vector4 text = Rgb(225, 225, 228);
        Vector4 textDisabled = Rgb(120, 120, 125);

        Vector4 accent = Rgb(214, 154, 68);       // warm amber — selection, checkmarks, active accents
        Vector4 accentHover = Rgb(230, 172, 88);
        Vector4 accentActive = Rgb(196, 136, 54);

        var colors = style.Colors;

        colors[(int)ImGuiCol.Text] = text;
        colors[(int)ImGuiCol.TextDisabled] = textDisabled;

        colors[(int)ImGuiCol.WindowBg] = bg1;
        colors[(int)ImGuiCol.ChildBg] = bg1;
        colors[(int)ImGuiCol.PopupBg] = bg2;
        colors[(int)ImGuiCol.Border] = border;
        colors[(int)ImGuiCol.BorderShadow] = Rgba(0, 0, 0, 0);

        colors[(int)ImGuiCol.FrameBg] = bg3;
        colors[(int)ImGuiCol.FrameBgHovered] = bg4;
        colors[(int)ImGuiCol.FrameBgActive] = bg5;

        colors[(int)ImGuiCol.TitleBg] = bg2;
        colors[(int)ImGuiCol.TitleBgActive] = Vector4.Lerp(bg2, accent, 0.14f);
        colors[(int)ImGuiCol.TitleBgCollapsed] = bg1;

        // Tabs (Blocks panel's category tabs): the unselected ones sit flush with the panel, the selected one
        // is lifted to the frame color — the same "one raised tab" look Phaser Editor's panels have.
        colors[(int)ImGuiCol.Tab] = bg1;
        colors[(int)ImGuiCol.TabHovered] = bg4;
        colors[(int)ImGuiCol.TabSelected] = bg3;
        colors[(int)ImGuiCol.TabDimmed] = bg1;
        colors[(int)ImGuiCol.TabDimmedSelected] = bg2;
        colors[(int)ImGuiCol.MenuBarBg] = bg2;

        colors[(int)ImGuiCol.ScrollbarBg] = bg0;
        colors[(int)ImGuiCol.ScrollbarGrab] = bg4;
        colors[(int)ImGuiCol.ScrollbarGrabHovered] = bg5;
        colors[(int)ImGuiCol.ScrollbarGrabActive] = accent;

        colors[(int)ImGuiCol.CheckMark] = accent;
        colors[(int)ImGuiCol.SliderGrab] = accent;
        colors[(int)ImGuiCol.SliderGrabActive] = accentActive;

        colors[(int)ImGuiCol.Button] = bg3;
        colors[(int)ImGuiCol.ButtonHovered] = bg4;
        colors[(int)ImGuiCol.ButtonActive] = bg5;

        // Header* backs Selectable/TreeNode/CollapsingHeader — this is what shows a selected Outline
        // row or an expanded Inspector section, so it gets the accent color rather than a neutral gray.
        colors[(int)ImGuiCol.Header] = WithAlpha(accent, 0.35f);
        colors[(int)ImGuiCol.HeaderHovered] = WithAlpha(accent, 0.55f);
        colors[(int)ImGuiCol.HeaderActive] = WithAlpha(accent, 0.75f);

        colors[(int)ImGuiCol.Separator] = border;
        colors[(int)ImGuiCol.SeparatorHovered] = accentHover;
        colors[(int)ImGuiCol.SeparatorActive] = accentActive;

        colors[(int)ImGuiCol.ResizeGrip] = WithAlpha(accent, 0.20f);
        colors[(int)ImGuiCol.ResizeGripHovered] = WithAlpha(accent, 0.55f);
        colors[(int)ImGuiCol.ResizeGripActive] = WithAlpha(accent, 0.80f);

        colors[(int)ImGuiCol.PlotLines] = accent;
        colors[(int)ImGuiCol.PlotLinesHovered] = accentHover;
        colors[(int)ImGuiCol.PlotHistogram] = accent;
        colors[(int)ImGuiCol.PlotHistogramHovered] = accentHover;

        colors[(int)ImGuiCol.TextSelectedBg] = WithAlpha(accent, 0.35f);
        colors[(int)ImGuiCol.DragDropTarget] = accent;

        colors[(int)ImGuiCol.NavCursor] = accent;
        colors[(int)ImGuiCol.NavWindowingHighlight] = WithAlpha(text, 0.70f);
        colors[(int)ImGuiCol.NavWindowingDimBg] = WithAlpha(bg0, 0.60f);
        colors[(int)ImGuiCol.ModalWindowDimBg] = WithAlpha(bg0, 0.60f);
    }

    private static Vector4 Rgb(int r, int g, int b) => new(r / 255f, g / 255f, b / 255f, 1f);

    private static Vector4 Rgba(int r, int g, int b, int a) => new(r / 255f, g / 255f, b / 255f, a / 255f);

    private static Vector4 WithAlpha(Vector4 color, float alpha) => new(color.X, color.Y, color.Z, alpha);

    /// <summary>A warm, muted sand/gold reserved for folder icons in the Files tree — inspired by (not
    /// matching) the warm folder-icon tint in the reference editor Arian shared, kept clearly apart in hue
    /// from the theme's own selection amber above so a folder icon never reads as "selected". Public because
    /// ContentBrowserPanel — outside this class — needs it for the Files tree's folder rows.</summary>
    public static readonly Vector4 FolderAccent = Rgb(196, 168, 110);
}
