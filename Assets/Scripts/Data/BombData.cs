using UnityEngine;
using AlchemistsArsenal.Combat;

namespace AlchemistsArsenal.Data
{
    /// <summary>
    /// A throwable potion-bomb. Pure data — the utility AI scores it and the
    /// physics layer launches it; neither behaviour lives here.
    /// </summary>
    [CreateAssetMenu(fileName = "BombData",
        menuName = "Alchemist's Arsenal/Combat/Bomb Data", order = 20)]
    public class BombData : ScriptableObject
    {
        [Header("Identity")]
        [SerializeField] private string displayName = "Bomb";
        [SerializeField] private ElementType element = ElementType.Fire;
        [SerializeField] private Sprite icon;

        [Header("Damage & Blast")]
        [Min(0)] [SerializeField] private int baseDamage = 20;
        [Min(0f)] [SerializeField] private float blastRadius = 2.5f;

        [Header("Throw Ballistics (consumed by the physics layer)")]
        [Min(0f)] [SerializeField] private float throwSpeed = 12f;
        [Min(0f)] [SerializeField] private float gravityScale = 1f;

        [Header("Range Preference (used by DistanceConsideration)")]
        [Min(0f)] [SerializeField] private float idealRange = 6f;
        [Min(0f)] [SerializeField] private float minSafeRange = 2f;
        [Min(0f)] [SerializeField] private float maxRange = 12f;

        [Header("Cadence")]
        [Min(0f)] [SerializeField] private float cooldownSeconds = 1.5f;

        public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? name : displayName;
        public ElementType Element => element;
        public Sprite Icon => icon;
        public int BaseDamage => baseDamage;
        public float BlastRadius => blastRadius;
        public float ThrowSpeed => throwSpeed;
        public float GravityScale => gravityScale;
        public float IdealRange => idealRange;
        public float MinSafeRange => minSafeRange;
        public float MaxRange => Mathf.Max(maxRange, idealRange);
        public float CooldownSeconds => cooldownSeconds;
    }
}
