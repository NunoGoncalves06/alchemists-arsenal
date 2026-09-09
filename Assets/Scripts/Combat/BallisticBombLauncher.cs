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

        [Tooltip("Leave 0 to auto-resolve to the project's combat layers (see CombatLayers).")]
        [SerializeField] private LayerMask detonationMask = 0;

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

        /// <summary>Wire the launcher in code (bootstrap).</summary>
        public void Configure(UtilityAI_CombatController controller, ElementalMatrix matrix, LayerMask mask = default)
        {
            this.controller = controller;
            elementalMatrix = matrix;
            if (mask.value != 0) detonationMask = mask;
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
            go.SetActive(false);
            go.transform.position = origin;

            var rb = go.AddComponent<Rigidbody2D>();
            rb.gravityScale = 1f;

            var col = go.AddComponent<CircleCollider2D>();
            col.radius = 0.16f;
            col.isTrigger = true;

            PlaceholderArt.AddRenderer(go, PlaceholderArt.Shape.Disc, new Color(0.95f, 0.9f, 0.5f), 8);
            go.transform.localScale = Vector3.one * 0.5f;

            var projectile = go.AddComponent<BombProjectile2D>();
            go.SetActive(true);
            return projectile;
        }
    }
}
