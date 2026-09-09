using System;
using System.Collections.Generic;
using UnityEngine;
using AlchemistsArsenal.Combat;

namespace AlchemistsArsenal.Data
{
    /// <summary>
    /// One afternoon-expedition level. Holds the monster waves, the optional boss,
    /// and light presentation (theme element, ground tint, arena width). The five
    /// biomes: Whispering Woods · Cinder Peaks · Frostbite Caverns · Venom Swamp ·
    /// The Coven's Peak.
    /// </summary>
    [CreateAssetMenu(fileName = "BiomeData",
        menuName = "Alchemist's Arsenal/Biomes/Biome Data", order = 30)]
    public class BiomeData : ScriptableObject
    {
        [Serializable]
        public struct Wave
        {
            public MonsterData monster;
            [Min(1)] public int count;
            [Min(0.05f)] public float spawnInterval;
            [Min(0f)] public float delayBeforeWave;
        }

        [SerializeField] private string biomeName = "Whispering Woods";
        [SerializeField] private ElementType theme = ElementType.Nature;
        [SerializeField] private Color groundTint = new Color(0.15f, 0.22f, 0.16f);
        [Min(8f)] [SerializeField] private float arenaWidth = 26f;

        [SerializeField] private Wave[] waves = new Wave[0];

        [Tooltip("Optional — a final biome carries a BossDefinition; others leave this null.")]
        [SerializeField] private BossDefinition boss;

        public string BiomeName => string.IsNullOrWhiteSpace(biomeName) ? name : biomeName;
        public ElementType Theme => theme;
        public Color GroundTint => groundTint;
        public float ArenaWidth => arenaWidth;
        public IReadOnlyList<Wave> Waves => waves;
        public BossDefinition Boss => boss;
        public bool HasBoss => boss != null;

        public void Configure(string name, ElementType theme, Color groundTint, float arenaWidth,
            Wave[] waves, BossDefinition boss)
        {
            biomeName = name;
            this.theme = theme;
            this.groundTint = groundTint;
            this.arenaWidth = arenaWidth;
            this.waves = waves ?? new Wave[0];
            this.boss = boss;
        }

        public static Wave MakeWave(MonsterData monster, int count, float spawnInterval, float delayBeforeWave = 1f) =>
            new Wave { monster = monster, count = count, spawnInterval = spawnInterval, delayBeforeWave = delayBeforeWave };
    }
}
