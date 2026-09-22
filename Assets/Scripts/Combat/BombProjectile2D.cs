using System;
using UnityEngine;
using AlchemistsArsenal.Data;

namespace AlchemistsArsenal.Combat
{
    /// <summary>One detonation, for telemetry + HUD feedback (reviewer X2 — this
    /// lives on the projectile, where the blast actually happens).</summary>
    public readonly struct DetonationInfo
    {
        public readonly Vector2 Position;
        public readonly ElementType Element;
        public readonly PotionGrade Grade;
        public readonly int HitCount;
        public readonly int TotalDamage;
        public readonly bool HadElementalAdvantage;
        public readonly ICombatant Thrower;
        /// <summary>The flask's own name ("Fireblood"), so reports name what was brewed.</summary>
        public readonly string BombName;

        public DetonationInfo(Vector2 pos, ElementType element, PotionGrade grade,
            int hitCount, int totalDamage, bool advantage, ICombatant thrower, string bombName = null)
        {
            Position = pos; Element = element; Grade = grade;
            HitCount = hitCount; TotalDamage = totalDamage;
            HadElementalAdvantage = advantage; Thrower = thrower;
            BombName = string.IsNullOrWhiteSpace(bombName) ? $"{element} Flask" : bombName;
        }
    }

    /// <summary>
    /// A thrown potion-bomb. Flight is 100% Rigidbody2D — initial velocity is set
    /// once from <see cref="BallisticSolver"/>, then gravity does the rest. There is
    /// no per-frame <c>transform</c> movement anywhere in this class.
    ///
    /// Detonation is detected across frames and always executed inside
    /// <see cref="FixedUpdate"/>, never in a collision callback.
    /// </summary>
    [RequireComponent(typeof(Rigidbody2D))]
    public class BombProjectile2D : MonoBehaviour
    {
        [Header("Flight")]
        [SerializeField] private bool preferHighArc = false;
        [SerializeField] private bool faceVelocity = true;

        [Header("Detonation")]
        [SerializeField] private float arriveRadius = 0.35f;
        [SerializeField] private float maxLifetime = 6f;
        [Tooltip("Leave 0 to auto-resolve to the project's combat layers (see CombatLayers).")]
        [SerializeField] private LayerMask detonationMask = 0;

        [Header("Knockback")]
        [SerializeField] private float knockbackImpulse = 9f;

        private Rigidbody2D _rb;
        private BombData _bomb;
        private ElementalMatrix _matrix;
        private ICombatant _thrower;
        private Vector2 _targetPos;
        private float _quality01 = 1f;
        private float _spawnTime;
        private bool _detonateQueued;
        private bool _detonated;
        private float _throwerMultiplier = 1f;

        public bool HasDetonated => _detonated;

        /// <summary>Raised once per bomb, the frame it detonates. Telemetry + HUD listen.</summary>
        public static event Action<DetonationInfo> OnDetonatedGlobal;

        private void Awake()
        {
            _rb = GetComponent<Rigidbody2D>();
            _rb.bodyType = RigidbodyType2D.Dynamic;
        }

        /// <summary>Arm the bomb: sets gravity + the ballistic launch velocity.</summary>
        public void Configure(in BombThrowRequest request, ElementalMatrix matrix, LayerMask mask)
        {
            _bomb = request.Bomb;
            _matrix = matrix;
            _thrower = request.Thrower;
            _targetPos = request.TargetPosition;
            _quality01 = request.PotionQuality01;
            _throwerMultiplier = request.ThrowerDamageMultiplier;
            detonationMask = CombatLayers.Effective(mask);
            _spawnTime = Time.time;

            if (_rb == null) _rb = GetComponent<Rigidbody2D>();
            _rb.gravityScale = _bomb != null ? _bomb.GravityScale : 1f;

            Vector2 origin = _rb.position;
            float speed = _bomb != null ? Mathf.Max(0.1f, _bomb.ThrowSpeed) : 10f;
            Vector2 gravity = Physics2D.gravity * _rb.gravityScale;

            Vector2 launchVelocity =
                BallisticSolver.TrySolveArc(origin, _targetPos, speed, gravity, preferHighArc, out Vector2 arc)
                    ? arc
                    : BallisticSolver.SolveLob(origin, _targetPos, speed, gravity);

            _rb.linearVelocity = launchVelocity;
        }

        private void FixedUpdate()
        {
            if (_detonated) return;

            if (!_detonateQueued)
            {
                Vector2 pos = _rb.position;
                float airTime = Time.time - _spawnTime;

                bool reachedTarget = Vector2.Distance(pos, _targetPos) <= arriveRadius;
                float blastReach = _bomb != null ? _bomb.BlastRadius : arriveRadius;
                bool fellToTarget =
                    airTime > 0.12f &&
                    _rb.linearVelocity.y <= 0f &&
                    pos.y <= _targetPos.y + 0.05f &&
                    Mathf.Abs(pos.x - _targetPos.x) <= Mathf.Max(blastReach, arriveRadius);
                bool expired = airTime >= maxLifetime;

                if (reachedTarget || fellToTarget || expired)
                    _detonateQueued = true;

                if (faceVelocity && _rb.linearVelocity.sqrMagnitude > 0.01f)
                    _rb.MoveRotation(Mathf.Atan2(_rb.linearVelocity.y, _rb.linearVelocity.x) * Mathf.Rad2Deg);
            }

            if (_detonateQueued)
                Detonate();
        }

