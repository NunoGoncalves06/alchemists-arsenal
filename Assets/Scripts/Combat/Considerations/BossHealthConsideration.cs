namespace AlchemistsArsenal.Combat.Considerations
{
    /// <summary>
    /// Raw input = boss HP fraction (0..1). The per-phase response curve turns this
    /// into intent: a rising curve for Neutral ("healthy → stay measured"), a bell
    /// near 0.3 for Enraged, a steep falling curve for Recovering.
    /// </summary>
    [UnityEngine.CreateAssetMenu(fileName = "BossConsideration_Health",
        menuName = "Alchemist's Arsenal/AI/Boss Consideration/Health", order = 60)]
    public class BossHealthConsideration : BossConsideration
    {
        protected override float GetRawScore(in BossPhaseContext context) => context.HealthFraction;
    }
}
