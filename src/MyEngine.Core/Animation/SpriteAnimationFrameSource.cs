namespace MyEngine.Core.Animation;

/// <summary>Where a SpriteAnimationClip's frames get their pixels from.</summary>
public enum SpriteAnimationFrameSource
{
    /// <summary>All frames are rectangles cut from one shared sheet texture (SpriteAnimationClip.SheetTexturePath).
    /// What the Editor's grid-slicer panel produces.</summary>
    SpriteSheet,

    /// <summary>Each frame is its own separate whole-image texture (SpriteAnimationFrame.TexturePath) — for
    /// art that was authored/exported as individual files rather than one packed sheet.</summary>
    TextureList,
}
