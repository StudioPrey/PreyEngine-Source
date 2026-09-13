using Microsoft.Xna.Framework;

namespace MyEngine.Core.Physics;

/// <summary>Pure ray/point intersection tests backing PhysicsWorld2D's Raycast/RaycastAll/Overlap* queries.
/// Like CollisionMath2D, this has no knowledge of GameObjects or Components — just shapes and numbers.</summary>
internal static class RaycastMath2D
{
    /// <summary><paramref name="direction"/> must already be unit length. Returns false if the ray misses,
    /// starts past <paramref name="maxDistance"/>, or points away from the circle.</summary>
    public static bool RayVsCircle(
        Vector2 origin, Vector2 direction, float maxDistance, Vector2 center, float radius,
        out float distance, out Vector2 point, out Vector2 normal)
    {
        distance = 0f; point = Vector2.Zero; normal = Vector2.Zero;

        Vector2 m = origin - center;
        float b = Vector2.Dot(m, direction);
        float c = Vector2.Dot(m, m) - radius * radius;

        // Origin is outside the circle and pointing away from it — no hit even though the infinite line
        // might intersect (b > 0 means the closest approach is behind the ray's direction... more
        // precisely, ahead of it in the direction opposite the ray, i.e. it's already moving away).
        if (c > 0f && b > 0f) return false;

        float discriminant = b * b - c;
        if (discriminant < 0f) return false;

        float t = -b - MathF.Sqrt(discriminant);
        if (t < 0f) t = 0f; // origin starts inside the circle — report an immediate hit
        if (t > maxDistance) return false;

        distance = t;
        point = origin + direction * t;
        Vector2 toPoint = point - center;
        normal = toPoint.LengthSquared() > 0.0001f ? Vector2.Normalize(toPoint) : -direction;
        return true;
    }

    /// <summary><paramref name="direction"/> must already be unit length. Uses the standard slab method,
    /// testing in the box's own (rotated) local space.</summary>
    public static bool RayVsBox(
        Vector2 origin, Vector2 direction, float maxDistance, BoxShape2D box,
        out float distance, out Vector2 point, out Vector2 normal)
    {
        distance = 0f; point = Vector2.Zero; normal = Vector2.Zero;

        Vector2 originLocal = origin - box.Center;
        var local = new Vector2(Vector2.Dot(originLocal, box.AxisX), Vector2.Dot(originLocal, box.AxisY));
        var dirLocal = new Vector2(Vector2.Dot(direction, box.AxisX), Vector2.Dot(direction, box.AxisY));

        float tMin = float.NegativeInfinity;
        float tMax = float.PositiveInfinity;
        int hitAxis = 0;
        float hitSign = -1f;

        for (int axis = 0; axis < 2; axis++)
        {
            float o = axis == 0 ? local.X : local.Y;
            float d = axis == 0 ? dirLocal.X : dirLocal.Y;
            float halfExtent = axis == 0 ? box.HalfExtents.X : box.HalfExtents.Y;

            if (MathF.Abs(d) < 1e-8f)
            {
                if (o < -halfExtent || o > halfExtent) return false; // parallel to this slab and outside it
                continue;
            }

            float inv = 1f / d;
            float t1 = (-halfExtent - o) * inv;
            float t2 = (halfExtent - o) * inv;
            float sign1 = -1f, sign2 = 1f;
            if (t1 > t2) { (t1, t2) = (t2, t1); (sign1, sign2) = (sign2, sign1); }

            if (t1 > tMin) { tMin = t1; hitAxis = axis; hitSign = sign1; }
            tMax = MathF.Min(tMax, t2);
            if (tMin > tMax) return false;
        }

        if (tMax < 0f) return false; // box is entirely behind the ray

        float t = tMin < 0f ? 0f : tMin;
        if (t > maxDistance) return false;

        var localNormal = hitAxis == 0 ? new Vector2(hitSign, 0f) : new Vector2(0f, hitSign);
        distance = t;
        point = origin + direction * t;
        normal = box.AxisX * localNormal.X + box.AxisY * localNormal.Y;
        return true;
    }

    public static bool PointInCircle(Vector2 point, Vector2 center, float radius) =>
        Vector2.DistanceSquared(point, center) <= radius * radius;

    public static bool PointInBox(Vector2 point, BoxShape2D box)
    {
        Vector2 d = point - box.Center;
        float localX = Vector2.Dot(d, box.AxisX);
        float localY = Vector2.Dot(d, box.AxisY);
        return localX >= -box.HalfExtents.X && localX <= box.HalfExtents.X &&
               localY >= -box.HalfExtents.Y && localY <= box.HalfExtents.Y;
    }

    public static bool CircleOverlapsCircle(Vector2 centerA, float radiusA, Vector2 centerB, float radiusB)
    {
        float radiusSum = radiusA + radiusB;
        return Vector2.DistanceSquared(centerA, centerB) <= radiusSum * radiusSum;
    }

    public static bool CircleOverlapsBox(Vector2 circleCenter, float radius, BoxShape2D box)
    {
        Vector2 d = circleCenter - box.Center;
        float localX = Math.Clamp(Vector2.Dot(d, box.AxisX), -box.HalfExtents.X, box.HalfExtents.X);
        float localY = Math.Clamp(Vector2.Dot(d, box.AxisY), -box.HalfExtents.Y, box.HalfExtents.Y);
        Vector2 closestPoint = box.Center + box.AxisX * localX + box.AxisY * localY;
        return Vector2.DistanceSquared(circleCenter, closestPoint) <= radius * radius;
    }
}
