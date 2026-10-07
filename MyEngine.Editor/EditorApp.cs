using System.Collections.Concurrent;
using ImGuiNET;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using MyEngine.Core;
using MyEngine.Core.Audio;
using MyEngine.Core.Components;
using MyEngine.Core.ECS;
using MyEngine.Core.Rendering;
using MyEngine.Core.Saving;
using MyEngine.Core.SceneSystem;
using MyEngine.Core.Scripting;
using MyEngine.Editor.Build;
using MyEngine.Editor.Panels;
using MyEngine.Editor.Scripting;
using MyEngine.EditorFramework;
using CoreInput = MyEngine.Core.InputSystem.Input;

namespace MyEngine.Editor;

public class EditorApp : Game
{
    private readonly GraphicsDeviceManager _graphics;
    private readonly string _projectPath;
    private ImGuiRenderer _imGui = null!;
    private EditorState _state = null!;
    private readonly GizmoSystem _gizmo = new();
    private CameraIconRenderer _cameraIcons = null!;
    private ProjectSettings _projectSettings = null!;

    // Small top-menu-bar watermark (see DrawMainMenuBar) — a single static image with no per-frame update
    // logic, unlike CameraIconRenderer/AudioIconRenderer (which track GameObjects and redraw every frame),
    // so it doesn't warrant its own class — just the same load-once/bind-once pattern, inlined.
    private Texture2D? _logoTexture;
    private IntPtr _logoTextureId = IntPtr.Zero;

    private RenderTarget2D? _sceneTarget;
    private IntPtr _sceneTextureId = IntPtr.Zero;
    private System.Numerics.Vector2 _requestedViewportSize = new(1280, 720);

    // Refreshed every Draw call while rendering the scene, then reused by the gizmo and by
    // both in-app and OS-level drag-and-drop (all need to convert between screen space and world space).
    private Matrix _currentViewMatrix = Matrix.Identity;
    private float _currentZoom = 1f;
    private System.Numerics.Vector2 _lastViewportMin;
    private System.Numerics.Vector2 _lastViewportMax;

    // The Editor's OWN free-roam view of the scene while NOT playing — completely independent of any
    // Camera2D component (see UpdateEditorCameraInput's doc comment for why), so panning/zooming around
    // while you work never touches the actual game camera's configured Position/Zoom. Play Mode instead
    // shows exactly what the real active Camera2D sees (see RenderSceneToTarget) — this pair of fields is
    // simply unused then.
    private Vector2 _editorCameraPosition = Vector2.Zero;
    private float _editorCameraZoom = 1f;

    // Files dropped onto the window from the OS (e.g. dragged in from File Explorer/Finder). The
    // FileDrop event can fire off the main thread on some platforms, so it's queued and drained in
    // Update() alongside the same pattern used for the filesystem watcher.
    private readonly ConcurrentQueue<(string[] Files, System.Numerics.Vector2 ScreenPosition)> _pendingFileDrops = new();

    // Set when an action (New Scene, Open Scene, switching a scene tab, ...) would discard unsaved
    // changes to the current scene — instead of discarding immediately, we hold the action here and
    // show a confirmation modal; the action only actually runs if the user confirms.
    private Action? _pendingDestructiveAction;
    private string _pendingDestructiveDescription = "";

    private string _lastWindowTitle = "";

    // Script compilation runs on a background thread (Roslyn compiles are CPU-only, no GraphicsDevice/ImGui
    // touching, so this is safe) so the "Compiling scripts..." overlay actually gets a chance to render
    // instead of the whole frame blocking until it's done. Only one compile runs at a time; a request that
    // arrives while one is already in flight just chains its callback onto the one currently running.
    private bool _isCompiling;
    private readonly ConcurrentQueue<ScriptCompilationResult> _pendingCompileResults = new();
    private Action? _onCompileComplete;

    private bool _isBuilding;
    private readonly ConcurrentQueue<BuildResult> _pendingBuildResults = new();
    private bool _showBuildSettings;
    private BuildResult? _lastBuildResult;
    private bool _showBuildReport;

    public EditorApp(string projectPath)
    {
        _projectPath = projectPath;
        _graphics = new GraphicsDeviceManager(this)
        {
            PreferredBackBufferWidth = 1600,
            PreferredBackBufferHeight = 900,
            GraphicsProfile = GraphicsProfile.HiDef,
        };
        Content.RootDirectory = "Content";
        IsMouseVisible = true;
        Window.AllowUserResizing = true;
    }

