using Microsoft.Xna.Framework;

namespace MyEngine.Core.Scripting;

/// <summary>
/// Frame timing for scripts, Unity-style. Updated once per frame by the host (alongside Input.Update())
/// — scripts just read DeltaTime/TotalTime, they never update this themselves.
/// </summary>
public static class Time
{
    /// <summary>Seconds since the last frame. Multiply movement/rotation by this so behavior is frame-rate independent.</summary>
    public static float DeltaTime { get; private set; }

    /// <summary>Total seconds elapsed since Play Mode (or the standalone Runtime) started.</summary>
    public static float TotalTime { get; private set; }

    public static void Update(GameTime gameTime)
    {
        DeltaTime = (float)gameTime.ElapsedGameTime.TotalSeconds;
        TotalTime += DeltaTime;
    }

    /// <summary>Resets TotalTime to zero. Called when entering Play Mode so each play session starts fresh.</summary>
    public static void Reset()
    {
        DeltaTime = 0f;
        TotalTime = 0f;
    }
}
