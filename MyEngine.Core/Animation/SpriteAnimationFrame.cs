using Microsoft.Xna.Framework;

namespace MyEngine.Core.Animation;

/// <summary>
/// One frame of a SpriteAnimationClip. Only one of the two fields below is meaningful at a time, depending
/// on the owning clip's FrameSource — kept as a plain data holder (no logic) rather than two subclasses
/// since a clip's frames never mix modes and SpriteAnimation always knows which field to read from the
/// clip's own FrameSource, so there's nothing a type split would actually protect against.
/// </summary>
public sealed class SpriteAnimationFrame
{
    /// <summary>SpriteSheet mode: source rectangle, in pixels, within the clip's SheetTexturePath.</summary>
    public Rectangle SourceRect { get; set; }

    /// <summary>TextureList mode: this frame's own whole-image texture, as a project-relative Assets path
    /// (same format as SpriteRenderer.TexturePath). Null/unused in SpriteSheet mode.</summary>
    public string? TexturePath { get; set; }
}
