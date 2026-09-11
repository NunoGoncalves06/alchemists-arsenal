using UnityEngine;
using System;
using AlchemistsArsenal.Systems;

namespace AlchemistsArsenal.Crafting
{
    public class PhysicsCauldronManager : MonoBehaviour
    {
        public static PhysicsCauldronManager Instance { get; private set; }

        [Header("Stirring Physics Settings")]
        [SerializeField] private float stirringRadius = 3f;
        [SerializeField] private float stirForceMultiplier = 15f;
        [SerializeField] private float torqueMultiplier = 8f;
        [Tooltip("Mouse world-speed that counts as a full-power stir. Higher = less twitchy.")]
        [SerializeField] private float stirSpeedForFullPower = 7f;
        [SerializeField] private LayerMask herbLayerMask;

        [Header("Heat Mechanics Settings")]
        [SerializeField] private float heatGainRate = 0.45f;
        [SerializeField] private float heatDecayRate = 0.28f;
        [Range(0f, 1f)] [SerializeField] private float minOptimalHeat = 0.4f;
        [Range(0f, 1f)] [SerializeField] private float maxOptimalHeat = 0.7f;

        [Header("Brew")]
        [Tooltip("Seconds of green-zone stirring to finish the brew. When done, quality locks.")]
        [SerializeField] private float brewSeconds = 12f;

        [Header("Quality Penalty Settings")]
        [SerializeField] private int baseDeductionPoints = 5;
        [SerializeField] private float deductionInterval = 1f;

        private float currentHeat = 0.2f; // Starts at 20% room temperature
        private Vector2 lastMousePosition;
        private Vector3 mouseVelocity;
        private float smoothedStirSpeed;
        private float nextDeductionTime;
        private bool mouseOverPot;

        // Public properties and events for UI/Presentation
        public float Heat01 => currentHeat;
        public float MinOptimalHeat => minOptimalHeat;
        public float MaxOptimalHeat => maxOptimalHeat;
        public Action<float> OnHeatChanged;

        /// <summary>0..1 brew completion — climbs only while stirring in the green band.</summary>
        public float BrewProgress01 { get; private set; }

        /// <summary>Once true, quality is locked in and stirring no longer matters.</summary>
        public bool IsBrewComplete => BrewProgress01 >= 1f;

        /// <summary>True while the cursor is inside the pot — stirring only bites here (the "spoon").</summary>
        public bool MouseOverCauldron => mouseOverPot;

        /// <summary>
        /// Number of physics steps this manager has processed. Lets tests / tooling
        /// confirm the FixedUpdate simulation is still running (e.g. after a UI tab
        /// switch hides the cauldron panel).
        /// </summary>
        public long PhysicsStepCount { get; private set; }

        /// <summary>
        /// Directly set the brew heat (0..1). Intended for biome ambient modifiers,
        /// scripted events, and simulation tests — normal play changes heat by stirring.
        /// </summary>
        public void SetHeat(float value01)
        {
            currentHeat = Mathf.Clamp01(value01);
            OnHeatChanged?.Invoke(currentHeat);
        }

