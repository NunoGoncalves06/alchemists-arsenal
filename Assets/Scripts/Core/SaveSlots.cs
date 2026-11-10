using System;
using AlchemistsArsenal.Combat;

namespace AlchemistsArsenal.Core
{
    /// <summary>
    /// What the slot screen says about a save, and which slot a new game is offered.
    /// Pure, so the slot tests can check it without the screen.
    /// </summary>
    public static class SaveSlots
    {
        public readonly struct SlotSummary
        {
            public readonly string Road, Night, LastPlayed;
            public SlotSummary(string road, string night, string lastPlayed) { Road = road; Night = night; LastPlayed = lastPlayed; }
        }

        /// <summary>
        /// The road the run is on, the nights it has slept (a save always sits at the
        /// start of a day, so day N has had N - 1), and when it was last played.
        /// </summary>
        public static SlotSummary Describe(RunState s, long nowUnix)
        {
            string road = BiomeLibrary.Name(s.TargetBiomeIndex);
            string night = s.day <= 1 ? "First day" : $"Night {s.day - 1}";
            DateTime local = DateTimeOffset.FromUnixTimeSeconds(s.lastSavedUnixSeconds).ToLocalTime().DateTime;
            return new SlotSummary(road, night, $"{local:d MMM, HH:mm} · {Ago(nowUnix - s.lastSavedUnixSeconds)}");
        }

        private static string Ago(long seconds)
        {
            if (seconds < 90) return "just now";
            long m = seconds / 60, h = m / 60, d = h / 24;
            if (h < 1) return $"{m} minutes ago";
            if (d < 1) return h == 1 ? "an hour ago" : $"{h} hours ago";
            return d == 1 ? "yesterday" : $"{d} days ago";
        }

        /// <summary>The first slot with no file at all (a corrupt file is not empty), or -1.</summary>
        public static int FirstEmpty(SaveSystem save)
        {
            for (int i = 0; i < SaveSystem.Slots; i++)
                if (!save.SlotExists(i)) return i;
            return -1;
        }

        /// <summary>The readable slot played most recently, or -1: what the title screen dresses itself for.</summary>
        public static int MostRecent(SaveSystem save)
        {
            int best = -1; long when = 0;
            for (int i = 0; i < SaveSystem.Slots; i++)
            {
                RunState s = save.SlotExists(i) ? save.Peek(i) : null;
                if (s != null && s.lastSavedUnixSeconds > when) { when = s.lastSavedUnixSeconds; best = i; }
            }
            return best;
        }
    }
}
