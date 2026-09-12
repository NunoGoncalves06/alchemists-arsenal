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
        public const int CurrentVersion = 1;

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
        public List<string> ownedAdventurers = new List<string> { "Rookie" };
        public List<string> unlockedDiary = new List<string>();

        /// <summary>
        /// The job taken at today's Counter — who ordered it, the grade they'll
        /// accept and what it pays. Persisted (rather than living on the Morning
        /// screen) so a save taken mid-morning still knows what it owes at Evening.
        /// Cleared when the day resolves.
        /// </summary>
        public ContractRecord contract = ContractRecord.None;

        public bool tutorialCompleted = false;
        public bool openingCinematicSeen = false;
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
