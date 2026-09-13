using Microsoft.Xna.Framework;
using MyEngine.Core.ECS;

namespace MyEngine.Core.Physics;

/// <summary>
/// MyEngine's built-in physics backend: a small, self-contained 2D rigid-body simulation with no external
/// dependencies. Broadphase is a brute-force AABB sweep (fine for the object counts a v1 2D game project
/// typically has — a spatial grid is the natural next optimization if that stops being true), narrowphase
/// is SAT-plus-clipping for boxes and closed-form tests for circles, and the solver is a standard
/// sequential-impulse resolver (velocity iterations, then a single position-correction pass) — the same
/// family of technique Box2D (and by extension Godot's and Unity's own 2D physics) is built on.
/// </summary>
public sealed class ManagedPhysicsBackend2D : IPhysicsBackend2D
{
    private const int VelocityIterations = 8;
    private const float PositionCorrectionPercent = 0.2f;
    private const float PositionSlop = 0.01f;
    private const float BroadphaseMargin = 0.05f;

    private readonly List<Rigidbody2D> _bodies = new();
    private readonly List<Collider2D> _colliders = new();
    private readonly List<PhysicsEvent2D> _pendingEvents = new();

    private HashSet<(Collider2D, Collider2D)> _previousContactPairs = new();
    private HashSet<(Collider2D, Collider2D)> _previousTriggerPairs = new();

    // ---------------------------------------------------------------- registration

    public void RegisterBody(Rigidbody2D body)
    {
        if (!_bodies.Contains(body)) _bodies.Add(body);
    }

    public void UnregisterBody(Rigidbody2D body) => _bodies.Remove(body);

    public void RegisterCollider(Collider2D collider)
    {
        if (!_colliders.Contains(collider)) _colliders.Add(collider);
    }

    public void UnregisterCollider(Collider2D collider)
    {
        _colliders.Remove(collider);

        // A collider can be removed (GameObject destroyed, or the component itself deleted) while still
        // mid-contact with something. Fire the Exit/TriggerExit events it's owed right now rather than
        // silently dropping them, then forget about the pair so we don't hold a reference to it forever.
        foreach (var pair in _previousContactPairs)
            if (pair.Item1 == collider || pair.Item2 == collider)
                _pendingEvents.Add(BuildEndEvent(PhysicsEventKind.CollisionExit, pair.Item1, pair.Item2));
        _previousContactPairs.RemoveWhere(p => p.Item1 == collider || p.Item2 == collider);

        foreach (var pair in _previousTriggerPairs)
            if (pair.Item1 == collider || pair.Item2 == collider)
                _pendingEvents.Add(BuildEndEvent(PhysicsEventKind.TriggerExit, pair.Item1, pair.Item2));
        _previousTriggerPairs.RemoveWhere(p => p.Item1 == collider || p.Item2 == collider);
    }

    private static PhysicsEvent2D BuildEndEvent(PhysicsEventKind kind, Collider2D a, Collider2D b) =>
        new(kind, a, b, Vector2.Zero, a.Transform.Position, Vector2.Zero);

    // ---------------------------------------------------------------- step

    public void Step(float fixedDeltaTime, Vector2 gravity)
    {
        var activeColliders = GetActive(_colliders);
        var massData = IntegrateForces(fixedDeltaTime, gravity);
        var contacts = new List<PreparedContact>();
        var currentContacts = new Dictionary<(Collider2D, Collider2D), ContactManifold>();
        var currentTriggerPairs = new HashSet<(Collider2D, Collider2D)>();

        DetectCollisions(activeColliders, massData, contacts, currentContacts, currentTriggerPairs);

        for (int iteration = 0; iteration < VelocityIterations; iteration++)
            foreach (var contact in contacts)
                SolveContact(contact);

        IntegratePositions(fixedDeltaTime);

        foreach (var contact in contacts)
            ApplyPositionCorrection(contact);

        RaiseCollisionEvents(currentContacts);
        RaiseTriggerEvents(currentTriggerPairs);
    }

