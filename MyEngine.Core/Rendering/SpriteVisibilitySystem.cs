using MyEngine.Core.Components;
using MyEngine.Core.Physics;

namespace MyEngine.Core.Rendering;

/// <summary>Scene-local sprite registry. This only decides visibility; normal Component.Draw dispatch
/// still owns rendering and order. Registration is infrequent, so a simple list avoids a second index
/// and keeps the steady-state pass allocation-free.</summary>
public sealed class SpriteVisibilitySystem
{
    private readonly List<SpriteRenderer> _registered = new();
    internal AABB2D? CurrentBounds { get; private set; }
    internal int RegisteredCount => _registered.Count;

    internal void Register(SpriteRenderer sprite)
    {
        if (!_registered.Contains(sprite)) _registered.Add(sprite);
        sprite.IsVisible = true;
    }

    internal void Unregister(SpriteRenderer sprite)
    {
        _registered.Remove(sprite);
        sprite.IsVisible = true;
    }

    internal void Refresh(AABB2D? visibleWorldBounds)
    {
        // Non-finite or reversed bounds are not a useful rejection test. Fail open just like an
        // invalid camera; a caller can always use null explicitly to request the legacy path.
        if (visibleWorldBounds is { } b &&
            (!AABB2D.IsFinite(b.Min) || !AABB2D.IsFinite(b.Max) || b.Min.X > b.Max.X || b.Min.Y > b.Max.Y))
            visibleWorldBounds = null;
        CurrentBounds = visibleWorldBounds;
        foreach (var sprite in _registered)
        {
            sprite.IsVisible = visibleWorldBounds == null ||
                (sprite.Enabled && sprite.Owner.ActiveInHierarchy &&
                 sprite.ComputeWorldBounds().Overlaps(visibleWorldBounds.Value));
        }
    }

    // Bounds apply only during Scene.Draw, not to an unrelated direct Component.Draw afterwards.
    internal void EndDraw() => CurrentBounds = null;
}
