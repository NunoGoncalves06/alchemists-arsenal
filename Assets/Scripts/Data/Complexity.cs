using UnityEngine;

namespace AlchemistsArsenal.Data
{
    /// <summary>
    /// How much of the morning a road asks the player to handle. The level is the
    /// road being walked today (<see cref="Core.RunState.TargetBiomeIndex"/>), not
    /// the day: replaying an early road keeps that road's simpler morning, and new
    /// mechanics only arrive with a new road.
    ///
    /// <list type="table">
    /// <item>L0, the Woods: Counter (one job), Prep, Cauldron, Bottling (pour and
    /// cork). Two-leaf recipes, the stir always clockwise. The grain comes malted and
    /// the flask is labelled for you.</item>
    /// <item>L1, the Cinder Peaks: the Malting bench (pour, lid, kiln) and labelling.
    /// Two jobs on the board, three-leaf recipes.</item>
    /// <item>L2 on: everything. Husks to skim, a bed to turn, stir directions that
    /// flip, three jobs; four- and five-leaf recipes on the last two roads.</item>
    /// </list>
    /// </summary>
    public static class Complexity
    {
        public const int MaxLevel = 4;

        public static int Level(Core.RunState s) => s == null ? MaxLevel : Mathf.Clamp(s.TargetBiomeIndex, 0, MaxLevel);

        /// <summary>Today's level, from the live run (full complexity when there is none, as in the headless sims).</summary>
        public static int Level() =>
            Level(Core.SaveSystem.Instance != null ? Core.SaveSystem.Instance.State : null);

        public static bool MaltingOn(int level) => level >= 1;
        public static bool LabelsOn(int level) => level >= 1;
        public static bool HusksOn(int level) => level >= 2;
        public static bool TurningOn(int level) => level >= 2;
        public static bool DirectionFlips(int level) => level >= 2;

        /// <summary>How many of the three jobs the board shows.</summary>
        public static int JobsOffered(int level) => level <= 0 ? 1 : level == 1 ? 2 : 3;

        /// <summary>Which recipe of an element is brewed: 2, 3, 3, 4 then 5 leaves.</summary>
        public static int RecipeTier(int level) => level switch { <= 0 => 0, 1 => 1, 2 => 1, 3 => 2, _ => 3 };

        /// <summary>Extra morning time for the extra steps the later roads bring.</summary>
        public static float ExtraMorningSeconds(int level) => 40f * Mathf.Clamp(level, 0, MaxLevel);
    }
}
