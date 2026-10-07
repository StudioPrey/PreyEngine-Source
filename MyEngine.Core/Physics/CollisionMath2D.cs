using Microsoft.Xna.Framework;

namespace MyEngine.Core.Physics;

/// <summary>
/// A box's world-space collision geometry for one instant: center, half-extents along its own (rotated)
/// axes, and the axes themselves as unit vectors. Recomputed fresh every physics step from the owning
/// Collider2D + Transform — nothing here is cached across frames.
/// </summary>
internal readonly struct BoxShape2D
{
    public Vector2 Center { get; }
    public Vector2 HalfExtents { get; }
    public Vector2 AxisX { get; }
    public Vector2 AxisY { get; }

    public BoxShape2D(Vector2 center, Vector2 halfExtents, float rotation)
    {
        Center = center;
        HalfExtents = halfExtents;
        AxisX = Vec2.Rotate(Vector2.UnitX, rotation);
        AxisY = Vec2.Rotate(Vector2.UnitY, rotation);
    }

    /// <summary>The 4 corners in a fixed winding order that GetEdgeNormal's mapping depends on:
    /// 0 = Center - ex - ey, 1 = Center + ex - ey, 2 = Center + ex + ey, 3 = Center - ex + ey
    /// (where ex = AxisX * HalfExtents.X, ey = AxisY * HalfExtents.Y).</summary>
    public Vector2[] GetCorners()
    {
        var ex = AxisX * HalfExtents.X;
        var ey = AxisY * HalfExtents.Y;
        return new[]
        {
            Center - ex - ey,
            Center + ex - ey,
            Center + ex + ey,
            Center - ex + ey,
        };
    }

    /// <summary>Outward unit normal for the edge running from corner[edgeIndex] to corner[(edgeIndex+1)%4].</summary>
    public Vector2 GetEdgeNormal(int edgeIndex) => edgeIndex switch
    {
        0 => -AxisY,
        1 => AxisX,
        2 => AxisY,
        _ => -AxisX,
    };

    /// <summary>How far this box extends to each side of its center along unit axis <paramref name="axis"/>.</summary>
    public float ProjectedRadius(Vector2 axis) =>
        MathF.Abs(Vector2.Dot(AxisX, axis)) * HalfExtents.X + MathF.Abs(Vector2.Dot(AxisY, axis)) * HalfExtents.Y;

    public AABB2D ToAABB()
    {
        var corners = GetCorners();
        var min = corners[0];
        var max = corners[0];
        for (int i = 1; i < 4; i++)
        {
            min = Vector2.Min(min, corners[i]);
            max = Vector2.Max(max, corners[i]);
        }
        return new AABB2D(min, max);
    }
}

/// <summary>Up to two world-space contact points between a pair of shapes, plus the separating normal
/// (always pointing from the first shape passed to the collision function toward the second).</summary>
internal sealed class ContactManifold
{
    public Vector2 Normal { get; }
    public int ContactCount { get; private set; }
    public Vector2 ContactPoint0 { get; private set; }
    public Vector2 ContactPoint1 { get; private set; }
    public float Penetration0 { get; private set; }
    public float Penetration1 { get; private set; }

    public ContactManifold(Vector2 normal) => Normal = normal;

    public void AddContact(Vector2 point, float penetration)
    {
        if (ContactCount == 0) { ContactPoint0 = point; Penetration0 = penetration; ContactCount = 1; }
        else if (ContactCount == 1) { ContactPoint1 = point; Penetration1 = penetration; ContactCount = 2; }
        // A third contact can't happen for the shape pairs this engine supports (box/box yields at most 2,
        // circle pairs yield exactly 1) — silently ignored rather than thrown, just in case.
    }

    /// <summary>Same contacts, with the normal reversed — for when a caller computed A-vs-B but needs the
    /// manifold expressed as B-vs-A.</summary>
    public ContactManifold Flipped()
    {
        var flipped = new ContactManifold(-Normal);
        if (ContactCount > 0) flipped.AddContact(ContactPoint0, Penetration0);
        if (ContactCount > 1) flipped.AddContact(ContactPoint1, Penetration1);
        return flipped;
    }

    /// <summary>The average of all contact points — used for the single ContactPoint exposed on Collision2D.</summary>
    public Vector2 AveragePoint() => ContactCount switch
    {
        0 => Vector2.Zero,
        1 => ContactPoint0,
        _ => (ContactPoint0 + ContactPoint1) * 0.5f,
    };

    public float MaxPenetration() => ContactCount switch
    {
        0 => 0f,
        1 => Penetration0,
        _ => MathF.Max(Penetration0, Penetration1),
    };
}

