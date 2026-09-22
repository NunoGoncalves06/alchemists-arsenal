using System;
using UnityEngine;
using AlchemistsArsenal.Data;
using AlchemistsArsenal.PhysicsKit;

namespace AlchemistsArsenal.Combat
{
    /// <summary>
    /// Physics resolver for boss attacks. <see cref="BossPhaseManager"/> requests a
    /// strike; the actual <c>OverlapCircleAll</c> + impulse is queued and executed
    /// in <see cref="FixedUpdate"/> so all physics stays on the physics step.
    /// </summary>
    public class BossAttackExecutor : MonoBehaviour
    {
        [Tooltip("Leave 0 to auto-resolve to the project's combat layers (see CombatLayers).")]
        [SerializeField] private LayerMask targetMask = 0;
        [SerializeField] private ElementalMatrix elementalMatrix;

        public void Configure(ElementalMatrix matrix, LayerMask mask = default)
        {
            elementalMatrix = matrix;
            if (mask.value != 0) targetMask = mask;
        }

        private readonly Collider2D[] _hits = new Collider2D[16];

        /// <summary>A strike landed: pattern, where, and how many adventurers it caught.</summary>
        public event Action<BossAttackPattern, Vector2, int> OnStrike;

        private bool _pending;
        private BossAttackPattern _pattern;
        private Vector2 _impact;
        private ICombatant _source;

        /// <summary>Queue a strike aimed at <paramref name="targetPos"/> (clamped to the pattern's range).</summary>
        public void Execute(BossAttackPattern pattern, Vector2 origin, Vector2 targetPos, ICombatant source)
        {
            if (pattern == null) return;

            Vector2 dir = targetPos - origin;
            float reach = Mathf.Min(dir.magnitude, pattern.Range);
            Vector2 aim = dir.sqrMagnitude > 0.0001f ? dir.normalized : Vector2.right;

            _pattern = pattern;
            _impact = origin + aim * reach;
            _source = source;
            _pending = true;
        }

        private void FixedUpdate()
        {
            if (!_pending) return;
            _pending = false;

            BossAttackPattern pattern = _pattern;
            Vector2 epicenter = _impact;

            int n = Physics2D.OverlapCircle(epicenter, pattern.AreaRadius,
                PhysicsQuery.Solid(CombatLayers.Effective(targetMask)), _hits);
            int caught = 0;
            Vector2 fromBoss = _source != null ? epicenter - _source.Position : Vector2.left;
            for (int i = 0; i < n; i++)
            {
                Collider2D hit = _hits[i];
                if (hit == null) continue;

                IDamageable damageable = hit.GetComponentInParent<IDamageable>();
                ICombatant combatant = hit.GetComponentInParent<ICombatant>();
                if (damageable == null || combatant == null || combatant.Team != Team.Adventurer) continue;

                float mult = elementalMatrix != null
                    ? elementalMatrix.GetMultiplier(pattern.Element, combatant.Element)
                    : 1f;
                int finalDamage = Mathf.Max(0, Mathf.RoundToInt(pattern.Damage * mult));
                damageable.ApplyDamage(new DamageInfo(finalDamage, pattern.Element, epicenter, _source));
                caught++;

                Rigidbody2D rb = hit.attachedRigidbody;
                if (rb != null)
                    RadialImpulse.Apply(rb, epicenter, pattern.AreaRadius, pattern.Knockback, fromBoss);
            }
            OnStrike?.Invoke(pattern, epicenter, caught);
        }

        private void OnDrawGizmosSelected()
        {
            if (_pattern == null) return;
            Gizmos.color = new Color(0.6f, 0.2f, 0.9f, 0.6f);
            Gizmos.DrawWireSphere(_impact, _pattern.AreaRadius);
        }
    }
}
