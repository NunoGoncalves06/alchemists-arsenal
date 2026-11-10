using System;
using UnityEngine;
using AlchemistsArsenal.Data;
using AlchemistsArsenal.PhysicsKit;

namespace AlchemistsArsenal.Combat
{
    /// <summary>
    /// Reference <see cref="ICombatant"/> + <see cref="IDamageable"/> component for
    /// adventurers and monsters. Reads position/velocity straight off its
    /// Rigidbody2D, registers with the right side's registry, and (if a sibling
    /// <see cref="IElementalWardProvider"/> is present) applies elemental damage
    /// reduction while a ward is active.
    /// </summary>
    [RequireComponent(typeof(Rigidbody2D))]
    public class CombatantBody : MonoBehaviour, ICombatant, IDamageable
    {
        [SerializeField] private Team team = Team.Monster;
        [SerializeField] private ElementType element = ElementType.Nature;
        [Min(1)] [SerializeField] private int maxHP = 60;
        [SerializeField] private int currentHP = -1; // -1 => start at maxHP

        [Header("Death")]
        [SerializeField] private bool destroyOnDeath = true;
        [Tooltip("Seconds the corpse lingers (for a death fade) before the GameObject is destroyed.")]
        [Min(0f)] [SerializeField] private float deathLingerSeconds = 1.2f;

        private Rigidbody2D _rb;
        private IElementalWardProvider _ward; // optional

        public int CurrentHP => currentHP;
        public int MaxHP => maxHP;
        public ElementType Element => element;
        public Team Team => team;
        public Vector2 Position => _rb != null ? _rb.position : (Vector2)transform.position;
        public Vector2 Velocity => _rb != null ? _rb.linearVelocity : Vector2.zero;
        public bool IsAlive => currentHP > 0;

        /// <summary>Walked off the road out of flasks: alive, but no longer a fighter or a target.</summary>
        public bool IsWithdrawn { get; private set; }

        public event Action<CombatantBody> OnDied;

        /// <summary>Raised on every applied hit (after mitigation) with the amount actually dealt.</summary>
        public event Action<DamageInfo> OnDamaged;

        /// <summary>Global death feed for telemetry / loot / diary (reviewer X1 — no new
        /// per-instance event needed; this rides the existing death path).</summary>
        public static event Action<CombatantBody> OnAnyDied;

        /// <summary>Global damage feed — the expedition HUD's floating damage numbers.</summary>
        public static event Action<CombatantBody, DamageInfo> OnAnyDamaged;

        /// <summary>Global feed of fighters leaving the road out of flasks (telemetry, HUD).</summary>
        public static event Action<CombatantBody> OnAnyWithdrew;

        private void Awake()
        {
            _rb = GetComponent<Rigidbody2D>();
            _ward = GetComponent<IElementalWardProvider>();
            if (currentHP < 0) currentHP = maxHP;
        }

        private void OnEnable()
        {
            // Every combatant lives on the Combatant layer, wherever it was built
            // (spawner, arena, test scene), so detonations and strikes can query
            // exactly them.
            if (gameObject.layer != GameLayers.Combatant) GameLayers.Assign(gameObject, GameLayers.Combatant);

            if (team == Team.Monster) MonsterRegistry.Register(this);
            else AdventurerRegistry.Register(this);
        }

        private void OnDisable()
        {
            if (team == Team.Monster) MonsterRegistry.Unregister(this);
            else AdventurerRegistry.Unregister(this);
        }

        /// <summary>
        /// Set the combatant's identity at spawn. Call this while the GameObject is
        /// still inactive (before Awake/OnEnable) so registry routing is correct.
        /// </summary>
        public void Initialise(Team team, ElementType element, int maxHp)
        {
            this.team = team;
            this.element = element;
            maxHP = Mathf.Max(1, maxHp);
            currentHP = maxHP;
        }

        /// <summary>How long the body stays after death (a boss has a longer fall than a pest).</summary>
        public void SetDeathLinger(float seconds) => deathLingerSeconds = Mathf.Max(0f, seconds);

        /// <summary>Copy stats from a <see cref="MonsterData"/> definition (spawn-time).</summary>
        public void InitialiseFrom(MonsterData data)
        {
            if (data == null) return;
            Initialise(Team.Monster, data.Element, data.MaxHealth);
        }

        /// <summary>
        /// Leave the road: out of the roster (so nothing targets them), out of every
        /// hit query, the AI and legs switched off, and a walk off the left edge at
        /// <paramref name="exitX"/>, where the body is put away. Never a death.
        /// </summary>
        public void Withdraw(float exitX)
        {
            if (IsWithdrawn || !IsAlive) return;
            IsWithdrawn = true;
            if (team == Team.Monster) MonsterRegistry.Unregister(this);
            else AdventurerRegistry.Unregister(this);
            foreach (var c in GetComponents<Collider2D>()) c.enabled = false;
            foreach (var b in GetComponents<MonoBehaviour>())
                if (b is UtilityAI_CombatController || b is AdventurerMovementController || b is IBombLauncher) b.enabled = false;
            OnAnyWithdrew?.Invoke(this);
            StartCoroutine(WalkOff(exitX));
        }

        private System.Collections.IEnumerator WalkOff(float exitX)
        {
            const float speed = 3.5f;
            if (_rb != null) { _rb.bodyType = RigidbodyType2D.Kinematic; _rb.linearVelocity = new Vector2(-speed, 0f); }
            float cap = 12f;   // whatever the arena, it is gone within this
            while (transform.position.x > exitX && cap > 0f)
            {
                if (_rb == null) transform.position += Vector3.left * speed * Time.deltaTime;
                cap -= Time.deltaTime;
                yield return null;
            }
            if (_rb != null) _rb.linearVelocity = Vector2.zero;
            gameObject.SetActive(false);
        }

        public void ApplyDamage(in DamageInfo info)
        {
            if (!IsAlive || IsWithdrawn) return;

            int amount = Mathf.Max(0, info.Amount);
            if (_ward != null && _ward.HasActiveWard && _ward.WardElement == info.Element)
                amount = Mathf.RoundToInt(amount * Mathf.Clamp01(_ward.WardMultiplier));

            currentHP = Mathf.Max(0, currentHP - amount);

            var applied = new DamageInfo(amount, info.Element, info.SourcePoint, info.Source);
            OnDamaged?.Invoke(applied);
            OnAnyDamaged?.Invoke(this, applied);

            if (currentHP > 0) return;

            // Stay in the roster (with IsAlive == false) until the GameObject is
            // actually gone — consumers all filter on IsAlive, and expedition
            // win/lose needs to see that a spawned side is fully down.
            OnDied?.Invoke(this);
            OnAnyDied?.Invoke(this);
            if (destroyOnDeath) Destroy(gameObject, deathLingerSeconds);
        }
    }
}
