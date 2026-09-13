using Microsoft.Xna.Framework.Audio;

namespace MyEngine.Core.Audio;

/// <summary>
/// Resolves a project-relative Assets path (e.g. "Audio/jump.wav") to a loaded SoundEffect, or null if it
/// can't be found/loaded. Core doesn't know how to read an audio file off disk — same reasoning as
/// RenderContext.TextureResolver, which this deliberately mirrors: whichever host is running wires this up
/// once at startup (the Editor points it at AssetDatabase.GetSound, the standalone Runtime at
/// RuntimeAssets.ResolveSound), so AudioSource never needs to know how a .wav actually gets loaded.
///
/// A separate static class from RenderContext, not a second property tacked onto it, since audio and
/// rendering are unrelated concerns — nothing about swapping the render backend (see IRenderer2D) should
/// ever have a reason to touch this, and vice versa.
/// </summary>
public static class AudioContext
{
    public static Func<string?, SoundEffect?>? ClipResolver { get; set; }
}
