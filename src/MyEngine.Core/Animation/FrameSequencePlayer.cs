namespace MyEngine.Core.Animation;

/// <summary>
/// Advances a frame index over time given a frame count, frame rate, and loop mode. Knows nothing about
/// sprites, textures, meshes, or rendering — it's pure "which of N frames am I on right now" bookkeeping,
/// so it's reusable by anything that plays back a sequence of discrete frames over time, not just 2D sprite
/// sheets. SpriteAnimation is the first consumer; a future skeletal/mesh frame player could compose this
/// same class instead of re-deriving the Once/Loop/PingPong timing math from scratch.
///
/// Usage: set FrameCount/FrameRate/LoopMode, call Play(), then call Advance(deltaSeconds) once per tick and
/// react to the returned FrameAdvanceResult (FrameChanged / Completed) plus the CurrentFrame property.
/// </summary>
public sealed class FrameSequencePlayer
{
    public int FrameCount { get; set; }
    public float FrameRate { get; set; } = 12f;
    public AnimationLoopMode LoopMode { get; set; } = AnimationLoopMode.Loop;

    public int CurrentFrame { get; private set; }
    public bool IsPlaying { get; private set; }

    /// <summary>True once a Once-mode sequence has reached its last frame and stopped there. Always false
    /// for Loop/PingPong, which never finish on their own.</summary>
    public bool IsFinished { get; private set; }

    private float _time;
    private int _direction = 1; // +1 or -1; only meaningful for PingPong
    private int _lastReportedCycle; // only meaningful for Loop — how many full wraps Completed has already fired for

    /// <param name="restart">True (default) rewinds to frame 0 before playing — the normal "start this
    /// clip" case. False resumes from wherever CurrentFrame/the internal clock already was, e.g. after a
    /// Pause().</param>
    public void Play(bool restart = true)
    {
        if (restart)
        {
            _time = 0f;
            CurrentFrame = 0;
            _direction = 1;
            _lastReportedCycle = 0;
            IsFinished = false;
        }
        IsPlaying = true;
    }

    /// <summary>Stops and rewinds to frame 0 — distinct from Pause(), which holds the current frame.</summary>
    public void Stop()
    {
        IsPlaying = false;
        IsFinished = false;
        _time = 0f;
        CurrentFrame = 0;
        _direction = 1;
        _lastReportedCycle = 0;
    }

    /// <summary>Holds playback at the current frame. Advance() becomes a no-op until Resume() or Play().</summary>
    public void Pause() => IsPlaying = false;

    /// <summary>Continues from the current frame. No-op if the sequence already finished (Once mode) —
    /// call Play() to restart it instead.</summary>
    public void Resume()
    {
        if (!IsFinished) IsPlaying = true;
    }

    /// <summary>Advances the internal clock by <paramref name="deltaSeconds"/> and recomputes CurrentFrame.
    /// Returns default (no change, not completed) if not currently playing, or if FrameCount/FrameRate
    /// aren't set to something playable.</summary>
    public FrameAdvanceResult Advance(float deltaSeconds)
    {
        if (!IsPlaying || FrameCount <= 0 || FrameRate <= 0f)
            return default;

        _time += deltaSeconds;
        int elapsedFrames = (int)(_time * FrameRate);
        int previousFrame = CurrentFrame;
        bool completed = false;

        switch (LoopMode)
        {
            case AnimationLoopMode.Once:
                if (elapsedFrames >= FrameCount - 1)
                {
                    CurrentFrame = FrameCount - 1;
                    completed = !IsFinished; // fire exactly once, the call that first reaches the end
                    IsFinished = true;
                    IsPlaying = false;
                }
                else
                {
                    CurrentFrame = elapsedFrames;
                }
                break;

            case AnimationLoopMode.Loop:
            {
                CurrentFrame = elapsedFrames % FrameCount;
                int cycle = elapsedFrames / FrameCount;
                if (cycle > _lastReportedCycle)
                {
                    completed = true; // one full loop (0..N-1 and back to 0) just finished
                    _lastReportedCycle = cycle;
                }
                break;
            }

            case AnimationLoopMode.PingPong:
            {
                // Triangle wave over a period twice the "one-way" length: 0,1,..,N-1,N-2,..,1,(0,1,..). The
                // FrameCount<=1 guard exists purely to avoid a mod-by-zero — a 1-frame clip has nowhere to
                // ping-pong to and just sits on frame 0.
                int period = FrameCount <= 1 ? 1 : (FrameCount - 1) * 2;
                int phase = elapsedFrames % period;
                CurrentFrame = phase <= FrameCount - 1 ? phase : period - phase;

                int newDirection = phase <= FrameCount - 1 ? 1 : -1;
                if (newDirection != _direction)
                {
                    completed = true; // reached one end and reversed
                    _direction = newDirection;
                }
                break;
            }
        }

        return new FrameAdvanceResult
        {
            FrameChanged = CurrentFrame != previousFrame,
            Completed = completed,
        };
    }
}
