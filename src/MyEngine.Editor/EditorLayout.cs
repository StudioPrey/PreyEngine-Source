using System.Numerics;
using ImGuiNET;

namespace MyEngine.Editor;

/// <summary>
/// Computes the Editor's fixed panel arrangement (Hierarchy left, Inspector right, Console and Content
/// Browser side by side along the bottom, Viewport filling the rest) every frame from the current window
/// size, and pins each panel window to its rectangle via SetNextWindowPos/SetNextWindowSize.
///
/// This deliberately does NOT use ImGui's docking system (DockSpace + DockBuilder). Pre-arranging a
/// dockspace normally needs the DockBuilder family of functions, but those live in imgui_internal.h, which
/// the ImGui.NET package this project uses does not expose — see
/// https://github.com/ImGuiNET/ImGui.NET/issues/81. Manually pinning 5 plain windows gets the same "always
/// in the same place, can't be dragged elsewhere" result using only SetNextWindowPos/SetNextWindowSize and
/// ordinary ImGuiWindowFlags, which are guaranteed to exist in any ImGui build.
/// </summary>
public static class EditorLayout
{
    /// <summary>Applied to every fixed panel's Begin() call: no dragging to move it, no drag-to-resize
    /// (size is fully computed here instead), no collapse arrow, and no participating in docking.</summary>
    public const ImGuiWindowFlags PanelFlags =
        ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoDocking;

    /// <summary>Same as PanelFlags, plus hiding the title bar and scrollbar — the toolbar is a plain strip
    /// of buttons, not a titled panel with content that could ever need to scroll.</summary>
    public const ImGuiWindowFlags ToolbarFlags = PanelFlags
        | ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse;

    /// <summary>30px buttons + the theme's (10,10) WindowPadding need 50px; a few extra pixels of
    /// breathing room on top of that keeps it comfortably short and tight — never taller than it needs to
    /// be, which is what actually matters for not covering whatever's pinned below it.</summary>
    private const float ToolbarHeight = 54f;

    private static Vector2 _toolbarPos, _toolbarSize;
    private static Vector2 _hierarchyPos, _hierarchySize;
    private static Vector2 _inspectorPos, _inspectorSize;
    private static Vector2 _consolePos, _consoleSize;
    private static Vector2 _contentBrowserPos, _contentBrowserSize;
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
        Vector2 bodySize = new Vector2(workSize.X, workSize.Y - ToolbarHeight);

        float hierarchyWidth = bodySize.X * 0.18f;
        float inspectorWidth = bodySize.X * 0.22f;
        float bottomHeight = bodySize.Y * 0.28f;
        float centerWidth = bodySize.X - hierarchyWidth - inspectorWidth;
        float topHeight = bodySize.Y - bottomHeight;
        float halfCenterWidth = centerWidth * 0.5f;

        _hierarchyPos = bodyPos;
        _hierarchySize = new Vector2(hierarchyWidth, bodySize.Y);

        _inspectorPos = new Vector2(bodyPos.X + hierarchyWidth + centerWidth, bodyPos.Y);
        _inspectorSize = new Vector2(inspectorWidth, bodySize.Y);

        _viewportPos = new Vector2(bodyPos.X + hierarchyWidth, bodyPos.Y);
        _viewportSize = new Vector2(centerWidth, topHeight);

        _consolePos = new Vector2(bodyPos.X + hierarchyWidth, bodyPos.Y + topHeight);
        _consoleSize = new Vector2(halfCenterWidth, bottomHeight);

        _contentBrowserPos = new Vector2(bodyPos.X + hierarchyWidth + halfCenterWidth, bodyPos.Y + topHeight);
        _contentBrowserSize = new Vector2(centerWidth - halfCenterWidth, bottomHeight);
    }

    private static void Pin(Vector2 pos, Vector2 size)
    {
        ImGui.SetNextWindowPos(pos, ImGuiCond.Always);
        ImGui.SetNextWindowSize(size, ImGuiCond.Always);
    }

    public static void PinToolbar() => Pin(_toolbarPos, _toolbarSize);
    public static void PinHierarchy() => Pin(_hierarchyPos, _hierarchySize);
    public static void PinInspector() => Pin(_inspectorPos, _inspectorSize);
    public static void PinConsole() => Pin(_consolePos, _consoleSize);
    public static void PinContentBrowser() => Pin(_contentBrowserPos, _contentBrowserSize);
    public static void PinViewport() => Pin(_viewportPos, _viewportSize);
}
