using UnityEngine;

namespace AlchemistsArsenal.Combat
{
    /// <summary>Payload for a single damage application from a bomb detonation.</summary>
    public readonly struct DamageInfo
    {
        public readonly int Amount;
        public readonly ElementType Element;
        public readonly Vector2 SourcePoint;
        public readonly ICombatant Source;

        public DamageInfo(int amount, ElementType element, Vector2 sourcePoint, ICombatant source)
        {
            Amount = amount;
            Element = element;
            SourcePoint = sourcePoint;
            Source = source;
        }
    }

    /// <summary>
    /// Write side of a combatant. Kept separate from <see cref="ICombatant"/> (the
    /// read side) so the utility-AI layer structurally cannot mutate combat state —
    /// only the physics/detonation layer takes a dependency on this.
    /// </summary>
    public interface IDamageable
    {
        void ApplyDamage(in DamageInfo info);
    }
}
