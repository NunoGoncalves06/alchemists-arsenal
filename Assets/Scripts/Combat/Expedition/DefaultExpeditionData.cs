using System.Collections.Generic;
using UnityEngine;
using AlchemistsArsenal.Combat.Considerations;
using AlchemistsArsenal.Data;

namespace AlchemistsArsenal.Combat
{
    /// <summary>
    /// Code-built default data for a self-contained expedition run — used by
    /// <see cref="ExpeditionBootstrap"/> when no authored assets are assigned.
    /// Kept out of the bootstrap so that file stays pure scene assembly + HUD, and
    /// so there is one obvious place to tweak the fallback tuning.
    /// </summary>
    public static class DefaultExpeditionData
    {
        // ---------------------------------------------------------------- matrix

        public static ElementalMatrix Matrix()
        {
            var m = ScriptableObject.CreateInstance<ElementalMatrix>();
            // Wheel: Fire > Nature > Water > Fire; plus Poison > Nature, Arcane > Poison.
            m.SetMultiplier(ElementType.Fire, ElementType.Nature, 2f);
            m.SetMultiplier(ElementType.Nature, ElementType.Fire, 0.5f);
            m.SetMultiplier(ElementType.Nature, ElementType.Water, 2f);
            m.SetMultiplier(ElementType.Water, ElementType.Nature, 0.5f);
            m.SetMultiplier(ElementType.Water, ElementType.Fire, 2f);
            m.SetMultiplier(ElementType.Fire, ElementType.Water, 0.5f);
            m.SetMultiplier(ElementType.Poison, ElementType.Nature, 2f);
            m.SetMultiplier(ElementType.Arcane, ElementType.Poison, 2f);
            return m;
        }

        // ---------------------------------------------------------------- loadout

        public static AdventurerLoadout Loadout() => AdventurerLoadout.Create(
            AdventurerLoadout.Slot(BombData.Create("Firebloom Flask", ElementType.Fire, 24, 2.6f, 13f, 6f, 2f, 13f, 1.4f), 6),
            AdventurerLoadout.Slot(BombData.Create("Tidevial", ElementType.Water, 22, 2.4f, 13f, 6f, 2f, 13f, 1.4f), 6),
            AdventurerLoadout.Slot(BombData.Create("Thornburst", ElementType.Nature, 20, 3.0f, 12f, 5f, 2f, 12f, 1.6f), 6),
            AdventurerLoadout.Slot(BombData.Create("Miremist Phial", ElementType.Poison, 16, 3.4f, 14f, 7f, 2f, 15f, 2f), 4));

        // ------------------------------------------------------ adventurer AI axes

        public static List<UtilityConsideration> AdventurerConsiderations()
        {
            var elemental = ScriptableObject.CreateInstance<ElementalVulnerabilityConsideration>();
            elemental.Configure(AnimationCurve.Linear(0f, 0f, 1f, 1f), 1.2f, "Favour the elemental matchup.");

            var distance = ScriptableObject.CreateInstance<DistanceConsideration>();
            distance.Configure(new AnimationCurve(
                new Keyframe(0f, 0f), new Keyframe(0.5f, 1f), new Keyframe(1f, 0.15f)), 1f, "Prefer the ideal band.");

            var health = ScriptableObject.CreateInstance<SelfHealthConsideration>();
            health.Configure(AnimationCurve.EaseInOut(0f, 0.35f, 1f, 1f), 0.5f, "Slight bias to act while healthy.");

            var ward = ScriptableObject.CreateInstance<WardAvoidanceConsideration>();
            ward.Configure(AnimationCurve.Linear(0f, 0f, 1f, 1f), 1.6f, "Avoid the boss's warded element.");

            return new List<UtilityConsideration> { elemental, distance, health, ward };
        }

        // ---------------------------------------------------------------- biome

