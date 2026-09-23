using System;
using System.Collections.Generic;
using AlchemistsArsenal.Data;

namespace AlchemistsArsenal.Core
{
    /// <summary>
    /// The one piece of persistent meta state for a save slot. Plain serializable —
    /// <see cref="SaveSystem"/> owns the live instance and mirrors it to JSON.
    ///
    /// Deliberately a bag of fields + a few helpers rather than a manager per
    /// concern (see DESIGN.md §3 / reviewer round 2 C-R2a): gold, roster, upgrades
    /// and biome progress are all just data here.
    /// </summary>
    [Serializable]
    public class RunState
    {
        /// <summary>Bumped whenever the shape below changes; <see cref="SaveSystem.Migrate"/> handles older files.</summary>
        public const int CurrentVersion = 3;   // 2: storyFlags, endingSeen · 3: contracts (one per fighter)

        public int saveVersion = CurrentVersion;
        public int slot = 0;
        public int day = 1;

        /// <summary>Index into the biome registry (0 = Whispering Woods … 4 = Coven's Peak).</summary>
        public int currentBiomeIndex = 0;

        /// <summary>Set when the player chose "replay" on the map — next expedition targets this instead of the current node.</summary>
        public int replayBiomeIndex = -1;

        public int gold = 0;

        /// <summary>Per-biome best grade, 0..3 stars. Length is fixed to the biome count.</summary>
        public int[] bestGrades = new int[5];

        public List<string> ownedHerbs = new List<string>();
        public List<string> ownedUpgrades = new List<string>();
        /// <summary>
        /// Legacy. Superseded by <see cref="roster"/>, which
        /// <see cref="SaveSystem.Migrate"/> seeds from this list exactly once.
        /// Kept because Migrate's re-seed of it is covered by a shipped test;
        /// nothing in the game reads it. Do not re-tangle the two.
        /// </summary>
        public List<string> ownedAdventurers = new List<string> { "Rookie" };

        /// <summary>
        /// The hired adventurers. Authoritative from the first Migrate onward.
        /// A List of a nested [Serializable] class round-trips through
        /// JsonUtility (the same shape as <see cref="contract"/>).
        /// </summary>
        public List<HeroRecord> roster = new List<HeroRecord>();
        public List<string> unlockedDiary = new List<string>();

        /// <summary>
        /// The jobs taken at today's Counter, one per fighter served — who it is
        /// for, the grade they'll accept and what it pays. Persisted (rather than
        /// living on the Morning screen) so a save taken mid-morning still knows what
        /// it owes at Evening. Cleared when the day resolves.
        /// </summary>
        public List<ContractRecord> contracts = new List<ContractRecord>();

        /// <summary>
        /// Legacy: the single job of a save written before fighters ordered their own
        /// flasks. <see cref="SaveSystem.Migrate"/> moves it into <see cref="contracts"/>;
        /// nothing else reads it.
        /// </summary>
        public ContractRecord contract = ContractRecord.None;

        public bool tutorialCompleted = false;
        public bool openingCinematicSeen = false;
        /// <summary>What the story has told so far (see Story.StoryDirector).</summary>
        public List<string> storyFlags = new List<string>();
        /// <summary>The Peak has fallen and the ending has been watched to the credits.</summary>
        public bool endingSeen = false;
        public long lastSavedUnixSeconds = 0;

        /// <summary>
        /// The last day whose expedition reward was banked. <see cref="GameLoopManager.BeginEvening"/>
        /// checks this so a mid-Evening quit + Continue can't re-bank the day (reviewer P7):
        /// the day is fully resolved (paid + advanced) the moment you reach Evening.
        /// </summary>
        public int lastResolvedDay = 0;

        // ----------------------------------------------------------------- helpers

        public int TargetBiomeIndex => replayBiomeIndex >= 0 ? replayBiomeIndex : currentBiomeIndex;
        public bool IsReplayDay => replayBiomeIndex >= 0;

        public bool HasUpgrade(string id) => ownedUpgrades.Contains(id);

        public HeroRecord FindHero(string id)
        {
            if (roster == null || string.IsNullOrEmpty(id)) return null;
            foreach (HeroRecord h in roster) if (h != null && h.id == id) return h;
            return null;
        }

        /// <summary>
        /// Most heroes that go out on one expedition — and so the most flasks one
        /// morning brews, since every fighter orders their own. Hiring is the only
        /// gate: nothing buys capacity any more. The later roads are tuned to need
        /// the bodies (see BiomeLibrary), which is what keeps them hard.
        /// </summary>
        public const int MaxParty = 4;

        public int DeployCap => MaxParty;

        /// <summary>Today's job for <paramref name="heroId"/>, or null if they have not been served.</summary>
        public ContractRecord ContractFor(string heroId)
        {
            if (contracts == null || string.IsNullOrEmpty(heroId)) return null;
            foreach (ContractRecord c in contracts)
                if (c != null && c.accepted && c.heroId == heroId) return c;
            return null;
        }

        /// <summary>The heroes actually going out, in roster order. Never empty.</summary>
        public List<HeroRecord> DeployedParty()
        {
            var party = new List<HeroRecord>();
            if (roster == null) return party;

            int cap = DeployCap;
            foreach (HeroRecord h in roster)
            {
                if (party.Count >= cap) break;
                if (h != null && h.deployed && h.IsFit(day)) party.Add(h);
            }

            // ExpeditionManager treats an empty adventurer list as "nobody has
            // died yet", so an expedition with no heroes never resolves. Always
            // send someone.
            if (party.Count == 0)
            {
                foreach (HeroRecord h in roster)
                    if (h != null && h.IsFit(day)) { party.Add(h); break; }
                if (party.Count == 0 && roster.Count > 0) party.Add(roster[0]);
            }
            return party;
        }
        /// <summary>
        /// Keep who is marked "going out" honest for today: trim to the cap, drop
        /// anyone resting, and make sure at least one hero is marked. If the whole
        /// roster is resting the first hero limps out, because an expedition with no
        /// heroes never resolves (ExpeditionManager reads an empty list as "nobody
        /// down yet"). Shared by save migration and the nightly roll-over.
        /// </summary>
        public void EnsureDeployment()
        {
            if (roster == null || roster.Count == 0) return;
            int cap = DeployCap, used = 0;
            foreach (HeroRecord h in roster)
            {
                if (h == null) continue;
                if (h.deployed && (used >= cap || !h.IsFit(day))) h.deployed = false;
                if (h.deployed) used++;
            }
            if (used > 0) return;

            HeroRecord pick = null;
            foreach (HeroRecord h in roster)
                if (h != null && h.IsFit(day)) { pick = h; break; }
            (pick ?? roster[0]).deployed = true;
        }

        public bool HasDiary(string id) => unlockedDiary.Contains(id);

        public void AddGold(int amount)
        {
            gold = Math.Max(0, gold + amount);
        }

        /// <summary>Record a biome result; only ever raises the stored grade.</summary>
        public void RecordGrade(int biomeIndex, int stars)
        {
            if (biomeIndex < 0 || biomeIndex >= bestGrades.Length) return;
            bestGrades[biomeIndex] = Math.Max(bestGrades[biomeIndex], Math.Clamp(stars, 0, 3));
        }

        public bool IsBiomeUnlocked(int biomeIndex) =>
            biomeIndex <= 0 || bestGrades[Math.Clamp(biomeIndex - 1, 0, bestGrades.Length - 1)] > 0;

        /// <summary>A fresh slot: only the starting Rookie and day 1.</summary>
        public static RunState NewGame(int slot)
        {
            return new RunState { slot = slot, saveVersion = CurrentVersion };
        }
    }
}
