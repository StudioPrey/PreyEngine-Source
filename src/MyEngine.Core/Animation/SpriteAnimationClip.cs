namespace MyEngine.Core.Animation;

/// <summary>
/// A named, orderable sequence of frames plus how it plays back. Pure authored data — no playback state
/// lives here (that's FrameSequencePlayer's job, held per-instance inside SpriteAnimation) — which is what
/// lets one SpriteAnimationClip be shared by playing it on any number of SpriteAnimation components at once
/// without them interfering with each other.
/// </summary>
public sealed class SpriteAnimationClip
{
    public string Name { get; set; } = "New Clip";

    public SpriteAnimationFrameSource FrameSource { get; set; } = SpriteAnimationFrameSource.SpriteSheet;

    /// <summary>Shared sheet texture (project-relative Assets path) when FrameSource == SpriteSheet;
    /// ignored in TextureList mode, where each frame carries its own texture path instead.</summary>
    public string? SheetTexturePath { get; set; }

    public List<SpriteAnimationFrame> Frames { get; set; } = new();

    /// <summary>Frames per second — uniform across the whole clip for v1 (no per-frame duration override).</summary>
    public float FrameRate { get; set; } = 12f;

    public AnimationLoopMode LoopMode { get; set; } = AnimationLoopMode.Loop;
}
