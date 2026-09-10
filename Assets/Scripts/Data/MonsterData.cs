using UnityEngine;
using AlchemistsArsenal.Combat;

namespace AlchemistsArsenal.Data
{
    /// <summary>
    /// Static definition for a monster archetype. A biome's <c>BiomeData</c> will
    /// reference a set of these to build its afternoon wave.
    /// </summary>
    [CreateAssetMenu(fileName = "MonsterData",
        menuName = "Alchemist's Arsenal/Combat/Monster Data", order = 21)]
    public class MonsterData : ScriptableObject
    {
        [Header("Identity")]
        [SerializeField] private string displayName = "Monster";
        [SerializeField] private ElementType element = ElementType.Nature;
        [SerializeField] private Sprite sprite;

        [Header("Stats")]
        [Min(1)] [SerializeField] private int maxHealth = 60;
        [Min(0f)] [SerializeField] private float moveSpeed = 2f;
        [Min(0.01f)] [SerializeField] private float mass = 1f;

        [Header("Loot (re-added — reviewer X1)")]
        [Min(0)] [SerializeField] private int goldMin = 3;
        [Min(0)] [SerializeField] private int goldMax = 7;
        [Tooltip("Herb id dropped on death, or empty. Rare drops key diary entries.")]
        [SerializeField] private string herbDropId = "";
        [Range(0f, 1f)] [SerializeField] private float herbDropChance = 0.35f;

        public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? name : displayName;
        public ElementType Element => element;
        public Sprite Sprite => sprite;
        public int MaxHealth => maxHealth;
        public float MoveSpeed => moveSpeed;
        public float Mass => mass;
        public int GoldMin => goldMin;
        public int GoldMax => goldMax;
        public string HerbDropId => herbDropId;
        public float HerbDropChance => herbDropChance;

        /// <summary>Build a monster archetype in code (bootstrap / tests / generators).</summary>
        public static MonsterData Create(string name, ElementType element, int maxHealth, float moveSpeed = 2f,
            int goldMin = 3, int goldMax = 7, string herbDropId = "", float herbDropChance = 0.35f)
        {
            var m = CreateInstance<MonsterData>();
            m.name = name;
            m.displayName = name;
            m.element = element;
            m.maxHealth = maxHealth;
            m.moveSpeed = moveSpeed;
            m.goldMin = goldMin;
            m.goldMax = goldMax;
            m.herbDropId = herbDropId;
            m.herbDropChance = herbDropChance;
            return m;
        }
    }
}
