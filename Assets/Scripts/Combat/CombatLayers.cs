using UnityEngine;
using AlchemistsArsenal.PhysicsKit;

namespace AlchemistsArsenal.Combat
{
    /// <summary>
    /// The layer masks combat overlaps use. A facade over <see cref="GameLayers"/>:
    /// this used to look up layers named "Combatants"/"Monsters"/"Adventurers",
    /// none of which existed, and fall back to every layer with a warning, so
    /// detonations hit walls and in-flight bombs.
    /// </summary>
    public static class CombatLayers
    {
        public static int Combatants => GameLayers.CombatantMask;
        public static int Monsters => GameLayers.CombatantMask;
        public static int Adventurers => GameLayers.CombatantMask;
        public static int AnyCombat => GameLayers.CombatantMask;

        /// <summary>The configured mask if one was set, else the combatant layer.</summary>
        public static int Effective(int configuredMask) =>
            configuredMask != 0 ? configuredMask : GameLayers.CombatantMask;
    }
}
