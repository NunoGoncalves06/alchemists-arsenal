using UnityEngine;

namespace AlchemistsArsenal.Combat.Considerations
{
    /// <summary>
    /// The adventurer-side half of the feedback loop. Scores 1 (fine) unless the
    /// target is warding against exactly this bomb's element, in which case it
    /// scores 0 — a high weight then makes the utility AI reach for a different
    /// element while the ward is up.
    /// </summary>
    [CreateAssetMenu(fileName = "Consideration_WardAvoidance",
        menuName = "Alchemist's Arsenal/AI/Consideration/Ward Avoidance", order = 43)]
    public class WardAvoidanceConsideration : UtilityConsideration
    {
        protected override float GetRawScore(in UtilityContext context)
        {
            if (!context.TargetWarded || context.Bomb == null) return 1f;
            return context.Bomb.Element == context.TargetWardElement ? 0f : 1f;
        }
    }
}
