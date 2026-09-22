using System.Collections.Generic;
using AlchemistsArsenal.Combat;
using AlchemistsArsenal.Core;
using AlchemistsArsenal.Data;

namespace AlchemistsArsenal.Story
{
    /// <summary>
    /// Decides which part of the story is due, and remembers what has been told.
    /// Everything it knows lives in <see cref="RunState.storyFlags"/> (and
    /// <see cref="RunState.endingSeen"/>), so it survives a quit at any moment: a
    /// cutscene earned by today's fight is flagged when the day is resolved and
    /// saved with it, and only cleared once it has actually been watched. If the
    /// game closes mid-scene, it plays at the next Day Intro instead.
    ///
    /// There is no story phase in the day loop. Cutscenes are an overlay: the
    /// opening between the first Day Intro and the morning, everything else at the
    /// start of the Evening that earned it.
    /// </summary>
    public static class StoryDirector
    {
        public const string WoodwosePending = "woodwose_pending";
        public const string WoodwoseSeen = "woodwose_seen";
        public const string EndingPending = "ending_pending";
        public const string EndingWatched = "ending";
        public const string Epilogue = "epilogue";
        public const string VeilWarned = "veil_warned";

        public static bool Has(RunState s, string flag) => s != null && s.storyFlags != null && s.storyFlags.Contains(flag);

        public static void Set(RunState s, string flag)
        {
            if (s == null) return;
            s.storyFlags ??= new List<string>();
            if (!s.storyFlags.Contains(flag)) s.storyFlags.Add(flag);
        }

        public static void Clear(RunState s, string flag) => s?.storyFlags?.Remove(flag);

        /// <summary>How many of the five roads have been cleared at least once.</summary>
        public static int RoadsCleared(RunState s)
        {
            if (s == null || s.bestGrades == null) return 0;
            int n = 0;
            foreach (int g in s.bestGrades) if (g > 0) n++;
            return n;
        }

        /// <summary>
        /// Called once per resolved day (inside BeginEvening's resolve-once block):
        /// flag whatever the day's fight has earned.
        /// </summary>
        public static void OnDayResolved(RunState s, int biome, ExpeditionReport r)
        {
            if (s == null || r == null || !r.won || !r.bossDefeated) return;
            if (biome == 0 && !Has(s, WoodwoseSeen)) Set(s, WoodwosePending);
            if (biome == BiomeLibrary.Count - 1 && !s.endingSeen) Set(s, EndingPending);
        }

        /// <summary>The cutscenes waiting to be watched, in the order they play.</summary>
        public static List<Cutscene> Due(RunState s)
        {
            var list = new List<Cutscene>();
            if (s == null) return list;
            if (Has(s, WoodwosePending)) list.Add(StoryScript.Woodwose);
            if (Has(s, EndingPending))
            {
                if (!Has(s, EndingWatched))
                {
                    list.Add(StoryScript.Reveal);
                    list.Add(StoryScript.Ending);
                }
                list.Add(StoryScript.Credits);
            }
            return list;
        }

        /// <summary>A cutscene was watched (or skipped) to its end: remember it.</summary>
        public static void Finished(RunState s, Cutscene c)
        {
            if (s == null || c == null) return;
            switch (c.Id)
            {
                case "opening":
                    s.openingCinematicSeen = true;
                    break;
                case "woodwose":
                    Clear(s, WoodwosePending);
                    Set(s, WoodwoseSeen);
                    break;
                case "ending":
                    Set(s, EndingWatched);
                    break;
                case "credits":
                    Clear(s, EndingPending);
                    s.endingSeen = true;
                    Set(s, Epilogue);
                    break;
            }
            DiaryManager.EvaluateStory(s);
        }

        // ------------------------------------------------------------ voices

        /// <summary>
        /// What a customer says at the Counter once the story has moved on, or null
        /// for their ordinary greeting. Veil's patience thins as Nell gets closer;
        /// Mira, who knew Ysolde, lets more slip.
        /// </summary>
        public static string CounterLine(CustomerDefinition c, RunState s)
        {
            if (c == null || s == null) return null;
            int roads = RoadsCleared(s);
            bool after = s.endingSeen;
            switch (c.Id)
            {
                case "envoy":
                    if (after) return "The Coven is more than one Matriarch, witch. I'm only here for a flask. Today.";
                    if (roads >= 4) return "The Peak is close now. Bring the cure, and the child's name. That is all she needs.";
                    if (roads >= 3)
                    {
                        Set(s, VeilWarned);
                        DiaryManager.EvaluateStory(s);
                        return "Is the child still asleep in the back room? Good. Keep them there.";
                    }
                    if (roads >= 2) return "You're asking questions about your grandmother. Don't. Ask about the cure.";
                    if (roads >= 1) return "The Matriarch wants the ingredients fresh. The child will keep. Sleepers always do.";
                    return "The Matriarch can lift it. Keep your heroes walking, witch, and keep brewing.";
                case "herbalist":
                    if (after) return "Tam's awake? Oh, love. Put the kettle on, I'll bring the thyme.";
                    if (roads >= 4) return "I should have told you sooner, Nell. I'm sorry. She made me promise.";
                    if (roads >= 2) return "That kettle of yours. Ysolde never once let it boil dry. Not in forty years.";
                    if (roads >= 1) return "Your grandmother bought her thyme from me for forty years. You grind it the way she did.";
                    return null;
                default:
                    return null;
            }
        }

        /// <summary>A line under the Day Intro's title that keeps the story in the room.</summary>
        public static string DayWhisper(RunState s)
        {
            if (s == null || s.day <= 1) return "";
            if (s.endingSeen) return "Tam is at the counter, pretending not to watch you brew.";
            int roads = RoadsCleared(s);
            if (roads >= 4) return "The kettle has been warm all night, and nobody lit the fire.";
            if (roads >= 2) return "Tam is still asleep. The kettle hums when you walk past it.";
            return "Tam is still asleep in the back room.";
        }

        /// <summary>The main menu's line, once the story is over.</summary>
        public static string MenuSubtitle(RunState s) => s != null && s.endingSeen
            ? "the kettle is on · the shop is open · the Coven is still out there"
            : "brew in the morning · fight in the afternoon · pay the rent at night";
    }
}
