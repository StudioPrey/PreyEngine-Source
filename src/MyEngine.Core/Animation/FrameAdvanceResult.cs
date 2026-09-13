namespace MyEngine.Core.Animation;

/// <summary>What happened during one FrameSequencePlayer.Advance(deltaSeconds) call.</summary>
public readonly struct FrameAdvanceResult
{
    /// <summary>True if CurrentFrame is different from what it was before this Advance() call.</summary>
    public bool FrameChanged { get; init; }

    /// <summary>True exactly on the Advance() call that finishes one "unit" of playback for the active
    /// AnimationLoopMode: reaching the last frame for Once, wrapping back to frame 0 for Loop, or reversing
    /// direction at either end for PingPong. See AnimationLoopMode's own doc comments for the reasoning
    /// behind each definition.</summary>
    public bool Completed { get; init; }
}