        // Collision detected here; actual detonation deferred to FixedUpdate.
        private void OnCollisionEnter2D(Collision2D collision) => TryQueueFromContact(collision.collider);
        private void OnTriggerEnter2D(Collider2D other) => TryQueueFromContact(other);

        private void TryQueueFromContact(Collider2D other)
        {
            if (_detonated || other == null) return;
            if (other.attachedRigidbody == _rb) return; // ignore the bomb's own body
            // Ignore the thrower's own side (Team.Adventurer) too — the bomb spawns
            // AT the thrower's position (BallisticBombLauncher fires it from Origin
            // = self.Position), so its trigger collider starts out overlapping the
            // thrower's. Without this it queued detonation on the very first
            // physics step, right at the thrower's feet, before it ever flew toward
            // the target — every throw was a point-blank self-detonation (confirmed
            // by a player report: a bomb visibly leaves the adventurer and vanishes
            // immediately, and the adventurer's own health drops — see Detonate()
            // for why that also damaged them). Team-filtered rather than checking
            // the specific thrower instance, matching BossAttackExecutor's existing
            // pattern (it filters to Team.Adventurer-only targets) — this also means
            // a second adventurer, if one's ever added, can't catch a stray blast.
            ICombatant hitCombatant = other.GetComponentInParent<ICombatant>();
            if (hitCombatant != null && hitCombatant.Team == Team.Adventurer) return;
            _detonateQueued = true;
        }

        private void Detonate()
        {
            _detonated = true;

            Vector2 epicenter = _rb.position;
            float radius = _bomb != null ? _bomb.BlastRadius : 2f;

            PotionGrade grade = CombatQuality.GradeFor01(_quality01);
            float damageMultiplier = CombatQuality.DamageMultiplier(grade);
            bool elementalEnabled = CombatQuality.ElementalBonusEnabled(grade);

            int hitCount = 0;
            int totalDamage = 0;
            bool hadAdvantage = false;

            Collider2D[] hits = Physics2D.OverlapCircleAll(epicenter, radius, detonationMask);
            for (int i = 0; i < hits.Length; i++)
            {
                Collider2D hit = hits[i];
                if (hit == null) continue;

                Rigidbody2D hitRb = hit.attachedRigidbody;
                if (hitRb == _rb) continue; // never the bomb's own body

                IDamageable damageable = hit.GetComponentInParent<IDamageable>();
                ICombatant combatant = hit.GetComponentInParent<ICombatant>();

                // Never the thrower's own side — a bomb doesn't hurt (or knock back)
                // whoever threw it. Epicenter can legitimately still be close to the
                // thrower (a short throw, or the target closed distance before it
                // landed), so this has to be checked here too, not just as the
                // early-detonation guard in TryQueueFromContact above. This was the
                // actual bug behind "no monsters die and my own health drops": with
                // no exclusion at all, and the adventurer's own element (Nature)
                // being exactly what Fire bombs get a x2 bonus against, a
                // self-detonation even LOGGED as a "successful x2 elemental hit".
                if (combatant != null && combatant.Team == Team.Adventurer) continue;

                if (hitRb != null)
                {
                    // Radial impulse, linear inverse-distance falloff to the blast edge.
                    Vector2 toHit = hitRb.position - epicenter;
                    float dist = toHit.magnitude;
                    float falloff = Mathf.Clamp01(1f - dist / Mathf.Max(radius, 0.01f));
                    Vector2 dir = dist > 0.001f ? toHit / dist : UnityEngine.Random.insideUnitCircle.normalized;

                    hitRb.AddForceAtPosition(dir * (knockbackImpulse * falloff), epicenter, ForceMode2D.Impulse);
                }

                if (damageable == null || combatant == null || _bomb == null) continue;

                float elementMultiplier = elementalEnabled && _matrix != null
                    ? _matrix.GetMultiplier(_bomb.Element, combatant.Element)
                    : 1f;
                if (elementMultiplier > 1.01f) hadAdvantage = true;

                int finalDamage = Mathf.Max(0,
                    Mathf.RoundToInt(_bomb.BaseDamage * damageMultiplier * elementMultiplier
                                     * _throwerMultiplier));

                damageable.ApplyDamage(new DamageInfo(finalDamage, _bomb.Element, epicenter, _thrower));
                hitCount++;
                totalDamage += finalDamage;
            }

            if (_bomb != null)
                OnDetonatedGlobal?.Invoke(new DetonationInfo(
                    epicenter, _bomb.Element, grade, hitCount, totalDamage, hadAdvantage, _thrower, _bomb.DisplayName));

            Destroy(gameObject);
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1f, 0.5f, 0.1f, 0.7f);
            Gizmos.DrawWireSphere(transform.position, _bomb != null ? _bomb.BlastRadius : 2f);
        }
    }
}
