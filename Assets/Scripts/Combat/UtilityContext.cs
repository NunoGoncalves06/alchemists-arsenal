using UnityEngine;
using AlchemistsArsenal.Data;

namespace AlchemistsArsenal.Combat
{
    /// <summary>
    /// Immutable snapshot of one candidate decision: "should <see cref="Self"/> throw
    /// <see cref="Bomb"/> at <see cref="Target"/> right now?". Built once per candidate
    /// by the controller and handed to every <c>UtilityConsideration</c> so scoring
    /// stays a pure function with no scene lookups.
    /// </summary>
    public readonly struct UtilityContext
    {
        public readonly ICombatant Self;
        public readonly ICombatant Target;
        public readonly BombData Bomb;
        public readonly ElementalMatrix Matrix;

        /// <summary>Potion quality carried from the morning craft, normalised 0..1.</summary>
        public readonly float PotionQuality01;

        /// <summary>Distance between self and target, world units.</summary>
        public readonly float Distance;

        /// <summary>Target speed along the line toward self (positive = closing in).</summary>
        public readonly float ClosingSpeed;

        /// <summary>Monsters (incl. the target) inside one blast radius of the target.</summary>
        public readonly int ClusterCount;

        public UtilityContext(
            ICombatant self,
            ICombatant target,
            BombData bomb,
            ElementalMatrix matrix,
            float potionQuality01,
            float distance,
            float closingSpeed,
            int clusterCount)
        {
            Self = self;
            Target = target;
            Bomb = bomb;
            Matrix = matrix;
            PotionQuality01 = Mathf.Clamp01(potionQuality01);
            Distance = distance;
            ClosingSpeed = closingSpeed;
            ClusterCount = clusterCount;
        }
    }
}
