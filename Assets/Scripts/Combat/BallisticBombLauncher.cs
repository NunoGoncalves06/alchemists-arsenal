using UnityEngine;
using AlchemistsArsenal.Art;
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
            BombProjectile2D projectile = SpawnProjectile(origin, request.Bomb.Element);
            projectile.Configure(in request, elementalMatrix, detonationMask);
        }

        private BombProjectile2D SpawnProjectile(Vector2 origin, ElementType element)
        {
            if (projectilePrefab != null)
                return Instantiate(projectilePrefab, origin, Quaternion.identity);

            // No prefab assigned — build a minimal projectile at runtime.
            var go = new GameObject("BombProjectile2D");
            go.SetActive(false);
            // Under the arena root (the thrower's parent), so it is torn down with the
            // world and never drawn into the next one's first frame.
            if (transform.parent != null) go.transform.SetParent(transform.parent, false);
            go.transform.position = origin;

            var rb = go.AddComponent<Rigidbody2D>();
            rb.gravityScale = 1f;

            // Same effective trigger size as before (0.16 at a 0.5 scale); the root is
            // unscaled now and the art lives on a child.
            var col = go.AddComponent<CircleCollider2D>();
            col.radius = 0.08f;
            col.isTrigger = true;

            // The actual flask, in its element's colours, trailing its colour behind
            // it. Every throw used to be the same pale yellow disc.
            var art = new GameObject("Art");
            art.transform.SetParent(go.transform, false);
            Core.PixelArt.AddSprite(art, PixelSprites.Flask(element), 260, 0.5f);

            Color c = Core.PixelArt.Element(element);
            var trail = go.AddComponent<TrailRenderer>();
            trail.sharedMaterial = SpriteMaterials.Particle(SpriteMaterials.ParticleBlend.Alpha);
            trail.time = 0.22f;
            trail.minVertexDistance = 0.04f;
            trail.widthCurve = AnimationCurve.Linear(0f, 0.16f, 1f, 0f);
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.Lerp(c, Color.white, 0.35f), 0f), new GradientColorKey(c, 1f) },
                new[] { new GradientAlphaKey(0.85f, 0f), new GradientAlphaKey(0f, 1f) });
            trail.colorGradient = g;
            trail.sortingOrder = 259;

            var projectile = go.AddComponent<BombProjectile2D>();
            go.SetActive(true);
            return projectile;
        }
    }
}
