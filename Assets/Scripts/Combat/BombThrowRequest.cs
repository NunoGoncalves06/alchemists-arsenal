using UnityEngine;
using AlchemistsArsenal.Data;

namespace AlchemistsArsenal.Combat
{
    /// <summary>
    /// The decision output of <see cref="UtilityAI_CombatController"/>. It carries
    /// everything the physics/launch layer needs to spawn and throw the bomb —
    /// the AI never touches a Rigidbody itself.
    /// </summary>
    public readonly struct BombThrowRequest
    {
        public readonly ICombatant Thrower;
        public readonly ICombatant Target;
        public readonly BombData Bomb;

        public readonly Vector2 Origin;
        public readonly Vector2 TargetPosition;

        /// <summary>Winning utility score (0..1) — handy for debug overlays / telemetry.</summary>
        public readonly float UtilityScore;

        /// <summary>Morning potion quality (0..1). The launcher applies the damage/blast band.</summary>
        public readonly float PotionQuality01;

        /// <summary>
        /// Everything about the <i>thrower</i> that scales this flask's damage:
        /// their perk attunement (does the flask match their element?) and their
        /// level. Snapshotted here at decision time, following PotionQuality01's
        /// precedent, so the detonation never has to reach back for a component.
        /// </summary>
        public readonly float ThrowerDamageMultiplier;

        public BombThrowRequest(
            ICombatant thrower,
            ICombatant target,
            BombData bomb,
            Vector2 origin,
            Vector2 targetPosition,
            float utilityScore,
            float potionQuality01,
            float throwerDamageMultiplier = 1f)
        {
            Thrower = thrower;
            Target = target;
            Bomb = bomb;
            Origin = origin;
            TargetPosition = targetPosition;
            UtilityScore = utilityScore;
            PotionQuality01 = potionQuality01;
            ThrowerDamageMultiplier = throwerDamageMultiplier <= 0f ? 1f : throwerDamageMultiplier;
        }
    }
}
