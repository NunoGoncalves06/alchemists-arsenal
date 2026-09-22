using UnityEngine;
using AlchemistsArsenal.Combat;

namespace AlchemistsArsenal.Data
{
    /// <summary>How a boss attack reaches the party.</summary>
    public enum BossAttackShape
    {
        /// <summary>A blow that lands on the spot marked at the start of the windup.</summary>
        Strike,
        /// <summary>A ring that bursts outward from the boss itself: get away from it.</summary>
        Shockwave,
        /// <summary>A spread of lobbed projectiles that fall on marked spots around the target.</summary>
        Volley,
    }

    /// <summary>
    /// One boss attack. Pure data — the boss HFSM sequences windup → strike →
    /// recovery, and <c>BossAttackExecutor</c> resolves the physics.
    /// </summary>
    [CreateAssetMenu(fileName = "BossAttackPattern",
        menuName = "Alchemist's Arsenal/Combat/Boss Attack Pattern", order = 27)]
    public class BossAttackPattern : ScriptableObject
    {
        [SerializeField] private string displayName = "Attack";
        [SerializeField] private ElementType element = ElementType.Arcane;
        [SerializeField] private BossAttackShape shape = BossAttackShape.Strike;

        [Header("Effect")]
        [Min(0)] [SerializeField] private int damage = 12;
        [Min(0f)] [SerializeField] private float range = 5f;
        [Min(0f)] [SerializeField] private float areaRadius = 1.6f;
        [Min(0f)] [SerializeField] private float knockback = 6f;

        [Header("Volley")]
        [Tooltip("Projectiles per volley.")]
        [Min(1)] [SerializeField] private int count = 1;
        [Tooltip("How far apart the landing spots are spread around the target.")]
        [Min(0f)] [SerializeField] private float spread = 1.6f;
        [Min(1f)] [SerializeField] private float projectileSpeed = 11f;
        [Tooltip("Lob high (slow to land, easy to see coming) instead of throwing flat and fast.")]
        [SerializeField] private bool lob;

        [Header("Timing")]
        [Min(0f)] [SerializeField] private float windupSeconds = 0.6f;
        [Min(0f)] [SerializeField] private float recoverySeconds = 0.8f;
        [Min(0f)] [SerializeField] private float cooldownSeconds = 2f;

        public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? name : displayName;
        public ElementType Element => element;
        public BossAttackShape Shape => shape;
        public int Count => Mathf.Max(1, count);
        public float Spread => spread;
        public float ProjectileSpeed => projectileSpeed;
        public bool Lob => lob;
        public int Damage => damage;
        public float Range => range;
        public float AreaRadius => areaRadius;
        public float Knockback => knockback;
        public float WindupSeconds => windupSeconds;
        public float RecoverySeconds => recoverySeconds;
        public float CooldownSeconds => cooldownSeconds;

        public static BossAttackPattern Create(string name, ElementType element, int damage,
            float range, float areaRadius, float windup, float recovery, float cooldown, float knockback)
        {
            var a = CreateInstance<BossAttackPattern>();
            a.name = name;
            a.displayName = name;
            a.element = element;
            a.damage = damage;
            a.range = range;
            a.areaRadius = areaRadius;
            a.windupSeconds = windup;
            a.recoverySeconds = recovery;
            a.cooldownSeconds = cooldown;
            a.knockback = knockback;
            return a;
        }

        /// <summary>A shaped attack: a shockwave (range is ignored; it bursts from the boss) or a volley.</summary>
        public static BossAttackPattern Create(string name, BossAttackShape shape, ElementType element, int damage,
            float range, float areaRadius, float windup, float recovery, float cooldown, float knockback,
            int count = 1, float spread = 1.6f, float projectileSpeed = 11f, bool lob = false)
        {
            var a = Create(name, element, damage, range, areaRadius, windup, recovery, cooldown, knockback);
            a.shape = shape;
            a.count = Mathf.Max(1, count);
            a.spread = spread;
            a.projectileSpeed = projectileSpeed;
            a.lob = lob;
            return a;
        }
    }
}
