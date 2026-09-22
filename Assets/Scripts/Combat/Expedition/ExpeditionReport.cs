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

        /// <summary>One line on how the road ended, shown in the Evening report.</summary>
        public string outcomeReason = "";

        public int partyTotal;
        public int partyDown;

        public int goldFromLoot;
        public int goldPaidByGrade;   // filled by Economy at hand-off to Evening
        public bool perfectTip;
        public PotionGrade craftedGrade = PotionGrade.Poor;

        // --- the Counter contract this day was worked against (filled at Evening) ---
        public string contractBuyer = "";
        public string contractTitle = "";
        public int contractFee;
        public int contractBonus;
        public bool contractMet = true;
        public PotionGrade contractRequired = PotionGrade.Poor;

        /// <summary>
        /// A replay of a cleared road, which pays half. Recorded at Evening because
        /// the run state forgets it the moment the day resolves; the report used to
        /// read it back from the run state and so never showed the halving.
        /// </summary>
        public bool replayDay;

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

        // ------------------------------------------------------------------ stars
        //
        // The three stars have to be three DIFFERENT things. ExpeditionManager only
        // calls Win() once every wave was killed off (a timed-out wave is not a
        // clear) and the guardian fell wherever one stood (a boss timeout or an
        // empty belt mid-boss is a Lose). So "cleared every wave" and "felled the
        // guardian" are both already inside star one. The old third star was
        // `bossDefeated || wavesCleared >= totalWaves`, which every win satisfied,
        // so no win could ever score just 1 star.
        //
        // The third star is the flask: the grade that actually detonated on the
        // road (the same craftedGrade the fee is paid on). It is the one axis a win
        // does not imply, it is reachable on every road on every day (bossless
        // biomes 1-3 and the boss-free day 1 included), and it is the morning's
        // work showing up in the afternoon's grade.

        /// <summary>The worst flask grade that still earns the third star.</summary>
        public const PotionGrade ThirdStarGrade = PotionGrade.Great;

        /// <summary>What each star is for, in order. The Evening report and the
        /// road map both read these, so the rules are written down once.</summary>
        public static readonly string[] StarRules =
        {
            "Bring the road home",
            "Bring every hero back",
            $"Deliver a {ThirdStarGrade} flask or better",
        };

        /// <summary>Star 1: every wave cleared, and the guardian felled where one stood.</summary>
        public bool StarRoadHome => won;

        /// <summary>Star 2: nobody in the party went down.</summary>
        public bool StarEveryHeroBack => won && partyDown == 0;

        /// <summary>Star 3: the flask fought at <see cref="ThirdStarGrade"/> or better.
        /// <see cref="PotionGrade"/> counts DOWN (Perfect = 0), so "or better" is <c>&lt;=</c>.</summary>
        public bool StarFineFlask => won && craftedGrade <= ThirdStarGrade;

        /// <summary>Star <paramref name="index"/> (0..2), in <see cref="StarRules"/> order.</summary>
        public bool StarEarned(int index) => index switch
        {
            0 => StarRoadHome,
            1 => StarEveryHeroBack,
            2 => StarFineFlask,
            _ => false,
        };

        /// <summary>0..3. A loss is 0; any win is at least 1, which is what opens the
        /// next road (<see cref="Core.RunState.IsBiomeUnlocked"/>).</summary>
        public int Stars => (StarRoadHome ? 1 : 0) + (StarEveryHeroBack ? 1 : 0) + (StarFineFlask ? 1 : 0);

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
