using UnityEngine;

namespace AlchemistsArsenal.Combat
{
    /// <summary>
    /// Resolves the physics layers combat overlaps should consider. Prefer a
    /// project layer named "Combatants" (or "Monsters"/"Adventurers"); if none of
    /// those exist yet, <see cref="Effective"/> falls back to every layer and warns
    /// once, rather than silently hitting UI / trigger colliders.
    /// </summary>
    public static class CombatLayers
    {
        private static bool _warned;

        public static int Monsters => SafeMask("Monsters");
        public static int Adventurers => SafeMask("Adventurers");
        public static int Combatants => SafeMask("Combatants");

        /// <summary>Any of the three combat layers that actually exist in the project.</summary>
        public static int AnyCombat => Monsters | Adventurers | Combatants;

        /// <summary>
        /// The mask a detonation / strike should use: the configured one if set,
        /// else the project's combat layers, else (with a one-time warning) all layers.
        /// </summary>
        public static int Effective(int configuredMask)
        {
            if (configuredMask != 0) return configuredMask;

            int combat = AnyCombat;
            if (combat != 0) return combat;

            if (!_warned)
            {
                _warned = true;
                Debug.LogWarning(
                    "[CombatLayers] No 'Combatants' / 'Monsters' / 'Adventurers' layer and no mask " +
                    "configured — combat overlaps will hit ALL layers. Add a Combatants layer and " +
                    "assign it on the combatant prefabs.");
            }
            return Physics2D.AllLayers;
        }

        private static int SafeMask(string layerName)
        {
            int layer = LayerMask.NameToLayer(layerName);
            return layer < 0 ? 0 : 1 << layer;
        }
    }
}