    private static List<Rigidbody2D> GetActive(List<Rigidbody2D> bodies)
    {
        var result = new List<Rigidbody2D>(bodies.Count);
        foreach (var b in bodies)
            if (b.Enabled && b.Owner.ActiveInHierarchy) result.Add(b);
        return result;
    }

    private static List<Collider2D> GetActive(List<Collider2D> colliders)
    {
        var result = new List<Collider2D>(colliders.Count);
        foreach (var c in colliders)
            if (c.Enabled && c.Owner.ActiveInHierarchy) result.Add(c);
        return result;
    }

    /// <summary>Applies gravity/accumulated forces to every active Dynamic body's velocity and returns each
    /// body's computed inverse mass/inertia for reuse by the rest of this step (collision solving needs it
    /// too, so it's computed once here rather than twice).</summary>
    private Dictionary<Rigidbody2D, (float InvMass, float InvInertia)> IntegrateForces(float dt, Vector2 gravity)
    {
        var massData = new Dictionary<Rigidbody2D, (float, float)>();

        foreach (var body in GetActive(_bodies))
        {
            if (body.BodyType != BodyType2D.Dynamic) continue;

            ComputeMassData(body, out float invMass, out float invInertia);
            massData[body] = (invMass, invInertia);

            var (force, torque) = body.ConsumePendingForces();

            Vector2 acceleration = gravity * body.GravityScale + force * invMass;
            Vector2 velocity = body.LinearVelocity + acceleration * dt;
            body.LinearVelocity = velocity / (1f + body.LinearDamping * dt);

            if (body.FreezeRotation)
            {
                body.AngularVelocity = 0f;
            }
            else
            {
                float angularVelocity = body.AngularVelocity + torque * invInertia * dt;
                body.AngularVelocity = angularVelocity / (1f + body.AngularDamping * dt);
            }
        }

        return massData;
    }

    private void IntegratePositions(float dt)
    {
        foreach (var body in GetActive(_bodies))
        {
            if (body.BodyType == BodyType2D.Static) continue;

            body.Transform.Position += body.LinearVelocity * dt;
            if (!body.FreezeRotation)
                body.Transform.Rotation += body.AngularVelocity * dt;
        }
    }

    // ---------------------------------------------------------------- mass properties

    private static void ComputeMassData(Rigidbody2D body, out float invMass, out float invInertia)
    {
        float mass = MathF.Max(body.Mass, 0.0001f);
        invMass = 1f / mass;

        if (body.FreezeRotation) { invInertia = 0f; return; }

        var colliders = new List<Collider2D>();
        foreach (var c in body.Owner.GetComponents<Collider2D>())
            if (c.Enabled) colliders.Add(c);

        if (colliders.Count == 0) { invInertia = 0f; return; }

        var weights = new float[colliders.Count];
        float totalWeight = 0f;
        for (int i = 0; i < colliders.Count; i++)
        {
            weights[i] = ComputeWorldArea(colliders[i]) * MathF.Max(colliders[i].Density, 0.0001f);
            totalWeight += weights[i];
        }

        if (totalWeight <= 0.0001f) { invInertia = 0f; return; }

        Vector2 bodyOrigin = body.Transform.Position;
        float totalInertia = 0f;
        for (int i = 0; i < colliders.Count; i++)
        {
            float colliderMass = mass * (weights[i] / totalWeight);
            float shapeCoefficient = ComputeShapeInertiaCoefficient(colliders[i]);
            Vector2 offset = GetWorldCenter(colliders[i]) - bodyOrigin;
            totalInertia += colliderMass * shapeCoefficient + colliderMass * offset.LengthSquared();
        }

        invInertia = totalInertia > 0.0001f ? 1f / totalInertia : 0f;
    }

