using Microsoft.Xna.Framework;

namespace MyEngine.Core.ECS;

/// <summary>
/// 2D transform: local position, rotation (radians) and scale, with parent/child hierarchy support.
/// Every GameObject owns exactly one Transform, created automatically in its constructor.
/// </summary>
public sealed class Transform : Component
{
    public Vector2 LocalPosition { get; set; } = Vector2.Zero;
    public float LocalRotation { get; set; } = 0f;
    public Vector2 LocalScale { get; set; } = Vector2.One;

    public Transform? Parent { get; private set; }
    private readonly List<Transform> _children = new();
    public IReadOnlyList<Transform> Children => _children;

    public void SetParent(Transform? newParent, bool keepWorldPosition = true)
    {
        if (newParent == this) return;

        Vector2 worldPos = Position;
        float worldRot = Rotation;

        Parent?._children.Remove(this);
        Parent = newParent;
        Parent?._children.Add(this);

        if (keepWorldPosition && Parent != null)
        {
            Position = worldPos;
            Rotation = worldRot;
        }
    }

    /// <summary>World-space position (computed from parent chain).</summary>
    public Vector2 Position
    {
        get => Parent == null ? LocalPosition : Vector2.Transform(LocalPosition, Parent.WorldMatrix);
        set
        {
            if (Parent == null) { LocalPosition = value; return; }
            var inv = Matrix.Invert(Parent.WorldMatrix);
            LocalPosition = Vector2.Transform(value, inv);
        }
    }

    /// <summary>World-space rotation in radians.</summary>
    public float Rotation
    {
        get => Parent == null ? LocalRotation : Parent.Rotation + LocalRotation;
        set => LocalRotation = Parent == null ? value : value - Parent.Rotation;
    }

    /// <summary>World-space scale.</summary>
    public Vector2 Scale => Parent == null ? LocalScale : Parent.Scale * LocalScale;

    /// <summary>Local transform matrix (relative to parent).</summary>
    public Matrix LocalMatrix =>
        Matrix.CreateScale(LocalScale.X, LocalScale.Y, 1f) *
        Matrix.CreateRotationZ(LocalRotation) *
        Matrix.CreateTranslation(LocalPosition.X, LocalPosition.Y, 0f);

    /// <summary>World transform matrix, walking up the parent chain.</summary>
    public Matrix WorldMatrix => Parent == null ? LocalMatrix : LocalMatrix * Parent.WorldMatrix;

    public Vector2 Forward
    {
        get
        {
            float r = Rotation;
            return new Vector2(MathF.Cos(r), MathF.Sin(r));
        }
    }
}
