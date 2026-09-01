using System.Reflection;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MyEngine.Core;
using MyEngine.Core.Components;
using MyEngine.Core.Rendering;
using MyEngine.Core.SceneSystem;
using MyEngine.Core.Scripting;
using CoreInput = MyEngine.Core.InputSystem.Input;

namespace MyEngine.Runtime;

/// <summary>
/// The whole standalone player. Deliberately as small as MyEngine.Editor's own Play Mode game loop —
/// Update calls Input/Time/Scene.Update in the same order EditorApp does while playing, Draw finds the
/// active Camera2D and renders straight to the backbuffer (no offscreen RenderTarget2D + ImGui image hop,
/// since there's no Editor UI to composite into here — one less full-frame copy per frame than the Editor
/// pays, which matters on the low-end hardware this is meant to run acceptably on).
///
/// What's deliberately absent, simply because this project never references MyEngine.Editor or ImGui.NET
/// at all: no grid, no gizmos, no camera-icon overlays, no collider outlines, no Hierarchy/Inspector/
/// Console/Content Browser, no Undo stack, no hot reload. None of that is filtered out at runtime — it was
/// never compiled in, so a build can't accidentally ship any of it.
/// </summary>
public sealed class RuntimeGame : Game
{
    private readonly GraphicsDeviceManager _graphics;
    private readonly string _gameDirectory;
    private GameManifest _manifest = null!;
    private Scene _scene = null!;

    public RuntimeGame(string gameDirectory)
    {
        _gameDirectory = gameDirectory;
        _graphics = new GraphicsDeviceManager(this);
        Content.RootDirectory = "Content";
        IsMouseVisible = true;
    }

    protected override void Initialize()
    {
        _manifest = GameManifest.Load(_gameDirectory);

        Window.Title = $"{_manifest.GameName} ({_manifest.Version})";
        _graphics.PreferredBackBufferWidth = _manifest.WindowWidth;
        _graphics.PreferredBackBufferHeight = _manifest.WindowHeight;
        _graphics.IsFullScreen = _manifest.Fullscreen;
        _graphics.ApplyChanges();

        base.Initialize();
    }

    protected override void LoadContent()
    {
        RenderContext.Initialize(GraphicsDevice);

        LoadScripts();

        var assetsRoot = Path.Combine(_gameDirectory, "Assets");
        var scenePath = Path.Combine(assetsRoot, _manifest.BootScenePath);
        _scene = SceneSerializer.Load(scenePath);
        RuntimeAssets.ResolveSceneTextures(_scene, assetsRoot, GraphicsDevice);
    }

    private void LoadScripts()
    {
        if (string.IsNullOrEmpty(_manifest.ScriptsAssemblyName)) return; // project had no scripts

        var dllPath = Path.Combine(_gameDirectory, _manifest.ScriptsAssemblyName);
        if (!File.Exists(dllPath)) return;

        var assembly = Assembly.LoadFrom(dllPath);
        ScriptRegistry.Register(assembly.GetTypes());
    }

    protected override void Update(GameTime gameTime)
    {
        CoreInput.Update();
        Time.Update(gameTime);
        _scene.Update(gameTime);

        base.Update(gameTime);
    }

    protected override void Draw(GameTime gameTime)
    {
        var camera = FindActiveCamera();

        GraphicsDevice.Clear(camera?.BackgroundColor ?? new Color(30, 30, 35));

        var viewportSize = new Vector2(GraphicsDevice.PresentationParameters.BackBufferWidth,
            GraphicsDevice.PresentationParameters.BackBufferHeight);
        var viewMatrix = camera?.GetViewMatrix(viewportSize) ?? Matrix.Identity;

        RenderContext.SpriteBatch.Begin(
            SpriteSortMode.BackToFront, BlendState.AlphaBlend, SamplerState.PointClamp,
            transformMatrix: viewMatrix);
        _scene.Draw(gameTime);
        RenderContext.SpriteBatch.End();

        base.Draw(gameTime);
    }

    private Camera2D? FindActiveCamera()
    {
        foreach (var go in _scene.GameObjects)
        {
            var camera = go.GetComponent<Camera2D>();
            if (camera != null && camera.IsActive) return camera;
        }
        return null;
    }
}
