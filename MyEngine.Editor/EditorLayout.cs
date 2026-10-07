using System.Numerics;
using ImGuiNET;

namespace MyEngine.Editor;

/// <summary>
/// Computes the Editor's fixed, Phaser-Editor-style panel arrangement every frame from the current window
/// size, and pins each panel window to its rectangle via SetNextWindowPos/SetNextWindowSize:
///
///   ┌──────────────────────────────────────────────────────────────┐
///   │ menu bar (File / Edit / GameObject / Scripts)                │
///   ├──────────────────────────────────────────────────────────────┤
///   │ toolbar: Play/Stop · Undo/Redo                    scene name │
///   ├────────────┬────────────────────────────────────┬────────────┤
///   │  Outline   │ scene tabs                         │            │
///   │  (scene    │ ┌────────────────────────────────┐ │            │
///   │  tree)     │ │ Viewport      [tool overlay ↙] │ │  Inspector │
///   ├────────────┤ └────────────────────────────────┘ │            │
///   │  Files     │ Blocks: Assets|Built-in|Prefabs|Console           │
///   │  (project) │                                    │            │
///   └────────────┴────────────────────────────────────┴────────────┘
///
/// Left column = the two "trees" (what's in the scene / what's in the project), center = the scene and the
/// palette you build it from, right = properties. This replaces the previous Unity-like arrangement
/// (Hierarchy left, Inspector right, one bottom panel).
///
/// This deliberately does NOT use ImGui's docking system (DockSpace + DockBuilder). Pre-arranging a
/// dockspace normally needs the DockBuilder family of functions, but those live in imgui_internal.h, which
/// the ImGui.NET package this project uses does not expose — see
/// https://github.com/ImGuiNET/ImGui.NET/issues/81. Manually pinning plain windows gets the same "always
/// in the same place, can't be dragged elsewhere" result using only SetNextWindowPos/SetNextWindowSize and
/// ordinary ImGuiWindowFlags, which are guaranteed to exist in any ImGui build.
/// </summary>
public static class EditorLayout
{
    /// <summary>Applied to every fixed panel's Begin() call: no dragging to move it, no drag-to-resize
    /// (size is fully computed here instead), and no collapse arrow.</summary>
    public const ImGuiWindowFlags PanelFlags =
        ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoCollapse;

    /// <summary>Same as PanelFlags, plus hiding the title bar and scrollbar — the toolbar is a plain strip
    /// of buttons, not a titled panel with content that could ever need to scroll.</summary>
    public const ImGuiWindowFlags ToolbarFlags = PanelFlags
        | ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse;

    /// <summary>26px buttons + the theme's 6px top/bottom WindowPadding = 38px. Never taller than it needs to
    /// be, which is what actually matters for not covering whatever's pinned below it.</summary>
    private const float ToolbarHeight = 38f;

    // Column/row proportions, each clamped so a very large or very small window still gets usable panels.
    private const float LeftColumnFraction = 0.17f, LeftColumnMin = 220f, LeftColumnMax = 340f;
    private const float InspectorFraction = 0.22f, InspectorMin = 280f, InspectorMax = 440f;
    private const float BlocksFraction = 0.30f, BlocksMin = 190f, BlocksMax = 340f;
    private const float OutlineFraction = 0.55f; // of the left column's height; Files gets the rest

    private static Vector2 _toolbarPos, _toolbarSize;
    private static Vector2 _outlinePos, _outlineSize;
    private static Vector2 _filesPos, _filesSize;
    private static Vector2 _inspectorPos, _inspectorSize;
    private static Vector2 _blocksPos, _blocksSize;
    private static Vector2 _viewportPos, _viewportSize;

    /// <summary>Call once per frame, before drawing any panel (from EditorApp.BuildUI). Recomputes every
    /// rectangle from the current window size — proportions never depend on anything saved from a
    /// previous run, so the arrangement is identical on every launch.</summary>
    public static void Refresh()
    {
        var viewport = ImGui.GetMainViewport();
        Vector2 workPos = viewport.WorkPos;   // top-left of the usable area, i.e. below the main menu bar
        Vector2 workSize = viewport.WorkSize;

        _toolbarPos = workPos;
        _toolbarSize = new Vector2(workSize.X, ToolbarHeight);

        // Everything else sits below the reserved toolbar strip.
        Vector2 bodyPos = new Vector2(workPos.X, workPos.Y + ToolbarHeight);
        Vector2 bodySize = new Vector2(workSize.X, MathF.Max(1f, workSize.Y - ToolbarHeight));

        float leftWidth = Math.Clamp(bodySize.X * LeftColumnFraction, LeftColumnMin, LeftColumnMax);
        float inspectorWidth = Math.Clamp(bodySize.X * InspectorFraction, InspectorMin, InspectorMax);
        float centerWidth = MathF.Max(1f, bodySize.X - leftWidth - inspectorWidth);

        // The Blocks palette never takes more than half the center column, so the Viewport always keeps a
        // usable share even in a short window.
        float blocksHeight = MathF.Min(Math.Clamp(bodySize.Y * BlocksFraction, BlocksMin, BlocksMax), bodySize.Y * 0.5f);
        float viewportHeight = MathF.Max(1f, bodySize.Y - blocksHeight);

        float outlineHeight = bodySize.Y * OutlineFraction;
        float filesHeight = MathF.Max(1f, bodySize.Y - outlineHeight);

        _outlinePos = bodyPos;
        _outlineSize = new Vector2(leftWidth, outlineHeight);

        _filesPos = new Vector2(bodyPos.X, bodyPos.Y + outlineHeight);
        _filesSize = new Vector2(leftWidth, filesHeight);

        _viewportPos = new Vector2(bodyPos.X + leftWidth, bodyPos.Y);
        _viewportSize = new Vector2(centerWidth, viewportHeight);

        _blocksPos = new Vector2(bodyPos.X + leftWidth, bodyPos.Y + viewportHeight);
        _blocksSize = new Vector2(centerWidth, blocksHeight);

        _inspectorPos = new Vector2(bodyPos.X + leftWidth + centerWidth, bodyPos.Y);
        _inspectorSize = new Vector2(inspectorWidth, bodySize.Y);
    }

    private static void Pin(Vector2 pos, Vector2 size)
    {
        ImGui.SetNextWindowPos(pos, ImGuiCond.Always);
        ImGui.SetNextWindowSize(size, ImGuiCond.Always);
    }

    public static void PinToolbar() => Pin(_toolbarPos, _toolbarSize);
    public static void PinOutline() => Pin(_outlinePos, _outlineSize);
    public static void PinFiles() => Pin(_filesPos, _filesSize);
    public static void PinInspector() => Pin(_inspectorPos, _inspectorSize);
    public static void PinBlocks() => Pin(_blocksPos, _blocksSize);
    public static void PinViewport() => Pin(_viewportPos, _viewportSize);
}
