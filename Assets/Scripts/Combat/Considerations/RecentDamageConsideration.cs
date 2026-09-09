namespace AlchemistsArsenal.Combat.Considerations
{
    /// <summary>
    /// Raw input = recent burst of dominant-element damage (0..1 vs the soft
    /// threshold). A rising curve pushes the boss toward Recovering right after a
    /// big spike; a falling curve suits phases that want a quiet field.
    /// </summary>
    [UnityEngine.CreateAssetMenu(fileName = "BossConsideration_RecentDamage",
        menuName = "Alchemist's Arsenal/AI/Boss Consideration/Recent Damage", order = 62)]
    public class RecentDamageConsideration : BossConsideration
    {
        protected override float GetRawScore(in BossPhaseContext context) => context.RecentDamageSpike01;
    }
}
