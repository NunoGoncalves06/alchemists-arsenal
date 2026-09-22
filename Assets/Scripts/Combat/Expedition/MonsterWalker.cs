using System.Collections.Generic;
using UnityEngine;

namespace AlchemistsArsenal.Combat
{
    /// <summary>
    /// Locomotion for monsters and the boss: steer toward the nearest adventurer and
    /// stop at <see cref="stopDistance"/>. Pure <see cref="Rigidbody2D"/> steering
    /// force — no transform writes. Attack logic lives elsewhere (boss:
    /// <see cref="BossPhaseManager"/>; basic monsters: contact damage below).
    /// </summary>
    [RequireComponent(typeof(Rigidbody2D))]
    [RequireComponent(typeof(CombatantBody))]
    public class MonsterWalker : MonoBehaviour
    {
        [SerializeField] private float moveSpeed = 2f;
        [SerializeField] private float stopDistance = 1.1f;
        [SerializeField] private float steerAccel = 12f;
        [SerializeField] private float maxSteerForce = 20f;

        [Header("Contact damage (basic monsters)")]
        [SerializeField] private bool dealsContactDamage = true;
        [SerializeField] private int contactDamage = 6;
        // 0.8 -> 1.1: getting boxed in by 2-3 monsters at once was enough DPS to
        // drop a full-health adventurer before it could retreat or the AI could
        // meaningfully fight back (playtest: near-instant losses).
        [SerializeField] private float contactInterval = 1.1f;

        private Rigidbody2D _rb;
        private CombatantBody _body;
        private float _nextContactTime;

        public void Configure(float speed)
        {
            moveSpeed = speed;
        }

        private void Awake()
        {
            _rb = GetComponent<Rigidbody2D>();
            _body = GetComponent<CombatantBody>();
        }

        private void FixedUpdate()
        {
            if (!_body.IsAlive) return;

            ICombatant target = Nearest(AdventurerRegistry.ActiveAdventurers, _rb.position);
            if (target == null)
            {
                Brake();
                return;
            }

            Vector2 toTarget = target.Position - _rb.position;
            float d = toTarget.magnitude;
            Vector2 dir = d > 0.001f ? toTarget / d : Vector2.zero;

            Vector2 desired = d > stopDistance ? dir * moveSpeed : Vector2.zero;
            // Steering is an acceleration: scaled by mass, a heavier monster walks at the
            // same speed but is harder to knock around.
            Vector2 steer = Vector2.ClampMagnitude((desired - _rb.linearVelocity) * steerAccel, maxSteerForce);
            _rb.AddForce(steer * _rb.mass, ForceMode2D.Force);

            if (dealsContactDamage && d <= stopDistance + 0.2f && Time.time >= _nextContactTime)
            {
                _nextContactTime = Time.time + contactInterval;
                if (target is Component c && c.TryGetComponent(out IDamageable dmg))
                    dmg.ApplyDamage(new DamageInfo(contactDamage, _body.Element, _rb.position, _body));
            }
        }

        private void Brake()
        {
            Vector2 steer = Vector2.ClampMagnitude(-_rb.linearVelocity * steerAccel, maxSteerForce);
            _rb.AddForce(steer * _rb.mass, ForceMode2D.Force);
        }

        private static ICombatant Nearest(IReadOnlyList<ICombatant> list, Vector2 from)
        {
            ICombatant best = null;
            float bestSqr = float.MaxValue;
            for (int i = 0; i < list.Count; i++)
            {
                ICombatant c = list[i];
                if (c == null || !c.IsAlive || c.Team != Team.Adventurer) continue;
                float sq = ((Vector2)c.Position - from).sqrMagnitude;
                if (sq < bestSqr) { bestSqr = sq; best = c; }
            }
            return best;
        }
    }
}