    protected override void LoadContent()
    {
        RenderContext.Initialize(GraphicsDevice);

        _imGui = new ImGuiRenderer(this);
        EditorTheme.ApplyFont();
        _imGui.RebuildFontAtlas();
        EditorTheme.Apply();
        _cameraIcons = new CameraIconRenderer(GraphicsDevice, _imGui);

        // See the _logoTexture field's comment — loaded the same way CameraIconRenderer loads camera_icon.png,
        // just without a dedicated class since there's no per-frame logic here. Missing file (e.g. someone
        // cleaned EditorAssets) degrades to simply not drawing a logo, never a startup crash.
        var logoPath = Path.Combine(AppContext.BaseDirectory, "EditorAssets", "engine_logo.png");
        if (File.Exists(logoPath))
        {
            using var logoStream = File.OpenRead(logoPath);
            _logoTexture = Texture2D.FromStream(GraphicsDevice, logoStream);
            _logoTextureId = _imGui.BindTexture(_logoTexture);
        }

        var assets = new AssetDatabase(_projectPath, GraphicsDevice, _imGui);
        assets.Refresh();
        // SpriteAnimation/AudioSource (and anything else resolving an asset path outside the per-frame
        // ResolveSceneAssets sweep below, e.g. swapping frames many times a second) call through these —
        // see RenderContext.TextureResolver's and AudioContext.ClipResolver's doc comments.
        RenderContext.TextureResolver = assets.GetTexture;
        AudioContext.ClipResolver = assets.GetSound;

        _state = new EditorState(_projectPath, assets);
        _projectSettings = ProjectSettings.Load(_projectPath);

        Window.FileDrop += OnFileDrop;

        // Unity-style: refocusing the Editor after editing scripts externally (VS Code, Visual Studio)
        // recompiles automatically, so "alt-tab back and hit Play" always runs the latest code.
        Activated += (_, _) => TriggerScriptCompilation();

        _state.LogMessage("Editor started. Welcome to MyEngine!");

        // Icons are real images (baked from vector masters), not per-frame draw-list shapes — see
        // EditorIconAtlas. Loaded here rather than next to the other textures above because its startup
        // warnings go to the Console log, which needs _state; nothing draws an icon before the first frame,
        // which comes after LoadContent returns.
        EditorIconAtlas.Load(GraphicsDevice, _imGui, _state.LogMessage);

        // IDE support: give VS Code / Visual Studio a project file that references the engine assemblies,
        // otherwise script editing there has no code completion for engine types. Refreshed on every open
        // because the references are absolute paths into whichever Editor version opened this project.
        // A failure here only costs IDE completion — scripts still compile and run — so it's logged, not fatal.
        var ideProblem = ScriptProjectGenerator.Ensure(_projectPath, out var ideFilesChanged);
        if (ideProblem != null)
            _state.LogMessage($"WARNING: {ideProblem}");
        else if (ideFilesChanged)
            _state.LogMessage("Updated the C# project for your IDE (GameScripts.csproj / GameScripts.sln) — open it via Scripts > Open C# Project in IDE.");

        // Scripts must be compiled (so ScriptRegistry knows about them) *before* the startup scene loads —
        // otherwise any GameObject with a script component would silently skip it as a "missing script".
        TriggerScriptCompilation(onComplete: InitializeStartupScene);
    }

    /// <summary>
    /// Opens whatever the user was last working on, instead of a throwaway placeholder scene:
    ///   1. The last scene this project had open (remembered across sessions via ProjectSettings.json).
    ///   2. If that's missing/deleted, the most recently modified .scene file under Assets/Scenes.
    ///   3. If the project has no scenes at all yet (a brand new project), create one and open that.
    /// </summary>
    private void InitializeStartupScene()
    {
        if (_projectSettings.LastOpenedScenePath != null)
        {
            var lastPath = Path.Combine(_projectPath, _projectSettings.LastOpenedScenePath);
            if (File.Exists(lastPath))
            {
                LoadScene(lastPath);
                return;
            }
        }

        var scenesFolder = _state.ScenesFolder; // creates Assets/Scenes if it doesn't exist yet
        var existingScenes = Directory.GetFiles(scenesFolder, "*.scene", SearchOption.AllDirectories);

        if (existingScenes.Length > 0)
        {
            var mostRecent = existingScenes.OrderByDescending(File.GetLastWriteTimeUtc).First();
            LoadScene(mostRecent);
            return;
        }

        // brand new project — no scenes anywhere yet, so create the very first one
        var relativePath = _state.Assets.CreateScene(EditorState.ScenesRelativeFolder, "Main");
        var fullPath = Path.Combine(_state.Assets.AssetsRoot, relativePath);
        LoadScene(fullPath);
    }

    private void OnFileDrop(object? sender, FileDropEventArgs e)
    {
        var mouse = Mouse.GetState();
        _pendingFileDrops.Enqueue((e.Files, new System.Numerics.Vector2(mouse.X, mouse.Y)));
    }

    protected override void Update(GameTime gameTime)
    {
        ProcessCompileResults();
        ProcessBuildResults();

        _state.Assets.ProcessPendingChanges();

        // Keeps every SpriteRenderer.Texture / AudioSource.Clip pointed at a *currently valid* resource.
        // Without this, any asset re-import — the file watcher noticing a change, the Refresh button, or
        // Refresh() being called after saving a scene/prefab — disposes and recreates Texture2D/SoundEffect
        // instances, and anything in the scene still holding the old (now-disposed) reference would render
        // as black/garbage (or throw the next time an AudioSource tries to play it). Cheap enough to just
        // do unconditionally every frame rather than trying to remember to call it after every single place
        // that can trigger a reimport.
        _state.Assets.ResolveSceneAssets(_state.EditScene);
        if (_state.IsPlaying)
            _state.Assets.ResolveSceneAssets(_state.PlayScene!);

        if (_state.ScriptCompileRequested)
        {
            _state.ClearScriptCompileRequest();
            TriggerScriptCompilation();
        }

        ProcessFileDrops();
        HandleGlobalShortcuts();
        UpdateWindowTitle();

        // Refreshed every frame regardless of Play Mode (harmless while editing — nothing reads Input
        // outside a running script, and Time.Reset() on StartPlay() keeps TotalTime meaningful either way).
        CoreInput.Update();
        Time.Update(gameTime);

        if (_state.IsPlaying)
        {
            _state.ActiveScene.Update(gameTime);
            SaveData.TickAutoSave((float)gameTime.ElapsedGameTime.TotalSeconds);
        }

        base.Update(gameTime);
    }

    // ---------------------------------------------------------------- script compilation

    /// <summary>Kicks off a background recompile of every .cs file under the project's Assets/ folder.
    /// If a compile is already running, <paramref name="onComplete"/> is chained to run after that one
    /// finishes rather than starting a second compile on top of it.</summary>
    private void TriggerScriptCompilation(Action? onComplete = null)
    {
        if (_isCompiling)
        {
            if (onComplete != null)
            {
                var previous = _onCompileComplete;
                _onCompileComplete = () => { previous?.Invoke(); onComplete(); };
            }
            return;
        }

        _isCompiling = true;
        _onCompileComplete = onComplete;

        var assetsRoot = _state.Assets.AssetsRoot;
        Task.Run(() =>
        {
            var result = ScriptCompiler.CompileProject(assetsRoot);
            _pendingCompileResults.Enqueue(result);
        });
    }

