using UnityEngine;
using AlchemistsArsenal.Combat;

namespace AlchemistsArsenal.Data
{
    /// <summary>
    /// One leaf on the Prep bench. A ScriptableObject like the rest of the game's
    /// definitions (BombData, MonsterData, BiomeData), so the physical properties a
    /// leaf has on the bench (how heavy, how bouncy) sit next to what it does in a
    /// recipe. The bench rolls its daily stock as runtime instances; an authored
    /// asset of this type works the same way.
    /// </summary>
    [CreateAssetMenu(fileName = "Herb", menuName = "Alchemist's Arsenal/Crafting/Herb", order = 10)]
    public class HerbData : ScriptableObject
    {
        [SerializeField] private string displayName = "Leaf";
        [SerializeField] private ElementType element = ElementType.Nature;
        [Range(1, 3)] [SerializeField] private int potency = 2;
        [Tooltip("Wilted leaves pay half. They also look it: darker, and they droop.")]
        [SerializeField] private bool wilted;
        [Tooltip("Body mass on the bench. Potent roots are denser than a dry leaf.")]
        [Min(0.05f)] [SerializeField] private float mass = 0.3f;

        public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? name : displayName;
        public ElementType Element => element;
        public int Potency => potency;
        public bool Wilted => wilted;
        public float Mass => mass;

        public static HerbData Create(string displayName, ElementType element, int potency, bool wilted)
        {
            var h = CreateInstance<HerbData>();
            h.name = displayName;
            h.displayName = displayName;
            h.element = element;
            h.potency = Mathf.Clamp(potency, 1, 3);
            h.wilted = wilted;
            h.mass = 0.22f + 0.06f * h.potency;
            return h;
        }

        public static string NameFor(ElementType e, int variant) => e switch
        {
            ElementType.Nature => variant == 0 ? "Bark Shaving" : "Moss Cap",
            ElementType.Fire => variant == 0 ? "Ember Root" : "Cinder Pod",
            ElementType.Water => variant == 0 ? "Frost Lily" : "Deepwater Kelp",
            ElementType.Poison => variant == 0 ? "Bog Spore" : "Viper Leaf",
            _ => variant == 0 ? "Star Anise" : "Hexbloom",
        };
    }
}