        public static BiomeData Biome(BossDefinition bossOverride = null)
        {
            var waves = new[]
            {
                BiomeData.MakeWave(MonsterData.Create("Bark Treant", ElementType.Nature, 60, 1.6f), 3, 0.7f, 1f),
                BiomeData.MakeWave(MonsterData.Create("Emberling", ElementType.Fire, 40, 2.8f), 4, 0.5f, 1.5f),
                BiomeData.MakeWave(MonsterData.Create("Frostkin", ElementType.Water, 50, 2.1f), 3, 0.6f, 1.5f),
            };

            var b = ScriptableObject.CreateInstance<BiomeData>();
            b.Configure("Venom Swamp", ElementType.Nature, new Color(0.12f, 0.17f, 0.13f), 26f,
                waves, bossOverride != null ? bossOverride : Boss());
            return b;
        }

        // ---------------------------------------------------------------- boss

        public static BossDefinition Boss() => BuildBoss("The Coven Matriarch", ElementType.Arcane, 520);

        /// <summary>Same HFSM scaffold (Neutral/Enraged/ElementalWard/Recovering), re-themed
        /// and re-scaled — used by <c>BiomeLibrary</c> for each biome's boss.</summary>
        public static BossDefinition BuildBoss(string displayName, ElementType core, int hp)
        {
            var threat = ScriptableObject.CreateInstance<ElementalThreatProfile>();
            threat.SetEntries(new[]
            {
                ThreatEntry(ElementType.Fire,   8f, 45f, 90f, ElementType.Water),
                ThreatEntry(ElementType.Water,  8f, 45f, 90f, ElementType.Nature),
                ThreatEntry(ElementType.Nature, 8f, 45f, 90f, ElementType.Fire),
                ThreatEntry(ElementType.Poison, 7f, 40f, 85f, ElementType.Arcane),
            });

            var bolt = BossAttackPattern.Create("Arcane Bolt", ElementType.Arcane, 10, 8f, 1.4f, 0.5f, 0.7f, 1.6f, 5f);
            var slam = BossAttackPattern.Create("Coven Slam", ElementType.Nature, 18, 3.5f, 2.4f, 0.8f, 1.0f, 2.6f, 9f);
            var pulse = BossAttackPattern.Create("Ward Pulse", ElementType.Water, 8, 4f, 2.0f, 0.4f, 0.6f, 1.4f, 7f);

            AnimationCurve rising = AnimationCurve.EaseInOut(0f, 0.05f, 1f, 1f);
            AnimationCurve falling = AnimationCurve.EaseInOut(0f, 1f, 1f, 0.05f);
            AnimationCurve fallSteep = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(0.35f, 0.15f), new Keyframe(1f, 0f));

            var phases = new[]
            {
                BossPhaseData.Create(BossPhase.Neutral, 2.5f,
                    new[] { MakeConsideration<BossHealthConsideration>(rising, 1f), MakeConsideration<ElementPressureConsideration>(falling, 0.8f) },
                    new[] { bolt }),
                BossPhaseData.Create(BossPhase.Enraged, 3f,
                    new[] { MakeConsideration<BossHealthConsideration>(falling, 1.1f), MakeConsideration<ElementPressureConsideration>(rising, 0.6f) },
                    new[] { slam, bolt }),
                BossPhaseData.Create(BossPhase.ElementalWard, 1f,
                    new[] { MakeConsideration<ElementThreatOverrideConsideration>(AnimationCurve.Linear(0f, 0f, 1f, 1f), 3f) },
                    new[] { pulse }),
                BossPhaseData.Create(BossPhase.Recovering, 1f,
                    new[] { MakeConsideration<RecentDamageConsideration>(rising, 1.2f), MakeConsideration<BossHealthConsideration>(fallSteep, 1f) },
                    new BossAttackPattern[0]),
            };

            var def = ScriptableObject.CreateInstance<BossDefinition>();
            def.Configure(displayName, core, Mathf.Max(1, hp), threat, phases);
            return def;
        }

        // ---------------------------------------------------------------- helpers

        private static BossConsideration MakeConsideration<T>(AnimationCurve curve, float weight) where T : BossConsideration
        {
            var c = ScriptableObject.CreateInstance<T>();
            c.Configure(curve, weight);
            return c;
        }

        private static ElementalThreatProfile.Entry ThreatEntry(
            ElementType e, float decay, float soft, float hard, ElementType ward) =>
            new ElementalThreatProfile.Entry
            {
                element = e, decayPerSecond = decay, softThreshold = soft, hardThreshold = hard, counterWard = ward
            };
    }
}
