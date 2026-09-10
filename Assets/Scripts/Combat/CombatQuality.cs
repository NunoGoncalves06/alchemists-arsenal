using UnityEngine;

namespace AlchemistsArsenal.Combat
{
    /// <summary>Four-band potion grade from the Level 2 rubric.</summary>
    public enum PotionGrade
    {
        Perfect = 0, // >= 95%
        Great   = 1, // 80% - 94%
        Okay    = 2, // 60% - 79%
        Poor    = 3  // < 60%
    }

    /// <summary>
    /// Single source of truth for how a morning potion's quality score converts into
    /// afternoon combat effects. Both the utility AI and the detonation code read
    /// from here so the "morning craft dictates afternoon combat" loop stays
    /// consistent. This 4-band grade is the ONLY grade the player ever sees
    /// (the older 3-band <c>QualityTier</c> was deleted — reviewer round 3).
    /// </summary>
    public static class CombatQuality
    {
        public const float PerfectCutoff01 = 0.95f;
        public const float GreatCutoff01   = 0.80f;
        public const float OkayCutoff01    = 0.60f;

        public static PotionGrade GradeFor01(float quality01)
        {
            float q = Mathf.Clamp01(quality01);
            if (q >= PerfectCutoff01) return PotionGrade.Perfect;
            if (q >= GreatCutoff01)   return PotionGrade.Great;
            if (q >= OkayCutoff01)    return PotionGrade.Okay;
            return PotionGrade.Poor;
        }

        public static PotionGrade GradeFor(int qualityScore0to100) =>
            GradeFor01(qualityScore0to100 / 100f);

        /// <summary>Base damage multiplier: 1.20 / 1.00 / 0.75 / 0.50.</summary>
        public static float DamageMultiplier(PotionGrade grade) => grade switch
        {
            PotionGrade.Perfect => 1.20f,
            PotionGrade.Great   => 1.00f,
            PotionGrade.Okay    => 0.75f,
            _                   => 0.50f,
        };

        /// <summary>Payment multiplier for the evening economy: 1.00 / 1.00 / 0.70 / 0.40.</summary>
        public static float PaymentMultiplier(PotionGrade grade) => grade switch
        {
            PotionGrade.Perfect => 1.00f,
            PotionGrade.Great   => 1.00f,
            PotionGrade.Okay    => 0.70f,
            _                   => 0.40f,
        };

        /// <summary>Elemental matrix multiplier only applies at Okay grade and above.</summary>
        public static bool ElementalBonusEnabled(PotionGrade grade) => grade != PotionGrade.Poor;

        /// <summary>Perfect potions pay a gold tip on top of the payment multiplier.</summary>
        public static bool GoldTip(PotionGrade grade) => grade == PotionGrade.Perfect;

        // --- convenience passthroughs keyed off a raw 0..1 quality --------------
        public static float DamageMultiplier01(float quality01) => DamageMultiplier(GradeFor01(quality01));
        public static bool ElementalBonusEnabled01(float quality01) => ElementalBonusEnabled(GradeFor01(quality01));
    }
}