    private static float ComputeWorldArea(Collider2D collider)
    {
        switch (collider)
        {
            case BoxCollider2D box:
                var scale = box.Transform.Scale;
                return MathF.Abs(box.Size.X * scale.X) * MathF.Abs(box.Size.Y * scale.Y);
            case CircleCollider2D circle:
                float r = GetWorldCircle(circle).Radius;
                return MathF.PI * r * r;
            default:
                return 0f;
        }
    }

    private static float ComputeShapeInertiaCoefficient(Collider2D collider)
    {
        switch (collider)
        {
            case BoxCollider2D box:
                var scale = box.Transform.Scale;
                float w = box.Size.X * scale.X;
                float h = box.Size.Y * scale.Y;
                return (w * w + h * h) / 12f;
            case CircleCollider2D circle:
                float r = GetWorldCircle(circle).Radius;
                return r * r / 2f;
            default:
                return 0f;
        }
    }

    // ---------------------------------------------------------------- world-space shape helpers

    private static Vector2 GetWorldCenter(Collider2D collider)
    {
        var t = collider.Transform;
        return t.Position + Vec2.Rotate(collider.Offset * t.Scale, t.Rotation);
    }

    private static BoxShape2D GetWorldBox(BoxCollider2D box)
    {
        var t = box.Transform;
        return new BoxShape2D(GetWorldCenter(box), box.Size * t.Scale * 0.5f, t.Rotation);
    }

    private static (Vector2 Center, float Radius) GetWorldCircle(CircleCollider2D circle)
    {
        var t = circle.Transform;
        float uniformScale = (MathF.Abs(t.Scale.X) + MathF.Abs(t.Scale.Y)) * 0.5f;
        return (GetWorldCenter(circle), circle.Radius * uniformScale);
    }

    private static AABB2D GetAABB(Collider2D collider)
    {
        switch (collider)
        {
            case BoxCollider2D box: return GetWorldBox(box).ToAABB();
            case CircleCollider2D circle:
                var (center, radius) = GetWorldCircle(circle);
                return AABB2D.FromCenterHalfExtents(center, new Vector2(radius, radius));
            default:
                var p = collider.Transform.Position;
                return new AABB2D(p, p);
        }
    }

    private static ContactManifold? Collide(Collider2D a, Collider2D b)
    {
        switch (a, b)
        {
            case (CircleCollider2D ca, CircleCollider2D cb):
                var wca = GetWorldCircle(ca);
                var wcb = GetWorldCircle(cb);
                return CollisionMath2D.CircleVsCircle(wca.Center, wca.Radius, wcb.Center, wcb.Radius);

            case (BoxCollider2D ba, BoxCollider2D bb):
                return CollisionMath2D.BoxVsBox(GetWorldBox(ba), GetWorldBox(bb));

            case (BoxCollider2D boxA, CircleCollider2D circleB):
                var wc1 = GetWorldCircle(circleB);
                // CircleVsBox's normal points box->circle, which already matches a(box)->b(circle) here.
                return CollisionMath2D.CircleVsBox(wc1.Center, wc1.Radius, GetWorldBox(boxA));

            case (CircleCollider2D circleA, BoxCollider2D boxB):
                var wc2 = GetWorldCircle(circleA);
                // CircleVsBox's normal points box->circle; we need a(circle)->b(box), so flip it.
                return CollisionMath2D.CircleVsBox(wc2.Center, wc2.Radius, GetWorldBox(boxB))?.Flipped();

            default:
                return null;
        }
    }

    // ---------------------------------------------------------------- collision detection

