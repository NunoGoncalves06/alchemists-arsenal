using UnityEngine;
using AlchemistsArsenal.Data;

namespace AlchemistsArsenal.Combat
{
    /// <summary>
    /// The five biomes, code-built (DESIGN.md §2.3). Phase 0 ships biome 0
    /// (Whispering Woods) fully; 1–4 are themed but shallow until Deepening 1.9.
    /// Architecture point (eval-gameplay-progression): every biome is a
    /// <see cref="BiomeData"/> — nothing special-cases an index.
    /// </summary>
    public static class BiomeLibrary
    {
        public const int Count = 5;

        public static readonly string[] Names =
        {
            "Whispering Woods", "Cinder Peaks", "Frostbite Caverns", "Venom Swamp", "The Coven's Peak"
        };

        public static readonly ElementType[] Themes =
        {
            ElementType.Nature, ElementType.Fire, ElementType.Water, ElementType.Poison, ElementType.Arcane
        };

        private static readonly Color[] Tints =
        {
            new Color(0.12f, 0.19f, 0.13f), new Color(0.22f, 0.12f, 0.10f), new Color(0.12f, 0.17f, 0.22f),
            new Color(0.14f, 0.18f, 0.10f), new Color(0.16f, 0.11f, 0.18f)
        };

        public static string Name(int i) => Names[Mathf.Clamp(i, 0, Count - 1)];
        public static ElementType Theme(int i) => Themes[Mathf.Clamp(i, 0, Count - 1)];

        public static BiomeData Get(int index)
        {
            index = Mathf.Clamp(index, 0, Count - 1);
            var b = ScriptableObject.CreateInstance<BiomeData>();

            switch (index)
            {
                case 0:
                    b.Configure(Names[0], Themes[0], Tints[0], 24f, new[]
                    {
                        BiomeData.MakeWave(MonsterData.Create("Bark Treant", ElementType.Nature, 55, 1.5f, 3, 6, "Emberleaf", 0.4f), 3, 0.7f, 1f),
                        BiomeData.MakeWave(MonsterData.Create("Thornling", ElementType.Nature, 34, 2.6f, 2, 5, ""), 4, 0.5f, 1.4f),
                        BiomeData.MakeWave(MonsterData.Create("Mossback", ElementType.Water, 46, 1.9f, 4, 8, "Frostmoss", 0.3f), 3, 0.6f, 1.4f),
                    }, DefaultExpeditionData.BuildBoss("Elder Woodwose", ElementType.Nature, 300));
                    break;

                case 1:
                    b.Configure(Names[1], Themes[1], Tints[1], 26f, new[]
                    {
                        BiomeData.MakeWave(MonsterData.Create("Emberling", ElementType.Fire, 40, 2.8f, 4, 8, "Cinder Heart", 0.15f), 4, 0.5f, 1f),
                        BiomeData.MakeWave(MonsterData.Create("Cinder Hound", ElementType.Fire, 55, 3.2f, 5, 10, ""), 3, 0.6f, 1.5f),
                        BiomeData.MakeWave(MonsterData.Create("Bark Treant", ElementType.Nature, 60, 1.6f, 4, 8, ""), 2, 0.7f, 1.5f),
                    }, null);
                    break;

                case 2:
                    b.Configure(Names[2], Themes[2], Tints[2], 26f, new[]
                    {
                        BiomeData.MakeWave(MonsterData.Create("Frostkin", ElementType.Water, 50, 2.1f, 4, 9, "Frostmoss", 0.35f), 4, 0.55f, 1f),
                        BiomeData.MakeWave(MonsterData.Create("Rimebeast", ElementType.Water, 80, 1.7f, 6, 12, ""), 3, 0.7f, 1.6f),
                    }, null);
                    break;

                case 3:
                    b.Configure(Names[3], Themes[3], Tints[3], 26f, new[]
                    {
                        BiomeData.MakeWave(MonsterData.Create("Miremaw", ElementType.Poison, 44, 2.2f, 4, 9, "Venom Sac", 0.4f), 4, 0.55f, 1f),
                        BiomeData.MakeWave(MonsterData.Create("Bog Treant", ElementType.Nature, 70, 1.6f, 6, 12, ""), 3, 0.7f, 1.6f),
                    }, null);
                    break;

                default: // 4 — The Coven's Peak
                    b.Configure(Names[4], Themes[4], Tints[4], 28f, new[]
                    {
                        BiomeData.MakeWave(MonsterData.Create("Coven Acolyte", ElementType.Arcane, 55, 2.4f, 8, 16, ""), 4, 0.5f, 1f),
                        BiomeData.MakeWave(MonsterData.Create("Warded Effigy", ElementType.Poison, 90, 1.5f, 10, 20, "Coven Sigil", 0.5f), 3, 0.7f, 1.6f),
                    }, DefaultExpeditionData.Boss());
                    break;
            }

            return b;
        }
    }
}
