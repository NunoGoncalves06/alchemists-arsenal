using UnityEngine;

namespace AlchemistsArsenal.Data
{
    /// <summary>
    /// Every quality point the morning can pay out or charge, in one place.
    ///
    /// These numbers used to live as literals inside each station and were
    /// transcribed by hand into GameLoopSimulationTest and the headless playtest,
    /// so a retune in a station went stale in two other files without anything
    /// failing. The stations, the test and the harness all read this class now.
    ///
    /// The budget is deliberately just over 100: every station is load-bearing and
    /// one botched step costs a grade (see <see cref="FlawlessMorning"/>).
    /// </summary>
    public static class QualityBudget
    {
        // ---------------------------------------------------------------- counter
        /// <summary>Taking the job whose element counters the road's main threat.</summary>
        public const int CounterRead = 4;

        // ---------------------------------------------------------------- malting
        /// <summary>Good grain in the steep, and what it takes to count as "to the line".</summary>
        public const int GrainTarget = 18, GrainSlack = 3;
        /// <summary>Steeped to the line; short of grain; crowded.</summary>
        public const int MaltFill = 2, MaltShort = 3, MaltOver = 2;
        /// <summary>Every husk skimmed off the steep; each husk left in it (up to a cap).</summary>
        public const int MaltSkim = 2, MaltHuskLeft = 1, MaltHuskCap = 4;
        /// <summary>Turned the germinating bed in time; let the rootlets mat.</summary>
        public const int MaltTurn = 2, MaltMatted = 3;
        /// <summary>The kiln pays this much over its drying bar, held in the heat band.</summary>
        public const int KilnTotal = 6;
        /// <summary>Per second too hot (the enzymes cook); per two seconds too cool (it keeps sprouting).</summary>
        public const int KilnScorch = 3, KilnCool = 1;
        /// <summary>Green malt left waiting for the kiln, every few seconds, up to a cap.</summary>
        public const int MaltOverModified = 1, MaltOverCap = 4;

        /// <summary>The most a malting can earn: what <see cref="Systems.ActiveOrder.MaltQuality01"/> is out of.</summary>
        public const int MaltMax = MaltFill + MaltSkim + MaltTurn + KilnTotal;

        // ------------------------------------------------------------------- prep
        /// <summary>A leaf on cue pays (base + potency) x wilt; potency is 1..3.</summary>
        public const int LeafOnCueBase = 2;
        public const float WiltedFactor = 0.5f;
        public const int WrongLeafBase = 6;
        public const int WrongLeafPerPotency = 2;

        public const int GrindStrikes = 3;
        /// <summary>A clean strike pays between these, by how close to ideal.</summary>
        public const int StrikeMin = 2, StrikeMax = 3;
        public const int StrikeMiss = 6;

        // --------------------------------------------------------------- cauldron
        /// <summary>The whole brew pays this much, spread over its progress bar.</summary>
        public const int BrewTotal = 16;
        /// <summary>Charged per second stirring backwards.</summary>
        public const int BrewPenalty = 6;
        /// <summary>
        /// Charged per second below the band, rising as the brew catches: merely too
        /// slow, then sticking to the bottom, then burning there (plus a little more
        /// the worse it has got).
        /// </summary>
        public const int BrewSlow = 2, BrewStick = 5, BrewBurn = 9;
        /// <summary>Each herb flung out of the pot by over-stirring.</summary>
        public const int Splash = 4;

        // --------------------------------------------------------------- bottling
        public const int PourMin = 4, PourMax = 9;
        public const int PourShort = 10;
        public const int PourOverflow = 16;
        public const int SealMin = 4, SealMax = 9;
        public const int SealMiss = 11;
        public const int SealAttempts = 2;
        public const int LabelRight = 4;
        public const int LabelWrong = 14;

        // ----------------------------------------------------------------- totals

        public static int LeafOnCue(int potency, bool wilted) =>
            Mathf.RoundToInt((LeafOnCueBase + Mathf.Clamp(potency, 1, 3)) * (wilted ? WiltedFactor : 1f));

        public static int LeafOutOfOrder(int potency, bool wilted) =>
            Mathf.Max(1, Mathf.RoundToInt((1f + Mathf.Clamp(potency, 1, 3) * 0.35f) * (wilted ? WiltedFactor : 1f)));

        public static int WrongLeaf(int potency) => WrongLeafBase + WrongLeafPerPotency * Mathf.Clamp(potency, 1, 3);

        /// <summary>The best morning possible: every step at its maximum.</summary>
        public static int FlawlessMorning() =>
            Systems.ActiveOrder.StartingQuality
            + CounterRead
            + MaltMax
            + 3 * LeafOnCue(3, false)
            + GrindStrikes * StrikeMax
            + RecipeBook.QualityDelta(MixOutcome.Perfect)
            + BrewTotal
            + PourMax + SealMax + LabelRight;
    }
}
