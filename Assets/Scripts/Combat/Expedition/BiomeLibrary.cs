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

        /// <summary>
        /// What each road asks of the party: fighters out, and the road upgrades owned
        /// (0 none, 1 the first tier, 2 both tiers — see <see cref="UpgradeCatalog.RoadTier"/>).
        /// The first two roads are for one fighter with nothing bought; from the third
        /// on, a road walked alone and bare is lost. Measured, not guessed: the
        /// harness's difficulty curve (HeadlessPlaytestRunner.RunDifficultyCurve)
        /// asserts both halves, and the Evening's economy test checks a good player can
        /// pay for it.
        /// </summary>
        public static readonly int[] FightersNeeded = { 1, 1, 2, 3, 3 };
        public static readonly int[] RoadTierNeeded = { 0, 0, 1, 1, 2 };

        /// <summary>The road's ask, as the biome map says it.</summary>
        public static string Needs(int i)
        {
            i = Mathf.Clamp(i, 0, Count - 1);
            if (FightersNeeded[i] <= 1 && RoadTierNeeded[i] == 0) return "One fighter can hold it, with nothing bought.";
            string kit = RoadTierNeeded[i] >= 2 ? "every road upgrade" : "the first road upgrades";
            return $"Take {FightersNeeded[i]} fighters and {kit}. Alone and bare, it cannot be held.";
        }

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
                    // From here on a road is a real fight: denser waves, tougher bodies
                    // and a harder bite, so one fighter's belt of flasks runs dry before
                    // the road does. The Wraiths are the off-element wave — the Nature
                    // flask that melts the ice only matches them evenly.
                    b.Configure(Names[2], Themes[2], Tints[2], 26f, new[]
                    {
                        BiomeData.MakeWave(MonsterData.Create("Frostkin", ElementType.Water, 105, 2.2f, 5, 10, "Frostmoss", 0.35f, contactDamage: 8), 8, 0.45f, 1f),
                        BiomeData.MakeWave(MonsterData.Create("Rimebeast", ElementType.Water, 180, 1.7f, 7, 13, "", contactDamage: 11), 5, 0.7f, 1.4f),
                        BiomeData.MakeWave(MonsterData.Create("Rime Wraith", ElementType.Arcane, 120, 2.6f, 6, 11, "", contactDamage: 9), 7, 0.45f, 1.4f),
                        BiomeData.MakeWave(MonsterData.Create("Frost Troll", ElementType.Water, 390, 1.3f, 10, 18, "", contactDamage: 15), 3, 1.1f, 1.6f),
                    }, null);
                    break;

                case 3:
                    b.Configure(Names[3], Themes[3], Tints[3], 26f, new[]
                    {
                        BiomeData.MakeWave(MonsterData.Create("Miremaw", ElementType.Poison, 165, 2.2f, 7, 12, "Venom Sac", 0.4f, contactDamage: 10), 8, 0.45f, 1f),
                        // The off-element wave: the Arcane flask that eats the venom only matches bark evenly.
                        BiomeData.MakeWave(MonsterData.Create("Bog Treant", ElementType.Nature, 255, 1.5f, 8, 14, "", contactDamage: 13), 5, 0.8f, 1.5f),
                        BiomeData.MakeWave(MonsterData.Create("Mire Leech", ElementType.Poison, 120, 2.8f, 5, 9, "", contactDamage: 9), 10, 0.3f, 1.4f),
                        BiomeData.MakeWave(MonsterData.Create("Bog Brute", ElementType.Poison, 630, 1.2f, 14, 24, "", contactDamage: 18), 3, 1.2f, 1.6f),
                        BiomeData.MakeWave(MonsterData.Create("Blight Maw", ElementType.Poison, 195, 2.3f, 8, 14, "", contactDamage: 12), 8, 0.4f, 1.4f),
                    }, null);
                    break;

                default: // 4 — The Coven's Peak
                    b.Configure(Names[4], Themes[4], Tints[4], 28f, new[]
                    {
                        BiomeData.MakeWave(MonsterData.Create("Coven Acolyte", ElementType.Arcane, 90, 2.4f, 10, 18, "", contactDamage: 12), 6, 0.45f, 1f),
                        BiomeData.MakeWave(MonsterData.Create("Warded Effigy", ElementType.Poison, 180, 1.5f, 12, 22, "Coven Sigil", 0.5f, contactDamage: 16), 4, 0.8f, 1.6f),
                        BiomeData.MakeWave(MonsterData.Create("Coven Hexling", ElementType.Arcane, 70, 3.0f, 8, 14, "", contactDamage: 10), 8, 0.3f, 1.4f),
                        BiomeData.MakeWave(MonsterData.Create("Coven Warden", ElementType.Arcane, 300, 1.3f, 16, 28, "", contactDamage: 20), 2, 1.2f, 1.6f),
                    }, DefaultExpeditionData.Matriarch());
                    break;
            }

            return b;
        }
    }
}
