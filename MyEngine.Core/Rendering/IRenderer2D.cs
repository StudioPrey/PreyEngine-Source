using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace MyEngine.Core.Rendering;

/// <summary>
/// Everything MyEngine.Core needs in order to draw one frame of 2D scene content, with no direct dependency
/// on SpriteBatch. This does NOT hide MonoGame's math/handle value types (Vector2, Color, Rectangle,
/// Texture2D, SpriteEffects) — those are used pervasively for transforms, colors, and asset handles
/// throughout the engine, and abstracting them away would be a much larger, unrelated undertaking with
/// little payoff. What this hides is specifically the act of submitting a draw call to a graphics backend,
/// which is the part that actually changes if the backend ever changes.
///
/// SpriteRenderer.Draw() is the only place in the engine that calls this today — see
/// RenderContext.Renderer2D for how a component gets a hold of the active instance. Swap what
/// RenderContext.Renderer2D returns (and provide a matching implementation of this interface) to change
/// rendering backends without touching SpriteRenderer, SpriteAnimation, or either host's (Editor/Runtime)
/// draw loop.
/// </summary>
public interface IRenderer2D
{
    /// <summary>Starts a batch of draw calls for one frame/pass, in <paramref name="viewMatrix"/> space.
    /// Sprites are expected to sort back-to-front by layerDepth (0 = nearest/front, 1 = farthest/back) —
    /// see SpriteRenderer.SortingOrder, which is defined in terms of that convention.</summary>
    void BeginScene(Matrix viewMatrix);

    /// <summary>Draws one sprite. <paramref name="sourceRect"/> null means "the whole texture" (a plain
    /// static sprite); non-null selects one region of it (a single animation frame from a sheet).</summary>
    void DrawSprite(Texture2D texture, Vector2 position, Rectangle? sourceRect, Color color,
        float rotation, Vector2 origin, Vector2 scale, SpriteEffects effects, float layerDepth);

    /// <summary>Draws an untextured filled rectangle — SpriteRenderer's placeholder mode (Texture == null),
    /// used before any art asset is assigned.</summary>
    void DrawFilledRect(Vector2 position, Color color, float rotation, Vector2 origin, Vector2 size,
        SpriteEffects effects, float layerDepth);

    /// <summary>Ends and flushes the batch started by BeginScene.</summary>
    void EndScene();
}
