using UnityEngine;
using AlchemistsArsenal.Combat;

namespace AlchemistsArsenal.Core
{
    /// <summary>
    /// Grade → evening payout. The single place the "morning quality dictates the
    /// wage" rule is computed (DESIGN.md §7.9.1). Pure functions.
    /// </summary>
    public static class Economy
    {
        public const int BaseFee = 50;
        public const int PerfectTip = 12;

        /// <summary>Fee for a delivered potion of <paramref name="grade"/>, plus whether a Perfect tip applied.</summary>
        public static (int paid, bool tip) Payout(PotionGrade grade, int baseFee = BaseFee, bool replay = false)
        {
            float mult = CombatQuality.PaymentMultiplier(grade);
            int paid = Mathf.RoundToInt(baseFee * mult);
            bool tip = CombatQuality.GoldTip(grade);
            if (tip) paid += PerfectTip;
            if (replay) paid = Mathf.RoundToInt(paid * 0.5f); // cleared-biome replay pays half (anti-farm, never zero)
            return (paid, tip);
        }
    }
}