        private void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
            }
            else
            {
                Destroy(gameObject);
            }
        }

        // Scene-scoped singleton: null Instance on destroy so an additive Shop
        // reload can't leave CauldronUI bound to a dead manager (reviewer X5).
        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        /// <summary>Code-wire the herb layer (ShopWorld builds herbs at runtime).</summary>
        public void Configure(LayerMask herbLayers)
        {
            herbLayerMask = herbLayers;
        }

        private void Start()
        {
            lastMousePosition = GetMouseWorldPosition();
        }

        private void Update()
        {
            // Gather input and calculate mouse velocity
            Vector2 currentMousePos = GetMouseWorldPosition();
            if (Time.deltaTime > 0)
            {
                // Mouse velocity in world space
                mouseVelocity = (Vector3)(currentMousePos - lastMousePosition) / Time.deltaTime;
            }
            lastMousePosition = currentMousePos;

            // The spoon only stirs when the cursor is actually in the pot.
            mouseOverPot = ((Vector2)transform.position - currentMousePos).sqrMagnitude <= stirringRadius * stirringRadius;

            // Normalised stir power (0..1). Clamped so a fast flick can't spike the
            // heat — a 2 cm twitch is a small nudge, not a jump to max.
            float rawStirSpeed = mouseOverPot ? mouseVelocity.magnitude : 0f;
            smoothedStirSpeed = Mathf.Lerp(smoothedStirSpeed, rawStirSpeed, Time.deltaTime * 6f);
            float stirPower = Mathf.Clamp01(smoothedStirSpeed / Mathf.Max(0.01f, stirSpeedForFullPower));

            // Heat dynamics: rises with stir power, decays when the spoon is idle.
            currentHeat += (stirPower > 0.05f ? stirPower * heatGainRate : -heatDecayRate) * Time.deltaTime;
            currentHeat = Mathf.Clamp01(currentHeat);
            OnHeatChanged?.Invoke(currentHeat);

            // Handle potion quality change based on heat score
            CheckHeatQualityImpact(stirPower);
        }

        private void FixedUpdate()
        {
            PhysicsStepCount++;

            // Apply physical forces to herbs inside the cauldron trigger zone
            ApplyStirringForces();
        }

        private void ApplyStirringForces()
        {
            // Only apply force if the spoon is in the pot and actually moving.
            if (!mouseOverPot || mouseVelocity.sqrMagnitude < 0.1f) return;

            // Find all herb rigidbodies in the stirring radius
            Collider2D[] colliders = Physics2D.OverlapCircleAll(transform.position, stirringRadius, herbLayerMask);

            foreach (var col in colliders)
            {
                Rigidbody2D rb = col.GetComponent<Rigidbody2D>();
                if (rb != null)
                {
                    Vector2 herbPos = rb.transform.position;
                    Vector2 cauldronCenter = transform.position;
                    Vector2 offset = herbPos - cauldronCenter;
                    float distance = offset.magnitude;

                    if (distance > 0.05f)
                    {
                        // 1. Tangential swirling force (perpendicular to radial vector)
                        Vector2 tangent = new Vector2(-offset.y, offset.x).normalized;
                        // Scale force by mouse velocity and distance falloff (stronger near center/paddle)
                        float forceScale = (1f / (distance + 0.5f)) * stirForceMultiplier;
                        Vector2 stirForce = tangent * mouseVelocity.magnitude * forceScale;

                        rb.AddForce(stirForce, ForceMode2D.Force);

                        // 2. Direct physical torque to rotate individual herb bodies
                        // Rotate herb in direction of the stir
                        float stirDirection = Vector3.Cross(offset.normalized, mouseVelocity.normalized).z;
                        float appliedTorque = stirDirection * mouseVelocity.magnitude * torqueMultiplier;
                        
                        rb.AddTorque(appliedTorque, ForceMode2D.Force);
                    }
                }
            }
        }

        private void CheckHeatQualityImpact(float stirPower)
        {
            ActiveOrder activeOrder = CraftingManager.Instance != null ? CraftingManager.Instance.CurrentOrder : null;
            if (activeOrder == null) return;

            // Brew is finished — quality is locked, the player just needs to send it.
            if (IsBrewComplete) return;

            bool inGreen = currentHeat >= minOptimalHeat && currentHeat <= maxOptimalHeat;
            bool stirring = stirPower > 0.05f;

            if (inGreen && stirring)
            {
                // Green band + stirring: the brew advances and quality climbs from
                // the born-at-25 floor (reviewer X4). Progress gives the minigame a
                // definite end instead of stirring forever.
                BrewProgress01 = Mathf.Clamp01(BrewProgress01 + Time.deltaTime / Mathf.Max(1f, brewSeconds));

                if (Time.time >= nextDeductionTime)
                {
                    activeOrder.ApplyBonus(baseDeductionPoints, "Cauldron Brewing",
                        $"Held the green zone ({currentHeat:P0})", Time.time);
                    nextDeductionTime = Time.time + deductionInterval;
                }
                return;
            }

            if (!inGreen && Time.time >= nextDeductionTime)
            {
                int penalty = baseDeductionPoints;
                string reason;
                if (currentHeat < minOptimalHeat)
                {
                    reason = $"Too cold ({currentHeat:P0}) — stir faster";
                    penalty += Mathf.RoundToInt((minOptimalHeat - currentHeat) / minOptimalHeat * 4f);
                }
                else
                {
                    reason = $"Overheating ({currentHeat:P0}) — ease off";
                    penalty += Mathf.RoundToInt((currentHeat - maxOptimalHeat) / (1f - maxOptimalHeat) * 6f);
                }

                activeOrder.ApplyDeduction(penalty, "Cauldron Brewing", reason, Time.time);
                nextDeductionTime = Time.time + deductionInterval;
            }
        }

        private Vector2 GetMouseWorldPosition()
        {
            if (Camera.main == null) return Vector2.zero;
            Vector3 mouseScreenPos = Input.mousePosition;
            mouseScreenPos.z = Mathf.Abs(Camera.main.transform.position.z);
            return Camera.main.ScreenToWorldPoint(mouseScreenPos);
        }

        private void OnDrawGizmosSelected()
        {
            // Visualize stirring area and optimal range in Editor
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(transform.position, stirringRadius);

            // Optimal heat range indicators
            Gizmos.color = Color.blue;
            Gizmos.DrawWireSphere(transform.position, stirringRadius * minOptimalHeat);
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(transform.position, stirringRadius * maxOptimalHeat);
        }
    }
}
