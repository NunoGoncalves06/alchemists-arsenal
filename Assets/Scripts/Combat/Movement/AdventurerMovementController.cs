using System.Collections.Generic;
using UnityEngine;
using AlchemistsArsenal.Data;

namespace AlchemistsArsenal.Combat
{
    /// <summary>
    /// Physics-driven positioning FSM for an adventurer. Complements
    /// <see cref="UtilityAI_CombatController"/> (which decides <em>what</em> to
    /// throw) by deciding <em>where to stand</em>.
    ///
    /// Approach → Reposition → Throw → Retreat. Reposition is the key state: once in
    /// range it strafes toward the ideal-range shell (from either side) instead of
    /// freezing wherever contact first happened, so throws land in the bomb's sweet
    /// spot. All motion is <see cref="Rigidbody2D"/> steering force — no transform
    /// writes. State selection is deliberate FSM sequencing, not a utility decision.
    /// </summary>
    [RequireComponent(typeof(Rigidbody2D))]
    [RequireComponent(typeof(CombatantBody))]
    public class AdventurerMovementController : MonoBehaviour
    {
        public enum MoveState { Approach, Reposition, Throw, Retreat }

        [Header("Steering")]
        [Min(0f)] [SerializeField] private float moveSpeed = 4.2f;
        [Min(0f)] [SerializeField] private float steerAccel = 22f;
        [Min(0f)] [SerializeField] private float maxSteerForce = 32f;

        [Header("Spacing (auto-filled from the loadout if one is set)")]
        [SerializeField] private AdventurerLoadout loadout;
        [Min(0f)] [SerializeField] private float idealRange = 6f;
        [Min(0f)] [SerializeField] private float minSafeRange = 2.5f;
        [Min(0f)] [SerializeField] private float maxRange = 11f;
        [Tooltip("Distance from idealRange within which the adventurer holds and throws.")]
        [Min(0.1f)] [SerializeField] private float throwTolerance = 2.5f;

        [Header("Retreat")]
        [Range(0f, 1f)] [SerializeField] private float retreatHealthFraction = 0.4f;
        [SerializeField] private bool logStateChanges;

        [Header("Circling")]
        [Tooltip("How much of the movement is sideways around the target rather than toward/away from it.")]
        [Range(0f, 1.5f)] [SerializeField] private float orbitWeight = 0.85f;
        [Tooltip("Seconds before the circling direction flips on its own.")]
        [Min(0.5f)] [SerializeField] private float orbitFlipSeconds = 3.5f;
        [Tooltip("Distance from an arena wall at which the fighter starts steering off it.")]
        [Min(0.5f)] [SerializeField] private float wallMargin = 3.2f;

        /// <summary>Arena half-extents; 0 means "no bounds known", and wall steering is skipped.</summary>
        private Vector2 _arenaHalf;
        private float _orbitSign = 1f;
        private float _nextOrbitFlip;

        public MoveState State { get; private set; } = MoveState.Approach;

        /// <summary>
        /// Tell the controller where the arena walls are so it can steer off them.
        /// Without this it only ever moved along the line to its target — straight in
        /// or straight back — so a monster closing from the right pinned it against
        /// the left wall with nowhere left to go (playtest: "trapped in the left side
        /// of the arena").
        /// </summary>
        public void ConfigureArena(Vector2 halfExtents) => _arenaHalf = halfExtents;

        /// <summary>
        /// Apply a hero archetype's spacing. This is the whole mechanical
        /// difference between a Skirmisher, a Marksman and a Bulwark — where they
        /// stand and when they break off — with no HP or damage difference at all,
        /// so archetype is a sidegrade rather than a power tier.
        ///
        /// Note the serialized <c>loadout</c> field is deliberately left unset by
        /// the callers that use this, so <c>ResolveRangesFromLoadout</c> stays
        /// inert and the archetype is the sole authority on spacing.
        /// </summary>
        public void Configure(Data.HeroArchetype archetype)
        {
            idealRange = archetype.IdealRange;
            minSafeRange = archetype.MinSafeRange;
            maxRange = Mathf.Max(archetype.MaxRange, archetype.IdealRange);
            throwTolerance = Mathf.Max(0.1f, archetype.ThrowTolerance);
            orbitWeight = Mathf.Clamp(archetype.OrbitWeight, 0f, 1.5f);
            retreatHealthFraction = Mathf.Clamp01(archetype.RetreatHealthFraction);
        }

