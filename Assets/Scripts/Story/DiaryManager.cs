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
    /// The diary is Nell's side of <see cref="StoryScript"/>: one entry per clue,
    /// in her words, so the twist can be worked out from the pages before the Peak
    /// says it aloud. The ids diary_00 / diary_ww / diary_perfect are kept from
    /// the first version so old saves keep what they unlocked.
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
                    DiaryEntryData.Create("diary_00", "The Night of the Crooked Charm",
                        "Tam was to leave for the Coven's school at dawn. We fought. I reached for Grandmother's kettle-charm, " +
                        "the little one that calms a room, and said it crooked because I was angry.\n" +
                        "Tam has not woken since. Sister Veil says the Matriarch can undo it, for the right ingredients. " +
                        "Five roads to the Peak. I will arm anyone who walks them.",
                        DiaryUnlock.Manual),

                    DiaryEntryData.Create("diary_ww", "Under the Whispering Woods",
                        "The heroes came back with bark in their hair. They said the trees whispered all the way home, " +
                        "the same word over and over.\n" +
                        "It was Grandmother's name. I have not written it here since she went up the mountain. Ysolde.",
                        DiaryUnlock.Biome(0)),

                    DiaryEntryData.Create("diary_perfect", "A Flask Without a Flaw",
                        "Ninety-six. I held the green the whole brew and the cork went in like it belonged.\n" +
                        "For one afternoon the fire did what fire is told. If I can do it once I can do it five times.",
                        DiaryUnlock.Perfect),

                    DiaryEntryData.Create("diary_woodwose", "What the Woodwose Remembered",
                        "The old guardian knew my face. \"Ysolde's girl. You have her hands.\" Then: " +
                        "\"She kept the kettle, so they could not find the child.\"\n" +
                        "Which child? There is only one kettle in this house, and it has not gone cold in forty years.",
                        DiaryUnlock.Boss(0)),

                    DiaryEntryData.Create("diary_cinder", "A Page That Would Not Burn",
                        "A scorched page from a Coven outpost in the Peaks, in Grandmother's hand. I would know it anywhere:\n" +
                        "\"...when they come for the child... let the child sleep where none may follow... " +
                        "only the one who spoke it may wake them...\"\nThe rest is ash.",
                        DiaryUnlock.Biome(1)),

                    DiaryEntryData.Create("diary_frost", "Sleepers in the Ice",
                        "There are children asleep in the caverns, under the ice, a violet thread tied round every wrist.\n" +
                        "Tam had a thread like that the night Veil brought the letter. I thought it was a ribbon from the school.",
                        DiaryUnlock.Biome(2)),

                    DiaryEntryData.Create("diary_swamp", "What Mira Would Not Say",
                        "Mira finally told me, hands shaking. Grandmother did not go up the mountain to study. " +
                        "She went to stop them taking children.\n" +
                        "\"If I don't come back,\" she told Mira, \"watch the kettle.\"",
                        DiaryUnlock.Biome(3)),

                    DiaryEntryData.Create("diary_veil", "The Envoy's Patience",
                        "Veil asked after the cure again today, and after Tam, by name. Not how Tam is. Where.\n" +
                        "I told her Tam is asleep in the back room. I wish I hadn't.",
                        DiaryUnlock.Flag(StoryDirector.VeilWarned)),

                    DiaryEntryData.Create("diary_mask", "Behind the Mask",
                        "Her mask broke along the old crack, and it was Grandmother under it. The Coven made her their Mother " +
                        "and sent Veil down for the child she hid.\n" +
                        "My charm was never a curse. It was her ward, doing what she built it to do.",
                        DiaryUnlock.Boss(BiomeLibrary.Count - 1)),

                    DiaryEntryData.Create("diary_end", "The Kettle, Boiling",
                        "The ward would lift only when the one who spoke it let the anger go. So I poured their cure into the fire " +
                        "and sat with Tam until I meant it.\n" +
                        "Tam woke up and asked if the kettle was on. It was.",
                        DiaryUnlock.Flag(StoryDirector.EndingWatched)),

                    DiaryEntryData.Create("diary_after", "The Doorway",
                        "Veil came back for Tam this morning. She found every hero I have ever armed standing in my doorway.\n" +
                        "The Coven is bigger than one Matriarch. Fine. The shop is bigger than one witch.",
                        DiaryUnlock.Flag(StoryDirector.Epilogue)),
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
                    DiaryUnlockKind.BossDefeated => report.won && report.bossDefeated && ParseInt(e.unlock.param) == biomeIndex,
                    _ => false,
                };
                if (hit) TryUnlock(state, e.id);
            }
        }

        /// <summary>Unlock every entry whose story flag has been set.</summary>
        public static void EvaluateStory(RunState state)
        {
            if (state == null) return;
            foreach (DiaryEntryData e in All)
                if (e.unlock.kind == DiaryUnlockKind.StoryFlag && !state.HasDiary(e.id) && StoryDirector.Has(state, e.unlock.param))
                    TryUnlock(state, e.id);
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
