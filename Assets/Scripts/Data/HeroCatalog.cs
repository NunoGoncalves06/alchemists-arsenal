using System.Collections.Generic;
using UnityEngine;
using AlchemistsArsenal.Combat;
using AlchemistsArsenal.Core;

namespace AlchemistsArsenal.Data
{
    /// <summary>
    /// The roster's balance numbers: what a level is worth, what it costs, what a
    /// hire costs, and who is on the notice board today. Plain static data in the
    /// style of <see cref="UpgradeCatalog"/> and <see cref="Economy"/> — there is
    /// deliberately no RosterManager; screens read and write
    /// <c>SaveSystem.State.roster</c> directly (docs/DESIGN.md §9).
    /// </summary>
    public static class HeroCatalog
    {
        public const int MaxLevel = 5;

        /// <summary>
        /// Five is not arbitrary: it is one hero per element, and it is also what
        /// the Evening body can show without a scroll view (the project has none).
        /// </summary>
        public const int MaxRoster = 5;

        // ------------------------------------------------------------ level curve

        // Deliberately modest, and this is the number that makes the whole feature
        // work. A hero's own attunement perk is +20% damage, so the entire four-
        // level climb (+24%) is worth roughly one perk: a level-1 Fire hero
        // carrying a Fire flask out-damages a level-5 Nature hero carrying that
        // same flask. Perk identity therefore survives the whole curve, and the
        // roster stays a matching puzzle rather than a levelling treadmill.
        public static float HpScale(int level) => 1f + 0.08f * (Clamp(level) - 1);
        public static float DamageScale(int level) => 1f + 0.06f * (Clamp(level) - 1);
        public static int AmmoBonus(int level) => 2 * (Clamp(level) - 1);

        // ------------------------------------------------------------- cost curve

        /// <summary>
        /// Gold to take a hero from <paramref name="currentLevel"/> to the next.
        /// 45 / 80 / 140 / 240 — 505 g to max one hero, against roughly 75-105 g
        /// on a clean early day and 200-280 g late.
        /// </summary>
        public static int LevelUpCost(int currentLevel)
        {
            int raw = Mathf.RoundToInt(45f * Mathf.Pow(1.75f, Clamp(currentLevel) - 1));
            return Mathf.RoundToInt(raw / 5f) * 5;
        }

        /// <summary>
        /// Gold for the next hire given how many heroes are already owned.
        /// 150 / 240 / 330 / 420 — each body dearer than the last.
        /// </summary>
        public static int HireCost(int rosterCount) => 60 + 90 * Mathf.Max(1, rosterCount);

        public static bool CanLevel(HeroRecord hero) => hero != null && hero.level < MaxLevel;

        /// <summary>
        /// Bench everyone who went down on <paramref name="resolvedDay"/> until the day
        /// after tomorrow, i.e. they miss exactly one expedition. Call before the day
        /// rolls over; then <see cref="RunState.EnsureDeployment"/> once it has.
        /// Returns how many were benched.
        /// </summary>
        public static int ApplyInjuries(RunState s, IEnumerable<string> downedIds, int resolvedDay)
        {
            if (s == null || s.roster == null || downedIds == null) return 0;
            int benched = 0;
            foreach (string id in downedIds)
            {
                HeroRecord h = s.FindHero(id);
                if (h == null) continue;
                h.restUntilDay = resolvedDay + 2;
                h.deployed = false;
                benched++;
            }
            return benched;
        }

        /// <summary>
        /// Hiring is gated on having actually won a day, not just on gold. A lost
        /// expedition pays no fee at all (only loot), so a player who spends to
        /// zero and then loses can spiral — proving a win first is the guard.
        /// </summary>
        public static bool CanHire(RunState s) =>
            s != null && s.roster != null && s.roster.Count < MaxRoster
            && s.bestGrades != null && s.bestGrades.Length > 0 && s.bestGrades[0] > 0;

        // ---------------------------------------------------------------- factory

        public static HeroRecord NewHire(string displayName, int index, ElementType affinity,
            string archetypeId, string portraitId)
        {
            return new HeroRecord
            {
                id = $"h{index}_{(displayName ?? "hero").ToLowerInvariant().Replace(' ', '_')}",
                displayName = string.IsNullOrWhiteSpace(displayName) ? "Rookie" : displayName,
                portraitId = string.IsNullOrWhiteSpace(portraitId) ? "rookie" : portraitId,
                level = 1,
                affinity = affinity,
                archetypeId = HeroPerks.ArchetypeExists(archetypeId) ? archetypeId : HeroPerks.DefaultArchetype,
                restUntilDay = 0,
                deployed = index == 0,
            };
        }

        /// <summary>The starting hero, and the seed a pre-roster save migrates to.</summary>
        public static HeroRecord NewHire(string displayName, int index) =>
            NewHire(displayName, index, ElementType.Nature, HeroPerks.DefaultArchetype, "rookie");

        // ------------------------------------------------------------- hire shelf

        /// <summary>
        /// Today's candidate on the notice board. Deterministic in
        /// (day, rosterCount) so reloading a save shows the same person rather
        /// than letting the player reroll for a better one.
        ///
        /// Names and portraits come from <see cref="CustomerCatalog"/>, every entry
        /// of which already has both a Buyer and a Fighter sprite — so a hire costs
        /// nothing in art.
        /// </summary>
        public static HeroRecord CandidateFor(int day, IReadOnlyList<HeroRecord> roster)
        {
            int count = roster != null ? roster.Count : 0;
            var rng = new System.Random(day * 6151 + count * 97);

            // Nobody already on the roster: two "Ser Halden"s side by side read as a
            // bug, and the notice board is meant to be filling a set.
            var pool = new List<CustomerDefinition>();
            // Sister Veil is the Coven's envoy, not a sword for hire: she visits the
            // shop (and the story needs her to), she never signs on.
            foreach (CustomerDefinition c in CustomerCatalog.All)
                if (c.Id != "envoy" && !OnRoster(roster, c.DisplayName)) pool.Add(c);
            if (pool.Count == 0)
                foreach (CustomerDefinition c in CustomerCatalog.All)
                    if (c.Id != "envoy") pool.Add(c);
            CustomerDefinition who = pool[rng.Next(0, pool.Count)];

            var arch = HeroPerks.Archetypes[rng.Next(0, HeroPerks.Archetypes.Length)];

            return NewHire(who.DisplayName, count, MissingAffinity(roster, rng),
                arch.Id, who.PortraitId);
        }

        /// <summary>
        /// Prefer an element nobody on the roster covers yet, so hiring reads as
        /// filling a set rather than rolling dice. Falls back to a random element
        /// once all five are covered.
        /// </summary>
        private static ElementType MissingAffinity(IReadOnlyList<HeroRecord> roster, System.Random rng)
        {
            var taken = new bool[5];
            if (roster != null)
                foreach (var h in roster)
                {
                    int i = (int)h.affinity;
                    if (i >= 0 && i < 5) taken[i] = true;
                }

            var free = new List<int>();
            for (int i = 0; i < 5; i++) if (!taken[i]) free.Add(i);

            return free.Count > 0
                ? (ElementType)free[rng.Next(0, free.Count)]
                : (ElementType)rng.Next(0, 5);
        }

        private static bool OnRoster(IReadOnlyList<HeroRecord> roster, string displayName)
        {
            if (roster == null) return false;
            foreach (HeroRecord h in roster)
                if (h != null && h.displayName == displayName) return true;
            return false;
        }

        private static int Clamp(int level) => Mathf.Clamp(level, 1, MaxLevel);
    }
}
