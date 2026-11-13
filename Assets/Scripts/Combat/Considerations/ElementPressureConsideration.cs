namespace AlchemistsArsenal.Combat.Considerations
{
    /// <summary>
    /// Raw input = accumulated pressure from the boss's dominant threat element,
    /// normalised against its hard threshold (0..1). Drives soft weighting toward
    /// defensive phases before the hard ward override kicks in.
    /// </summary>
    [UnityEngine.CreateAssetMenu(fileName = "BossConsideration_ElementPressure",
        menuName = "Alchemist's Arsenal/AI/Boss Consideration/Element Pressure", order = 61)]
    public class ElementPressureConsideration : BossConsideration
    {
        protected override float GetRawScore(in BossPhaseContext context) => context.DominantThreatPressure01;
    }
}