/// <summary>
/// Narrowphase collision detection: given two exact shapes already known to be plausibly overlapping (past
/// the broadphase AABB test), determines whether they actually touch and produces a manifold describing
/// how. Pure geometry — no knowledge of GameObjects, Components, or the solver that consumes these results.
/// </summary>
internal static class CollisionMath2D
{
    /// <summary>Contacts within this distance of "just touching" still count — avoids manifolds flickering
    /// in and out of existence from floating-point noise when two shapes are resting exactly against
    /// each other.</summary>
    private const float ContactSlop = 0.01f;

    public static ContactManifold? CircleVsCircle(Vector2 centerA, float radiusA, Vector2 centerB, float radiusB)
    {
        Vector2 delta = centerB - centerA;
        float distance = delta.Length();
        float radiusSum = radiusA + radiusB;
        if (distance >= radiusSum) return null;

        Vector2 normal = distance > 0.0001f ? delta / distance : Vector2.UnitX;
        float penetration = radiusSum - distance;
        Vector2 contactPoint = centerA + normal * radiusA;

        var manifold = new ContactManifold(normal);
        manifold.AddContact(contactPoint, penetration);
        return manifold;
    }

    /// <summary>Normal points from the box toward the circle.</summary>
    public static ContactManifold? CircleVsBox(Vector2 circleCenter, float radius, BoxShape2D box)
    {
        Vector2 d = circleCenter - box.Center;
        float localX = Vector2.Dot(d, box.AxisX);
        float localY = Vector2.Dot(d, box.AxisY);

        float clampedX = Math.Clamp(localX, -box.HalfExtents.X, box.HalfExtents.X);
        float clampedY = Math.Clamp(localY, -box.HalfExtents.Y, box.HalfExtents.Y);

        bool inside = localX > -box.HalfExtents.X && localX < box.HalfExtents.X &&
                      localY > -box.HalfExtents.Y && localY < box.HalfExtents.Y;

        Vector2 contactPoint;
        Vector2 normal;
        float penetration;

        if (inside)
        {
            // The circle's center is already inside the box (deep interpenetration, or a small circle
            // fully swallowed by a big box) — push out toward whichever face is nearest.
            float distToPosX = box.HalfExtents.X - localX;
            float distToNegX = box.HalfExtents.X + localX;
            float distToPosY = box.HalfExtents.Y - localY;
            float distToNegY = box.HalfExtents.Y + localY;
            float min = MathF.Min(MathF.Min(distToPosX, distToNegX), MathF.Min(distToPosY, distToNegY));

            Vector2 localNormal;
            if (min == distToPosX) localNormal = new Vector2(1f, 0f);
            else if (min == distToNegX) localNormal = new Vector2(-1f, 0f);
            else if (min == distToPosY) localNormal = new Vector2(0f, 1f);
            else localNormal = new Vector2(0f, -1f);

            normal = box.AxisX * localNormal.X + box.AxisY * localNormal.Y;
            contactPoint = circleCenter;
            penetration = min + radius;
        }
        else
        {
            Vector2 closestPoint = box.Center + box.AxisX * clampedX + box.AxisY * clampedY;
            Vector2 delta = circleCenter - closestPoint;
            float distance = delta.Length();
            if (distance >= radius) return null;

            normal = distance > 0.0001f ? delta / distance : box.AxisX;
            contactPoint = closestPoint;
            penetration = radius - distance;
        }

        var manifold = new ContactManifold(normal);
        manifold.AddContact(contactPoint, penetration);
        return manifold;
    }