    private void ProcessCompileResults()
    {
        while (_pendingCompileResults.TryDequeue(out var result))
        {
            _isCompiling = false;

            if (result.Success)
            {
                ScriptRegistry.Register(result.ScriptTypes);
                _state.LogMessage(result.ScriptTypes.Count > 0
                    ? $"Scripts compiled successfully ({result.ScriptTypes.Count} script type(s))."
                    : "No scripts found to compile.");
            }
            else
            {
                _state.LogMessage($"Script compilation FAILED ({result.Errors.Count} error(s)):");
                foreach (var error in result.Errors)
                    _state.LogMessage($"  {error}");
            }

            var callback = _onCompileComplete;
            _onCompileComplete = null;
            callback?.Invoke();
        }
    }

    /// <summary>Kicks off a background standalone build (see ProjectBuilder) — mirrors
    /// TriggerScriptCompilation's background-thread pattern so the multi-second `dotnet publish` step
    /// doesn't freeze the UI. Ignored if a build is already running.</summary>
    private void TriggerBuild()
    {
        if (_isBuilding) return;
        _isBuilding = true;
        _state.LogMessage($"Building '{_projectSettings.GameName}'...");

        var projectPath = _projectPath;
        var settingsSnapshot = _projectSettings; // ProjectSettings is a plain data holder; safe to read from the background thread
        Task.Run(() =>
        {
            var result = ProjectBuilder.Build(projectPath, settingsSnapshot);
            _pendingBuildResults.Enqueue(result);
        });
    }

    private void ProcessBuildResults()
    {
        while (_pendingBuildResults.TryDequeue(out var result))
        {
            _isBuilding = false;

            foreach (var line in result.LogLines)
                _state.LogMessage(line);

            _state.LogMessage(result.Success
                ? $"Build succeeded: {result.OutputDirectory}"
                : $"Build FAILED: {result.Message}");

            _lastBuildResult = result;
            _showBuildReport = true;
        }
    }

    private void UpdateWindowTitle()
    {
        var projectName = Path.GetFileName(_projectPath.TrimEnd(Path.DirectorySeparatorChar));
        var sceneName = _state.CurrentScenePath != null
            ? Path.GetFileNameWithoutExtension(_state.CurrentScenePath)
            : _state.EditScene.Name;
        var dirtyMark = _state.IsDirty ? " *" : "";

        var title = $"MyEngine Editor — {projectName} — {sceneName}{dirtyMark}";
        if (title == _lastWindowTitle) return;

        Window.Title = title;
        _lastWindowTitle = title;
    }

    /// <summary>Ctrl+Z / Ctrl+Y (and Ctrl+Shift+Z) for undo/redo, ignored while typing into a text field.</summary>
    private void HandleGlobalShortcuts()
    {
        var io = ImGui.GetIO();
        if (io.WantTextInput) return;

        bool ctrl = io.KeyCtrl;
        if (!ctrl) return;

        if (ImGui.IsKeyPressed(ImGuiKey.Z) && !io.KeyShift) _state.PerformUndo();
        else if (ImGui.IsKeyPressed(ImGuiKey.Y) || (ImGui.IsKeyPressed(ImGuiKey.Z) && io.KeyShift)) _state.PerformRedo();
    }

    // ---------------------------------------------------------------- OS-level file drop

    private void ProcessFileDrops()
    {
        while (_pendingFileDrops.TryDequeue(out var drop))
            foreach (var file in drop.Files)
                ImportDroppedFile(file, drop.ScreenPosition);
    }

    /// <summary>Imports a file dragged in from outside the app (e.g. the OS file manager) into the Content
    /// Browser's current folder. If the drop landed on the Viewport specifically, also places a sprite
    /// there immediately — matching the existing behavior for dragging an asset already inside the Files/Assets panels.</summary>
    private void ImportDroppedFile(string sourcePath, System.Numerics.Vector2 dropScreenPosition)
    {
        if (Directory.Exists(sourcePath))
        {
            _state.LogMessage($"Skipped '{Path.GetFileName(sourcePath)}' — drop image files individually, not whole folders.");
            return;
        }
        if (!File.Exists(sourcePath)) return;

        try
        {
            var relative = _state.Assets.ImportExternalFile(sourcePath, _state.ContentBrowserFolder);
            _state.LogMessage($"Imported '{Path.GetFileName(sourcePath)}' into Assets/{_state.ContentBrowserFolder}.");

            if (IsOverViewport(dropScreenPosition))
            {
                var local = dropScreenPosition - _lastViewportMin;
                var worldPos = Vector2.Transform(new Vector2(local.X, local.Y), Matrix.Invert(_currentViewMatrix));
                AssetDropHandler.CreateSpriteFromTexture(_state, relative, worldPos);
            }
        }
        catch (Exception ex)
        {
            _state.LogMessage($"ERROR importing '{Path.GetFileName(sourcePath)}': {ex.Message}");
        }
    }

    private bool IsOverViewport(System.Numerics.Vector2 screenPos) =>
        screenPos.X >= _lastViewportMin.X && screenPos.X <= _lastViewportMax.X &&
        screenPos.Y >= _lastViewportMin.Y && screenPos.Y <= _lastViewportMax.Y;