        private Rigidbody2D _rb;
        private CombatantBody _body;

        private void Awake()
        {
            _rb = GetComponent<Rigidbody2D>();
            _body = GetComponent<CombatantBody>();
            ResolveRangesFromLoadout();
        }

        private void FixedUpdate()
        {
            // A downed hero lingers for its death fade; it must not keep running around.
            if (_body != null && !_body.IsAlive) return;

            ICombatant target = Nearest(MonsterRegistry.ActiveMonsters, _rb.position);

            MoveState next = DecideState(target);
            if (next != State)
            {
                if (logStateChanges) Debug.Log($"[Move] {name}: {State} → {next}");
                State = next;
            }

            Vector2 desired = DesiredVelocity(target);
            // An acceleration, scaled by mass (see MonsterWalker).
            Vector2 steer = Vector2.ClampMagnitude((desired - _rb.linearVelocity) * steerAccel, maxSteerForce);
            _rb.AddForce(steer * _rb.mass, ForceMode2D.Force);
        }

        // FSM transition function — plain guards, by design.
        private MoveState DecideState(ICombatant target)
        {
            if (target == null) return MoveState.Approach;

            float hp = _body.MaxHP > 0 ? (float)_body.CurrentHP / _body.MaxHP : 1f;
            float d = Vector2.Distance(_rb.position, target.Position);

            // Only a real HP scare makes the adventurer flee — being crowded just
            // means back off to the firing shell (Reposition), not sprint off-camera
            // forever (playtest: "disappears into thin air").
            if (hp <= retreatHealthFraction) return MoveState.Retreat;

            // Inside the bomb's own minimum safe range the decision engine will not
            // throw at all. Calling that Throw meant braking to a stop and standing
            // there doing nothing while two monsters chewed on us — the movement FSM
            // thought it was in position, the AI knew it was not. Too close is always
            // Reposition, so we open the gap and then throw.
            if (d < minSafeRange * 1.15f) return MoveState.Reposition;

            if (Mathf.Abs(d - idealRange) <= throwTolerance) return MoveState.Throw;
            return MoveState.Reposition;
        }

        private Vector2 DesiredVelocity(ICombatant target)
        {
            if (target == null) return Vector2.zero;

            Vector2 toTarget = target.Position - _rb.position;
            float d = toTarget.magnitude;
            Vector2 dir = d > 0.001f ? toTarget / d : Vector2.right;

            // Radial: how much to close or open the gap.
            float radial;
            switch (State)
            {
                case MoveState.Retreat: radial = -moveSpeed; break;
                case MoveState.Throw:   radial = 0f; break;
                default:
                    radial = d < minSafeRange ? -moveSpeed
                        : Mathf.Clamp(d - idealRange, -moveSpeed, moveSpeed);
                    break;
            }

            // Tangential: the piece that turns "back away" into "circle around".
            // Retreating along a straight line is what corners you; sliding sideways
            // as you give ground keeps the whole arena available.
            UpdateOrbitDirection(dir);
            Vector2 tangent = new Vector2(-dir.y, dir.x) * _orbitSign;
            // Circling costs retreat speed (the two are summed then clamped), so back
            // off the circling while something is inside our minimum range — opening
            // that gap is the whole job at that moment.
            bool crowded = d < minSafeRange * 1.15f;
            float orbit = State == MoveState.Throw || crowded ? orbitWeight * 0.35f : orbitWeight;

            Vector2 desired = dir * radial + tangent * (moveSpeed * orbit);

            // Sliding, not shoving. Adding a push away from the wall is not enough:
            // while a monster crowds you from the open side, "back away" still points
            // into the wall and the two mostly cancel, so you grind along it at a
            // crawl and stay cornered — which is exactly what being trapped on the
            // left of the arena looked like. Project the into-wall component OUT of
            // the desired velocity first, so what is left runs parallel to the wall,
            // and only then add a gentle push off it.
            Vector2 open = OpenDirection();
            if (open.sqrMagnitude > 0.0004f)
            {
                Vector2 normal = open.normalized;
                float into = Vector2.Dot(desired, normal);
                if (into < 0f) desired -= normal * into;
                desired += normal * (moveSpeed * Mathf.Clamp01(open.magnitude) * 0.8f);
            }

            return Vector2.ClampMagnitude(desired, moveSpeed);
        }

