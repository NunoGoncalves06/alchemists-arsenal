namespace AlchemistsArsenal.Data
{
    /// <summary>One purchasable permanent upgrade — plain data, no ScriptableObject
    /// needed since the set is fixed (mirrors <see cref="AlchemistsArsenal.Combat.Economy"/>'s
    /// "plain static class" style for fixed game-balance data).</summary>
    public readonly struct UpgradeDefinition
    {
        public readonly string Id;
        public readonly string DisplayName;
        public readonly string Description;
        public readonly int Cost;

        public UpgradeDefinition(string id, string displayName, string description, int cost)
        {
            Id = id;
            DisplayName = displayName;
            Description = description;
            Cost = cost;
        }
    }

    /// <summary>
    /// The fixed set of permanent upgrades gold buys at the Evening shop.
    /// <see cref="AlchemistsArsenal.Core.RunState.ownedUpgrades"/> /
    /// <c>HasUpgrade(id)</c> already existed with nowhere to spend the gold — this is
    /// that spend. Applied by <see cref="AlchemistsArsenal.Core.LoadoutBuilder"/>
    /// (damage / ammo / cooldown) and <see cref="AlchemistsArsenal.Core.ExpeditionWorld"/>
    /// (max HP) by checking <c>RunState.HasUpgrade</c> — no new manager needed.
    /// </summary>
    public static class UpgradeCatalog
    {
        public const string HeavierFlasks = "dmg1";
        public const string SpareVials = "ammo1";
        public const string ThickBoots = "hp1";
        public const string QuickHands = "cd1";
        public const string SecondPack = "party2";
        public const string ThirdPack = "party3";

        public static readonly UpgradeDefinition[] All =
        {
            new UpgradeDefinition(HeavierFlasks, "Heavier Flasks", "+25% bomb damage", 40),
            new UpgradeDefinition(SpareVials, "Spare Vials", "+5 ammo per expedition", 30),
            new UpgradeDefinition(ThickBoots, "Thick Boots", "+30 max HP", 35),
            new UpgradeDefinition(QuickHands, "Quick Hands", "-25% bomb cooldown", 45),
            new UpgradeDefinition(SecondPack, "Second Pack", "Send a second hero on every expedition", 220),
            new UpgradeDefinition(ThirdPack, "Third Pack", "Send a third hero on every expedition", 520),
        };

        /// <summary>
        /// Whether an upgrade can be bought at all yet, regardless of gold.
        /// Only the party-capacity nodes are gated, and they are gated on having
        /// cleared a road rather than on price — that is what keeps the late game
        /// hard, since gold alone can never buy your way to a full party.
        ///
        /// Deliberately a switch rather than a field on
        /// <see cref="UpgradeDefinition"/>: a new constructor argument would touch
        /// every existing entry for the sake of two.
        /// </summary>
        public static bool IsAvailable(string id, Core.RunState s)
        {
            if (s == null) return true;
            return id switch
            {
                SecondPack => ClearedBiome(s, 1),
                ThirdPack => ClearedBiome(s, 3),
                _ => true,
            };
        }

        /// <summary>The road you must have starred before a capacity node unlocks.</summary>
        public static string UnlockHint(string id) => id switch
        {
            SecondPack => "Clear Cinder Peaks first",
            ThirdPack => "Clear Venom Swamp first",
            _ => "",
        };

        private static bool ClearedBiome(Core.RunState s, int index) =>
            s.bestGrades != null && index < s.bestGrades.Length && s.bestGrades[index] > 0;
    }
}
