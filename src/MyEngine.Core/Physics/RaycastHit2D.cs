using Microsoft.Xna.Framework;

namespace MyEngine.Core.Physics;

/// <summary>One hit result from PhysicsWorld2D.Raycast/RaycastAll — matches Unity's RaycastHit2D shape.</summary>
public readonly struct RaycastHit2D
{
    public Collider2D Collider { get; }
    public Vector2 Point { get; }
    public Vector2 Normal { get; }
    public float Distance { get; }

    public RaycastHit2D(Collider2D collider, Vector2 point, Vector2 normal, float distance)
    {
        Collider = collider;
        Point = point;
        Normal = normal;
        Distance = distance;
    }
}