        /// <summary>
        /// Pick which way round to circle: away from whichever wall is closest, and
        /// otherwise flipping every few seconds so the fighter does not wear a groove
        /// into one side of the arena.
        /// </summary>
        private void UpdateOrbitDirection(Vector2 dir)
        {
            Vector2 tangent = new Vector2(-dir.y, dir.x);
            Vector2 openness = OpenDirection();

            if (openness != Vector2.zero)
            {
                // Circle the way that heads into open space.
                float along = Vector2.Dot(tangent, openness);
                if (Mathf.Abs(along) > 0.25f)
                {
                    _orbitSign = Mathf.Sign(along);
                    _nextOrbitFlip = Time.time + orbitFlipSeconds;
                    return;
                }
            }

            if (Time.time >= _nextOrbitFlip)
            {
                _orbitSign = -_orbitSign;
                _nextOrbitFlip = Time.time + orbitFlipSeconds;
            }
        }

        /// <summary>
        /// Vector pointing away from whichever walls we are crowding, 0 when clear.
        /// The vertical margin is capped against the arena's own half-height — the
        /// band is much shorter than it is wide, and a full-width margin there would
        /// pin the fighter to the centre line instead of letting them use it.
        /// </summary>
        private Vector2 OpenDirection()
        {
            if (_arenaHalf.x <= 0f || _arenaHalf.y <= 0f) return Vector2.zero;

            float marginX = Mathf.Min(wallMargin, _arenaHalf.x * 0.45f);
            float marginY = Mathf.Min(wallMargin, _arenaHalf.y * 0.45f);

            Vector2 p = _rb.position;
            Vector2 open = Vector2.zero;

            float leftGap = p.x + _arenaHalf.x;
            float rightGap = _arenaHalf.x - p.x;
            if (leftGap < marginX) open.x += 1f - leftGap / marginX;
            if (rightGap < marginX) open.x -= 1f - rightGap / marginX;

            float bottomGap = p.y + _arenaHalf.y;
            float topGap = _arenaHalf.y - p.y;
            if (bottomGap < marginY) open.y += 1f - bottomGap / marginY;
            if (topGap < marginY) open.y -= 1f - topGap / marginY;

            return open;
        }

        private void ResolveRangesFromLoadout()
        {
            if (loadout == null || loadout.Slots == null) return;

            float ideal = 0f, minSafe = float.MaxValue, max = 0f;
            int n = 0;
            foreach (AdventurerLoadout.BombSlot slot in loadout.Slots)
            {
                if (slot.bomb == null) continue;
                ideal += slot.bomb.IdealRange;
                minSafe = Mathf.Min(minSafe, slot.bomb.MinSafeRange);
                max = Mathf.Max(max, slot.bomb.MaxRange);
                n++;
            }
            if (n == 0) return;

            idealRange = ideal / n;
            minSafeRange = minSafe;
            maxRange = max;
        }

        private static ICombatant Nearest(IReadOnlyList<ICombatant> list, Vector2 from)
        {
            ICombatant best = null;
            float bestSqr = float.MaxValue;
            for (int i = 0; i < list.Count; i++)
            {
                ICombatant c = list[i];
                if (c == null || !c.IsAlive || c.Team != Team.Monster) continue;
                float sq = ((Vector2)c.Position - from).sqrMagnitude;
                if (sq < bestSqr) { bestSqr = sq; best = c; }
            }
            return best;
        }
    }
}
