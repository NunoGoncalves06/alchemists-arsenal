using UnityEngine;

namespace AlchemistsArsenal.PhysicsKit
{
    /// <summary>
    /// The project's physics layers and who touches whom.
    ///
    /// These are fixed layer <b>indices</b>, deliberately not names looked up from
    /// TagManager. The old CombatLayers looked for layers called "Combatants" etc.,
    /// none of which existed, so every combat overlap silently fell back to "all
    /// layers": bombs detonated on arena walls and on each other, and knockback
    /// pushed other bombs around. Unity layers 8..31 work unnamed, so nothing here
    /// depends on a ProjectSettings edit (which an open Editor can overwrite).
    /// Name them in Project Settings > Tags and Layers if you like; it is cosmetic.
    ///
    /// The collision matrix is applied per collider through
    /// <see cref="Collider2D.excludeLayers"/> by <see cref="Assign"/>, not through the
    /// global Physics2D matrix: a global change made during Play in the Editor can be
    /// written back into Physics2DSettings.asset.
    /// </summary>
    public static class GameLayers
    {
        public const int Combatant = 8;
        public const int Projectile = 9;
        public const int ArenaBounds = 10;
        /// <summary>Loose shop objects: leaves, the pestle, corks, coins.</summary>
        public const int ShopProp = 11;
        /// <summary>Fixed shop geometry: the pot's walls, the mortar bowl, the flask glass.</summary>
        public const int ShopStatic = 12;
        /// <summary>Poured liquid droplets.</summary>
        public const int Liquid = 13;
        /// <summary>Visual-only physics chunks (boss shards, corpses): they bounce off walls, not people.</summary>
        public const int Debris = 14;

        public static int MaskOf(int layer) => 1 << layer;
        public static int CombatantMask => 1 << Combatant;
        public static int ShopPropMask => 1 << ShopProp;
        public static int ShopStaticMask => 1 << ShopStatic;
        public static int LiquidMask => 1 << Liquid;

        /// <summary>Every layer a collider on <paramref name="layer"/> is allowed to contact.</summary>
        public static int ContactsOf(int layer)
        {
            switch (layer)
            {
                case Combatant:   return MaskOf(Combatant) | MaskOf(ArenaBounds) | MaskOf(Projectile);
                case Projectile:  return MaskOf(Combatant);
                case ArenaBounds: return MaskOf(Combatant) | MaskOf(Debris);
                case ShopProp:    return MaskOf(ShopProp) | MaskOf(ShopStatic) | MaskOf(Liquid);
                case ShopStatic:  return MaskOf(ShopProp) | MaskOf(Liquid);
                case Liquid:      return MaskOf(ShopStatic) | MaskOf(ShopProp) | MaskOf(Liquid);
                case Debris:      return MaskOf(ArenaBounds) | MaskOf(Debris);
                default:          return ~0;
            }
        }

        /// <summary>
        /// Put <paramref name="go"/> (and, by default, its children) on
        /// <paramref name="layer"/> and make every collider there exclude the layers
        /// it should not touch. Call after the colliders are added.
        /// </summary>
        public static void Assign(GameObject go, int layer, bool includeChildren = true)
        {
            if (go == null) return;
            go.layer = layer;
            int exclude = ~ContactsOf(layer);
            if (includeChildren)
            {
                foreach (Transform t in go.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = layer;
                foreach (Collider2D c in go.GetComponentsInChildren<Collider2D>(true)) c.excludeLayers = exclude;
            }
            else
            {
                foreach (Collider2D c in go.GetComponents<Collider2D>()) c.excludeLayers = exclude;
            }
        }
    }
}
