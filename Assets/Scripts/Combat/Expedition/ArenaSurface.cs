using UnityEngine;
using AlchemistsArsenal.PhysicsKit;

namespace AlchemistsArsenal.Combat
{
    /// <summary>
    /// What the ground of a road does to whoever fights on it. Two regions have a
    /// floor that matters, and both are plain physics rather than scripted
    /// slowdowns:
    /// <list type="bullet">
    /// <item><b>Frostbite Caverns: ice.</b> Everyone's grip (steering force) is cut
    /// and their damping lowered, so bodies are slow to start, slow to stop, and a
    /// blast sends them sliding a long way.</item>
    /// <item><b>Venom Swamp: bog pools.</b> <see cref="AreaEffector2D"/> drag fields
    /// on the pools the swamp's floor is drawn with (<see cref="Art.BiomeArt.BogPools"/>),
    /// so wading through one is slow for hero and monster alike.</item>
    /// </list>
    /// Gravity wells and geysers were left out on purpose: anything that bends a
    /// projectile's path would make the heroes' <see cref="BallisticSolver"/> aim
    /// wrong. Projectiles never touch either surface.
    /// </summary>
    public static class ArenaSurface
    {
        /// <summary>Fraction of normal grip on this ground.</summary>
        public static float Traction(ElementType theme) => theme == ElementType.Water ? 0.4f : 1f;

        /// <summary>Fraction of normal damping on this ground.</summary>
        public static float Damping(ElementType theme) => theme == ElementType.Water ? 0.35f : 1f;

        /// <summary>Put a freshly built combatant on this ground.</summary>
        public static void Apply(GameObject body, ElementType theme)
        {
            if (body == null) return;
            float grip = Traction(theme), damp = Damping(theme);
            if (Mathf.Approximately(grip, 1f) && Mathf.Approximately(damp, 1f)) return;

            var rb = body.GetComponent<Rigidbody2D>();
            if (rb != null)
            {
                rb.linearDamping *= damp;
                foreach (var col in body.GetComponents<Collider2D>()) col.sharedMaterial = PhysicsMaterials.Ice;
            }
            var walker = body.GetComponent<MonsterWalker>();
            if (walker != null) walker.Traction = grip;
            var mover = body.GetComponent<AdventurerMovementController>();
            if (mover != null) mover.Traction = grip;
        }

        /// <summary>Lay the swamp's bog pools into <paramref name="world"/> as drag fields.</summary>
        public static void BuildBogs(Transform world)
        {
            foreach (var (center, radius) in Art.BiomeArt.BogPools)
            {
                var go = new GameObject("BogPool");
                go.transform.SetParent(world, false);
                go.transform.position = center;
                // The pools are drawn as flat ellipses: a horizontal capsule matches them.
                var cap = go.AddComponent<CapsuleCollider2D>();
                cap.direction = CapsuleDirection2D.Horizontal;
                cap.size = new Vector2(radius * 2.1f, radius * 1.15f);
                cap.isTrigger = true;
                cap.usedByEffector = true;
                var bog = go.AddComponent<AreaEffector2D>();
                bog.useColliderMask = true;
                bog.colliderMask = GameLayers.CombatantMask;
                bog.forceMagnitude = 0f;
                bog.linearDamping = 7f;
                GameLayers.Assign(go, GameLayers.ArenaBounds);
            }
        }
    }
}
