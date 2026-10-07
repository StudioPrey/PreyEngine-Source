namespace MyEngine.Runtime;

/// <summary>
/// The engine splash's timing, as a pure function of elapsed time — deliberately free of any MonoGame types so
/// it can be tested without a window: fade in, hold, fade out, done. Uses smoothstep for the two fades, which
/// reads as a soft "breathing" transition rather than the mechanical look of a linear ramp.
///
/// <para>Advanced by GAME time (the fixed-step Update), not wall-clock, so a slow frame can't make the splash
/// vanish early. It is intentionally not skippable: it is the engine's attribution, and 2.5 seconds is short.</para>
/// </summary>
internal sealed class SplashTimeline
{
    public const float FadeInSeconds = 0.6f;
    public const float HoldSeconds = 1.3f;
    public const float FadeOutSeconds = 0.6f;
    public const float TotalSeconds = FadeInSeconds + HoldSeconds + FadeOutSeconds;

    public float Elapsed { get; private set; }
    public bool IsFinished => Elapsed >= TotalSeconds;

    /// <summary>0 = invisible, 1 = fully shown.</summary>
    public float Alpha => Evaluate(Elapsed);

    /// <summary>Moves time forward. Zero, negative or non-finite steps are ignored.</summary>
    public void Advance(float seconds)
    {
        if (!(seconds > 0f) || float.IsInfinity(seconds) || IsFinished) return;
        Elapsed += seconds;
    }

    public static float Evaluate(float t)
    {
        if (!(t > 0f) || t >= TotalSeconds) return 0f;
        if (t < FadeInSeconds) return Smooth(t / FadeInSeconds);
        if (t < FadeInSeconds + HoldSeconds) return 1f;
        return Smooth(1f - (t - FadeInSeconds - HoldSeconds) / FadeOutSeconds);
    }

    private static float Smooth(float x) => x * x * (3f - 2f * x);
}

/// <summary>Where the splash image goes inside the window.</summary>
internal static class SplashLayout
{
    /// <summary>Largest the image is allowed to be scaled UP (1.0 = its native pixels). The artwork is a fixed
    /// bitmap; stretching it far past native size only makes it soft. Because its edges are baked to fade into
    /// the flat background color, a smaller image floating in a big window looks intentional, not letterboxed.</summary>
    public const float MaxScale = 1.5f;

    /// <summary>Largest whole-pixel rectangle with the image's aspect ratio that fits the window (scaled down
    /// freely, up only to <see cref="MaxScale"/>), centered. Zero-sized if any input is not positive.</summary>
    public static (int X, int Y, int Width, int Height) Fit(int imageWidth, int imageHeight, int viewWidth, int viewHeight)
    {
        if (imageWidth <= 0 || imageHeight <= 0 || viewWidth <= 0 || viewHeight <= 0) return (0, 0, 0, 0);

        float scale = Math.Min(MaxScale, Math.Min(viewWidth / (float)imageWidth, viewHeight / (float)imageHeight));
        int width = Math.Max(1, (int)MathF.Round(imageWidth * scale));
        int height = Math.Max(1, (int)MathF.Round(imageHeight * scale));
        return ((viewWidth - width) / 2, (viewHeight - height) / 2, width, height);
    }
}
