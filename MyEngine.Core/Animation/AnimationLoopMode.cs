namespace MyEngine.Core.Animation;

/// <summary>
/// How a FrameSequencePlayer behaves once it reaches the last frame. Dimension-agnostic on purpose — this
/// describes timing/looping behavior only, nothing sprite-specific, so it's equally meaningful for a future
/// 3D/skeletal frame player reusing FrameSequencePlayer.
/// </summary>
public enum AnimationLoopMode
{
    /// <summary>Plays once and stops on the last frame. FrameSequencePlayer.IsPlaying becomes false and
    /// IsFinished becomes true the instant the last frame is reached.</summary>
    Once,

    /// <summary>Plays 0, 1, 2, ..., N-1, 0, 1, 2, ... forever. Never sets IsFinished.</summary>
    Loop,

    /// <summary>Plays 0, 1, ..., N-1, N-2, ..., 1, 0, 1, ... forever (a "bounce" back and forth).
    /// Never sets IsFinished.</summary>
    PingPong,
}