    private void DetectCollisions(
        List<Collider2D> activeColliders,
        Dictionary<Rigidbody2D, (float InvMass, float InvInertia)> massData,
        List<PreparedContact> contacts,
        Dictionary<(Collider2D, Collider2D), ContactManifold> currentContacts,
        HashSet<(Collider2D, Collider2D)> currentTriggerPairs)
    {
        for (int i = 0; i < activeColliders.Count; i++)
        {
            var colliderA = activeColliders[i];
            var boundsA = GetAABB(colliderA).Expanded(BroadphaseMargin);

            for (int j = i + 1; j < activeColliders.Count; j++)
            {
                var colliderB = activeColliders[j];
                if (colliderA.Owner == colliderB.Owner) continue; // colliders on one body never collide with each other

                var bodyA = colliderA.Owner.GetComponent<Rigidbody2D>();
                var bodyB = colliderB.Owner.GetComponent<Rigidbody2D>();
                bool eitherCanMove = (bodyA != null && bodyA.BodyType != BodyType2D.Static)
                                   || (bodyB != null && bodyB.BodyType != BodyType2D.Static);
                if (!eitherCanMove) continue; // two things that can never move relative to each other

                if (!boundsA.Overlaps(GetAABB(colliderB).Expanded(BroadphaseMargin))) continue;

                var manifold = Collide(colliderA, colliderB);
                if (manifold == null) continue;

                if (colliderA.IsTrigger || colliderB.IsTrigger)
                {
                    currentTriggerPairs.Add((colliderA, colliderB));
                    continue;
                }

                currentContacts[(colliderA, colliderB)] = manifold;

                var (invMassA, invInertiaA) = GetMass(bodyA, massData);
                var (invMassB, invInertiaB) = GetMass(bodyB, massData);
                if (invMassA + invMassB <= 0f) continue; // nothing here the solver could actually move

                contacts.Add(new PreparedContact(
                    colliderA, colliderB, bodyA, bodyB, invMassA, invInertiaA, invMassB, invInertiaB, manifold));
            }
        }
    }

    private static (float, float) GetMass(Rigidbody2D? body, Dictionary<Rigidbody2D, (float InvMass, float InvInertia)> massData) =>
        body != null && massData.TryGetValue(body, out var m) ? m : (0f, 0f);

    // ---------------------------------------------------------------- solving

    private readonly struct PreparedContact
    {
        public readonly Collider2D ColliderA;
        public readonly Collider2D ColliderB;
        public readonly Rigidbody2D? BodyA;
        public readonly Rigidbody2D? BodyB;
        public readonly float InvMassA, InvInertiaA, InvMassB, InvInertiaB;
        public readonly ContactManifold Manifold;
        public readonly float Friction;
        public readonly float Restitution;

        public PreparedContact(
            Collider2D colliderA, Collider2D colliderB, Rigidbody2D? bodyA, Rigidbody2D? bodyB,
            float invMassA, float invInertiaA, float invMassB, float invInertiaB, ContactManifold manifold)
        {
            ColliderA = colliderA; ColliderB = colliderB; BodyA = bodyA; BodyB = bodyB;
            InvMassA = invMassA; InvInertiaA = invInertiaA; InvMassB = invMassB; InvInertiaB = invInertiaB;
            Manifold = manifold;
            Friction = MathF.Sqrt(MathF.Max(colliderA.Friction, 0f) * MathF.Max(colliderB.Friction, 0f));
            Restitution = MathF.Max(colliderA.Restitution, colliderB.Restitution);
        }
    }

    private static void SolveContact(in PreparedContact contact)
    {
        ResolveContactPoint(contact, contact.Manifold.ContactPoint0);
        if (contact.Manifold.ContactCount > 1)
            ResolveContactPoint(contact, contact.Manifold.ContactPoint1);
    }

