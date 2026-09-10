using System;
using System.Collections.Generic;
using UnityEngine;
using AlchemistsArsenal.Combat;
using AlchemistsArsenal.Core;

namespace AlchemistsArsenal.Story
{
    /// <summary>
    /// Owns the diary entry set and evaluates unlock conditions against run
    /// milestones (DESIGN.md §7.9.4). Pure static — state lives in
    /// <see cref="RunState.unlockedDiary"/>.
    ///
    /// Phase 0 ships the opening cinematic + 2 entries; the arc fills out in
    /// Deepening 3.6.
    /// </summary>
    public static class DiaryManager
    {
        public static event Action<DiaryEntryData> OnEntryUnlocked;

        private static List<DiaryEntryData> _entries;

        public static IReadOnlyList<DiaryEntryData> All
        {
            get
            {
                if (_entries != null) return _entries;
                _entries = new List<DiaryEntryData>
                {
                    DiaryEntryData.Create("diary_00", "The Morning It Broke",
                        "It was a small spell. A kettle-charm, the kind grandmother could do half-asleep.\n" +
                        "I was not half-asleep. I was angry, and I was quick, and the words came out crooked.\n" +
                        "Now they sleep and do not wake, and the forest has something I need to undo it.",
                        DiaryUnlock.Manual),

                    DiaryEntryData.Create("diary_ww", "Under the Whispering Woods",
                        "The adventurer came back with bark in their hair and a grin.\n" +
                        "\"The trees talk,\" they said. They do. They said a name I have not written here.\n" +
                        "One biome down. The sketch of the thing at the centre gains an arm.",
                        DiaryUnlock.Biome(0)),

                    DiaryEntryData.Create("diary_perfect", "A Flask Without a Flaw",
                        "Ninety-six. I held the green the whole brew and the cork went in like it belonged.\n" +
                        "For one afternoon the fire did what fire is told. If I can do it once I can do it five times.",
                        DiaryUnlock.Perfect),
                };
                return _entries;
            }
        }

        public static DiaryEntryData Get(string id) => ((List<DiaryEntryData>)All).Find(e => e.id == id);

        /// <summary>New game: unlock the opening cinematic so it plays after the first Day Intro.</summary>
        public static void UnlockOpening(RunState state)
        {
            TryUnlock(state, "diary_00");
        }

        /// <summary>Called by the loop at BeginEvening with the finished run.</summary>
        public static void EvaluateAfterExpedition(RunState state, int biomeIndex, ExpeditionReport report)
        {
            if (state == null || report == null) return;

            foreach (DiaryEntryData e in All)
            {
                if (state.HasDiary(e.id)) continue;
                bool hit = e.unlock.kind switch
                {
                    DiaryUnlockKind.BiomeCleared => report.won && ParseInt(e.unlock.param) == biomeIndex,
                    DiaryUnlockKind.FirstPerfectPotion => report.craftedGrade == PotionGrade.Perfect,
                    DiaryUnlockKind.IngredientDiscovered => report.herbDrops.ContainsKey(e.unlock.param ?? ""),
                    _ => false,
                };
                if (hit) TryUnlock(state, e.id);
            }
        }

        private static void TryUnlock(RunState state, string id)
        {
            if (state == null || state.HasDiary(id)) return;
            state.unlockedDiary.Add(id);
            DiaryEntryData entry = Get(id);
            if (entry != null)
            {
                Debug.Log($"[Diary] unlocked '{entry.entryTitle}'");
                OnEntryUnlocked?.Invoke(entry);
            }
        }

        private static int ParseInt(string s) => int.TryParse(s, out int v) ? v : -1;
    }
}
