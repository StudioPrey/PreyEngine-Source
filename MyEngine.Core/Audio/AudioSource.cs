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
    private string? _clipPath;
    /// <summary>Project-relative Assets path to a .wav file (e.g. "Audio/jump.wav"). Serialized. Setting
    /// this immediately re-resolves Clip via AudioContext.ClipResolver too (Editor: AssetDatabase.GetSound;
    /// Runtime: RuntimeAssets.ResolveSound), so a script assigning ClipPath at runtime doesn't silently end
    /// up with nothing to play — previously Clip only ever got (re)resolved by ResolveSceneAssets at
    /// scene-load time or the Inspector's own drag-drop handler, both of which a runtime ClipPath
    /// assignment skipped entirely. ClipResolver is null very early during startup, before either host has
    /// wired one up yet — this is a harmless no-op then (Clip simply stays whatever it already was),
    /// exactly the same as assigning ClipPath used to do unconditionally.</summary>
    public string? ClipPath
    {
        get => _clipPath;
        set
        {
            _clipPath = value;
            Clip = AudioContext.ClipResolver?.Invoke(value);
        }
    }

    /// <summary>The loaded clip Play()/PlayOneShot() actually use. Not serialized — resolved from ClipPath,
    /// either by that setter above or by ResolveSceneAssets after a scene loads.</summary>
    public SoundEffect? Clip { get; set; }

    private float _volume = 1f;
    /// <summary>0 (silent) to 1 (full volume). Clamped on assignment so an out-of-range value from a script
    /// can't reach MonoGame's SoundEffectInstance, which throws rather than clamps. Pushed live to whatever
    /// instance(s) are currently playing (see PushLiveInstanceProperties) — Play()/PlayPreview() are no
    /// longer the only moments this takes effect, so a designer can drag the Inspector slider while
    /// previewing, and a script can duck/fade volume without a stop+restart.</summary>
    public float Volume
    {
        get => _volume;
        set { _volume = Math.Clamp(value, 0f, 1f); PushLiveInstanceProperties(); }
    }

    private float _pitch;
    /// <summary>-1 (one octave down) to 1 (one octave up); 0 is unchanged. Pushed live — see Volume.</summary>
    public float Pitch
    {
        get => _pitch;
        set { _pitch = Math.Clamp(value, -1f, 1f); PushLiveInstanceProperties(); }
    }

    private float _pan;
    /// <summary>-1 (full left) to 1 (full right); 0 is centered. Pushed live — see Volume.</summary>
    public float Pan
    {
        get => _pan;
        set { _pan = Math.Clamp(value, -1f, 1f); PushLiveInstanceProperties(); }
    }

    public bool Loop { get; set; }

    /// <summary>Starts playing automatically the first time this component ticks during Play Mode —
    /// the same "no script required" convenience as SpriteAnimation.DefaultClip.</summary>
    public bool PlayOnStart { get; set; }

    private bool _hasAutoStarted;
    private SoundEffectInstance? _mainInstance;
    private bool _awaitingFinishedNotification;
    private readonly List<SoundEffectInstance> _oneShotInstances = new();

    /// <summary>A completely separate instance from _mainInstance, used exclusively by PlayPreview/
    /// StopPreview — see PlayPreview's doc comment for why Preview can't share _mainInstance.</summary>
    private SoundEffectInstance? _previewInstance;

    public bool IsPlaying => _mainInstance is { State: SoundState.Playing };
    public bool IsPaused => _mainInstance is { State: SoundState.Paused };

    /// <summary>True while the isolated instance PlayPreview started is actually still audible — the
    /// Inspector's Preview button polls this each frame to know when to reset itself back to "Play
    /// Preview", the same way it would poll IsPlaying for _mainInstance.</summary>
    public bool IsPreviewPlaying => _previewInstance is { State: SoundState.Playing };

    /// <summary>Applies the current Volume/Pitch/Pan to whichever of _mainInstance/_previewInstance are
    /// currently alive, immediately — called from each of those three setters. IsDisposed (not just a null
    /// check) is the actually-safe guard: Stop()/StopPreview() dispose-and-null together so in practice
    /// there's no window where a field is non-null but disposed, but checking the field alone would be
    /// relying on that staying true forever rather than on what actually makes touching the instance safe.
    /// One-shot instances from PlayOneShot are deliberately NOT touched — each is a fire-and-forget
    /// snapshot of Volume/Pitch/Pan at the moment it started, not something meant to track live changes.</summary>
    private void PushLiveInstanceProperties()
    {
        if (_mainInstance is { IsDisposed: false } main)
        {
            main.Volume = _volume;
            main.Pitch = _pitch;
            main.Pan = _pan;
        }
        if (_previewInstance is { IsDisposed: false } preview)
        {
            preview.Volume = _volume;
            preview.Pitch = _pitch;
            preview.Pan = _pan;
        }
    }

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

    /// <summary>Starts an isolated preview of Clip on a completely separate instance from _mainInstance —
    /// used exclusively by the Inspector's "Play Preview" button (see InspectorPanel.DrawAudioPreview).
    /// Needed because Play() is also what PlayOnStart/game code use: sharing _mainInstance meant previewing
    /// an AudioSource that's simultaneously playing in Play Mode — or previewing it, then entering Play
    /// Mode — had the two contexts fight over the same instance, so Stop Preview could cut off actual
    /// gameplay audio and vice versa. Never loops regardless of the Loop setting — a preview that's meant
    /// to be a quick, deliberately-stopped check has no business looping forever if you forget it's
    /// running. No-op if Clip is unassigned, the same tolerance Play() gives an empty clip.</summary>
    public void PlayPreview()
    {
        if (Clip == null) return;

        _previewInstance?.Dispose();
        _previewInstance = Clip.CreateInstance();
        _previewInstance.Volume = Volume;
        _previewInstance.Pitch = Pitch;
        _previewInstance.Pan = Pan;
        _previewInstance.Play();
    }

    /// <summary>Stops and disposes the instance PlayPreview started. Never touches _mainInstance or any
    /// PlayOneShot instance, so it can't affect actual gameplay audio — the whole point of PlayPreview
    /// using a separate instance in the first place.</summary>
    public void StopPreview()
    {
        _previewInstance?.Stop();
        _previewInstance?.Dispose();
        _previewInstance = null;
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
    /// instance, the preview instance (if PlayPreview left one running), and every still-playing one-shot.
    /// Distinct from Stop() (which only touches the main instance, leaving one-shots to finish on their
    /// own): this is for whole-scene teardown, where nothing should keep making noise once the scene itself
    /// is gone — see Scene.Teardown, which calls this on every AudioSource in the Play Mode scene being
    /// discarded, since simply dropping the C# Scene reference doesn't stop MonoGame's underlying native
    /// audio playback. Also called from OnDetach, so a preview left running when its component gets
    /// removed (or its GameObject deleted) doesn't leak a native instance with nothing left to stop it.</summary>
    public void StopAllSounds()
    {
        _mainInstance?.Stop();
        _mainInstance?.Dispose();
        _mainInstance = null;
        _awaitingFinishedNotification = false;

        _previewInstance?.Stop();
        _previewInstance?.Dispose();
        _previewInstance = null;

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