    private static void ResolveContactPoint(in PreparedContact contact, Vector2 point)
    {
        Vector2 normal = contact.Manifold.Normal;
        Vector2 posA = contact.ColliderA.Transform.Position;
        Vector2 posB = contact.ColliderB.Transform.Position;
        Vector2 rA = point - posA;
        Vector2 rB = point - posB;

        Vector2 relativeVelocity = RelativeVelocityAt(contact.BodyA, contact.BodyB, rA, rB);
        float velocityAlongNormal = Vector2.Dot(relativeVelocity, normal);
        if (velocityAlongNormal > 0f) return; // already separating, nothing to resolve

        float rnA = Vec2.Cross(rA, normal);
        float rnB = Vec2.Cross(rB, normal);
        float normalMass = contact.InvMassA + contact.InvMassB
            + contact.InvInertiaA * rnA * rnA + contact.InvInertiaB * rnB * rnB;
        if (normalMass <= 0.0001f) return;

        float jn = MathF.Max(-(1f + contact.Restitution) * velocityAlongNormal / normalMass, 0f);
        Vector2 normalImpulse = jn * normal;
        ApplyImpulse(contact.BodyA, -normalImpulse, rA, contact.InvMassA, contact.InvInertiaA);
        ApplyImpulse(contact.BodyB, normalImpulse, rB, contact.InvMassB, contact.InvInertiaB);

        // Friction, using the relative velocity *after* the normal impulse was applied.
        relativeVelocity = RelativeVelocityAt(contact.BodyA, contact.BodyB, rA, rB);
        Vector2 tangent = new(-normal.Y, normal.X);
        float velocityAlongTangent = Vector2.Dot(relativeVelocity, tangent);

        float rtA = Vec2.Cross(rA, tangent);
        float rtB = Vec2.Cross(rB, tangent);
        float tangentMass = contact.InvMassA + contact.InvMassB
            + contact.InvInertiaA * rtA * rtA + contact.InvInertiaB * rtB * rtB;
        if (tangentMass <= 0.0001f) return;

        float maxFriction = contact.Friction * jn;
        float jt = Math.Clamp(-velocityAlongTangent / tangentMass, -maxFriction, maxFriction);
        Vector2 frictionImpulse = jt * tangent;
        ApplyImpulse(contact.BodyA, -frictionImpulse, rA, contact.InvMassA, contact.InvInertiaA);
        ApplyImpulse(contact.BodyB, frictionImpulse, rB, contact.InvMassB, contact.InvInertiaB);
    }

    /// <summary>Velocity of the point rB (on body B) relative to the point rA (on body A), where rA/rB are
    /// offsets from each body's origin to the shared world-space contact point. Either body may be null
    /// (an implicit-Static collider with no Rigidbody2D), treated as stationary.</summary>
    private static Vector2 RelativeVelocityAt(Rigidbody2D? bodyA, Rigidbody2D? bodyB, Vector2 rA, Vector2 rB)
    {
        Vector2 velA = bodyA?.LinearVelocity ?? Vector2.Zero;
        Vector2 velB = bodyB?.LinearVelocity ?? Vector2.Zero;
        float angA = bodyA?.AngularVelocity ?? 0f;
        float angB = bodyB?.AngularVelocity ?? 0f;
        return (velB + Vec2.Cross(angB, rB)) - (velA + Vec2.Cross(angA, rA));
    }

    private static void ApplyImpulse(Rigidbody2D? body, Vector2 impulse, Vector2 r, float invMass, float invInertia)
    {
        if (body == null || invMass <= 0f) return;
        body.LinearVelocity += impulse * invMass;
        body.AngularVelocity += invInertia * Vec2.Cross(r, impulse);
    }

    /// <summary>A single post-solve positional nudge per contact (rather than per contact point) — cheaper
    /// and avoids double-correcting a 2-point manifold, at the cost of slightly less precise stacking
    /// behavior than a full per-point Baumgarte scheme. Reasonable for a v1.</summary>
    private static void ApplyPositionCorrection(in PreparedContact contact)
    {
        float totalInvMass = contact.InvMassA + contact.InvMassB;
        if (totalInvMass <= 0f) return;

        float penetration = contact.Manifold.MaxPenetration();
        float magnitude = MathF.Max(penetration - PositionSlop, 0f) / totalInvMass * PositionCorrectionPercent;
        if (magnitude <= 0f) return;

        Vector2 correction = magnitude * contact.Manifold.Normal;
        if (contact.BodyA != null && contact.InvMassA > 0f)
            contact.BodyA.Transform.Position -= correction * contact.InvMassA;
        if (contact.BodyB != null && contact.InvMassB > 0f)
            contact.BodyB.Transform.Position += correction * contact.InvMassB;
    }

