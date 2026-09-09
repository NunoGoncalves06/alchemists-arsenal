using System;
using System.Collections.Generic;

namespace AlchemistsArsenal.Systems
{
    [Serializable]
    public enum QualityTier
    {
        Perfect, // >= 90
        Good,    // >= 70
        Poor     // < 70
    }

    [Serializable]
    public class DeductionLog
    {
        public int pointsDeducted;
        public string station;
        public string reason;
        public float timestamp;

        public DeductionLog(int points, string stationName, string deductionReason, float time)
        {
            pointsDeducted = points;
            station = stationName;
            reason = deductionReason;
            timestamp = time;
        }
    }

    [Serializable]
    public class ActiveOrder
    {
        public string orderId;
        public string potionName;
        public int currentStationIndex; // 0 to 3
        public int qualityScore = 100; // Starting quality

        public List<DeductionLog> deductions = new List<DeductionLog>();

        // Event for when quality changes
        public Action<int> OnQualityChanged;

        public ActiveOrder(string id, string name)
        {
            orderId = id;
            potionName = name;
            currentStationIndex = 0;
            qualityScore = 100;
            deductions = new List<DeductionLog>();
        }

        /// <summary>
        /// Applies a quality deduction, clamping the final quality between 0 and 100.
        /// </summary>
        public void ApplyDeduction(int points, string stationName, string reason, float timeStamp = 0f)
        {
            if (points <= 0) return;

            qualityScore -= points;
            if (qualityScore < 0) qualityScore = 0;

            deductions.Add(new DeductionLog(points, stationName, reason, timeStamp));
            OnQualityChanged?.Invoke(qualityScore);
        }

        /// <summary>
        /// Advances the order to the next crafting station.
        /// </summary>
        public void AdvanceStation()
        {
            if (currentStationIndex < 3)
            {
                currentStationIndex++;
            }
        }

        /// <summary>
        /// Gets the QualityTier based on the current quality score.
        /// </summary>
        public QualityTier GetTier()
        {
            if (qualityScore >= 90) return QualityTier.Perfect;
            if (qualityScore >= 70) return QualityTier.Good;
            return QualityTier.Poor;
        }
    }
}
