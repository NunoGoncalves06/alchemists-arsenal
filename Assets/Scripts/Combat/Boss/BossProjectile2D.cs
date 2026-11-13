using UnityEngine;
using AlchemistsArsenal.Data;
using AlchemistsArsenal.PhysicsKit;

namespace AlchemistsArsenal.Combat
{
    /// <summary>
    /// One thing a boss throws: a thorn, a hex orb. A dynamic body on a ballistic
    /// arc (solved by <see cref="BallisticSolver"/>, flown by gravity), on the
    /// Projectile layer so it only meets combatants. It bursts when it comes down
    /// on its marked spot, or early if it runs into an adventurer on the way;
    /// the boss's own side never sets it off.
    ///
    /// Carries no art: the boss rig dresses it (<see cref="BossAttackExecutor.OnProjectileLaunched"/>).
    /// </summary>
    [RequireComponent(typeof(Rigidbody2D))]
    public class BossProjectile2D : MonoBehaviour
    {
        private const float MaxLifetime = 5f;
        private const float ArriveRadius = 0.35f;

        private Rigidbody2D _rb;
        private BossAttackExecutor _owner;
        private BossAttackPattern _pattern;
        private ICombatant _source;
        private ElementalMatrix _matrix;
        private LayerMask _mask;
        private Vector2 _target;
        private float _spawnTime;
        private bool _queued, _done;

        /// <summary>Point the body along its flight (a thorn), or leave it be (an orb).</summary>
        public bool FaceVelocity { get; set; }

        public BossAttackPattern Pattern => _pattern;
        public Vector2 Target => _target;
        public Rigidbody2D Body => _rb;

        public static BossProjectile2D Launch(BossAttackExecutor owner, BossAttackPattern pattern, Vector2 from, Vector2 to,
            ICombatant source, ElementalMatrix matrix, LayerMask mask, Transform parent)
        {
            var go = new GameObject("BossProjectile_" + (pattern != null ? pattern.DisplayName : "?"));
            go.SetActive(false);
            if (parent != null) go.transform.SetParent(parent, false);
            go.transform.position = from;

            var rb = go.AddComponent<Rigidbody2D>();
            rb.bodyType = RigidbodyType2D.Dynamic;
            rb.gravityScale = BossAttackExecutor.ProjectileGravity;
            rb.interpolation = RigidbodyInterpolation2D.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            var col = go.AddComponent<CircleCollider2D>();
            col.radius = 0.22f;
            col.isTrigger = true;
            GameLayers.Assign(go, GameLayers.Projectile);

            var p = go.AddComponent<BossProjectile2D>();
            p._rb = rb;
            p._owner = owner;
            p._pattern = pattern;
            p._source = source;
            p._matrix = matrix;
            p._mask = mask;
            p._target = to;
            go.SetActive(true);

            p._spawnTime = Time.time;
            rb.linearVelocity = BossAttackExecutor.LaunchVelocity(pattern, from, to);
            return p;
        }

        private void FixedUpdate()
        {
            if (_done) return;
            if (!_queued)
            {
                Vector2 pos = _rb.position;
                float air = Time.time - _spawnTime;
                bool arrived = Vector2.Distance(pos, _target) <= ArriveRadius;
                bool cameDown = air > 0.12f && _rb.linearVelocity.y <= 0f && pos.y <= _target.y + 0.05f;
                if (arrived || cameDown || air >= MaxLifetime) _queued = true;

                if (FaceVelocity && _rb.linearVelocity.sqrMagnitude > 0.01f)
                    _rb.MoveRotation(Mathf.Atan2(_rb.linearVelocity.y, _rb.linearVelocity.x) * Mathf.Rad2Deg);
            }
            if (_queued) Burst();
        }

        private void OnTriggerEnter2D(Collider2D other)
        {
            if (_done || other == null) return;
            ICombatant c = other.GetComponentInParent<ICombatant>();
            if (c != null && c.Team == Team.Adventurer && c.IsAlive) _queued = true;
        }

        private void Burst()
        {
            _done = true;
            Vector2 at = _rb.position;
            int caught = _pattern != null
                ? BossAttackExecutor.Resolve(_pattern, at, _pattern.AreaRadius, _source, _matrix, _mask)
                : 0;
            if (_owner != null) _owner.ReportLanding(_pattern, at, caught);
            Destroy(gameObject);
        }
    }
}
