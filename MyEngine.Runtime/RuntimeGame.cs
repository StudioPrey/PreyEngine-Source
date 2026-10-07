using System.Reflection;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MyEngine.Core;
using MyEngine.Core.Audio;
using MyEngine.Core.Components;
using MyEngine.Core.Rendering;
using MyEngine.Core.Saving;
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
/// Startup order: the whole game (scripts, scene, assets, save data) loads in LoadContent, before the window is
/// even visible. If the engine splash is enabled it then plays first, and the game loop proper — input, time,
/// scene update, drawing — only begins once it has finished.
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
    private EngineSplash? _splash;

    public RuntimeGame(string gameDirectory)
    {
        _gameDirectory = gameDirectory;
        _graphics = new GraphicsDeviceManager(this);
        Content.RootDirectory = "Content";
        IsMouseVisible = true;
    }

    /// <summary>The manifest, or null before it has been read — the crash reporter uses it to name the game
    /// and must cope with a crash that happened before that point.</summary>
    internal GameManifest? LoadedManifest => _manifest;

    protected override void Initialize()
    {
        _manifest = GameManifest.Load(_gameDirectory);

        Window.Title = $"{_manifest.GameName} ({_manifest.Version})";
        _graphics.PreferredBackBufferWidth = _manifest.WindowWidth;
        _graphics.PreferredBackBufferHeight = _manifest.WindowHeight;
        _graphics.IsFullScreen = _manifest.Fullscreen;
        _graphics.SynchronizeWithVerticalRetrace = _manifest.VSync;
        Window.AllowUserResizing = _manifest.Resizable;
        // Fixed time step (the default) at this rate: both Update and Draw run at it, and Time.DeltaTime is
        // exactly 1/TargetFps. 60 — the default — is what Play Mode in the Editor runs at.
        TargetElapsedTime = TimeSpan.FromSeconds(1.0 / GameManifest.NormalizeTargetFps(_manifest.TargetFps));
        _graphics.ApplyChanges();

        base.Initialize();
    }

    protected override void LoadContent()
    {
        RenderContext.Initialize(GraphicsDevice);

        var assetsRoot = Path.Combine(_gameDirectory, "Assets");
        RuntimeAssets.Initialize(assetsRoot, GraphicsDevice);
        // SpriteAnimation/AudioSource (and anything else that needs to resolve an asset path outside of
        // the once-off scene load below, e.g. swapping frames many times a second) call through these —
        // see RenderContext.TextureResolver's and AudioContext.ClipResolver's doc comments.
        RenderContext.TextureResolver = RuntimeAssets.ResolveTexture;
        AudioContext.ClipResolver = RuntimeAssets.ResolveSound;

        // Save data has to be open before any script can run; Awake/Start happen on the first scene Update.
        SaveData.Initialize(SaveData.GetGameDirectory(_manifest.GameName));

        LoadScripts();

        var scenePath = Path.Combine(assetsRoot, _manifest.BootScenePath);
        _scene = SceneSerializer.Load(scenePath);
        RuntimeAssets.ResolveSceneAssets(_scene);

        if (_manifest.ShowEngineSplash)
            TryCreateSplash();
    }

    /// <summary>The splash is a courtesy, not a requirement: if it can't be prepared (an image the graphics
    /// driver refuses, a build missing the embedded artwork) the game starts without it and the reason goes in
    /// the log file, instead of every player hitting a crash before the game even begins.</summary>
    private void TryCreateSplash()
    {
        try
        {
            _splash = EngineSplash.Create(GraphicsDevice);
        }
        catch (Exception ex) when (ex is InvalidOperationException or InvalidDataException or IOException or NotSupportedException)
        {
            CrashReporter.TryAppend(_gameDirectory, $"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC] NOTE: the engine splash could not be shown and was skipped: {ex.Message}{Environment.NewLine}");
        }
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
        if (_splash != null)
        {
            // The game has not started yet: no input, no Time, no scene update (so no Awake/Start, physics or
            // audio) until the splash is done. Its texture and batch are released as soon as it is.
            _splash.Update(gameTime);
            if (_splash.IsFinished)
            {
                _splash.Dispose();
                _splash = null;
            }
            base.Update(gameTime);
            return;
        }

        CoreInput.Update();
        Time.Update(gameTime);
        _scene.Update(gameTime);
        SaveData.TickAutoSave((float)gameTime.ElapsedGameTime.TotalSeconds);

        base.Update(gameTime);
    }

    protected override void OnExiting(object sender, ExitingEventArgs args)
    {
        SaveData.Save(); // never throws; on failure the data stays in memory and LastError says why
        base.OnExiting(sender, args);
    }

    protected override void Draw(GameTime gameTime)
    {
        if (_splash != null)
        {
            _splash.Draw(GraphicsDevice);
            base.Draw(gameTime);
            return;
        }

        var camera = FindActiveCamera();

        GraphicsDevice.Clear(camera?.BackgroundColor ?? new Color(30, 30, 35));

        var viewportSize = new Vector2(GraphicsDevice.PresentationParameters.BackBufferWidth,
            GraphicsDevice.PresentationParameters.BackBufferHeight);
        var viewMatrix = camera?.GetViewMatrix(viewportSize) ?? Matrix.Identity;

        RenderContext.Renderer2D.BeginScene(viewMatrix);
        _scene.Draw(gameTime, MyEngine.Core.Physics.AABB2D.FromViewMatrix(viewMatrix, viewportSize));
        RenderContext.Renderer2D.EndScene();

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
