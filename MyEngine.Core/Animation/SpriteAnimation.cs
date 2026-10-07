using Microsoft.Xna.Framework;
using MyEngine.Core.Components;
using MyEngine.Core.ECS;
using MyEngine.Core.Rendering;

namespace MyEngine.Core.Animation;

/// <summary>
/// Plays back a SpriteAnimationClip by driving a sibling SpriteRenderer's TexturePath/Texture/SourceRect
/// each frame. Deliberately never draws anything itself — SpriteRenderer stays the one place sprite drawing
/// happens, and so it's also the only thing that needs to know about IRenderer2D; SpriteAnimation only ever
/// touches SpriteRenderer's already-existing public fields.
///
/// ## Setup
/// Add both a SpriteRenderer and a SpriteAnimation to the same GameObject — order doesn't matter, the
/// sibling SpriteRenderer is looked up on demand, not cached at attach time. Define one or more clips
/// (AddClip, or the Editor's Inspector/grid-slicer panel), then either set DefaultClip to auto-play one on
/// start, or call Play("ClipName") from a script.
///
/// ## From a script
/// <code>
/// var anim = GetComponent&lt;SpriteAnimation&gt;();
/// anim.Play("Walk");
/// ...
/// if (Input.IsKeyDown(Keys.Space)) anim.Play("Jump", restart: true);
/// </code>
/// To react to a clip finishing or advancing a frame, override the callbacks on Component/Script — see
/// OnAnimationComplete/OnAnimationFrame in Component.cs — rather than polling IsPlaying/CurrentFrameIndex
/// every tick.
///
/// ## Editor-time preview
/// Component.Update (and therefore SpriteAnimation.Update) only ever runs during Play Mode — see
/// EditorApp.Update, which calls Scene.Update only while IsPlaying. Tick(float) is the same per-frame
/// advance logic Update(GameTime) calls, exposed separately so the Inspector's animation preview can drive
/// it directly by hand while the game isn't running (see InspectorPanel.DrawSpriteAnimation).
/// </summary>
public sealed class SpriteAnimation : Component, IAnimationSource
{
    private readonly Dictionary<string, SpriteAnimationClip> _clips = new();
    private readonly FrameSequencePlayer _player = new();
    private SpriteAnimationClip? _currentClip;
    private bool _hasAutoStarted;

    public IReadOnlyDictionary<string, SpriteAnimationClip> Clips => _clips;

    /// <summary>Clip to start playing automatically the first time this component ticks during Play Mode
    /// (Unity calls the equivalent "Play On Awake"). Null/empty = nothing plays until a script calls Play().</summary>
    public string? DefaultClip { get; set; }

    public string? CurrentClipName => _currentClip?.Name;
    public bool IsPlaying => _player.IsPlaying;
    public int CurrentFrameIndex => _player.CurrentFrame;

    // ---------------------------------------------------------------- clip authoring

    /// <summary>Adds a new empty clip named <paramref name="name"/> (or a de-duplicated "name 2", "name 3",
    /// ... if that name is already taken) and returns it for the caller to fill in.</summary>
    public SpriteAnimationClip AddClip(string name)
    {
        var uniqueName = name;
        int suffix = 2;
        while (_clips.ContainsKey(uniqueName))
            uniqueName = $"{name} {suffix++}";

        var clip = new SpriteAnimationClip { Name = uniqueName };
        _clips[uniqueName] = clip;
        return clip;
    }

    /// <summary>Removes the named clip. Stops playback first if it was the one currently playing.</summary>
    public bool RemoveClip(string name)
    {
        if (_currentClip?.Name == name) Stop();
        return _clips.Remove(name);
    }

    public SpriteAnimationClip? GetClip(string name) => _clips.GetValueOrDefault(name);

    /// <summary>Renames a clip. The dictionary key and SpriteAnimationClip.Name are kept in sync
    /// deliberately (Play(name) looks up by key) — use this instead of setting clip.Name directly, which
    /// would desync the two. Returns false without changing anything if oldName doesn't exist or newName
    /// is already taken by a different clip.</summary>
    public bool RenameClip(string oldName, string newName)
    {
        if (oldName == newName) return true;
        if (string.IsNullOrWhiteSpace(newName)) return false;
        if (!_clips.TryGetValue(oldName, out var clip) || _clips.ContainsKey(newName)) return false;

        _clips.Remove(oldName);
        clip.Name = newName;
        _clips[newName] = clip;
        if (DefaultClip == oldName) DefaultClip = newName;
        return true;
    }

