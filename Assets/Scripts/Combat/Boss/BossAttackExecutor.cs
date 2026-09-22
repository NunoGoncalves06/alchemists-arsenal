using System;
using System.Collections.Generic;
using UnityEngine;
using AlchemistsArsenal.Data;
using AlchemistsArsenal.PhysicsKit;

namespace AlchemistsArsenal.Combat
{
    /// <summary>
    /// Physics resolver for boss attacks. <see cref="BossBehaviourRunner"/> marks an
    /// attack at the start of its windup (<see cref="Telegraph"/>) and throws it at
    /// the end (<see cref="Execute"/>); the overlap, the impulses and the projectile
    /// spawns are queued and happen in <see cref="FixedUpdate"/>, on the physics step.
    ///
    /// Three shapes (<see cref="BossAttackShape"/>):
    /// <list type="bullet">
    /// <item><b>Strike</b>: lands on the spot marked at the start of the windup.</item>
    /// <item><b>Shockwave</b>: bursts from where the boss planted itself; the answer
    /// is distance.</item>
    /// <item><b>Volley</b>: <see cref="BossProjectile2D"/>s on real ballistic arcs
    /// (the same <see cref="BallisticSolver"/> the heroes' flasks use), one per
    /// marked spot around the target.</item>
    /// </list>
    /// Every spot is published to <see cref="DangerZones"/> for as long as it is
    /// live, and to <see cref="OnTelegraph"/> for the decals. This class draws
    /// nothing: the rig (Vfx.BossVisual) listens to the events.
    /// </summary>
    public class BossAttackExecutor : MonoBehaviour
    {
        [Tooltip("Leave 0 to auto-resolve to the project's combat layers (see CombatLayers).")]
        [SerializeField] private LayerMask targetMask = 0;
        [SerializeField] private ElementalMatrix elementalMatrix;
        [Tooltip("Where volleys leave from, relative to the body (world units).")]
        [SerializeField] private Vector2 projectileOrigin = new Vector2(0f, 2.2f);

        /// <summary>Gravity on boss projectiles (a multiple of Physics2D.gravity).</summary>
        public const float ProjectileGravity = 1.3f;

        public void Configure(ElementalMatrix matrix, LayerMask mask = default)
        {
            elementalMatrix = matrix;
            if (mask.value != 0) targetMask = mask;
        }

        public void Configure(ElementalMatrix matrix, Vector2 volleyOrigin)
        {
            Configure(matrix);
            projectileOrigin = volleyOrigin;
        }

        /// <summary>A windup began: the pattern, where the boss stands, every spot it will hit, and the windup.</summary>
        public event Action<BossAttackPattern, Vector2, IReadOnlyList<Vector2>, float> OnTelegraph;

        /// <summary>A windup was abandoned (the boss changed phase mid-swing).</summary>
        public event Action OnTelegraphCancelled;

        /// <summary>The blow is thrown: the pattern and where it leaves from.</summary>
        public event Action<BossAttackPattern, Vector2> OnFire;

        /// <summary>A blow or a projectile landed: pattern, where, and how many adventurers it caught.</summary>
        public event Action<BossAttackPattern, Vector2, int> OnStrike;

        /// <summary>A projectile left the boss (so the rig can dress it).</summary>
        public event Action<BossProjectile2D> OnProjectileLaunched;

        private static readonly Collider2D[] _hits = new Collider2D[16];

        private readonly List<Vector2> _spots = new List<Vector2>();
        private readonly List<Vector2> _firing = new List<Vector2>();
        private bool _pending;
        private BossAttackPattern _pattern;
        private Vector2 _impact;
        private ICombatant _source;

        /// <summary>Where a blow aimed from <paramref name="origin"/> at <paramref name="target"/> will land.</summary>
        public static Vector2 ImpactPoint(BossAttackPattern pattern, Vector2 origin, Vector2 target)
        {
            if (pattern == null) return target;
            if (pattern.Shape == BossAttackShape.Shockwave) return origin;
            Vector2 dir = target - origin;
            float reach = Mathf.Min(dir.magnitude, pattern.Range);
            Vector2 aim = dir.sqrMagnitude > 0.0001f ? dir.normalized : Vector2.left;
            return origin + aim * reach;
        }

        /// <summary>
        /// A volley's landing spots: the first on the aim point, the rest on a ring of
        /// <see cref="BossAttackPattern.Spread"/> around it, so standing still is
        /// never safe and one step out of the middle usually is.
        /// </summary>
        public static void VolleySpots(BossAttackPattern pattern, Vector2 origin, Vector2 aim, List<Vector2> into)
        {
            into.Add(aim);
            int rest = pattern.Count - 1;
            if (rest <= 0) return;
            Vector2 fwd = aim - origin;
            float a0 = fwd.sqrMagnitude > 0.01f ? Mathf.Atan2(fwd.y, fwd.x) : Mathf.PI;
            for (int i = 0; i < rest; i++)
            {
                float a = a0 + (i + 0.5f) / rest * Mathf.PI * 2f;
                into.Add(aim + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * pattern.Spread);
            }
        }

        /// <summary>The launch velocity for a projectile from <paramref name="from"/> to <paramref name="to"/>.</summary>
        public static Vector2 LaunchVelocity(BossAttackPattern pattern, Vector2 from, Vector2 to)
        {
            Vector2 g = Physics2D.gravity * ProjectileGravity;
            float speed = pattern != null ? pattern.ProjectileSpeed : 11f;
            bool high = pattern != null && pattern.Lob;
            return BallisticSolver.TrySolveArc(from, to, speed, g, high, out Vector2 v)
                ? v
                : BallisticSolver.SolveLob(from, to, speed, g);
        }