    /// <summary>Right-drag pans, scroll zooms toward the cursor — free navigation completely independent
    /// of any Camera2D component, the same way Unity/Godot's own Scene view camera is independent of
    /// whatever camera the game itself uses. Called right after _imGui.BeforeLayout (which calls
    /// ImGui.NewFrame internally) and before RenderSceneToTarget, so this frame's input already affects
    /// this same frame's picture rather than lagging a frame behind.
    ///
    /// Root-cause note: this used to run BEFORE BeforeLayout/NewFrame instead of after. io.MouseWheel/
    /// MouseDelta are only populated for the current frame once NewFrame has processed this frame's queued
    /// input events; reading them beforehand meant this method only ever saw whatever value NewFrame had
    /// left over from the previous frame, so scroll-to-zoom (and, less visibly, right-drag pan) never
    /// reacted to input in any dependable way. Moving the BeforeLayout call ahead of this one is the fix —
    /// see Draw's own comment.
    ///
    /// Checked against LAST frame's viewport bounds (_lastViewportMin/Max, only known once BuildUI — which
    /// still runs AFTER this — has drawn the panel); the bounds themselves barely move frame-to-frame
    /// outside an active window resize, so that one-frame lag stays imperceptible on its own.</summary>
    private void UpdateEditorCameraInput()
    {
        if (_state.IsPlaying) return;

        var io = ImGui.GetIO();
        if (!IsOverViewport(io.MousePos)) return;

        if (ImGui.IsMouseDown(ImGuiMouseButton.Right))
        {
            _editorCameraPosition -= new Vector2(io.MouseDelta.X, io.MouseDelta.Y) / _editorCameraZoom;
        }

        if (io.MouseWheel != 0f)
        {
            float oldZoom = _editorCameraZoom;
            float newZoom = MathHelper.Clamp(oldZoom * MathF.Pow(1.1f, io.MouseWheel), 0.05f, 20f);

            var viewportCenter = (_lastViewportMin + _lastViewportMax) * 0.5f;
            var mouseFromCenter = new Vector2(io.MousePos.X - viewportCenter.X, io.MousePos.Y - viewportCenter.Y);
            var worldUnderCursor = _editorCameraPosition + mouseFromCenter / oldZoom;

            _editorCameraZoom = newZoom;
            _editorCameraPosition = worldUnderCursor - mouseFromCenter / newZoom;
        }
    }

    // ---------------------------------------------------------------- draw

    protected override void Draw(GameTime gameTime)
    {
        // _imGui.BeforeLayout runs first specifically so UpdateEditorCameraInput (right after it) reads
        // this frame's actual io.MouseWheel/MouseDelta/MousePos — see that method's doc comment for why
        // the previous ordering silently starved it of current input.
        _imGui.BeforeLayout(gameTime);

        UpdateEditorCameraInput();
        EnsureSceneRenderTarget();
        RenderSceneToTarget(gameTime);

        GraphicsDevice.SetRenderTarget(null);
        GraphicsDevice.Clear(new Color(20, 20, 22));

        BuildUI();
        _imGui.AfterLayout();

        base.Draw(gameTime);
    }

    private void EnsureSceneRenderTarget()
    {
        int width = Math.Max(1, (int)_requestedViewportSize.X);
        int height = Math.Max(1, (int)_requestedViewportSize.Y);

        if (_sceneTarget != null && _sceneTarget.Width == width && _sceneTarget.Height == height)
            return;

        if (_sceneTarget != null)
        {
            _imGui.UnbindTexture(_sceneTextureId);
            _sceneTarget.Dispose();
        }

        _sceneTarget = new RenderTarget2D(GraphicsDevice, width, height, false, SurfaceFormat.Color, DepthFormat.None);
        _sceneTextureId = _imGui.BindTexture(_sceneTarget);
    }

    private void RenderSceneToTarget(GameTime gameTime)
    {
        if (_sceneTarget == null) return;

        var camera = _state.ActiveScene.GameObjects
            .Select(g => g.GetComponent<Camera2D>())
            .FirstOrDefault(c => c != null && c.IsActive);

        GraphicsDevice.SetRenderTarget(_sceneTarget);
        GraphicsDevice.Clear(camera?.BackgroundColor ?? new Color(30, 30, 35));

        var targetSize = new Vector2(_sceneTarget.Width, _sceneTarget.Height);
        if (_state.IsPlaying)
        {
            // Exactly what the real, active Camera2D sees — matches what a player would see.
            _currentViewMatrix = camera?.GetViewMatrix(targetSize)
                                  ?? Matrix.CreateTranslation(targetSize.X / 2f, targetSize.Y / 2f, 0f);
            _currentZoom = camera?.Zoom ?? 1f;
        }
        else
        {
            // The Editor's own free-roam view (see UpdateEditorCameraInput) — same formula shape as
            // Camera2D.GetViewMatrix, just built from _editorCameraPosition/_editorCameraZoom instead of
            // any component's, so navigating while you edit never touches the actual game camera's
            // configured Position/Zoom.
            _currentViewMatrix = Matrix.CreateTranslation(-_editorCameraPosition.X, -_editorCameraPosition.Y, 0f)
                                  * Matrix.CreateScale(_editorCameraZoom)
                                  * Matrix.CreateTranslation(targetSize.X / 2f, targetSize.Y / 2f, 0f);
            _currentZoom = _editorCameraZoom;
        }

        // BackToFront so SpriteRenderer.SortingOrder (encoded as layerDepth) actually controls draw order,
        // and so the grid (layerDepth 1, drawn first below) always stays behind every GameObject. Goes
        // through IRenderer2D (see RenderContext.Renderer2D) the same way RuntimeGame's Draw does, so both
        // hosts stay symmetric — GridRenderer below is the one deliberate exception, an editor-only overlay
        // that keeps drawing via the raw SpriteBatch escape hatch (RenderContext.SpriteBatch/Pixel) into
        // this same already-open batch, since it isn't part of the swappable game-content render path.
        RenderContext.Renderer2D.BeginScene(_currentViewMatrix);

        if (_state.ShowGrid)
        {
            GridRenderer.Draw(RenderContext.SpriteBatch, RenderContext.Pixel, _currentViewMatrix,
                new Vector2(_sceneTarget.Width, _sceneTarget.Height), _currentZoom);
        }

        _state.ActiveScene.Draw(gameTime, MyEngine.Core.Physics.AABB2D.FromViewMatrix(_currentViewMatrix, targetSize));

        RenderContext.Renderer2D.EndScene();
    }