    /// <summary>Normal points from box A toward box B. Generates up to 2 contact points via the standard
    /// SAT-plus-clipping approach (find the least-penetrating face, clip the other box's nearest edge
    /// against it) so stacked/resting boxes settle instead of jittering on a single contact point.</summary>
    public static ContactManifold? BoxVsBox(BoxShape2D a, BoxShape2D b)
    {
        if (!TryFindLeastPenetratingAxis(a, b, out int edgeA, out float overlapA)) return null;
        if (!TryFindLeastPenetratingAxis(b, a, out int edgeB, out float overlapB)) return null;

        BoxShape2D reference, incident;
        int referenceEdge;
        bool referenceIsA;

        // Prefer A's axis on (near-)ties, so the normal doesn't flicker between A and B's face from one
        // step to the next when the overlap is close to symmetric.
        if (overlapB < overlapA - 0.0001f)
        {
            reference = b; incident = a; referenceEdge = edgeB; referenceIsA = false;
        }
        else
        {
            reference = a; incident = b; referenceEdge = edgeA; referenceIsA = true;
        }

        Vector2 referenceNormal = reference.GetEdgeNormal(referenceEdge);
        Vector2[] referenceCorners = reference.GetCorners();
        Vector2 refV0 = referenceCorners[referenceEdge];
        Vector2 refV1 = referenceCorners[(referenceEdge + 1) % 4];

        // Incident edge = the incident box's edge whose outward normal is most anti-parallel to the
        // reference normal — the face most directly "facing into" the reference box.
        Vector2[] incidentCorners = incident.GetCorners();
        int incidentEdge = 0;
        float lowestDot = float.MaxValue;
        for (int i = 0; i < 4; i++)
        {
            float dot = Vector2.Dot(incident.GetEdgeNormal(i), referenceNormal);
            if (dot < lowestDot) { lowestDot = dot; incidentEdge = i; }
        }
        Vector2 incV0 = incidentCorners[incidentEdge];
        Vector2 incV1 = incidentCorners[(incidentEdge + 1) % 4];

        // Clip the incident edge against the reference face's two side planes.
        Vector2 tangent = Vector2.Normalize(refV1 - refV0);
        float negSideOffset = -Vector2.Dot(tangent, refV0);
        float posSideOffset = Vector2.Dot(tangent, refV1);

        if (ClipSegment(incV0, incV1, -tangent, negSideOffset, out Vector2 p0, out Vector2 p1) < 2)
            return null;
        if (ClipSegment(p0, p1, tangent, posSideOffset, out Vector2 q0, out Vector2 q1) < 2)
            return null;

        float referenceFaceOffset = Vector2.Dot(referenceNormal, refV0);
        Vector2 worldNormal = referenceIsA ? referenceNormal : -referenceNormal;
        var manifold = new ContactManifold(worldNormal);

        float separation0 = Vector2.Dot(referenceNormal, q0) - referenceFaceOffset;
        if (separation0 <= ContactSlop) manifold.AddContact(q0, -separation0);

        float separation1 = Vector2.Dot(referenceNormal, q1) - referenceFaceOffset;
        if (separation1 <= ContactSlop) manifold.AddContact(q1, -separation1);

        return manifold.ContactCount > 0 ? manifold : null;
    }

    /// <summary>Tests <paramref name="reference"/>'s two face axes as candidate separating axes against
    /// <paramref name="other"/>. Returns false the moment either axis fully separates the boxes (no
    /// collision possible). Otherwise returns the edge index (into reference.GetCorners()) of the
    /// least-penetrating face and how much overlap remains along it, so the caller can compare against
    /// the other box's own two axes to decide which box actually contributes the reference face.</summary>
    private static bool TryFindLeastPenetratingAxis(BoxShape2D reference, BoxShape2D other, out int edgeIndex, out float overlap)
    {
        edgeIndex = -1;
        overlap = float.MaxValue;

        for (int candidate = 0; candidate < 2; candidate++)
        {
            Vector2 axis = candidate == 0 ? reference.AxisX : reference.AxisY;
            float centerDistance = Vector2.Dot(other.Center - reference.Center, axis);
            float combinedRadius = reference.ProjectedRadius(axis) + other.ProjectedRadius(axis);
            float axisOverlap = combinedRadius - MathF.Abs(centerDistance);

            if (axisOverlap < 0f) return false;

            if (axisOverlap < overlap)
            {
                overlap = axisOverlap;
                // GetEdgeNormal: edge 1 = +AxisX, edge 3 = -AxisX, edge 2 = +AxisY, edge 0 = -AxisY —
                // pick whichever of the candidate axis's two faces is the one actually facing "other".
                edgeIndex = candidate == 0
                    ? (centerDistance >= 0f ? 1 : 3)
                    : (centerDistance >= 0f ? 2 : 0);
            }
        }

        return true;
    }

    /// <summary>Clips the segment [v0,v1] to the half-plane dot(p, normal) &lt;= offset, returning how many
    /// of the (0, 1, or 2) result points remain. If the segment crosses the plane, the crossing point
    /// replaces whichever endpoint was clipped away — the standard single-plane Sutherland-Hodgman step.</summary>
    private static int ClipSegment(Vector2 v0, Vector2 v1, Vector2 normal, float offset, out Vector2 out0, out Vector2 out1)
    {
        out0 = Vector2.Zero;
        out1 = Vector2.Zero;
        int count = 0;

        float d0 = Vector2.Dot(normal, v0) - offset;
        float d1 = Vector2.Dot(normal, v1) - offset;

        if (d0 <= 0f) { out0 = v0; count = 1; }
        if (d1 <= 0f)
        {
            if (count == 0) out0 = v1; else out1 = v1;
            count++;
        }
        if (d0 * d1 < 0f)
        {
            float t = d0 / (d0 - d1);
            Vector2 crossing = v0 + t * (v1 - v0);
            if (count == 0) out0 = crossing; else out1 = crossing;
            count++;
        }

        return count;
    }
}
