using System.Collections.Generic;
using AlchemistsArsenal.Combat.Considerations;

namespace AlchemistsArsenal.Combat
{
    /// <summary>
    /// IAUS aggregation for boss phase selection. Reuses
    /// <see cref="UtilityScorer.ModificationFactor"/> / <see cref="UtilityScorer.ApplyAxis"/>
    /// so the boss and the adventurer share one compensation-factor implementation.
    /// </summary>
    public static class BossPhaseScorer
    {
        public static float Score(IReadOnlyList<BossConsideration> considerations, in BossPhaseContext context)
        {
            if (considerations == null || considerations.Count == 0)
                return 0f;

            int activeCount = 0;
            for (int i = 0; i < considerations.Count; i++)
            {
                BossConsideration c = considerations[i];
                if (c != null && c.Weight > 0f) activeCount++;
            }
            if (activeCount == 0) return 0f;

            float modFactor = UtilityScorer.ModificationFactor(activeCount);
            float result = 1f;

            for (int i = 0; i < considerations.Count; i++)
            {
                BossConsideration c = considerations[i];
                if (c == null || c.Weight <= 0f) continue;

                result = UtilityScorer.ApplyAxis(result, c.Score(in context), c.Weight, modFactor);
                if (result <= 0f) break;
            }

            return result;
        }
    }
}
