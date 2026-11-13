namespace AlchemistsArsenal.Data
{
    /// <summary>
    /// One purchasable permanent upgrade — plain data, no ScriptableObject needed
    /// since the set is fixed. <see cref="Bench"/> names the shop bench it rebuilds
    /// (empty for an upgrade that goes out on the road with the party).
    /// </summary>
    public readonly struct UpgradeDefinition
    {
        public readonly string Id;
        public readonly string DisplayName;
        public readonly string Description;
        public readonly int Cost;
        /// <summary>The bench it changes ("Steep", "Kiln", "Prep", "Cauldron", "Bottling"), or "" for the road.</summary>
        public readonly string Bench;
        /// <summary>An upgrade that must be owned first (a second tier), or "".</summary>
        public readonly string Requires;

        public UpgradeDefinition(string id, string displayName, string description, int cost,
            string bench = "", string requires = "")
        {
            Id = id;
            DisplayName = displayName;
            Description = description;
            Cost = cost;
            Bench = bench ?? "";
            Requires = requires ?? "";
        }

        public bool IsBench => Bench.Length > 0;
    }

    /// <summary>
    /// The fixed set of permanent upgrades gold buys at the Evening shop.
    ///
    /// <para><b>Bench upgrades</b> rebuild a bench — it looks different the next
    /// morning (a copper pot with a clockwork paddle over it, a draught kiln with a
    /// copper cowl, a brass mortar) and it works differently: several of them let a
    /// bench carry on by itself, which is what makes brewing for a whole party in one
    /// morning manageable. <b>Road upgrades</b> go out with the party (damage, ammo,
    /// health, throwing speed), and the later roads are tuned so that a party that
    /// never buys any cannot hold them.</para>
    ///
    /// Applied where they act, by checking <c>RunState.HasUpgrade</c>: the benches,
    /// <see cref="Core.LoadoutBuilder"/> and <see cref="Core.ExpeditionWorld"/> — no
    /// new manager.
    /// </summary>
    public static class UpgradeCatalog
    {
        // ------------------------------------------------------------ the road
        public const string HeavierFlasks = "dmg1";
        public const string SpareVials = "ammo1";
        public const string ThickBoots = "hp1";
        public const string QuickHands = "cd1";
        public const string TemperedGlass = "dmg2";
        public const string Bandolier = "ammo2";
        public const string HardLeathers = "hp2";

        // ---------------------------------------------------------- the benches
        public const string SteepingVat = "vat2";
        public const string DraughtKiln = "kiln2";
        public const string DryingRack = "rack2";
        public const string BrassMortar = "mortar2";
        public const string ClockworkStirrer = "stir2";
        public const string GlassFunnel = "funnel2";

        /// <summary>
        /// Retired: party capacity used to be bought here on top of the hire. Hiring
        /// a fighter is all it takes now (see <see cref="Core.RunState.MaxParty"/>).
        /// The ids stay so a save that owns them still loads.
        /// </summary>
        public const string SecondPack = "party2";
        public const string ThirdPack = "party3";

        public static readonly UpgradeDefinition[] All =
        {
            // Bench machines. Priced by when their mechanic arrives (the later, the dearer),
            // so a saver first affords one on the evening of day 3 (even with the full quick-hands bonus), and a good player owns
            // them all near the end of road 4: see GameLoopSimulationTest.TestCurveIsAffordable.
            new UpgradeDefinition(DryingRack, "Drying Rack",
                "Herbs hung to dry over the Prep bench. No leaf comes wilted any more, and every one is sound and potent.",
                80, "Prep"),
            new UpgradeDefinition(GlassFunnel, "Auto Doser",
                "A copper tank with a tap over the flask: it measures the brew in to the line. You only cork and label.",
                356, "Bottling"),
            new UpgradeDefinition(BrassMortar, "Auto Crusher",
                "A brass ram on a frame over the mortar: it strikes the leaves itself, clean every time. You only put the leaves in.",
                358, "Prep"),
            new UpgradeDefinition(ClockworkStirrer, "Clockwork Stirrer",
                "A wind-up paddle over a copper pot stirs the whole brew, the right way round. No spoon: a little less than a perfect hand, but you are free.",
                360, "Cauldron"),
            new UpgradeDefinition(DraughtKiln, "Draught Kiln",
                "A damper and a stoker: the kiln keeps its own fire in the band, and dries a quarter faster.",
                362, "Kiln"),
            new UpgradeDefinition(SteepingVat, "Turning Vat",
                "A copper vat with a paddle and a strainer: it seals at the line, skims the husks and turns the bed. You pour the grain in and take the malt out to its basket.",
                364, "Steep"),

            new UpgradeDefinition(HeavierFlasks, "Heavier Flasks", "+25% bomb damage", 40),
            new UpgradeDefinition(SpareVials, "Spare Vials", "+5 ammo per expedition", 30),
            new UpgradeDefinition(ThickBoots, "Thick Boots", "+30 max HP for every fighter", 35),
            new UpgradeDefinition(QuickHands, "Quick Hands", "-25% bomb cooldown", 45),
            new UpgradeDefinition(TemperedGlass, "Tempered Glass", "+25% more bomb damage", 160, requires: HeavierFlasks),
            new UpgradeDefinition(Bandolier, "Bandolier", "+8 more ammo per expedition", 120, requires: SpareVials),
            new UpgradeDefinition(HardLeathers, "Hardened Leathers", "+50 more max HP for every fighter", 140, requires: ThickBoots),
        };

        public static UpgradeDefinition? Find(string id)
        {
            foreach (var up in All) if (up.Id == id) return up;
            return null;
        }

        /// <summary>Whether an upgrade can be bought at all yet, regardless of gold: its first tier owned.</summary>
        public static bool IsAvailable(string id, Core.RunState s)
        {
            var up = Find(id);
            if (up == null || s == null) return true;
            return up.Value.Requires.Length == 0 || s.HasUpgrade(up.Value.Requires);
        }

        /// <summary>
        /// How much of the road kit a run owns: 2 with every road upgrade, 1 with the
        /// four first-tier ones, else 0. What <see cref="Combat.BiomeLibrary.RoadTierNeeded"/> is measured in.
        /// </summary>
        public static int RoadTier(Core.RunState s)
        {
            if (s == null) return 0;
            bool first = s.HasUpgrade(HeavierFlasks) && s.HasUpgrade(SpareVials) && s.HasUpgrade(ThickBoots) && s.HasUpgrade(QuickHands);
            if (!first) return 0;
            bool second = s.HasUpgrade(TemperedGlass) && s.HasUpgrade(Bandolier) && s.HasUpgrade(HardLeathers);
            return second ? 2 : 1;
        }

        /// <summary>The road kit a tier stands for, in gold.</summary>
        public static int RoadTierCost(int tier)
        {
            int sum = 0;
            foreach (var up in All)
            {
                if (up.IsBench) continue;
                bool secondTier = up.Requires.Length > 0;
                if (tier >= 1 && !secondTier) sum += up.Cost;
                if (tier >= 2 && secondTier) sum += up.Cost;
            }
            return sum;
        }

        /// <summary>Why an upgrade is not available yet.</summary>
        public static string UnlockHint(string id)
        {
            var up = Find(id);
            if (up == null || up.Value.Requires.Length == 0) return "";
            var first = Find(up.Value.Requires);
            return first != null ? $"Needs {first.Value.DisplayName} first" : "";
        }
    }
}
