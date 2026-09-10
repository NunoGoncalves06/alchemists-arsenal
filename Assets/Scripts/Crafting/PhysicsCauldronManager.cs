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
        [SerializeField] private LayerMask herbLayerMask;

        [Header("Heat Mechanics Settings")]
        [SerializeField] private float heatGainRate = 0.25f;
        [SerializeField] private float heatDecayRate = 0.12f;
        [Range(0f, 1f)] [SerializeField] private float minOptimalHeat = 0.4f;
        [Range(0f, 1f)] [SerializeField] private float maxOptimalHeat = 0.7f;

        [Header("Quality Penalty Settings")]
        [SerializeField] private int baseDeductionPoints = 5;
        [SerializeField] private float deductionInterval = 1f;

        private float currentHeat = 0.2f; // Starts at 20% room temperature
        private Vector2 lastMousePosition;
        private Vector3 mouseVelocity;
        private float smoothedStirSpeed;
        private float nextDeductionTime;

        // Public properties and events for UI/Presentation
        public float Heat01 => currentHeat;
        public float MinOptimalHeat => minOptimalHeat;
        public float MaxOptimalHeat => maxOptimalHeat;
        public Action<float> OnHeatChanged;

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

            // Smooth the stirring speed based on mouse movement speed
            float rawStirSpeed = mouseVelocity.magnitude;
            smoothedStirSpeed = Mathf.Lerp(smoothedStirSpeed, rawStirSpeed, Time.deltaTime * 5f);

            // Heat dynamics: increase with stirring, decay with lack thereof
            if (smoothedStirSpeed > 1f)
            {
                // Stirring generates heat proportionally
                currentHeat += (smoothedStirSpeed * 0.02f + heatGainRate) * Time.deltaTime;
            }
            else
            {
                // Decay heat when not stirring
                currentHeat -= heatDecayRate * Time.deltaTime;
            }

            currentHeat = Mathf.Clamp01(currentHeat);
            OnHeatChanged?.Invoke(currentHeat);

            // Handle potion quality deduction based on heat score
            CheckHeatQualityImpact();
        }

        private void FixedUpdate()
        {
            PhysicsStepCount++;

            // Apply physical forces to herbs inside the cauldron trigger zone
            ApplyStirringForces();
        }

        private void ApplyStirringForces()
        {
            // Only apply force if user is actively moving the mouse/stirring
            if (mouseVelocity.sqrMagnitude < 0.1f) return;

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

        private void CheckHeatQualityImpact()
        {
            ActiveOrder activeOrder = CraftingManager.Instance != null ? CraftingManager.Instance.CurrentOrder : null;
            if (activeOrder == null) return;

            // Check if heat is out of bounds
            bool isTooCold = currentHeat < minOptimalHeat;
            bool isTooHot = currentHeat > maxOptimalHeat;

            if (isTooCold || isTooHot)
            {
                if (Time.time >= nextDeductionTime)
                {
                    int finalDeduction = baseDeductionPoints;
                    string reason = "";

                    if (isTooCold)
                    {
                        reason = $"Brewing temperature too cold ({currentHeat:P0} < {minOptimalHeat:P0})";
                        // Scaled penalty based on how far off it is
                        float severity = (minOptimalHeat - currentHeat) / minOptimalHeat;
                        finalDeduction += Mathf.RoundToInt(severity * 5f);
                    }
                    else if (isTooHot)
                    {
                        reason = $"Brewing temperature too hot ({currentHeat:P0} > {maxOptimalHeat:P0})";
                        float severity = (currentHeat - maxOptimalHeat) / (1f - maxOptimalHeat);
                        finalDeduction += Mathf.RoundToInt(severity * 10f); // Overheating is more penalizing!
                    }

                    activeOrder.ApplyDeduction(finalDeduction, "Cauldron Brewing", reason, Time.time);
                    Debug.LogWarning($"[QUALITY PENALTY] {reason}. Deducted {finalDeduction} points. New Quality: {activeOrder.qualityScore}");

                    nextDeductionTime = Time.time + deductionInterval;
                }
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
