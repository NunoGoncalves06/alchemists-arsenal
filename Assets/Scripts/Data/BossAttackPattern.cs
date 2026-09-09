using UnityEngine;
using AlchemistsArsenal.Combat;

namespace AlchemistsArsenal.Data
{
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

        [Header("Effect")]
        [Min(0)] [SerializeField] private int damage = 12;
        [Min(0f)] [SerializeField] private float range = 5f;
        [Min(0f)] [SerializeField] private float areaRadius = 1.6f;
        [Min(0f)] [SerializeField] private float knockback = 6f;

        [Header("Timing")]
        [Min(0f)] [SerializeField] private float windupSeconds = 0.6f;
        [Min(0f)] [SerializeField] private float recoverySeconds = 0.8f;
        [Min(0f)] [SerializeField] private float cooldownSeconds = 2f;

        public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? name : displayName;
        public ElementType Element => element;
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
    }
}
