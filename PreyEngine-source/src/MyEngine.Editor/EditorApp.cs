using ImGuiNET;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MyEngine.Core.Components;
using MyEngine.Core.Rendering;
using MyEngine.Core.SceneSystem;
using MyEngine.Editor.Panels;
using MyEngine.Editor.UndoSystem;
using MyEngine.EditorFramework;

namespace MyEngine.Editor;

public class EditorApp : Game
{
    private readonly GraphicsDeviceManager _graphics;
    private readonly string _projectPath;
    private ImGuiRenderer _imGui = null!;
    private EditorState _state = null!;
    private readonly GizmoSystem _gizmo = new();

    private RenderTarget2D? _sceneTarget;
    private IntPtr _sceneTextureId = IntPtr.Zero;
    private System.Numerics.Vector2 _requestedViewportSize = new(1280, 720);

    // Refreshed every Draw call while rendering the scene, then reused by the gizmo and by
    // viewport drag-and-drop (both need to convert between screen space and world space).
    private Matrix _currentViewMatrix = Matrix.Identity;
    private float _currentZoom = 1f;

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
        Window.Title = $"MyEngine Editor — {Path.GetFileName(projectPath.TrimEnd(Path.DirectorySeparatorChar))}";
    }

    protected override void LoadContent()
    {
        RenderContext.Initialize(GraphicsDevice);

        _imGui = new ImGuiRenderer(this);
        _imGui.RebuildFontAtlas();

        var assets = new AssetDatabase(_projectPath, GraphicsDevice, _imGui);
        assets.Refresh();

        _state = new EditorState(_projectPath, assets);

        BuildDemoScene();
        _state.LogMessage("Editor started. Welcome to MyEngine!");
    }

    /// <summary>A tiny starter scene so the viewport isn't empty on first run.</summary>
    private void BuildDemoScene()
    {
        var cameraObj = _state.EditScene.CreateGameObject("Main Camera");
        cameraObj.AddComponent<Camera2D>();

        var square = _state.EditScene.CreateGameObject("Square");
        var renderer = square.AddComponent<SpriteRenderer>();
        renderer.Color = new Color(90, 160, 235);
        renderer.Size = new Vector2(120, 120);
    }

    protected override void Update(GameTime gameTime)
    {
        _state.Assets.ProcessPendingChanges();
        HandleGlobalShortcuts();

        if (_state.IsPlaying)
            _state.ActiveScene.Update(gameTime);

        base.Update(gameTime);
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

    protected override void Draw(GameTime gameTime)
    {
        EnsureSceneRenderTarget();
        RenderSceneToTarget(gameTime);

        GraphicsDevice.SetRenderTarget(null);
        GraphicsDevice.Clear(new Color(36, 36, 40));

        _imGui.BeforeLayout(gameTime);
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

        _currentViewMatrix = camera?.GetViewMatrix(new Vector2(_sceneTarget.Width, _sceneTarget.Height))
                              ?? Matrix.CreateTranslation(_sceneTarget.Width / 2f, _sceneTarget.Height / 2f, 0f);
        _currentZoom = camera?.Zoom ?? 1f;

        // BackToFront so SpriteRenderer.SortingOrder (encoded as layerDepth) actually controls draw order,
        // and so the grid (layerDepth 1, drawn first below) always stays behind every GameObject.
        RenderContext.SpriteBatch.Begin(
            sortMode: SpriteSortMode.BackToFront,
            blendState: BlendState.AlphaBlend,
            samplerState: SamplerState.PointClamp,
            transformMatrix: _currentViewMatrix);

        if (_state.ShowGrid)
        {
            GridRenderer.Draw(RenderContext.SpriteBatch, RenderContext.Pixel, _currentViewMatrix,
                new Vector2(_sceneTarget.Width, _sceneTarget.Height), _currentZoom);
        }

        _state.ActiveScene.Draw(gameTime);

        RenderContext.SpriteBatch.End();
    }

    private void BuildUI()
    {
        ImGui.DockSpaceOverViewport(0, ImGui.GetMainViewport(), ImGuiDockNodeFlags.PassthruCentralNode);

        DrawMainMenuBar();

        HierarchyPanel.Draw(_state);
        InspectorPanel.Draw(_state);
        ConsolePanel.Draw(_state);

        var contentBrowserResult = ContentBrowserPanel.Draw(_state);
        if (contentBrowserResult.SceneToOpen != null)
            LoadScene(contentBrowserResult.SceneToOpen);

        var viewportResult = ViewportPanel.Draw(_state, _sceneTextureId);
        _requestedViewportSize = viewportResult.Size;
        if (viewportResult.RequestedSceneSwitch != null)
            LoadScene(viewportResult.RequestedSceneSwitch);

        _gizmo.Update(_state, _currentViewMatrix, _currentZoom, viewportResult.ImageScreenMin, viewportResult.IsHovered);
        HandleViewportDrop(viewportResult);
    }

    /// <summary>Converts a texture/prefab dropped on the Viewport into a placed GameObject at the drop's world position.</summary>
    private void HandleViewportDrop(ViewportResult vr)
    {
        if (vr.DroppedTexturePath == null && vr.DroppedPrefabPath == null) return;

        var localPoint = vr.DropScreenPosition - vr.ImageScreenMin;
        var localXna = new Vector2(localPoint.X, localPoint.Y);
        var worldPos = Vector2.Transform(localXna, Matrix.Invert(_currentViewMatrix));

        if (vr.DroppedTexturePath != null)
        {
            var go = _state.ActiveScene.CreateGameObject(Path.GetFileNameWithoutExtension(vr.DroppedTexturePath));
            var sr = go.AddComponent<SpriteRenderer>();
            sr.TexturePath = vr.DroppedTexturePath;
            sr.Texture = _state.Assets.GetTexture(vr.DroppedTexturePath);
            go.Transform.Position = worldPos;
            _state.SelectGameObject(go);
            _state.LogMessage($"Placed sprite '{go.Name}'.");

            var command = CreateDeleteGameObjectCommand.ForCreate(
                $"Create {go.Name}", _state.ActiveScene, _state.Assets.ResolveSceneTextures, go);
            _state.Undo.Push(command);
        }
        else if (vr.DroppedPrefabPath != null)
        {
            var asset = _state.Assets.Find(vr.DroppedPrefabPath);
            if (asset == null) return;

            try
            {
                var root = PrefabSerializer.Instantiate(asset.FullPath, _state.ActiveScene, worldPosition: worldPos);
                _state.SelectGameObject(root);
                _state.LogMessage($"Instantiated prefab '{root.Name}'.");

                var command = CreateDeleteGameObjectCommand.ForCreate(
                    $"Instantiate {root.Name}", _state.ActiveScene, _state.Assets.ResolveSceneTextures, root);
                _state.Undo.Push(command);
            }
            catch (Exception ex)
            {
                _state.LogMessage($"ERROR instantiating prefab: {ex.Message}");
            }
        }
    }

    private void DrawMainMenuBar()
    {
        if (!ImGui.BeginMainMenuBar()) return;

        bool editingAllowed = !_state.IsPlaying;

        if (ImGui.BeginMenu("File"))
        {
            if (ImGui.MenuItem("New Scene", string.Empty, false, editingAllowed))
            {
                _state.EditScene = new Scene("Untitled Scene");
                _state.CurrentScenePath = null;
                _state.SelectGameObject(null);
                _state.Undo.Clear();
                _state.LogMessage("Created new scene.");
            }

            if (ImGui.MenuItem("Save Scene", string.Empty, false, editingAllowed && _state.CurrentScenePath != null))
                SaveScene(_state.CurrentScenePath!);

            if (ImGui.MenuItem("Save Scene As...", string.Empty, false, editingAllowed))
            {
                var path = Path.Combine(_state.ScenesFolder, $"{_state.EditScene.Name}.scene");
                SaveScene(path);
            }

            if (ImGui.BeginMenu("Open Scene", editingAllowed))
            {
                var files = Directory.Exists(_state.ScenesFolder)
                    ? Directory.GetFiles(_state.ScenesFolder, "*.scene")
                    : Array.Empty<string>();

                if (files.Length == 0) ImGui.TextDisabled("(no scenes saved yet)");

                foreach (var file in files)
                {
                    if (ImGui.MenuItem(Path.GetFileNameWithoutExtension(file)))
                        LoadScene(file);
                }
                ImGui.EndMenu();
            }

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

        ImGui.EndMainMenuBar();
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
            _state.Assets.ResolveSceneTextures(scene);
            _state.EditScene = scene;
            _state.CurrentScenePath = path;
            _state.RegisterSceneTab(path);
            _state.SelectGameObject(null);
            _state.Undo.Clear();
            _state.LogMessage($"Loaded scene from {path}");
        }
        catch (Exception ex)
        {
            _state.LogMessage($"ERROR loading scene: {ex.Message}");
        }
    }

    protected override void UnloadContent()
    {
        _state?.Assets.Dispose();
        base.UnloadContent();
    }
}
