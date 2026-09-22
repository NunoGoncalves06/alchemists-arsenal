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

        // One BiomeData per index, built on first request (reviewer P4 — CreateInstance
        // SOs are not GC'd, so callers must not rebuild them every frame).
        private static readonly BiomeData[] _cache = new BiomeData[Count];

        public static BiomeData Get(int index)
        {
            index = Mathf.Clamp(index, 0, Count - 1);
            if (_cache[index] != null) return _cache[index];
            var b = ScriptableObject.CreateInstance<BiomeData>();
            _cache[index] = b;

            switch (index)
            {
                case 0:
                    // Wave 1 was 3x Treant landing almost together (0.7s apart) —
                    // against a lone adventurer that's an instant 3-way pile-on before
                    // the AI/movement even gets a foothold (playtest: near-instant
                    // losses on the very first fight). 2x, spread further apart, gives
                    // the opening fight room to actually be fought.
                    b.Configure(Names[0], Themes[0], Tints[0], 24f, new[]
                    {
                        BiomeData.MakeWave(MonsterData.Create("Bark Treant", ElementType.Nature, 55, 1.5f, 3, 6, "Emberleaf", 0.4f), 2, 1.1f, 1f),
                        // Wave 2's 4 Thornlings (2.6 move speed — the fastest early
                        // monster) landing 0.5s apart was the actual killer once wave 1
                        // stopped being one (playtest: consistently 2-for-2 on wave 1,
                        // then dead a few seconds into wave 2). 3x, spread further apart.
                        BiomeData.MakeWave(MonsterData.Create("Thornling", ElementType.Nature, 34, 2.6f, 2, 5, ""), 3, 0.8f, 1.4f),
                        // The off-element wave: the biome reads as Nature, so the
                        // Counter steers you to a Fire flask, which Water halves. That is
                        // the intended lesson — one flask cannot cover a whole road.
                        BiomeData.MakeWave(MonsterData.Create("Mossback", ElementType.Water, 46, 1.9f, 4, 8, "Frostmoss", 0.3f), 3, 0.6f, 1.4f),
                    }, DefaultExpeditionData.Woodwose());
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
                    }, DefaultExpeditionData.Matriarch());
                    break;
            }

            return b;
        }
    }
}
