using UnityEngine;
using AlchemistsArsenal.Combat;

namespace AlchemistsArsenal.Data
{
    /// <summary>
    /// A hired adventurer class. Bundles the combat tuning the expedition bootstrap
    /// needs to stand one up: HP, movement, elemental affinity, and the bomb loadout
    /// the morning shop filled.
    /// </summary>
    [CreateAssetMenu(fileName = "AdventurerData",
        menuName = "Alchemist's Arsenal/Combat/Adventurer Data", order = 24)]
    public class AdventurerData : ScriptableObject
    {
        [SerializeField] private string adventurerClass = "Rookie";
        [SerializeField] private Sprite characterSprite;
        [SerializeField] private ElementType affinity = ElementType.Nature;

        [Header("Combat")]
        [Min(1)] [SerializeField] private int maxHealth = 100;
        [Min(0f)] [SerializeField] private float moveSpeed = 3.5f;
        [Tooltip("Scales every bomb's cooldown. Ninja < 1, Rookie = 1.")]
        [Min(0.05f)] [SerializeField] private float throwCooldownScale = 1f;
        [Tooltip("Extra blast radius fraction applied by the launcher (Berserker).")]
        [Range(0f, 1f)] [SerializeField] private float splashBonus = 0f;

        [SerializeField] private AdventurerLoadout loadout;

        public string AdventurerClass => string.IsNullOrWhiteSpace(adventurerClass) ? name : adventurerClass;
        public Sprite CharacterSprite => characterSprite;
        public ElementType Affinity => affinity;
        public int MaxHealth => maxHealth;
        public float MoveSpeed => moveSpeed;
        public float ThrowCooldownScale => throwCooldownScale;
        public float SplashBonus => splashBonus;
        public AdventurerLoadout Loadout => loadout;

        public static AdventurerData Create(string className, ElementType affinity, int hp,
            float moveSpeed, AdventurerLoadout loadout, float cooldownScale = 1f, float splashBonus = 0f)
        {
            var a = CreateInstance<AdventurerData>();
            a.name = className;
            a.adventurerClass = className;
            a.affinity = affinity;
            a.maxHealth = hp;
            a.moveSpeed = moveSpeed;
            a.throwCooldownScale = cooldownScale;
            a.splashBonus = splashBonus;
            a.loadout = loadout;
            return a;
        }
    }
}
