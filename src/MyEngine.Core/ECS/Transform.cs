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

    /// <summary>Reparents this Transform. No-op if <paramref name="newParent"/> is this Transform itself,
    /// or any descendant of it (found by walking up from newParent through its own Parent chain looking
    /// for `this`) — either case would create a cycle in the parent chain (A parent of B parent of A),
    /// which WorldMatrix/Position/Rotation/Scale below all have zero protection against on their own: each
    /// just walks Parent recursively with no cycle guard, so a cycle silently allowed in here would stack
    /// overflow the moment any of them (or GameObject.ActiveInHierarchy, or Scene.Destroy) is next evaluated.</summary>
    public void SetParent(Transform? newParent, bool keepWorldPosition = true)
    {
        if (newParent == this) return;
        if (newParent != null && IsSelfOrAncestorOf(newParent)) return;

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

    /// <summary>True if this Transform is <paramref name="candidate"/>, or is found anywhere along
    /// candidate's Parent chain (i.e. this Transform is an ancestor of candidate).</summary>
    private bool IsSelfOrAncestorOf(Transform candidate)
    {
        var t = candidate;
        while (t != null)
        {
            if (t == this) return true;
            t = t.Parent;
        }
        return false;
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