    private void BuildUI()
    {
        // Must be the very first thing this method does, before ANY panel below gets a chance to draw —
        // see ContentBrowserPanel.ProcessConfirmedDelete's own doc comment for why. (It used to run from
        // inside the bottom panel's Draw instead, which is too late: InspectorPanel draws before it and,
        // whenever the asset about to be deleted was also the one selected in the Files/Assets panels, was
        // already queuing an ImGui.Image draw command against its thumbnail earlier in this very frame —
        // a command the renderer would then throw on once this deletion unbound that same texture id.)
        ContentBrowserPanel.ProcessConfirmedDelete(_state);

        EditorLayout.Refresh();

        // The menu bar is drawn LAST, after every fixed panel — Dear ImGui's Z-order for a set of
        // windows re-declared every frame favors whichever was touched most recently that frame, and the
        // File menu's dropdown needs to win that over the Toolbar/panels, not the other way around
        // (which is what rendering the menu bar first was causing: its own dropdown showing up behind
        // the Toolbar's icons instead of on top of them).
        Toolbar.Draw(_state);

        OutlinePanel.Draw(_state);

        var sceneFromFiles = FilesPanel.Draw(_state);
        if (sceneFromFiles != null)
            RequestLoadScene(sceneFromFiles);

        InspectorPanel.Draw(_state);

        var blocksResult = BlocksPanel.Draw(_state);
        if (blocksResult.SceneToOpen != null)
            RequestLoadScene(blocksResult.SceneToOpen);

        var viewportResult = ViewportPanel.Draw(_state, _sceneTextureId);
        _requestedViewportSize = viewportResult.Size;
        _lastViewportMin = viewportResult.ImageScreenMin;
        _lastViewportMax = viewportResult.ImageScreenMax;

        if (viewportResult.RequestedSceneSwitch != null)
            RequestLoadScene(viewportResult.RequestedSceneSwitch);

        _cameraIcons.Draw(_state, _currentViewMatrix, viewportResult.ImageScreenMin, viewportResult.ImageScreenMax, viewportResult.DrawList,
            new Vector2(_projectSettings.WindowWidth, _projectSettings.WindowHeight));
        AudioIconRenderer.Draw(_state, _currentViewMatrix, viewportResult.ImageScreenMin, viewportResult.ImageScreenMax, viewportResult.DrawList);
        ColliderGizmoRenderer.Draw(_state, _currentViewMatrix, _currentZoom, viewportResult.ImageScreenMin, viewportResult.ImageScreenMax, viewportResult.DrawList);
        _gizmo.Update(_state, _currentViewMatrix, _currentZoom,
            viewportResult.ImageScreenMin, viewportResult.IsHovered, viewportResult.DrawList);
        ViewportSelector.Update(_state, _currentViewMatrix, _currentZoom, viewportResult.ImageScreenMin, viewportResult.ImageScreenMax,
            viewportResult.IsHovered, _gizmo.IsDragging);
        HandleViewportDrop(viewportResult);

        DrawMainMenuBar();

        DrawUnsavedChangesModal();
        DrawCompilingOverlay();
    }

    /// <summary>Converts a texture/prefab/audio clip dropped on the Viewport (from within the app, e.g. the
    /// Files tree or Blocks panel) into a placed GameObject at the drop's world position.</summary>
    private void HandleViewportDrop(ViewportResult vr)
    {
        if (vr.DroppedTexturePath == null && vr.DroppedPrefabPath == null && vr.DroppedAudioPath == null) return;

        var localPoint = vr.DropScreenPosition - vr.ImageScreenMin;
        var worldPos = Vector2.Transform(new Vector2(localPoint.X, localPoint.Y), Matrix.Invert(_currentViewMatrix));

        if (vr.DroppedTexturePath != null)
            AssetDropHandler.CreateSpriteFromTexture(_state, vr.DroppedTexturePath, worldPos);
        else if (vr.DroppedPrefabPath != null)
            AssetDropHandler.InstantiatePrefab(_state, vr.DroppedPrefabPath, worldPos);
        else if (vr.DroppedAudioPath != null)
            AssetDropHandler.CreateAudioSourceFromClip(_state, vr.DroppedAudioPath, worldPos);
    }

    // ---------------------------------------------------------------- scene lifecycle (dirty-aware)

    /// <summary>Requests loading <paramref name="path"/>. A no-op if it's already the open scene (avoids
    /// silently discarding in-memory edits for nothing, e.g. double-clicking the scene you're already in).
    /// If there are unsaved changes to a *different* scene, asks for confirmation first.</summary>
    private void RequestLoadScene(string path)
    {
        if (string.Equals(path, _state.CurrentScenePath, StringComparison.OrdinalIgnoreCase))
            return;

        var sceneName = Path.GetFileNameWithoutExtension(path);
        RequestDestructiveAction($"open '{sceneName}'", () => LoadScene(path));
    }

    private void RequestNewScene()
    {
        RequestDestructiveAction("start a new scene", () =>
        {
            _state.EditScene = new Scene("Untitled Scene");
            _state.CurrentScenePath = null;
            _state.SelectGameObject(null);
            _state.Undo.Clear();
            _state.MarkClean();
            _state.LogMessage("Created new scene.");
        });
    }

    /// <summary>Runs <paramref name="action"/> immediately if the current scene has no unsaved changes;
    /// otherwise holds it so DrawUnsavedChangesModal shows a confirmation next frame — a switch can never
    /// silently throw away edits the user hasn't saved yet.</summary>
    private void RequestDestructiveAction(string description, Action action)
    {
        if (!_state.IsDirty)
        {
            action();
            return;
        }

        _pendingDestructiveAction = action;
        _pendingDestructiveDescription = description;
    }