    // ---------------------------------------------------------------- events

    /// <summary>Turns this step's touching pairs into Enter/Stay events (with real contact normal/point/
    /// relative-velocity data) and last step's pairs that dropped out into Exit events, then remembers
    /// this step's pairs for next time.</summary>
    private void RaiseCollisionEvents(Dictionary<(Collider2D, Collider2D), ContactManifold> currentContacts)
    {
        foreach (var (pair, manifold) in currentContacts)
        {
            var kind = _previousContactPairs.Contains(pair) ? PhysicsEventKind.CollisionStay : PhysicsEventKind.CollisionEnter;
            Vector2 point = manifold.AveragePoint();
            Vector2 relativeVelocity = RelativeVelocityAtWorldPoint(pair.Item1, pair.Item2, point);
            _pendingEvents.Add(new PhysicsEvent2D(kind, pair.Item1, pair.Item2, manifold.Normal, point, relativeVelocity));
        }

        foreach (var pair in _previousContactPairs)
            if (!currentContacts.ContainsKey(pair))
                _pendingEvents.Add(BuildEndEvent(PhysicsEventKind.CollisionExit, pair.Item1, pair.Item2));

        _previousContactPairs = new HashSet<(Collider2D, Collider2D)>(currentContacts.Keys);
    }

    /// <summary>Same Enter/Stay/Exit bookkeeping as RaiseCollisionEvents, but triggers only ever need to
    /// identify the other collider (Unity's OnTriggerEnter2D(Collider2D) takes no manifold), so there's no
    /// per-pair data to carry.</summary>
    private void RaiseTriggerEvents(HashSet<(Collider2D, Collider2D)> currentTriggerPairs)
    {
        foreach (var pair in currentTriggerPairs)
        {
            var kind = _previousTriggerPairs.Contains(pair) ? PhysicsEventKind.TriggerStay : PhysicsEventKind.TriggerEnter;
            _pendingEvents.Add(BuildEndEvent(kind, pair.Item1, pair.Item2));
        }

        foreach (var pair in _previousTriggerPairs)
            if (!currentTriggerPairs.Contains(pair))
                _pendingEvents.Add(BuildEndEvent(PhysicsEventKind.TriggerExit, pair.Item1, pair.Item2));

        _previousTriggerPairs = currentTriggerPairs;
    }

    private static Vector2 RelativeVelocityAtWorldPoint(Collider2D a, Collider2D b, Vector2 worldPoint)
    {
        var bodyA = a.Owner.GetComponent<Rigidbody2D>();
        var bodyB = b.Owner.GetComponent<Rigidbody2D>();
        Vector2 rA = worldPoint - a.Transform.Position;
        Vector2 rB = worldPoint - b.Transform.Position;
        return RelativeVelocityAt(bodyA, bodyB, rA, rB);
    }

    public IReadOnlyList<PhysicsEvent2D> ConsumeEvents()
    {
        if (_pendingEvents.Count == 0) return Array.Empty<PhysicsEvent2D>();
        var result = _pendingEvents.ToArray();
        _pendingEvents.Clear();
        return result;
    }

    // ---------------------------------------------------------------- queries

