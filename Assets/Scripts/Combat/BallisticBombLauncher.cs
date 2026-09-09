using UnityEngine;
using AlchemistsArsenal.Data;

namespace AlchemistsArsenal.Combat
{
    /// <summary>
    /// Bridges the utility AI to the physics world. Subscribes to
    /// <see cref="UtilityAI_CombatController.OnBombThrowRequested"/> and, for each
    /// request, spawns a <see cref="BombProjectile2D"/> and arms it. All motion is
    /// the projectile's Rigidbody2D — this class only places the spawn.
    /// </summary>
    [RequireComponent(typeof(Rigidbody2D))]
    public class BallisticBombLauncher : MonoBehaviour, IBombLauncher
    {
        [Header("Wiring")]
        [SerializeField] private UtilityAI_CombatController controller;
        [SerializeField] private BombProjectile2D projectilePrefab;
        [SerializeField] private ElementalMatrix elementalMatrix;

        [Tooltip("Optional spawn point. Falls back to the request's Origin.")]
        [SerializeField] private Transform muzzle;

        [Tooltip("Layers the detonation overlap check considers.")]
        [SerializeField] private LayerMask detonationMask = ~0;

        private void Reset() => controller = GetComponentInParent<UtilityAI_CombatController>();

        private void Awake()
        {
            if (controller == null) controller = GetComponentInParent<UtilityAI_CombatController>();
        }

        private void OnEnable()
        {
            if (controller != null) controller.OnBombThrowRequested += Launch;
        }

        private void OnDisable()
        {
            if (controller != null) controller.OnBombThrowRequested -= Launch;
        }

        public void Launch(BombThrowRequest request)
        {
            if (request.Bomb == null) return;

            Vector2 origin = muzzle != null ? (Vector2)muzzle.position : request.Origin;
            BombProjectile2D projectile = SpawnProjectile(origin);
            projectile.Configure(in request, elementalMatrix, detonationMask);
        }

        private BombProjectile2D SpawnProjectile(Vector2 origin)
        {
            if (projectilePrefab != null)
                return Instantiate(projectilePrefab, origin, Quaternion.identity);

            // No prefab assigned — build a minimal projectile at runtime.
            var go = new GameObject("BombProjectile2D");
            var rb = go.AddComponent<Rigidbody2D>();
            rb.position = origin;                 // physics-space placement, not transform
            rb.gravityScale = 1f;
            var col = go.AddComponent<CircleCollider2D>();
            col.radius = 0.15f;
            return go.AddComponent<BombProjectile2D>();
        }
    }
}
