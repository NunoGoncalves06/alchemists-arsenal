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

        /// <summary>
        /// What each fighter past the first adds to the day's purse, as a share of the average contract.
        /// 0.6 -> 0.8 with the quick-hands bonus: the bench machines had to cost more (a saver with the full
        /// bonus could buy one on evening 2), and the bigger party purse keeps the good player's shop on time.
        /// </summary>
        public const float PurseStep = 0.8f;

        /// <summary>The purse for <paramref name="fighters"/>: 1 for one, then <see cref="PurseStep"/> more per fighter.</summary>
        public static float PurseScale(int fighters) => 1f + PurseStep * Mathf.Max(0, fighters - 1);

        /// <summary>
        /// The day's pay for the whole party, as one purse: the average of what each
        /// fighter's contract pays (grade, met or refused, replay all already applied),
        /// times <see cref="PurseScale"/>. A party of three used to bring home three
        /// full contracts, which emptied the shop by the third road.
        /// </summary>
        public static int SharedPurse(System.Collections.Generic.IReadOnlyList<int> paid)
        {
            if (paid == null || paid.Count == 0) return 0;
            float sum = 0f;
            foreach (int p in paid) sum += p;
            return Mathf.RoundToInt(sum / paid.Count * PurseScale(paid.Count));
        }

        /// <summary>
        /// The quick-hands bonus on the day's purse, by how much of the morning was left
        /// when every flask had been handed over: tiers up to +25 %, never a penalty.
        /// </summary>
        public static float SpeedBonus(float left01) =>
            left01 >= 0.50f ? 0.25f : left01 >= 0.35f ? 0.15f : left01 >= 0.20f ? 0.10f : left01 >= 0.10f ? 0.05f : 0f;

        /// <summary>The gold <see cref="SpeedBonus"/> adds to <paramref name="purse"/> (the contracts' pay, not the loot).</summary>
        public static int SpeedBonusGold(int purse, float left01) => Mathf.RoundToInt(Mathf.Max(0, purse) * SpeedBonus(left01));

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
