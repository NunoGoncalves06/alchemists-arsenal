using UnityEngine;

namespace AlchemistsArsenal.Combat.Considerations
{
    /// <summary>
    /// Scores the throw distance. The raw input is the target distance remapped so
    /// that <see cref="PointBlankRaw"/> = point-blank (inside the bomb's min-safe
    /// range), ~0.5 = the bomb's ideal range, and 1 = at/over max range. The response
    /// curve then shapes the preference — a bell centred near 0.5 gives a classic
    /// "not too close, not too far" band.
    /// </summary>
    [CreateAssetMenu(fileName = "Consideration_Distance",
        menuName = "Alchemist's Arsenal/AI/Consideration/Distance", order = 41)]
    public class DistanceConsideration : UtilityConsideration
    {
        /// <summary>
        /// Point-blank is a poor throw, never a forbidden one. It used to score a raw
        /// 0, and the curve maps 0 to 0, which is an absolute veto in this scorer
        /// (see UtilityScorer.ApplyAxis): a single low-HP monster hugging the hero
        /// meant no throw scored at all, the hero stood there, and the wave ran out
        /// its 45 s cap. The veto was guarding against self-knockback, but bombs have
        /// only ever damaged Team.Monster since the thrower fix, so it guarded
        /// nothing. The movement FSM still backs off from point-blank (Reposition).
        /// </summary>
        public const float PointBlankRaw = 0.15f;

        protected override float GetRawScore(in UtilityContext context)
        {
            if (context.Bomb == null) return 0f;

            float min = context.Bomb.MinSafeRange;
            float ideal = Mathf.Max(context.Bomb.IdealRange, min + 0.01f);
            float max = Mathf.Max(context.Bomb.MaxRange, ideal + 0.01f);
            float d = context.Distance;

            if (d <= min) return PointBlankRaw;
            if (d >= max) return 1f;

            // Piecewise remap: [min..ideal] -> [floor..0.5], [ideal..max] -> [0.5..1].
            return d <= ideal
                ? Mathf.Lerp(PointBlankRaw, 0.5f, Mathf.InverseLerp(min, ideal, d))
                : Mathf.Lerp(0.5f, 1f, Mathf.InverseLerp(ideal, max, d));
        }
    }
}
