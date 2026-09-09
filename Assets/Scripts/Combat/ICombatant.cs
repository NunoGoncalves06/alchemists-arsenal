using UnityEngine;

namespace AlchemistsArsenal.Combat
{
    /// <summary>
    /// Anything the combat AI can reason about — adventurers and monsters alike.
    /// Deliberately read-only: the utility scorer inspects combatants, it never
    /// mutates them (damage application lives in the physics/combat resolution layer).
    /// </summary>
    public interface ICombatant
    {
        int CurrentHP { get; }
        int MaxHP { get; }
        ElementType Element { get; }
        Team Team { get; }

        /// <summary>World-space position used for distance / cluster scoring.</summary>
        Vector2 Position { get; }

        /// <summary>Current planar velocity, for closing-velocity scoring. Zero if unknown.</summary>
        Vector2 Velocity { get; }

        bool IsAlive { get; }
    }
}
