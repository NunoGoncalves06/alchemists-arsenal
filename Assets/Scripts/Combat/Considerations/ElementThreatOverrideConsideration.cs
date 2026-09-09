namespace AlchemistsArsenal.Combat.Considerations
{
    /// <summary>
    /// Binary axis: 1 while an elemental ward is latched, 0 otherwise. Give the
    /// ElementalWard phase this consideration at a dominant weight and the phase
    /// scores itself into control whenever the accumulator latches — so the "hard
    /// override" is just utility scoring, not a separate code path.
    /// </summary>
    [UnityEngine.CreateAssetMenu(fileName = "BossConsideration_WardLatch",
        menuName = "Alchemist's Arsenal/AI/Boss Consideration/Ward Latch Override", order = 63)]
    public class ElementThreatOverrideConsideration : BossConsideration
    {
        protected override float GetRawScore(in BossPhaseContext context) =>
            context.WardLatched ? 1f : 0f;
    }
}