    public bool Raycast(Vector2 origin, Vector2 direction, float maxDistance, bool includeTriggers, out RaycastHit2D hit)
    {
        hit = default;
        if (direction.LengthSquared() < 0.0001f) return false;
        Vector2 dir = Vector2.Normalize(direction);

        bool found = false;
        float bestDistance = maxDistance;

        foreach (var collider in _colliders)
        {
            if (!collider.Enabled || !collider.Owner.ActiveInHierarchy) continue;
            if (!includeTriggers && collider.IsTrigger) continue;
            if (!TryRayVsCollider(origin, dir, bestDistance, collider, out float distance, out var point, out var normal))
                continue;

            found = true;
            bestDistance = distance;
            hit = new RaycastHit2D(collider, point, normal, distance);
        }

        return found;
    }

    public IReadOnlyList<RaycastHit2D> RaycastAll(Vector2 origin, Vector2 direction, float maxDistance, bool includeTriggers)
    {
        var results = new List<RaycastHit2D>();
        if (direction.LengthSquared() < 0.0001f) return results;
        Vector2 dir = Vector2.Normalize(direction);

        foreach (var collider in _colliders)
        {
            if (!collider.Enabled || !collider.Owner.ActiveInHierarchy) continue;
            if (!includeTriggers && collider.IsTrigger) continue;
            if (TryRayVsCollider(origin, dir, maxDistance, collider, out float distance, out var point, out var normal))
                results.Add(new RaycastHit2D(collider, point, normal, distance));
        }

        results.Sort((a, b) => a.Distance.CompareTo(b.Distance));
        return results;
    }

    private static bool TryRayVsCollider(
        Vector2 origin, Vector2 dir, float maxDistance, Collider2D collider,
        out float distance, out Vector2 point, out Vector2 normal)
    {
        switch (collider)
        {
            case CircleCollider2D circle:
                var (center, radius) = GetWorldCircle(circle);
                return RaycastMath2D.RayVsCircle(origin, dir, maxDistance, center, radius, out distance, out point, out normal);
            case BoxCollider2D box:
                return RaycastMath2D.RayVsBox(origin, dir, maxDistance, GetWorldBox(box), out distance, out point, out normal);
            default:
                distance = 0f; point = Vector2.Zero; normal = Vector2.Zero;
                return false;
        }
    }

    public IReadOnlyList<Collider2D> OverlapPoint(Vector2 point, bool includeTriggers)
    {
        var results = new List<Collider2D>();
        foreach (var collider in _colliders)
        {
            if (!collider.Enabled || !collider.Owner.ActiveInHierarchy) continue;
            if (!includeTriggers && collider.IsTrigger) continue;
            if (OverlapsPoint(collider, point)) results.Add(collider);
        }
        return results;
    }

    private static bool OverlapsPoint(Collider2D collider, Vector2 point)
    {
        switch (collider)
        {
            case CircleCollider2D circle:
                var (center, radius) = GetWorldCircle(circle);
                return RaycastMath2D.PointInCircle(point, center, radius);
            case BoxCollider2D box:
                return RaycastMath2D.PointInBox(point, GetWorldBox(box));
            default:
                return false;
        }
    }

    public IReadOnlyList<Collider2D> OverlapCircle(Vector2 center, float radius, bool includeTriggers)
    {
        var results = new List<Collider2D>();
        foreach (var collider in _colliders)
        {
            if (!collider.Enabled || !collider.Owner.ActiveInHierarchy) continue;
            if (!includeTriggers && collider.IsTrigger) continue;
            if (OverlapsCircle(collider, center, radius)) results.Add(collider);
        }
        return results;
    }

    private static bool OverlapsCircle(Collider2D collider, Vector2 center, float radius)
    {
        switch (collider)
        {
            case CircleCollider2D circle:
                var (otherCenter, otherRadius) = GetWorldCircle(circle);
                return RaycastMath2D.CircleOverlapsCircle(center, radius, otherCenter, otherRadius);
            case BoxCollider2D box:
                return RaycastMath2D.CircleOverlapsBox(center, radius, GetWorldBox(box));
            default:
                return false;
        }
    }
}
