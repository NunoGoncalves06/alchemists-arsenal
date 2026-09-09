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

        public IReadOnlyList<BombSlot> Slots => slots;
    }
}