    /// <summary>
    /// Deliberately a plain conditional window rather than ImGui.OpenPopup/BeginPopupModal: those are
    /// scoped by the ID stack active at the moment OpenPopup is called, and RequestDestructiveAction can
    /// be triggered from deep inside a File-menu click while this is drawn from BuildUI's top level —
    /// two different ID-stack contexts that could hash to different popup IDs and silently never open.
    /// A flag-driven window has no such risk: it just checks _pendingDestructiveAction every frame.
    /// </summary>
    private void DrawUnsavedChangesModal()
    {
        if (_pendingDestructiveAction == null) return;

        var viewport = ImGui.GetMainViewport();
        var center = new System.Numerics.Vector2(
            viewport.Pos.X + viewport.Size.X * 0.5f,
            viewport.Pos.Y + viewport.Size.Y * 0.5f);
        ImGui.SetNextWindowPos(center, ImGuiCond.Always, new System.Numerics.Vector2(0.5f, 0.5f));

        bool open = true;
        const ImGuiWindowFlags flags = ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoCollapse
            | ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoDocking;

        ImGui.Begin("Unsaved Changes", ref open, flags);

        ImGui.Text($"'{_state.EditScene.Name}' has unsaved changes.");
        ImGui.Text($"Continuing to {_pendingDestructiveDescription} will discard them.");
        ImGui.Spacing();

        if (ImGui.Button("Save & Continue", new System.Numerics.Vector2(160, 0)))
        {
            if (_state.CurrentScenePath != null)
            {
                SaveScene(_state.CurrentScenePath);
            }
            else
            {
                var path = Path.Combine(_state.ScenesFolder, $"{_state.EditScene.Name}.scene");
                SaveScene(path);
            }
            RunPendingDestructiveAction();
        }

        ImGui.SameLine();
        if (ImGui.Button("Discard & Continue", new System.Numerics.Vector2(160, 0)))
            RunPendingDestructiveAction();

        ImGui.SameLine();
        if (ImGui.Button("Cancel", new System.Numerics.Vector2(100, 0)) || !open)
            _pendingDestructiveAction = null;

        ImGui.End();
    }

    private void RunPendingDestructiveAction()
    {
        _pendingDestructiveAction?.Invoke();
        _pendingDestructiveAction = null;
    }

    /// <summary>Same flag-driven plain-window approach as DrawUnsavedChangesModal, for the same reason —
    /// no dependency on ImGui popup ID-stack scoping, just a check against C# state every frame.</summary>
    private void DrawCompilingOverlay()
    {
        if (!_isCompiling) return;

        var viewport = ImGui.GetMainViewport();
        var center = new System.Numerics.Vector2(
            viewport.Pos.X + viewport.Size.X * 0.5f,
            viewport.Pos.Y + viewport.Size.Y * 0.5f);
        ImGui.SetNextWindowPos(center, ImGuiCond.Always, new System.Numerics.Vector2(0.5f, 0.5f));

        const ImGuiWindowFlags flags = ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoCollapse
            | ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.AlwaysAutoResize
            | ImGuiWindowFlags.NoDocking | ImGuiWindowFlags.NoMove;

        ImGui.Begin("##CompilingOverlay", flags);
        int dots = (int)(ImGui.GetTime() * 2.0) % 4;
        ImGui.Text("Compiling scripts" + new string('.', dots));
        ImGui.End();
    }

