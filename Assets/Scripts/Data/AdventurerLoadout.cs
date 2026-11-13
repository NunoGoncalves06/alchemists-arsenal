using System;
using System.Collections.Generic;
using UnityEngine;

namespace AlchemistsArsenal.Data
{
    /// <summary>
    /// The set of bombs an adventurer walks into a biome carrying, with a starting
    /// count for each. The morning shop fills this; the afternoon controller reads
    /// it (and tracks its own runtime ammo copy — this asset is never mutated at play time).
    /// </summary>
    [CreateAssetMenu(fileName = "AdventurerLoadout",
        menuName = "Alchemist's Arsenal/Combat/Adventurer Loadout", order = 23)]
    public class AdventurerLoadout : ScriptableObject
    {
        [Serializable]
        public struct BombSlot
        {
            public BombData bomb;
            [Min(0)] public int count;
        }

        [SerializeField] private BombSlot[] slots = new BombSlot[0];

        [Tooltip("How well the morning went for THIS flask (0..1). Scales damage and gates the elemental bonus.")]
        [Range(0f, 1f)] [SerializeField] private float potionQuality01 = 1f;

        public IReadOnlyList<BombSlot> Slots => slots;

        /// <summary>
        /// The 0..1 quality of the brew in these flasks. Per-loadout rather than
        /// read globally off CraftingManager, so heroes carrying different brews
        /// each fight at their own quality.
        /// </summary>
        public float PotionQuality01 => potionQuality01;

        public void SetPotionQuality(float quality01) =>
            potionQuality01 = Mathf.Clamp01(quality01);

        /// <summary>Build a loadout in code (bootstrap / tests / generators).</summary>
        public void SetSlots(params BombSlot[] newSlots) => slots = newSlots ?? new BombSlot[0];

        public static AdventurerLoadout Create(params BombSlot[] slots)
        {
            var lo = CreateInstance<AdventurerLoadout>();
            lo.SetSlots(slots);
            return lo;
        }

        public static BombSlot Slot(BombData bomb, int count) => new BombSlot { bomb = bomb, count = count };
    }
}
