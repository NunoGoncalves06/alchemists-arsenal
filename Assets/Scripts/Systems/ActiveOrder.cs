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
    /// The day's single potion order. Created only by the Counter confirmation
    /// (DESIGN.md §7.6.1) and modified — up and down — by the crafting stations.
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
