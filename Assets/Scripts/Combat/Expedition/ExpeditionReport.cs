using System;
using System.Collections.Generic;
using AlchemistsArsenal.Combat;

namespace AlchemistsArsenal.Combat
{
    /// <summary>
    /// Everything the Evening Report screen shows, accumulated by
    /// <see cref="ExpeditionTelemetry"/> during the run. Pure data — no lifetime,
    /// no singleton (DESIGN.md §3).
    /// </summary>
    [Serializable]
    public class ExpeditionReport
    {
        public string biomeName = "";
        public bool won;
        public bool bossDefeated;
        public int wavesCleared;
        public int totalWaves;
        public float durationSeconds;

        public int partyTotal;
        public int partyDown;

        public int goldFromLoot;
        public int goldPaidByGrade;   // filled by Economy at hand-off to Evening
        public bool perfectTip;
        public PotionGrade craftedGrade = PotionGrade.Poor;

        public readonly Dictionary<string, int> herbDrops = new Dictionary<string, int>();
        public readonly List<BombLine> bombs = new List<BombLine>();

        [Serializable]
        public class BombLine
        {
            public string name;
            public ElementType element;
            public PotionGrade grade;
            public int throws;
            public int hits;
            public int totalDamage;
            public bool everHadAdvantage;
        }

        public int TotalGold => goldFromLoot + goldPaidByGrade;

        /// <summary>0..3 stars: cleared + boss + no losses.</summary>
        public int Stars
        {
            get
            {
                if (!won) return 0;
                int s = 1;
                if (partyDown == 0) s++;
                if (bossDefeated || wavesCleared >= totalWaves) s++;
                return Math.Clamp(s, 0, 3);
            }
        }

        public BombLine LineFor(string name, ElementType element, PotionGrade grade)
        {
            var line = bombs.Find(b => b.name == name);
            if (line == null)
            {
                line = new BombLine { name = name, element = element, grade = grade };
                bombs.Add(line);
            }
            return line;
        }

        public void AddHerb(string id)
        {
            if (string.IsNullOrEmpty(id)) return;
            herbDrops.TryGetValue(id, out int n);
            herbDrops[id] = n + 1;
        }
    }
}
