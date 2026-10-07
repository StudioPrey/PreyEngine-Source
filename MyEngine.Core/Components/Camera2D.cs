using Microsoft.Xna.Framework;
using MyEngine.Core.ECS;

namespace MyEngine.Core.Components;

/// <summary>
/// 2D camera. Position/rotation come from the owning GameObject's Transform.
/// Attach one to a GameObject and mark it Active to use it for rendering.
/// Optionally set FollowTarget to have the camera smoothly track another GameObject (e.g. the player).
/// </summary>
public sealed class Camera2D : Component
{
    public float Zoom { get; set; } = 1f;
    public bool IsActive { get; set; } = true;
    public Color BackgroundColor { get; set; } = new Color(30, 30, 35);

    /// <summary>If set, the camera smoothly moves toward this GameObject's position every Update.</summary>
    public GameObject? FollowTarget { get; set; }

    /// <summary>0 = camera never moves, 1 = camera snaps instantly to the target. Values around 0.1-0.2 feel smooth.</summary>
    public float FollowSmoothing { get; set; } = 0.15f;

    /// <summary>Offset from the target's position, in world units (e.g. to look slightly ahead of the player).</summary>
    public Vector2 FollowOffset { get; set; } = Vector2.Zero;

    public override void Update(GameTime gameTime)
    {
        if (FollowTarget == null) return;

        var desired = FollowTarget.Transform.Position + FollowOffset;
        var current = Transform.Position;
        var t = MathHelper.Clamp(FollowSmoothing, 0f, 1f);
        Transform.Position = Vector2.Lerp(current, desired, t);
    }

    public Matrix GetViewMatrix(Vector2 viewportSize)
    {
        return
            Matrix.CreateTranslation(-Transform.Position.X, -Transform.Position.Y, 0f) *
            Matrix.CreateRotationZ(-Transform.Rotation) *
            Matrix.CreateScale(Zoom, Zoom, 1f) *
            Matrix.CreateTranslation(viewportSize.X * 0.5f, viewportSize.Y * 0.5f, 0f);
    }

    /// <summary>Converts a point in screen/viewport space to world space.</summary>
    public Vector2 ScreenToWorld(Vector2 screenPoint, Vector2 viewportSize)
    {
        var inv = Matrix.Invert(GetViewMatrix(viewportSize));
        return Vector2.Transform(screenPoint, inv);
    }

    /// <summary>Converts a world-space point to screen/viewport space — the inverse of ScreenToWorld.</summary>
    public Vector2 WorldToScreen(Vector2 worldPoint, Vector2 viewportSize)
        => Vector2.Transform(worldPoint, GetViewMatrix(viewportSize));
}

