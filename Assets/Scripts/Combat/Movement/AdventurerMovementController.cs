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
        [Min(0f)] [SerializeField] private float moveSpeed = 3.5f;
        [Min(0f)] [SerializeField] private float steerAccel = 18f;
        [Min(0f)] [SerializeField] private float maxSteerForce = 28f;

        [Header("Spacing (auto-filled from the loadout if one is set)")]
        [SerializeField] private AdventurerLoadout loadout;
        [Min(0f)] [SerializeField] private float idealRange = 6f;
        [Min(0f)] [SerializeField] private float minSafeRange = 2.5f;
        [Min(0f)] [SerializeField] private float maxRange = 11f;
        [Tooltip("Distance from idealRange within which the adventurer holds and throws.")]
        [Min(0.1f)] [SerializeField] private float throwTolerance = 2.5f;

        [Header("Retreat")]
        [Range(0f, 1f)] [SerializeField] private float retreatHealthFraction = 0.3f;
        [SerializeField] private bool logStateChanges;

        public MoveState State { get; private set; } = MoveState.Approach;

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
            ICombatant target = Nearest(MonsterRegistry.ActiveMonsters, _rb.position);

            MoveState next = DecideState(target);
            if (next != State)
            {
                if (logStateChanges) Debug.Log($"[Move] {name}: {State} → {next}");
                State = next;
            }

            Vector2 desired = DesiredVelocity(target);
            Vector2 steer = Vector2.ClampMagnitude((desired - _rb.linearVelocity) * steerAccel, maxSteerForce);
            _rb.AddForce(steer, ForceMode2D.Force);
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
            if (Mathf.Abs(d - idealRange) <= throwTolerance) return MoveState.Throw;
            return MoveState.Reposition;
        }

        private Vector2 DesiredVelocity(ICombatant target)
        {
            if (target == null) return Vector2.zero;

            Vector2 toTarget = target.Position - _rb.position;
            float d = toTarget.magnitude;
            Vector2 dir = d > 0.001f ? toTarget / d : Vector2.right;

            switch (State)
            {
                case MoveState.Retreat:
                    return -dir * moveSpeed;

                case MoveState.Throw:
                    return Vector2.zero; // brake and hold the firing line

                // Approach + Reposition: seek the ideal-range shell. `gap` is
                // positive when too far (move in), negative when too close (back off).
                // Inside minSafeRange, back off at full speed.
                default:
                    float gap = d - idealRange;
                    if (d < minSafeRange) return -dir * moveSpeed;
                    float speed = Mathf.Clamp(gap, -moveSpeed, moveSpeed);
                    return dir * speed;
            }
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
