using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Audio;
using MyEngine.Core.ECS;

namespace MyEngine.Core.Audio;

/// <summary>
/// Plays a sound clip. Add one to any GameObject, assign a Clip (drag a .wav asset onto it in the
/// Inspector, or set ClipPath/Clip from a script), and call Play() — or enable Play On Start to have it
/// begin automatically the moment Play Mode starts, no script required, the same way
/// SpriteAnimation.DefaultClip works.
///
/// ## From a script
/// <code>
/// var audio = GetComponent&lt;AudioSource&gt;();
/// audio.Play();
/// ...
/// audio.PlayOneShot(); // fires an independent, overlapping copy without disturbing the main Play/Stop state
/// </code>
///
/// ## v1 format support
/// Only WAV is supported for now (loaded via SoundEffect.FromStream, which needs no content-pipeline
/// build step — consistent with how SpriteRenderer's textures are loaded directly from PNG/JPG bytes).
/// Compressed formats (OGG/MP3) are a natural future addition behind the same ClipPath/Clip fields; nothing
/// about this component's public API would need to change to support them later.
/// </summary>
public sealed class AudioSource : Component
{
    /// <summary>Project-relative Assets path to a .wav file (e.g. "Audio/jump.wav"). Serialized; Clip is
    /// resolved from this after load the same way SpriteRenderer.Texture is resolved from TexturePath —
    /// see AudioContext.ClipResolver.</summary>
    public string? ClipPath { get; set; }

    /// <summary>The loaded clip Play()/PlayOneShot() actually use. Not serialized — always re-resolved from
    /// ClipPath after a scene loads.</summary>
    public SoundEffect? Clip { get; set; }

    private float _volume = 1f;
    /// <summary>0 (silent) to 1 (full volume). Clamped on assignment so an out-of-range value from a script
    /// can't reach MonoGame's SoundEffectInstance, which throws rather than clamps.</summary>
    public float Volume
    {
        get => _volume;
        set => _volume = Math.Clamp(value, 0f, 1f);
    }

    private float _pitch;
    /// <summary>-1 (one octave down) to 1 (one octave up); 0 is unchanged.</summary>
    public float Pitch
    {
        get => _pitch;
        set => _pitch = Math.Clamp(value, -1f, 1f);
    }

    private float _pan;
    /// <summary>-1 (full left) to 1 (full right); 0 is centered.</summary>
    public float Pan
    {
        get => _pan;
        set => _pan = Math.Clamp(value, -1f, 1f);
    }

    public bool Loop { get; set; }

    /// <summary>Starts playing automatically the first time this component ticks during Play Mode —
    /// the same "no script required" convenience as SpriteAnimation.DefaultClip.</summary>
    public bool PlayOnStart { get; set; }

    private bool _hasAutoStarted;
    private SoundEffectInstance? _mainInstance;
    private bool _awaitingFinishedNotification;
    private readonly List<SoundEffectInstance> _oneShotInstances = new();

    public bool IsPlaying => _mainInstance is { State: SoundState.Playing };
    public bool IsPaused => _mainInstance is { State: SoundState.Paused };

    /// <summary>Stops whatever this AudioSource was already playing (if anything) and starts Clip from the
    /// beginning. No-op if Clip is unassigned — an AudioSource mid-setup in the Inspector shouldn't be able
    /// to throw in a running scene, the same tolerance SpriteAnimation.Play gives an empty clip.</summary>
    public void Play()
    {
        if (Clip == null) return;

        _mainInstance?.Dispose();
        _mainInstance = Clip.CreateInstance();
        _mainInstance.Volume = Volume;
        _mainInstance.Pitch = Pitch;
        _mainInstance.Pan = Pan;
        _mainInstance.IsLooped = Loop;
        _mainInstance.Play();
        _awaitingFinishedNotification = true;
    }

    /// <summary>Plays <paramref name="clip"/> (or this AudioSource's own Clip, if omitted) as an independent,
    /// fire-and-forget instance using this AudioSource's current Volume/Pitch/Pan — it does not loop
    /// regardless of the Loop setting, and does not affect IsPlaying/Stop()/Pause(), which only ever track
    /// the main instance started by Play(). Meant for rapid, possibly-overlapping one-off sounds (footsteps,
    /// impacts, UI clicks) that shouldn't interrupt or be interrupted by whatever Play() is doing.</summary>
    public void PlayOneShot(SoundEffect? clip = null)
    {
        var sound = clip ?? Clip;
        if (sound == null) return;

        var instance = sound.CreateInstance();
        instance.Volume = Volume;
        instance.Pitch = Pitch;
        instance.Pan = Pan;
        instance.Play();
        _oneShotInstances.Add(instance);
    }

    /// <summary>Stops the main instance. One-shots started by PlayOneShot are left to finish naturally —
    /// use StopAllSounds to stop everything unconditionally.</summary>
    public void Stop()
    {
        _mainInstance?.Stop();
        _mainInstance?.Dispose();
        _mainInstance = null;
        _awaitingFinishedNotification = false;
    }

    public void Pause() => _mainInstance?.Pause();

    /// <summary>Continues the main instance from where Pause() left it. No-op if nothing is paused.</summary>
    public void Resume()
    {
        if (_mainInstance is { State: SoundState.Paused })
            _mainInstance.Resume();
    }

    /// <summary>Immediately stops and disposes every native sound resource this component owns — the main
    /// instance and every still-playing one-shot. Distinct from Stop() (which only touches the main
    /// instance, leaving one-shots to finish on their own): this is for whole-scene teardown, where nothing
    /// should keep making noise once the scene itself is gone — see EditorState.StopPlay, which calls this
    /// on every AudioSource in the Play Mode scene being discarded, since simply dropping the C# Scene
    /// reference doesn't stop MonoGame's underlying native audio playback.</summary>
    public void StopAllSounds()
    {
        _mainInstance?.Stop();
        _mainInstance?.Dispose();
        _mainInstance = null;
        _awaitingFinishedNotification = false;

        foreach (var instance in _oneShotInstances)
        {
            instance.Stop();
            instance.Dispose();
        }
        _oneShotInstances.Clear();
    }

    public override void Update(GameTime gameTime)
    {
        if (!_hasAutoStarted)
        {
            _hasAutoStarted = true;
            if (PlayOnStart) Play();
        }

        // Detects natural completion by polling State, since MonoGame's SoundEffectInstance has no
        // completion event of its own. Never fires for a looping instance — MonoGame handles looping
        // internally and IsLooped instances simply never reach Stopped on their own, which is exactly
        // the right behavior here: "finished" isn't a meaningful moment for something that loops forever.
        if (_awaitingFinishedNotification && _mainInstance is { State: SoundState.Stopped })
        {
            _awaitingFinishedNotification = false;
            foreach (var c in Owner.Components) c.OnAudioFinished(this);
        }

        for (int i = _oneShotInstances.Count - 1; i >= 0; i--)
        {
            if (_oneShotInstances[i].State == SoundState.Stopped)
            {
                _oneShotInstances[i].Dispose();
                _oneShotInstances.RemoveAt(i);
            }
        }
    }

    public override void OnDetach() => StopAllSounds();
}
