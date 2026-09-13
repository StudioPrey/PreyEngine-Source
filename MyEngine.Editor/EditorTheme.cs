using System.Numerics;
using ImGuiNET;

namespace MyEngine.Editor;

/// <summary>
/// The Editor's visual theme: a dark, flat color palette with modest rounding, replacing ImGui's built-in
/// default look. Purely cosmetic — sets ImGuiStyle colors/sizes once at startup and never touches any
/// engine or editor logic.
///
/// Sticks to ImGuiCol/ImGuiStyleVar members confirmed present in this project's exact ImGui.NET build.
/// Two renames to be aware of if this file needs editing later: ImGuiCol_NavHighlight became NavCursor in
/// Dear ImGui 1.91.4, and ImGuiCol_TabActive/TabUnfocused/TabUnfocusedActive became TabSelected/TabDimmed/
/// TabDimmedSelected in 1.90.9 — tab colors are left at their default here rather than guess which spelling
/// this build exposes.
/// </summary>
public static class EditorTheme
{
    /// <summary>Common Windows system UI fonts, tried in order of preference. Loaded via an absolute path
    /// from the OS font directory rather than a bundled file, since this project doesn't ship any font
    /// assets — if none of these are found (e.g. running on a non-Windows OS), this quietly falls back to
    /// ImGui's built-in bitmap font instead of failing.</summary>
    private static readonly string[] PreferredFonts = { "segoeui.ttf", "calibri.ttf", "tahoma.ttf", "arial.ttf" };

    /// <summary>Call once at startup, before ImGuiRenderer.RebuildFontAtlas() — fonts must be added to the
    /// atlas before it's baked and uploaded to the GPU, so this can't happen afterward like the color/style
    /// changes in Apply() below can.</summary>
    public static void ApplyFont()
    {
        var io = ImGui.GetIO();
        string fontsDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);

        foreach (var fileName in PreferredFonts)
        {
            string path = Path.Combine(fontsDirectory, fileName);
            if (File.Exists(path))
            {
                io.Fonts.AddFontFromFileTTF(path, 17f);
                return;
            }
        }

        io.Fonts.AddFontDefault(); // no system font found — explicit fallback to ImGui's built-in bitmap font
    }

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

        style.WindowPadding = new Vector2(10f, 10f);
        style.FramePadding = new Vector2(8f, 4f);
        style.ItemSpacing = new Vector2(8f, 6f);
        style.ItemInnerSpacing = new Vector2(6f, 4f);
        style.IndentSpacing = 16f;
        style.ScrollbarSize = 14f;
        style.GrabMinSize = 10f;

        style.WindowBorderSize = 1f;
        style.ChildBorderSize = 1f;
        style.PopupBorderSize = 1f;
        style.FrameBorderSize = 1f;
        style.TabBorderSize = 0f;

        style.WindowRounding = 4f;
        style.ChildRounding = 3f;
        style.FrameRounding = 3f;
        style.PopupRounding = 4f;
        style.ScrollbarRounding = 6f;
        style.GrabRounding = 3f;
        style.TabRounding = 3f;

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

        Vector4 bg0 = Rgb(18, 18, 20);    // deepest background (behind everything)
        Vector4 bg1 = Rgb(26, 26, 29);    // window/panel background
        Vector4 bg2 = Rgb(33, 33, 37);    // panel header / menu bar / title bar
        Vector4 bg3 = Rgb(42, 42, 47);    // frame background (inputs, checkboxes, buttons)
        Vector4 bg4 = Rgb(52, 52, 58);    // hovered frame
        Vector4 bg5 = Rgb(63, 63, 70);    // active/pressed frame

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

        colors[(int)ImGuiCol.TitleBg] = Vector4.Lerp(bg2, bg1, 0.4f);
        colors[(int)ImGuiCol.TitleBgActive] = Vector4.Lerp(bg2, accent, 0.18f);
        colors[(int)ImGuiCol.TitleBgCollapsed] = bg1;
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

        // Header* backs Selectable/TreeNode/CollapsingHeader — this is what shows a selected Hierarchy
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
}