    private void DrawMainMenuBar()
    {
        if (!ImGui.BeginMainMenuBar()) return;

        // Small engine watermark next to the menu, the same way many editors (including the reference
        // Arian shared) show their own logo beside File/Edit/... — purely decorative, drawn only when the
        // asset actually loaded (see LoadContent).
        if (_logoTextureId != IntPtr.Zero)
        {
            ImGui.Image(_logoTextureId, new System.Numerics.Vector2(18f, 18f));
            ImGui.SameLine();
        }

        bool editingAllowed = !_state.IsPlaying;

        if (ImGui.BeginMenu("File"))
        {
            if (ImGui.MenuItem("New Scene", string.Empty, false, editingAllowed))
                RequestNewScene();

            if (ImGui.MenuItem("Save Scene", string.Empty, false, editingAllowed && _state.CurrentScenePath != null))
                SaveScene(_state.CurrentScenePath!);

            if (ImGui.MenuItem("Save Scene As...", string.Empty, false, editingAllowed))
            {
                var path = Path.Combine(_state.ScenesFolder, $"{_state.EditScene.Name}.scene");
                SaveScene(path);
            }

            if (ImGui.BeginMenu("Open Scene", editingAllowed))
            {
                // Recursive: scenes always live under Assets/Scenes, but the user can organize them into
                // subfolders in there (e.g. Assets/Scenes/Levels/), and this should still find those.
                var files = Directory.Exists(_state.ScenesFolder)
                    ? Directory.GetFiles(_state.ScenesFolder, "*.scene", SearchOption.AllDirectories)
                    : Array.Empty<string>();

                if (files.Length == 0) ImGui.TextDisabled("(no scenes saved yet)");

                foreach (var file in files)
                {
                    var label = Path.GetRelativePath(_state.ScenesFolder, file);
                    if (ImGui.MenuItem(label))
                        RequestLoadScene(file);
                }
                ImGui.EndMenu();
            }

            ImGui.Separator();
            if (ImGui.MenuItem("Build Settings...")) _showBuildSettings = true;
            if (ImGui.MenuItem("Build", string.Empty, false, editingAllowed && !_isBuilding))
                TriggerBuild();
            if (ImGui.MenuItem("Show Last Build Report", string.Empty, false, _lastBuildResult != null))
                _showBuildReport = true;
            if (ImGui.MenuItem("Open Play Mode Save Folder"))
                OpenPlayModeSaveFolder();

            ImGui.Separator();
            if (ImGui.MenuItem("Exit")) Exit();

            ImGui.EndMenu();
        }

        if (ImGui.BeginMenu("Edit"))
        {
            if (ImGui.MenuItem($"Undo{DescribeSuffix(_state.Undo.NextUndoDescription)}", "Ctrl+Z", false, _state.Undo.CanUndo))
                _state.PerformUndo();
            if (ImGui.MenuItem($"Redo{DescribeSuffix(_state.Undo.NextRedoDescription)}", "Ctrl+Y", false, _state.Undo.CanRedo))
                _state.PerformRedo();
            ImGui.EndMenu();
        }

        if (ImGui.BeginMenu("GameObject", editingAllowed))
        {
            if (ImGui.MenuItem("Empty GameObject"))
                AssetDropHandler.CreateEmptyGameObject(_state);

            ImGui.Separator();
            // 5 basic shapes to start from — see PrimitiveShapeGenerator's doc comment for why these are
            // real generated texture assets rather than a special-cased draw mode.
            if (ImGui.MenuItem("Square")) AssetDropHandler.CreatePrimitiveShape(_state, PrimitiveShape.Square);
            if (ImGui.MenuItem("Circle")) AssetDropHandler.CreatePrimitiveShape(_state, PrimitiveShape.Circle);
            if (ImGui.MenuItem("Triangle")) AssetDropHandler.CreatePrimitiveShape(_state, PrimitiveShape.Triangle);
            if (ImGui.MenuItem("Capsule")) AssetDropHandler.CreatePrimitiveShape(_state, PrimitiveShape.Capsule);
            if (ImGui.MenuItem("Star")) AssetDropHandler.CreatePrimitiveShape(_state, PrimitiveShape.Star);
            ImGui.EndMenu();
        }

        if (ImGui.BeginMenu("Scripts"))
        {
            if (ImGui.MenuItem("Recompile", string.Empty, false, !_isCompiling))
                TriggerScriptCompilation();
            if (ImGui.MenuItem("Open C# Project in IDE"))
            {
                var error = ExternalEditorLauncher.OpenScriptProject(_projectPath, _state.LogMessage);
                if (error != null) _state.LogMessage($"ERROR: {error}");
            }
            ImGui.EndMenu();
        }

        ImGui.EndMainMenuBar();

        DrawBuildSettingsPopup();
        DrawBuildReportPopup();
    }

    private void DrawBuildSettingsPopup()
    {
        if (_showBuildSettings)
        {
            ImGui.OpenPopup("Build Settings");
            _showBuildSettings = false;
        }

        ImGui.SetNextWindowSize(new System.Numerics.Vector2(480f, 0f));
        if (!ImGui.BeginPopupModal("Build Settings", ImGuiWindowFlags.NoResize)) return;

        // ImGui.InputText needs a ref to a live string variable, and C# can't ref a property directly —
        // each field is copied into a local, edited there, then written back to ProjectSettings only when
        // InputText reports a change this frame (which, without EnterReturnsTrue, is every keystroke).
        string gameName = _projectSettings.GameName;
        if (ImGui.InputText("Game Name", ref gameName, 64)) _projectSettings.GameName = gameName;

        string version = _projectSettings.GameVersion;
        if (ImGui.InputText("Version", ref version, 32)) _projectSettings.GameVersion = version;

        var width = _projectSettings.WindowWidth;
        if (ImGui.InputInt("Window Width", ref width)) _projectSettings.WindowWidth = Math.Max(320, width);
        var height = _projectSettings.WindowHeight;
        if (ImGui.InputInt("Window Height", ref height)) _projectSettings.WindowHeight = Math.Max(240, height);

        bool fullscreen = _projectSettings.Fullscreen;
        if (ImGui.Checkbox("Start Fullscreen", ref fullscreen)) _projectSettings.Fullscreen = fullscreen;

        bool resizable = _projectSettings.Resizable;
        if (ImGui.Checkbox("Resizable Window", ref resizable)) _projectSettings.Resizable = resizable;

        bool vsync = _projectSettings.VSync;
        if (ImGui.Checkbox("VSync", ref vsync)) _projectSettings.VSync = vsync;

        var targetFps = _projectSettings.TargetFps;
        if (ImGui.InputInt("Target FPS", ref targetFps)) _projectSettings.TargetFps = GameManifest.NormalizeTargetFps(targetFps);
        DrawWrappedHint($"60 matches Play Mode in the Editor. Allowed range: {GameManifest.MinTargetFps} to {GameManifest.MaxTargetFps}.");

        ImGui.Spacing();
        string iconPath = _projectSettings.IconPath;
        if (ImGui.InputText("Icon Path (.ico)", ref iconPath, 260)) _projectSettings.IconPath = iconPath;
        ImGui.TextDisabled("Relative to the project folder. Leave empty to use MyEngine's default icon.");

        string bootScene = _projectSettings.BootScenePath;
        if (ImGui.InputText("Boot Scene", ref bootScene, 260)) _projectSettings.BootScenePath = bootScene;
        ImGui.TextDisabled("Relative to Assets/, e.g. Scenes/Main.scene. Leave empty to use the last-opened scene.");

        ImGui.Spacing();
        string company = _projectSettings.CompanyName;
        if (ImGui.InputText("Company", ref company, 128)) _projectSettings.CompanyName = company;
        string copyright = _projectSettings.Copyright;
        if (ImGui.InputText("Copyright", ref copyright, 128)) _projectSettings.Copyright = copyright;
        DrawWrappedHint("Shown in the game's Windows file properties (Details tab). Company defaults to the game name; the Copyright line is left out when empty.");

        ImGui.Spacing();
        bool showSplash = _projectSettings.ShowEngineSplash;
        if (ImGui.Checkbox("Show PreyEngine splash at startup", ref showSplash)) _projectSettings.ShowEngineSplash = showSplash;
        DrawWrappedHint("Credits the engine your game is made with (recommended). PreyEngine is open source, so this is your choice; its license notices are included in every build either way.");

        ImGui.Spacing();
        ImGui.Separator();
        if (ImGui.Button("Save", new System.Numerics.Vector2(120f, 0f)))
        {
            _projectSettings.Save(_projectPath);
            ImGui.CloseCurrentPopup();
        }
        ImGui.SameLine();
        if (ImGui.Button("Close", new System.Numerics.Vector2(120f, 0f)))
            ImGui.CloseCurrentPopup();

        ImGui.EndPopup();
    }

