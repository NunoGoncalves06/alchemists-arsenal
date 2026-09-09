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

        public BombThrowRequest(
            ICombatant thrower,
            ICombatant target,
            BombData bomb,
            Vector2 origin,
            Vector2 targetPosition,
            float utilityScore,
            float potionQuality01)
        {
            Thrower = thrower;
            Target = target;
            Bomb = bomb;
            Origin = origin;
            TargetPosition = targetPosition;
            UtilityScore = utilityScore;
            PotionQuality01 = potionQuality01;
        }
    }
}
