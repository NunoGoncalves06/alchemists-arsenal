using UnityEngine;

namespace AlchemistsArsenal.Combat.Considerations
{
    /// <summary>
    /// Scores the throw distance. The raw input is the target distance remapped so
    /// that 0 = point-blank (below the bomb's min-safe range, self-knockback risk),
    /// ~0.5 = the bomb's ideal range, and 1 = at/over max range. The response curve
    /// then shapes the preference — a bell centred near 0.5 gives a classic
    /// "not too close, not too far" band.
    /// </summary>
    [CreateAssetMenu(fileName = "Consideration_Distance",
        menuName = "Alchemist's Arsenal/AI/Consideration/Distance", order = 41)]
    public class DistanceConsideration : UtilityConsideration
    {
        protected override float GetRawScore(in UtilityContext context)
        {
            if (context.Bomb == null) return 0f;

            float min = context.Bomb.MinSafeRange;
            float ideal = Mathf.Max(context.Bomb.IdealRange, min + 0.01f);
            float max = Mathf.Max(context.Bomb.MaxRange, ideal + 0.01f);
            float d = context.Distance;

            if (d <= min) return 0f;
            if (d >= max) return 1f;

            // Piecewise remap: [min..ideal] -> [0..0.5], [ideal..max] -> [0.5..1].
            return d <= ideal
                ? Mathf.Lerp(0f, 0.5f, Mathf.InverseLerp(min, ideal, d))
                : Mathf.Lerp(0.5f, 1f, Mathf.InverseLerp(ideal, max, d));
        }
    }
}