    /// <summary>Small grey helper text under a setting that wraps to the window width instead of running off the edge.</summary>
    private static void DrawWrappedHint(string text)
    {
        ImGui.PushTextWrapPos(0f);
        ImGui.TextDisabled(text);
        ImGui.PopTextWrapPos();
    }

    /// <summary>The window shown after every build (and from File &gt; Show Last Build Report): where the build and
    /// its zip are, how big they are, and anything worth fixing before publishing.</summary>
    private void DrawBuildReportPopup()
    {
        if (_showBuildReport)
        {
            ImGui.OpenPopup("Build Report");
            _showBuildReport = false;
        }

        ImGui.SetNextWindowSize(new System.Numerics.Vector2(640f, 0f));
        if (!ImGui.BeginPopupModal("Build Report", ImGuiWindowFlags.NoResize)) return;

        var result = _lastBuildResult;
        if (result == null)
        {
            ImGui.TextDisabled("No build has been run yet.");
        }
        else if (!result.Success)
        {
            ImGui.TextColored(new System.Numerics.Vector4(0.95f, 0.4f, 0.4f, 1f), "Build failed");
            ImGui.TextWrapped(result.Message);
            ImGui.Spacing();
            DrawWrappedHint("The Console panel has the full log of what happened.");
        }
        else
        {
            ImGui.TextColored(new System.Numerics.Vector4(0.45f, 0.85f, 0.5f, 1f), "Build succeeded");
            ImGui.Spacing();
            foreach (var line in result.ReportLines)
            {
                // Warning lines are the ones the report indents with a dash; everything else is plain information.
                if (line.StartsWith("  - ", StringComparison.Ordinal))
                    ImGui.TextColored(new System.Numerics.Vector4(0.95f, 0.72f, 0.3f, 1f), line);
                else
                    ImGui.TextWrapped(line);
            }
        }

        ImGui.Spacing();
        ImGui.Separator();

        if (result is { Success: true, OutputDirectory: not null })
        {
            if (ImGui.Button("Open Builds Folder", new System.Numerics.Vector2(180f, 0f)))
            {
                var buildsFolder = Path.GetDirectoryName(result.OutputDirectory) ?? result.OutputDirectory;
                var error = ExternalEditorLauncher.Open(buildsFolder);
                if (error != null) _state.LogMessage($"ERROR: {error}");
            }
            ImGui.SameLine();
        }
        if (ImGui.Button("Close", new System.Numerics.Vector2(120f, 0f)))
            ImGui.CloseCurrentPopup();

        ImGui.EndPopup();
    }

    /// <summary>File &gt; Open Play Mode Save Folder: shows where SaveData keeps this project's files while testing,
    /// so a developer can inspect or delete them (deleting them is how to test "first launch" again).</summary>
    private void OpenPlayModeSaveFolder()
    {
        try
        {
            var folder = _state.PlayModeSaveDirectory;
            Directory.CreateDirectory(folder);
            var error = ExternalEditorLauncher.Open(folder);
            if (error != null) _state.LogMessage($"ERROR: {error}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _state.LogMessage($"ERROR: could not open the Play Mode save folder: {ex.Message}");
        }
    }

    private static string DescribeSuffix(string? description) => description != null ? $" ({description})" : "";

    private void SaveScene(string path)
    {
        try
        {
            SceneSerializer.Save(_state.EditScene, path);
            _state.CurrentScenePath = path;
            _state.RegisterSceneTab(path);
            _state.Assets.Refresh();
            _state.MarkClean();
            RememberLastOpenedScene(path);
            _state.LogMessage($"Saved scene to {path}");
        }
        catch (Exception ex)
        {
            _state.LogMessage($"ERROR saving scene: {ex.Message}");
        }
    }

    private void LoadScene(string path)
    {
        try
        {
            var scene = SceneSerializer.Load(path);
            _state.Assets.ResolveSceneAssets(scene);
            _state.EditScene = scene;
            _state.CurrentScenePath = path;
            _state.RegisterSceneTab(path);
            _state.SelectGameObject(null);
            _state.Undo.Clear();
            _state.MarkClean();
            RememberLastOpenedScene(path);
            _state.LogMessage($"Loaded scene from {path}");
        }
        catch (Exception ex)
        {
            _state.LogMessage($"ERROR loading scene: {ex.Message}");
        }
    }

    /// <summary>Records which scene is open right now so the next launch of this project resumes here
    /// instead of showing a placeholder.</summary>
    private void RememberLastOpenedScene(string absolutePath)
    {
        _projectSettings.LastOpenedScenePath = Path.GetRelativePath(_projectPath, absolutePath);
        _projectSettings.Save(_projectPath);
    }

    protected override void UnloadContent()
    {
        SaveData.Save(); // closing the Editor mid-Play-Mode: write whatever the game had saved (never throws)
        Window.FileDrop -= OnFileDrop;
        _cameraIcons?.Dispose();
        if (_logoTexture != null)
        {
            _imGui.UnbindTexture(_logoTextureId);
            _logoTexture.Dispose();
        }
        _state?.Assets.Dispose();
        base.UnloadContent();
    }
}
