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
    /// </summary>
    public static class UtilityScorer
    {
        /// <summary>
        /// Score one candidate. Returns 0..1. A null/empty consideration list scores 0.
        /// </summary>
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

            float modificationFactor = 1f - (1f / activeCount);
            float result = 1f;

            for (int i = 0; i < considerations.Count; i++)
            {
                UtilityConsideration c = considerations[i];
                if (c == null || c.Weight <= 0f) continue;

                float score = c.Score(in context);

                // Compensation: pull the score up toward 1 by a share of its own deficit.
                float makeUp = (1f - score) * modificationFactor * score;
                float compensated = Mathf.Clamp01(score + makeUp);

                // Weight controls how hard this axis can veto the action.
                float weighted = Mathf.Clamp01(1f - c.Weight * (1f - compensated));

                result *= weighted;

                // Early-out: nothing can raise the product again.
                if (result <= 0f) break;
            }

            return result;
        }
    }
}
