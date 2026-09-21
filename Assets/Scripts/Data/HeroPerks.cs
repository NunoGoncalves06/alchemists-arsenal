using AlchemistsArsenal.Combat;

namespace AlchemistsArsenal.Data
{
    /// <summary>
    /// How a hero fights. Three archetypes that differ only in <i>where they
    /// stand</i> and <i>what their AI weights</i> — no HP, no damage, no ammo.
    /// Mirrors <see cref="UpgradeCatalog"/>'s plain-static-data style.
    /// </summary>
    public readonly struct HeroArchetype
    {
        public readonly string Id;
        public readonly string DisplayName;
        public readonly string Blurb;

        // Spacing — fed to AdventurerMovementController.Configure.
        public readonly float IdealRange;
        public readonly float MinSafeRange;
        public readonly float MaxRange;
        public readonly float ThrowTolerance;
        public readonly float OrbitWeight;
        public readonly float RetreatHealthFraction;

        // IAUS axis weights — fed to UtilityConsideration.Configure. The defaults
        // they override live in DefaultExpeditionData.AdventurerConsiderations.
        public readonly float ElementalWeight;
        public readonly float DistanceWeight;
        public readonly float SelfHealthWeight;

        public HeroArchetype(string id, string displayName, string blurb,
            float idealRange, float minSafeRange, float maxRange, float throwTolerance,
            float orbitWeight, float retreatHealthFraction,
            float elementalWeight, float distanceWeight, float selfHealthWeight)
        {
            Id = id; DisplayName = displayName; Blurb = blurb;
            IdealRange = idealRange; MinSafeRange = minSafeRange; MaxRange = maxRange;
            ThrowTolerance = throwTolerance; OrbitWeight = orbitWeight;
            RetreatHealthFraction = retreatHealthFraction;
            ElementalWeight = elementalWeight; DistanceWeight = distanceWeight;
            SelfHealthWeight = selfHealthWeight;
        }
    }

    /// <summary>
    /// The perk system. A perk is a pair — <b>(affinity element, archetype)</b> —
    /// and that is the whole of it. There is no per-hero stat table to keep
    /// balanced, because every hero's perk is worth exactly the same:
    ///
    /// <list type="bullet">
    /// <item><b>Attunement:</b> ×<see cref="AttunementMultiplier"/> thrown damage
    /// when the flask in hand matches the hero's element.</item>
    /// <item><b>Ward:</b> ×<see cref="WardMultiplier"/> damage taken from that
    /// same element.</item>
    /// <item><b>Archetype:</b> positioning and AI weights — a sidegrade with no
    /// power delta at all.</item>
    /// </list>
    ///
    /// <para><b>Why this is symmetric by construction.</b> Attunement never
    /// consults the elemental matrix; its condition is "the flask matches me", and
    /// every element is brewable. So a Fire hero with a Fire flask gets precisely
    /// what a Poison hero gets with a Poison flask. The matrix's own asymmetry
    /// (Poison and Arcane are strictly better attackers with no counter) is real,
    /// but it lives in the matrix, which the perk sidesteps — so it never becomes
    /// a disparity <i>between heroes</i>. Do not "fix" that by keying a perk to a
    /// matrix relationship; that is exactly what would break the promise.</para>
    ///
    /// <para>The ward half is exposed to which monsters actually exist, and by raw
    /// head-count Arcane looks weakest (5 mobs, all in biome 4). Its counterweight
    /// is that every biome's boss uses the same Arcane bolt in both its Neutral and
    /// Enraged phases, so an Arcane ward is the only one that pays out on all five
    /// roads. Wards also apply to monster contact damage, which bypasses the
    /// matrix but not the ward.</para>
    /// </summary>
    public static class HeroPerks
    {
        /// <summary>Damage multiplier when the carried flask matches the hero's affinity.</summary>
        public const float AttunementMultiplier = 1.20f;

        /// <summary>Incoming-damage multiplier for the hero's own element.</summary>
        public const float WardMultiplier = 0.75f;

        public const string DefaultArchetype = "skirmisher";

        public static readonly HeroArchetype[] Archetypes =
        {
            // Closes the gap. A short flight time means less lead error against a
            // moving target, so more throws land — paid for in contact damage.
            new HeroArchetype("skirmisher", "Skirmisher",
                "Fights close. Lands more flasks, takes more hits.",
                idealRange: 4.5f, minSafeRange: 2.5f, maxRange: 11f, throwTolerance: 2.0f,
                orbitWeight: 1.15f, retreatHealthFraction: 0.30f,
                elementalWeight: 1.0f, distanceWeight: 1.0f, selfHealthWeight: 0.5f),

            // Hangs back and waits for the matchup. Safer, but the decision
            // engine's hard max-range gate means it genuinely idles at the edges.
            new HeroArchetype("marksman", "Marksman",
                "Keeps their distance and waits for the right target.",
                idealRange: 8.5f, minSafeRange: 2.5f, maxRange: 11f, throwTolerance: 3.0f,
                orbitWeight: 0.55f, retreatHealthFraction: 0.45f,
                elementalWeight: 1.6f, distanceWeight: 1.3f, selfHealthWeight: 0.5f),

            // Keeps throwing while hurt. That low retreat threshold is the whole
            // perk — it is tankiness bought with a real risk of going down.
            new HeroArchetype("bulwark", "Bulwark",
                "Holds the line. Keeps throwing long after others run.",
                idealRange: 6.0f, minSafeRange: 2.5f, maxRange: 11f, throwTolerance: 2.5f,
                orbitWeight: 0.85f, retreatHealthFraction: 0.22f,
                elementalWeight: 1.2f, distanceWeight: 1.0f, selfHealthWeight: 0.2f),
        };

        public static bool ArchetypeExists(string id)
        {
            if (string.IsNullOrEmpty(id)) return false;
            foreach (var a in Archetypes) if (a.Id == id) return true;
            return false;
        }

        public static HeroArchetype Archetype(string id)
        {
            foreach (var a in Archetypes) if (a.Id == id) return a;
            return Archetypes[0];
        }

        /// <summary>
        /// The thrown-damage multiplier this hero applies to a flask of
        /// <paramref name="flaskElement"/>. 1.0 unless it matches their affinity.
        /// </summary>
        public static float AttunementFor(ElementType affinity, ElementType flaskElement) =>
            affinity == flaskElement ? AttunementMultiplier : 1f;

        /// <summary>"Ember Hand", "Tide Eye", "Bramble Ward" — the perk's name.</summary>
        public static string PerkName(ElementType affinity, string archetypeId) =>
            $"{ElementWord(affinity)} {ArchetypeWord(archetypeId)}";

        /// <summary>Two lines of plain English for the roster card.</summary>
        public static string AttunementBlurb(ElementType affinity) =>
            $"+{UnityEngine.Mathf.RoundToInt((AttunementMultiplier - 1f) * 100f)}% damage with a {affinity} flask";

        public static string WardBlurb(ElementType affinity) =>
            $"-{UnityEngine.Mathf.RoundToInt((1f - WardMultiplier) * 100f)}% damage taken from {affinity}";

        private static string ElementWord(ElementType e) => e switch
        {
            ElementType.Fire => "Ember",
            ElementType.Water => "Tide",
            ElementType.Nature => "Bramble",
            ElementType.Poison => "Mire",
            _ => "Hex",
        };

        private static string ArchetypeWord(string archetypeId) => archetypeId switch
        {
            "marksman" => "Eye",
            "bulwark" => "Ward",
            _ => "Hand",
        };
    }
}
