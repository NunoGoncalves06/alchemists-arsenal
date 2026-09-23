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

        // Station upgrades: each changes how its bench looks and works.
        public const string SteepingVat = "vat2";
        public const string DraughtKiln = "kiln2";
        /// <summary>
        /// Retired: party capacity used to be bought here on top of the hire. Hiring
        /// a fighter is all it takes now (see <see cref="Core.RunState.MaxParty"/>).
        /// The ids stay so a save that owns them still loads.
        /// </summary>
        public const string SecondPack = "party2";
        public const string ThirdPack = "party3";

        public static readonly UpgradeDefinition[] All =
        {
            new UpgradeDefinition(HeavierFlasks, "Heavier Flasks", "+25% bomb damage", 40),
            new UpgradeDefinition(SpareVials, "Spare Vials", "+5 ammo per expedition", 30),
            new UpgradeDefinition(ThickBoots, "Thick Boots", "+30 max HP", 35),
            new UpgradeDefinition(QuickHands, "Quick Hands", "-25% bomb cooldown", 45),
        };

        /// <summary>Whether an upgrade can be bought at all yet, regardless of gold.</summary>
        public static bool IsAvailable(string id, Core.RunState s) => true;

        /// <summary>Why an upgrade is not available yet (empty while nothing is gated).</summary>
        public static string UnlockHint(string id) => "";
    }
}
