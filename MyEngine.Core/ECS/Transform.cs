using Microsoft.Xna.Framework;

namespace MyEngine.Core.ECS;

/// <summary>
/// 2D transform: local position, rotation (radians) and scale, with parent/child hierarchy support.
/// Every GameObject owns exactly one Transform, created automatically in its constructor.
/// </summary>
public sealed class Transform : Component
{
    // The three local values are properties over private fields (rather than auto-properties) for exactly one
    // reason: every write has to invalidate the cached matrices below. Reads stay a plain field load.
    private Vector2 _localPosition = Vector2.Zero;
    private float _localRotation;
    private Vector2 _localScale = Vector2.One;

    public Vector2 LocalPosition
    {
        get => _localPosition;
        set { _localPosition = value; _localDirty = true; MarkWorldDirty(); }
    }

    public float LocalRotation
    {
        get => _localRotation;
        set { _localRotation = value; _localDirty = true; MarkWorldDirty(); }
    }

    public Vector2 LocalScale
    {
        get => _localScale;
        set { _localScale = value; _localDirty = true; MarkWorldDirty(); }
    }

    // ---------------------------------------------------------------- cached matrices
    // Before this cache, every read of WorldMatrix (and so of Position/Scale/Rotation on anything parented)
    // rebuilt three matrices and multiplied them up the whole parent chain — per call, and a sprite alone
    // makes three such calls a frame; physics makes many more. Now each matrix is built at most once after
    // a change and re-used until something it depends on changes again. The values are computed with the
    // exact same expressions as before, so results are bit-for-bit identical — only the redundant work is gone.
    //
    // Invariant that makes the early-out in MarkWorldDirty valid: a Transform whose cached world matrix is
    // clean always has a clean parent (WorldMatrix can only become clean by first reading the parent's, which
    // cleans it), and a dirty Transform always has only dirty descendants (marking recurses into every child
    // unless the node was already dirty, which by this same invariant means they already are).
    private Matrix _localMatrix;
    private Matrix _worldMatrix;
    private bool _localDirty = true;
    private bool _worldDirty = true;

    /// <summary>Marks this Transform's cached world matrix — and, recursively, every descendant's — stale.</summary>
    private void MarkWorldDirty()
    {
        if (_worldDirty) return;
        _worldDirty = true;
        foreach (var child in _children)
            child.MarkWorldDirty();
    }

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
        MarkWorldDirty(); // this whole subtree's world matrices now hang off a different parent

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
    public Matrix LocalMatrix
    {
        get
        {
            if (_localDirty)
            {
                _localMatrix =
                    Matrix.CreateScale(_localScale.X, _localScale.Y, 1f) *
                    Matrix.CreateRotationZ(_localRotation) *
                    Matrix.CreateTranslation(_localPosition.X, _localPosition.Y, 0f);
                _localDirty = false;
            }
            return _localMatrix;
        }
    }

    /// <summary>World transform matrix — this Transform's local matrix times its parent's world matrix, cached
    /// until this Transform or any ancestor changes (see the cache note near the top of this class).</summary>
    public Matrix WorldMatrix
    {
        get
        {
            if (_worldDirty)
            {
                _worldMatrix = Parent == null ? LocalMatrix : LocalMatrix * Parent.WorldMatrix;
                _worldDirty = false;
            }
            return _worldMatrix;
        }
    }

    public Vector2 Forward
    {
        get
        {
            float r = Rotation;
            return new Vector2(MathF.Cos(r), MathF.Sin(r));
        }
    }
}
