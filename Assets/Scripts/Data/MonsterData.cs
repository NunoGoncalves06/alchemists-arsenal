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

        public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? name : displayName;
        public ElementType Element => element;
        public Sprite Sprite => sprite;
        public int MaxHealth => maxHealth;
        public float MoveSpeed => moveSpeed;
        public float Mass => mass;
    }
}
