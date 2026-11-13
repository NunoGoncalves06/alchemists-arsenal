using UnityEngine;

namespace AlchemistsArsenal.Combat.Considerations
{
    /// <summary>
    /// Scores the adventurer's own health fraction (raw input = CurrentHP / MaxHP).
    /// The response curve decides what "low health" should mean for a given bomb:
    /// a falling curve makes a bomb preferred while healthy, a rising curve makes a
    /// panic/crowd-clear bomb preferred when nearly dead.
    /// </summary>
    [CreateAssetMenu(fileName = "Consideration_SelfHealth",
        menuName = "Alchemist's Arsenal/AI/Consideration/Self Health", order = 42)]
    public class SelfHealthConsideration : UtilityConsideration
    {
        protected override float GetRawScore(in UtilityContext context)
        {
            ICombatant self = context.Self;
            if (self == null || self.MaxHP <= 0) return 1f;
            return Mathf.Clamp01((float)self.CurrentHP / self.MaxHP);
        }
    }
}
