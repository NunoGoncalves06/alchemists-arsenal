using UnityEngine;
using AlchemistsArsenal.Data;

namespace AlchemistsArsenal.Combat
{
    /// <summary>
    /// Physics resolver for boss attacks. <see cref="BossPhaseManager"/> requests a
    /// strike; the actual <c>OverlapCircleAll</c> + impulse is queued and executed
    /// in <see cref="FixedUpdate"/> so all physics stays on the physics step.
    /// </summary>
    public class BossAttackExecutor : MonoBehaviour
    {
        [SerializeField] private LayerMask targetMask = ~0;
        [SerializeField] private ElementalMatrix elementalMatrix;

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

            Collider2D[] hits = Physics2D.OverlapCircleAll(epicenter, pattern.AreaRadius, targetMask);
            for (int i = 0; i < hits.Length; i++)
            {
                Collider2D hit = hits[i];
                if (hit == null) continue;

                IDamageable damageable = hit.GetComponentInParent<IDamageable>();
                ICombatant combatant = hit.GetComponentInParent<ICombatant>();
                if (damageable == null || combatant == null || combatant.Team != Team.Adventurer) continue;

                float mult = elementalMatrix != null
                    ? elementalMatrix.GetMultiplier(pattern.Element, combatant.Element)
                    : 1f;
                int finalDamage = Mathf.Max(0, Mathf.RoundToInt(pattern.Damage * mult));
                damageable.ApplyDamage(new DamageInfo(finalDamage, pattern.Element, epicenter, _source));

                Rigidbody2D rb = hit.attachedRigidbody;
                if (rb != null)
                {
                    Vector2 toHit = rb.position - epicenter;
                    float dist = toHit.magnitude;
                    float falloff = Mathf.Clamp01(1f - dist / Mathf.Max(pattern.AreaRadius, 0.01f));
                    Vector2 kdir = dist > 0.001f ? toHit / dist : Random.insideUnitCircle.normalized;
                    rb.AddForceAtPosition(kdir * (pattern.Knockback * falloff), epicenter, ForceMode2D.Impulse);
                }
            }
        }

        private void OnDrawGizmosSelected()
        {
            if (_pattern == null) return;
            Gizmos.color = new Color(0.6f, 0.2f, 0.9f, 0.6f);
            Gizmos.DrawWireSphere(_impact, _pattern.AreaRadius);
        }
    }
}
