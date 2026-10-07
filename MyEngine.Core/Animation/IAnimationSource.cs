namespace MyEngine.Core.Animation;

/// <summary>
/// Implemented by any component that plays back a named clip and reports progress to sibling components via
/// Component.OnAnimationComplete/OnAnimationFrame (see Component.cs). SpriteAnimation (2D, frame/rect-based)
/// is the first and, for now, only implementor; the point of pulling this out as an interface rather than
/// having those two Component callbacks reference SpriteAnimation directly is so a future non-sprite
/// animation source — a 3D skeletal/mesh animation player, for example — can implement IAnimationSource and
/// immediately get the exact same callback delivery on Component, with no changes to Component.cs at all.
/// Keep this interface's surface to "identity + coarse status" only; anything clip-authoring-specific
/// (frame data, frame rate, loop mode, ...) belongs on the concrete player, not here.
/// </summary>
public interface IAnimationSource
{
    /// <summary>Name of the clip currently playing (or last played), or null if nothing has ever played.</summary>
    string? CurrentClipName { get; }

    /// <summary>Whether playback is currently advancing. False while paused, stopped, or after a
    /// non-looping clip has finished.</summary>
    bool IsPlaying { get; }
}
