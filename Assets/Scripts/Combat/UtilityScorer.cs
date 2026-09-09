using System.Collections.Generic;
using UnityEngine;
using AlchemistsArsenal.Combat.Considerations;

namespace AlchemistsArsenal.Combat
{
    /// <summary>
    /// Aggregates a set of <see cref="UtilityConsideration"/> scores into a single
    /// 0..1 action score using Dave Mark's Infinite Axis Utility System.
    ///
    /// Naive multiplication punishes an action once per axis, so adding more
    /// considerations always lowers every score. The compensation factor adds back
    /// a "make-up" value that scales with how many axes are in play, keeping scores
    /// comparable regardless of consideration count.
    ///
    /// <see cref="ModificationFactor"/> and <see cref="ApplyAxis"/> are exposed so
    /// the boss phase scorer reuses the exact same maths on a different context type.
    /// </summary>
    public static class UtilityScorer
    {
        /// <summary>IAUS compensation factor for a given number of active axes.</summary>
        public static float ModificationFactor(int activeAxisCount) =>
            activeAxisCount <= 1 ? 0f : 1f - (1f / activeAxisCount);

        /// <summary>
        /// Fold one axis into the running product: compensate the raw score, then
        /// let <paramref name="weight"/> decide how hard it can veto.
        /// </summary>
        public static float ApplyAxis(float runningProduct, float score, float weight, float modFactor)
        {
            if (weight <= 0f) return runningProduct;

            score = Mathf.Clamp01(score);
            float makeUp = (1f - score) * modFactor * score;
            float compensated = Mathf.Clamp01(score + makeUp);
            float weighted = Mathf.Clamp01(1f - weight * (1f - compensated));
            return runningProduct * weighted;
        }

        /// <summary>Score one candidate. Returns 0..1. A null/empty list scores 0.</summary>
        public static float ScoreAction(IReadOnlyList<UtilityConsideration> considerations, in UtilityContext context)
        {
            if (considerations == null || considerations.Count == 0)
                return 0f;

            int activeCount = 0;
            for (int i = 0; i < considerations.Count; i++)
            {
                UtilityConsideration c = considerations[i];
                if (c != null && c.Weight > 0f) activeCount++;
            }
            if (activeCount == 0) return 0f;

            float modFactor = ModificationFactor(activeCount);
            float result = 1f;

            for (int i = 0; i < considerations.Count; i++)
            {
                UtilityConsideration c = considerations[i];
                if (c == null || c.Weight <= 0f) continue;

                result = ApplyAxis(result, c.Score(in context), c.Weight, modFactor);
                if (result <= 0f) break;
            }

            return result;
        }
    }
}
