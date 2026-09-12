using UnityEngine;
using AlchemistsArsenal.Combat;
using AlchemistsArsenal.Data;

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

        /// <summary>
        /// What the customer actually hands over for the job they commissioned: the
        /// contract's own fee run through the grade multiplier, plus its completion
        /// bonus — or half of everything if the flask came back below the grade they
        /// asked for. A refused contract is the cost of taking the big commission
        /// with a sloppy brew, which is the point of having a choice at the Counter.
        /// </summary>
        public static (int paid, bool tip, bool met) ContractPayout(PotionGrade grade,
            ContractRecord contract, bool replay)
        {
            bool hasContract = contract != null && contract.accepted;
            int fee = hasContract ? Mathf.Max(1, contract.fee) : BaseFee;

            (int paid, bool tip) = Payout(grade, fee, replay: false);

            bool met = !hasContract || contract.Meets(grade);
            if (met) paid += hasContract ? contract.bonus : 0;
            else paid = Mathf.RoundToInt(paid * 0.5f);

            if (replay) paid = Mathf.RoundToInt(paid * 0.5f);
            return (paid, tip, met);
        }
    }
}