    // ---------------------------------------------------------------- playback control

    /// <param name="clipName">Must match a clip already added via AddClip. No-op (logs nothing, throws
    /// nothing) if the name isn't found or the clip has no frames yet — an animator mid-setup in the
    /// Inspector shouldn't be able to crash a running scene.</param>
    /// <param name="restart">True (default) always plays from frame 0. False resumes from the current
    /// frame/clock if this is already the playing clip (useful for e.g. re-triggering Play("Walk") every
    /// tick from a movement script without visibly restarting the stride each frame); switching to a
    /// *different* clip always restarts regardless of this flag, since resuming an unrelated clip's old
    /// clock position wouldn't mean anything.</param>
    public void Play(string clipName, bool restart = true)
    {
        if (!_clips.TryGetValue(clipName, out var clip) || clip.Frames.Count == 0) return;

        bool isSameClip = _currentClip == clip;
        _currentClip = clip;
        _player.FrameCount = clip.Frames.Count;
        _player.FrameRate = clip.FrameRate;
        _player.LoopMode = clip.LoopMode;
        _player.Play(restart: restart || !isSameClip);
        ApplyFrameToRenderer();
    }

    /// <summary>Stops and rewinds. CurrentClipName becomes null.</summary>
    public void Stop()
    {
        _player.Stop();
        _currentClip = null;
    }

    /// <summary>Holds on the current frame. Unlike Stop(), CurrentClipName/CurrentFrameIndex are preserved
    /// so Resume() picks back up exactly where it left off.</summary>
    public void Pause() => _player.Pause();
    public void Resume() => _player.Resume();

    // ---------------------------------------------------------------- ticking

    public override void Update(GameTime gameTime)
    {
        if (!_hasAutoStarted)
        {
            _hasAutoStarted = true;
            if (!string.IsNullOrEmpty(DefaultClip)) Play(DefaultClip);
        }

        Tick((float)gameTime.ElapsedGameTime.TotalSeconds);
    }

    /// <summary>Advances playback by <paramref name="deltaSeconds"/> and, if the discrete frame changed,
    /// pushes it to the sibling SpriteRenderer and notifies OnAnimationFrame/OnAnimationComplete. Called
    /// from Update(GameTime) during Play Mode; called directly by the Inspector's preview otherwise — see
    /// this class's own doc comment.</summary>
    public void Tick(float deltaSeconds)
    {
        if (_currentClip == null) return;

        var result = _player.Advance(deltaSeconds);
        if (result.FrameChanged)
        {
            ApplyFrameToRenderer();
            foreach (var c in Owner.Components) c.OnAnimationFrame(this, _currentClip.Name, _player.CurrentFrame);
        }
        if (result.Completed)
        {
            foreach (var c in Owner.Components) c.OnAnimationComplete(this, _currentClip.Name);
        }
    }

    /// <summary>Looked up fresh every call rather than cached at OnAttach — SpriteAnimation and
    /// SpriteRenderer can legitimately be added to a GameObject in either order, and caching a
    /// possibly-still-null reference from attach time would make behavior depend on that order.</summary>
    private void ApplyFrameToRenderer()
    {
        if (_currentClip == null || _currentClip.Frames.Count == 0) return;
        var renderer = Owner.GetComponent<SpriteRenderer>();
        if (renderer == null) return; // no sibling SpriteRenderer yet — see this class's Inspector panel, which surfaces this clearly rather than silently doing nothing

        var frame = _currentClip.Frames[_player.CurrentFrame];

        if (_currentClip.FrameSource == SpriteAnimationFrameSource.SpriteSheet)
        {
            if (renderer.TexturePath != _currentClip.SheetTexturePath)
            {
                renderer.TexturePath = _currentClip.SheetTexturePath;
                renderer.Texture = RenderContext.TextureResolver?.Invoke(_currentClip.SheetTexturePath);
            }
            renderer.SourceRect = frame.SourceRect;
        }
        else
        {
            renderer.TexturePath = frame.TexturePath;
            renderer.Texture = RenderContext.TextureResolver?.Invoke(frame.TexturePath);
            renderer.SourceRect = null;
        }
    }
}
