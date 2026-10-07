using Microsoft.Xna.Framework;
using MyEngine.Core.ECS;

namespace MyEngine.Core.Physics;

/// <summary>
/// MyEngine's built-in physics backend: a small, self-contained 2D rigid-body simulation with no external
/// dependencies. Broadphase is a sort-and-sweep over per-collider AABBs (see DetectCollisions), narrowphase
/// is SAT-plus-clipping for boxes and closed-form tests for circles, and the solver is a standard
/// sequential-impulse resolver (velocity iterations, then a single position-correction pass) — the same
/// family of technique Box2D (and by extension Godot's and Unity's own 2D physics) is built on.
///
/// The step is written data-oriented: the GameObject/Component world is read ONCE at the start of a step into
/// flat, reused scratch arrays (one AABB, one body reference, one "can move" flag per active collider), the
/// simulation then works off those arrays, and nothing is allocated per step in steady state. Results are
/// bit-for-bit identical to the earlier brute-force version — the candidate pairs the sweep finds are
/// re-ordered into exactly the (i, j) order the nested loops produced, so the contact list the sequential
/// solver iterates (whose result depends on order) is unchanged.
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

    // ---------------------------------------------------------------- reusable per-step scratch
    // Everything below is cleared and refilled every Step instead of being allocated fresh — a 60 Hz
    // physics step used to allocate several lists, two dictionaries and a hash set (plus one list and one
    // array per body for mass data) every single time, which was the single biggest source of GC pressure in
    // the engine. None of it is state that survives a step; it lives in fields purely to keep its storage.

    private readonly List<Collider2D> _activeColliders = new();
    private readonly List<Rigidbody2D> _activeBodies = new();
    private readonly List<PreparedContact> _contacts = new();
    private readonly Dictionary<(Collider2D, Collider2D), ContactManifold> _currentContacts = new();
    private HashSet<(Collider2D, Collider2D)> _currentTriggerPairs = new();

    /// <summary>Stamp written into <see cref="Rigidbody2D.SolverStamp"/> for every dynamic active body each
    /// step; a body whose stamp doesn't match the current step wasn't simulated this step (not dynamic, or
    /// disabled) and so has no mass data — the same "absent from the dictionary" answer the per-step
    /// Dictionary this replaced gave.</summary>
    private int _stepId;

    // Broadphase scratch, indexed by position in _activeColliders.
    private float[] _minX = new float[64], _maxX = new float[64], _minY = new float[64], _maxY = new float[64];
    private Rigidbody2D?[] _bodyOf = new Rigidbody2D?[64];
    private bool[] _canMove = new bool[64];
    private float[] _sortKeys = new float[64];
    private int[] _sortIndex = new int[64];
    private long[] _pairKeys = new long[256];

    // ComputeMassData scratch.
    private readonly List<Collider2D> _massColliders = new();
    private float[] _massWeights = new float[8];

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
        _stepId++;

        GatherActive(_colliders, _activeColliders);
        GatherActive(_bodies, _activeBodies);
        IntegrateForces(fixedDeltaTime, gravity);

        _contacts.Clear();
        _currentContacts.Clear();
        _currentTriggerPairs.Clear();

        DetectCollisions();

        // Iterating a span by reference: PreparedContact is a fat struct (a dozen references and floats), and
        // the earlier foreach copied every one of them, per contact, per iteration.
        var contacts = System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_contacts);

        for (int iteration = 0; iteration < VelocityIterations; iteration++)
            foreach (ref readonly var contact in contacts)
                SolveContact(contact);

        IntegratePositions(fixedDeltaTime);

        foreach (ref readonly var contact in contacts)
            ApplyPositionCorrection(contact);

        RaiseCollisionEvents(_currentContacts);
        RaiseTriggerEvents(_currentTriggerPairs);

        // The trigger set this step just filled becomes "previous" for the next; the old previous set (now
        // stale) is recycled as next step's scratch — RaiseTriggerEvents has already finished reading both.
        (_previousTriggerPairs, _currentTriggerPairs) = (_currentTriggerPairs, _previousTriggerPairs);

        // Don't hold Collider2D/Rigidbody2D references (and through them whole GameObjects) alive between steps.
        _activeColliders.Clear();
        _activeBodies.Clear();
        _contacts.Clear();
        _currentContacts.Clear();
    }

    private static void GatherActive(List<Rigidbody2D> source, List<Rigidbody2D> destination)
    {
        destination.Clear();
        foreach (var b in source)
            if (b.Enabled && b.Owner.ActiveInHierarchy) destination.Add(b);
    }

    private static void GatherActive(List<Collider2D> source, List<Collider2D> destination)
    {
        destination.Clear();
        foreach (var c in source)
            if (c.Enabled && c.Owner.ActiveInHierarchy) destination.Add(c);
    }

    /// <summary>Applies gravity/accumulated forces to every active Dynamic body's velocity, and records each
    /// one's computed inverse mass/inertia on the body itself (Rigidbody2D.SolverInvMass / SolverInvInertia,
    /// stamped with this step's id) for the rest of the step — collision solving needs it too, so it's
    /// computed once here rather than twice.</summary>
    private void IntegrateForces(float dt, Vector2 gravity)
    {
        foreach (var body in _activeBodies)
        {
            if (body.BodyType != BodyType2D.Dynamic) continue;

            ComputeMassData(body, out float invMass, out float invInertia);
            body.SolverInvMass = invMass;
            body.SolverInvInertia = invInertia;
            body.SolverStamp = _stepId;

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
    }

    private void IntegratePositions(float dt)
    {
        foreach (var body in _activeBodies)
        {
            if (body.BodyType == BodyType2D.Static) continue;

            body.Transform.Position += body.LinearVelocity * dt;
            if (!body.FreezeRotation)
                body.Transform.Rotation += body.AngularVelocity * dt;
        }
    }

    // ---------------------------------------------------------------- mass properties

    private void ComputeMassData(Rigidbody2D body, out float invMass, out float invInertia)
    {
        float mass = MathF.Max(body.Mass, 0.0001f);
        invMass = 1f / mass;

        if (body.FreezeRotation) { invInertia = 0f; return; }

        // Same enabled Collider2Ds in the same order as GetComponents<Collider2D>() gave, gathered into a
        // reused list instead of a fresh list + iterator per body per step.
        var colliders = _massColliders;
        colliders.Clear();
        var components = body.Owner.Components;
        for (int c = 0; c < components.Count; c++)
            if (components[c] is Collider2D { Enabled: true } collider) colliders.Add(collider);

        if (colliders.Count == 0) { invInertia = 0f; return; }

        if (_massWeights.Length < colliders.Count)
            _massWeights = new float[Math.Max(colliders.Count, _massWeights.Length * 2)];
        var weights = _massWeights;
        float totalWeight = 0f;
        for (int i = 0; i < colliders.Count; i++)
        {
            weights[i] = ComputeWorldArea(colliders[i]) * MathF.Max(colliders[i].Density, 0.0001f);
            totalWeight += weights[i];
        }

        if (totalWeight <= 0.0001f) { invInertia = 0f; colliders.Clear(); return; }

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
        colliders.Clear();
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

    /// <summary>Broadphase + narrowphase for this step's active colliders, filling <see cref="_contacts"/>,
    /// <see cref="_currentContacts"/> and <see cref="_currentTriggerPairs"/>.
    ///
    /// Broadphase is sort-and-sweep. Every active collider's margin-expanded AABB (and its Rigidbody2D and
    /// "can this ever move" flag) is computed ONCE into flat arrays — the brute-force version this replaced
    /// re-derived the second collider's AABB (a sin/cos and a matrix read) inside its inner loop, once per
    /// PAIR, i.e. N²/2 times a step. The colliders are then sorted by the left edge of their box, and each
    /// one is only tested against the neighbours whose left edge starts before its own right edge — the
    /// rest can't overlap on X, so they're never looked at.
    ///
    /// Candidates are collected as packed (i, j) keys and sorted before the narrowphase runs, so the
    /// narrowphase sees pairs in exactly the i-ascending, then j-ascending order the old nested loops
    /// produced. That is not cosmetic: the solver applies contacts one after another (each one sees the
    /// velocities the previous one left behind), so a different order gives a different — equally valid, but
    /// not identical — simulation. Keeping the order keeps every existing game's physics exactly as it was.</summary>
    private void DetectCollisions()
    {
        int n = _activeColliders.Count;
        if (n < 2) return;
        EnsureBroadphaseCapacity(n);

        for (int i = 0; i < n; i++)
        {
            var collider = _activeColliders[i];
            var bounds = GetAABB(collider).Expanded(BroadphaseMargin);
            _minX[i] = bounds.Min.X; _maxX[i] = bounds.Max.X;
            _minY[i] = bounds.Min.Y; _maxY[i] = bounds.Max.Y;

            var body = collider.Owner.GetComponent<Rigidbody2D>();
            _bodyOf[i] = body;
            _canMove[i] = body != null && body.BodyType != BodyType2D.Static;

            _sortKeys[i] = bounds.Min.X;
            _sortIndex[i] = i;
        }

        Array.Sort(_sortKeys, _sortIndex, 0, n);

        int pairCount = 0;
        for (int sa = 0; sa < n; sa++)
        {
            int ia = _sortIndex[sa];
            float maxXa = _maxX[ia];

            for (int sb = sa + 1; sb < n; sb++)
            {
                int ib = _sortIndex[sb];
                if (_minX[ib] > maxXa) break; // sorted by left edge: nothing further right can overlap ia on X

                // Same-owner and both-immovable pairs are dropped here, before the overlap test — cheaper than
                // letting them reach the narrowphase, and (they're pure predicates) it can't change which
                // pairs survive.
                if (!_canMove[ia] && !_canMove[ib]) continue;
                if (_activeColliders[ia].Owner == _activeColliders[ib].Owner) continue;

                // The full overlap test, identical to the old one, on the identical expanded boxes.
                if (!(_minX[ia] <= _maxX[ib] && _maxX[ia] >= _minX[ib] && _minY[ia] <= _maxY[ib] && _maxY[ia] >= _minY[ib]))
                    continue;

                int lo = Math.Min(ia, ib), hi = Math.Max(ia, ib);
                if (pairCount == _pairKeys.Length) Array.Resize(ref _pairKeys, pairCount * 2);
                _pairKeys[pairCount++] = ((long)lo << 32) | (uint)hi;
            }
        }

        Array.Sort(_pairKeys, 0, pairCount);

        for (int p = 0; p < pairCount; p++)
        {
            int i = (int)(_pairKeys[p] >> 32);
            int j = (int)(_pairKeys[p] & 0xFFFFFFFF);
            var colliderA = _activeColliders[i];
            var colliderB = _activeColliders[j];
            var bodyA = _bodyOf[i];
            var bodyB = _bodyOf[j];

            var manifold = Collide(colliderA, colliderB);
            if (manifold == null) continue;

            if (colliderA.IsTrigger || colliderB.IsTrigger)
            {
                _currentTriggerPairs.Add((colliderA, colliderB));
                continue;
            }

            _currentContacts[(colliderA, colliderB)] = manifold;

            var (invMassA, invInertiaA) = GetMass(bodyA);
            var (invMassB, invInertiaB) = GetMass(bodyB);
            if (invMassA + invMassB <= 0f) continue; // nothing here the solver could actually move

            _contacts.Add(new PreparedContact(
                colliderA, colliderB, bodyA, bodyB, invMassA, invInertiaA, invMassB, invInertiaB, manifold));
        }

        // Drop the references this pass parked in the scratch arrays (see Step's note about not pinning
        // GameObjects between steps).
        Array.Clear(_bodyOf, 0, n);
    }

    private void EnsureBroadphaseCapacity(int n)
    {
        if (_minX.Length >= n) return;
        int size = Math.Max(n, _minX.Length * 2);
        _minX = new float[size]; _maxX = new float[size]; _minY = new float[size]; _maxY = new float[size];
        _bodyOf = new Rigidbody2D?[size]; _canMove = new bool[size];
        _sortKeys = new float[size]; _sortIndex = new int[size];
    }

    /// <summary>The body's inverse mass/inertia as computed by this step's IntegrateForces — or (0, 0) for
    /// anything that wasn't simulated as a dynamic body this step (no body, a static/kinematic one, a
    /// disabled one), which is what makes it an immovable obstacle to the solver.</summary>
    private (float, float) GetMass(Rigidbody2D? body) =>
        body != null && body.SolverStamp == _stepId ? (body.SolverInvMass, body.SolverInvInertia) : (0f, 0f);

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

        /// <summary>The two colliders' world positions, read once when the contact is prepared. Nothing moves
        /// between here and the last velocity iteration (positions are only integrated afterwards), so this is
        /// the same value the solver used to re-read — through the Transform — on every one of its iterations.</summary>
        public readonly Vector2 PosA, PosB;

        public PreparedContact(
            Collider2D colliderA, Collider2D colliderB, Rigidbody2D? bodyA, Rigidbody2D? bodyB,
            float invMassA, float invInertiaA, float invMassB, float invInertiaB, ContactManifold manifold)
        {
            ColliderA = colliderA; ColliderB = colliderB; BodyA = bodyA; BodyB = bodyB;
            InvMassA = invMassA; InvInertiaA = invInertiaA; InvMassB = invMassB; InvInertiaB = invInertiaB;
            Manifold = manifold;
            PosA = colliderA.Transform.Position;
            PosB = colliderB.Transform.Position;
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
        Vector2 rA = point - contact.PosA;
        Vector2 rB = point - contact.PosB;

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

        // Refilled in place, in the current contacts' own (insertion) order — the same contents and iteration
        // order a freshly built HashSet from those keys had — instead of allocating a new set every step.
        _previousContactPairs.Clear();
        foreach (var pair in currentContacts.Keys)
            _previousContactPairs.Add(pair);
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

        // (Step swaps this set in as the new "previous" once every reader is done with both.)
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
