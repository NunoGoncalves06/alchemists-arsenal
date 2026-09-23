using System;
using System.Collections.Generic;
using AlchemistsArsenal.Combat;

namespace AlchemistsArsenal.Systems
{
    [Serializable]
    public class DeductionLog
    {
        public int pointsDelta;      // negative = penalty, positive = clean-step bonus
        public string station;
        public string reason;
        public float timestamp;

        public DeductionLog(int delta, string stationName, string changeReason, float time)
        {
            pointsDelta = delta;
            station = stationName;
            reason = changeReason;
            timestamp = time;
        }
    }

    /// <summary>
    /// Where an order is in the shop: the next bench it needs. Benches take orders
    /// in exactly this sequence and no other, so a flask can never skip a stage.
    /// </summary>
    public enum BrewStage { Malting = 0, Prep = 1, Cauldron = 2, Bottling = 3, Done = 4 }

    /// <summary>
    /// One fighter's potion order. Created only by the Counter (DESIGN.md §7.6.1),
    /// one per fighter who steps up to it, and modified — up and down — by the
    /// benches it passes through, in <see cref="BrewStage"/> order.
    ///
    /// It is <b>born at <see cref="StartingQuality"/> (Poor)</b>, not 100: an
    /// untouched order is a bad potion, not a free Great one (reviewer C-R2b).
    /// The rubric's "decay" framing still holds — clean brewing is how you climb
    /// from 25 toward 100 and then hold it.
    /// </summary>
    [Serializable]
    public class ActiveOrder
    {
        public const int StartingQuality = 25;

        public string orderId;
        public string potionName;
        public ElementType element = ElementType.Fire;
        public int currentStationIndex; // 0..3
        public int qualityScore = StartingQuality;

        /// <summary>The fighter who ordered it, and who will carry it out of the shop.</summary>
        public string heroId = "";
        public string heroName = "";

        /// <summary>The next bench this order needs.</summary>
        public BrewStage stage = BrewStage.Prep;

        /// <summary>0 for the first fighter served today, 1 for the next…</summary>
        public int queueIndex;

        /// <summary>The job taken at the Counter for it (null for a test order).</summary>
        [NonSerialized] public Data.ContractRecord contract;

        /// <summary>
        /// Its own working mixture — the recipe the job decided, the leaves actually
        /// crushed into it, whether the mortar work is done. One per order, because
        /// two fighters' potions are two different recipes in flight at once.
        /// </summary>
        [NonSerialized] public Data.BrewMixture Mixture;

        public bool Finished => stage == BrewStage.Done;

        public List<DeductionLog> deductions = new List<DeductionLog>();

        /// <summary>Raised on every quality change — <b>raise and lower</b> (reviewer X4).</summary>
        public Action<int> OnQualityChanged;

        public ActiveOrder(string id, string name, ElementType element = ElementType.Fire)
        {
            orderId = id;
            potionName = name;
            this.element = element;
            currentStationIndex = 0;
            qualityScore = StartingQuality;
            deductions = new List<DeductionLog>();
        }

        /// <summary>
        /// Move quality by <paramref name="delta"/> (either sign), clamp to 0..100,
        /// log it, and fire <see cref="OnQualityChanged"/>. The one mutation path.
        /// </summary>
        public void AdjustQuality(int delta, string stationName, string reason, float timeStamp = 0f)
        {
            if (delta == 0) return;

            int before = qualityScore;
            qualityScore = Math.Clamp(qualityScore + delta, 0, 100);
            int applied = qualityScore - before;
            if (applied == 0) return;

            deductions.Add(new DeductionLog(applied, stationName, reason, timeStamp));
            OnQualityChanged?.Invoke(qualityScore);
        }

        /// <summary>Back-compat wrapper for penalty call sites (points is a positive magnitude).</summary>
        public void ApplyDeduction(int points, string stationName, string reason, float timeStamp = 0f)
        {
            if (points <= 0) return;
            AdjustQuality(-points, stationName, reason, timeStamp);
        }

        /// <summary>Clean-step reward (points is a positive magnitude).</summary>
        public void ApplyBonus(int points, string stationName, string reason, float timeStamp = 0f)
        {
            if (points <= 0) return;
            AdjustQuality(points, stationName, reason, timeStamp);
        }

        public void AdvanceStation()
        {
            if (currentStationIndex < 3) currentStationIndex++;
        }

        /// <summary>0..1 quality, the form the combat layer consumes.</summary>
        public float Quality01 => qualityScore / 100f;

        /// <summary>The 4-band combat grade — the single grade the player ever sees.</summary>
        public PotionGrade GetGrade() => CombatQuality.GradeFor(qualityScore);
    }
}