        /// <summary>Roughly how long that flight takes (for how long the landing spot stays marked).</summary>
        public static float FlightSeconds(BossAttackPattern pattern, Vector2 from, Vector2 to)
        {
            Vector2 v = LaunchVelocity(pattern, from, to);
            if (Mathf.Abs(v.x) > 0.05f) return Mathf.Abs((to.x - from.x) / v.x);
            float g = Mathf.Abs(Physics2D.gravity.y * ProjectileGravity);
            return g > 0.01f ? 2f * Mathf.Abs(v.y) / g : 1f;
        }

        /// <summary>Where this boss's volleys leave from, for a body standing at <paramref name="bodyPos"/>.</summary>
        public Vector2 LaunchPoint(Vector2 bodyPos) => bodyPos + projectileOrigin;

        /// <summary>Start of a windup: mark every spot this attack will hit.</summary>
        public void Telegraph(BossAttackPattern pattern, Vector2 origin, Vector2 aim, float windup)
        {
            if (pattern == null) return;
            _spots.Clear();
            if (pattern.Shape == BossAttackShape.Volley)
            {
                VolleySpots(pattern, origin, aim, _spots);
                Vector2 from = LaunchPoint(origin);
                foreach (Vector2 s in _spots)
                    DangerZones.Mark(s, pattern.AreaRadius, windup + FlightSeconds(pattern, from, s));
            }
            else
            {
                _spots.Add(aim);
                DangerZones.Mark(aim, pattern.AreaRadius, windup);
            }
            OnTelegraph?.Invoke(pattern, origin, _spots, windup);
        }

        public void CancelTelegraph() => OnTelegraphCancelled?.Invoke();

        /// <summary>Queue the blow for the next physics step.</summary>
        public void Execute(BossAttackPattern pattern, Vector2 origin, Vector2 impact, ICombatant source)
        {
            if (pattern == null) return;
            _pattern = pattern;
            _impact = impact;
            _source = source;
            _firing.Clear();
            if (pattern.Shape == BossAttackShape.Volley)
            {
                if (_spots.Count == 0) VolleySpots(pattern, origin, impact, _spots);
                _firing.AddRange(_spots);
            }
            _pending = true;
        }

        private void FixedUpdate()
        {
            if (!_pending) return;
            _pending = false;

            BossAttackPattern pattern = _pattern;
            Vector2 bodyPos = _source != null ? _source.Position : (Vector2)transform.position;

            if (pattern.Shape == BossAttackShape.Volley)
            {
                Vector2 from = LaunchPoint(bodyPos);
                OnFire?.Invoke(pattern, from);
                Transform parent = transform.parent;
                for (int i = 0; i < _firing.Count; i++)
                {
                    var p = BossProjectile2D.Launch(this, pattern, from, _firing[i], _source, elementalMatrix, targetMask, parent);
                    OnProjectileLaunched?.Invoke(p);
                }
                return;
            }

            OnFire?.Invoke(pattern, _impact);
            int caught = Resolve(pattern, _impact, pattern.AreaRadius, _source, elementalMatrix, targetMask);
            OnStrike?.Invoke(pattern, _impact, caught);
        }

        /// <summary>A projectile of ours landed.</summary>
        internal void ReportLanding(BossAttackPattern pattern, Vector2 at, int caught) => OnStrike?.Invoke(pattern, at, caught);

        /// <summary>
        /// Damage and shove every adventurer within <paramref name="radius"/> of
        /// <paramref name="epicenter"/>. Shared by strikes, shockwaves and projectiles.
        /// </summary>
        public static int Resolve(BossAttackPattern pattern, Vector2 epicenter, float radius, ICombatant source,
            ElementalMatrix matrix, LayerMask mask)
        {
            // A projectile can outlive the boss that threw it; a destroyed body is no source.
            if (source is UnityEngine.Object o && o == null) source = null;
            int n = Physics2D.OverlapCircle(epicenter, radius, PhysicsQuery.Solid(CombatLayers.Effective(mask)), _hits);
            int caught = 0;
            Vector2 fromBoss = source != null ? epicenter - source.Position : Vector2.left;
            for (int i = 0; i < n; i++)
            {
                Collider2D hit = _hits[i];
                if (hit == null) continue;

                IDamageable damageable = hit.GetComponentInParent<IDamageable>();
                ICombatant combatant = hit.GetComponentInParent<ICombatant>();
                if (damageable == null || combatant == null || combatant.Team != Team.Adventurer || !combatant.IsAlive) continue;

                float mult = matrix != null ? matrix.GetMultiplier(pattern.Element, combatant.Element) : 1f;
                int finalDamage = Mathf.Max(0, Mathf.RoundToInt(pattern.Damage * mult));
                damageable.ApplyDamage(new DamageInfo(finalDamage, pattern.Element, epicenter, source));
                caught++;

                Rigidbody2D rb = hit.attachedRigidbody;
                if (rb != null)
                    RadialImpulse.Apply(rb, epicenter, radius, pattern.Knockback, fromBoss);
            }
            return caught;
        }

        private void OnDrawGizmosSelected()
        {
            if (_pattern == null) return;
            Gizmos.color = new Color(0.6f, 0.2f, 0.9f, 0.6f);
            foreach (Vector2 s in _spots) Gizmos.DrawWireSphere(s, _pattern.AreaRadius);
        }
    }
}
