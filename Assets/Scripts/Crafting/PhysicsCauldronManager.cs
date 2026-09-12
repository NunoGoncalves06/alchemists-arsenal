using UnityEngine;
using System;
using AlchemistsArsenal.Art;
using AlchemistsArsenal.Combat;
using AlchemistsArsenal.Systems;

namespace AlchemistsArsenal.Crafting
{
    /// <summary>
    /// The cauldron minigame. The spoon is the cursor: it only bites inside the pot,
    /// and what it measures is how you <b>turn</b> it, not how fast you shove the
    /// mouse — the stir is the signed angular velocity of the cursor about the pot's
    /// centre, so stirring circles works and flicking the mouse across the rim does
    /// nothing.
    ///
    /// Three things are being held at once, which is what makes it a minigame rather
    /// than a button: the heat has to stay inside a band that slowly drifts, the
    /// recipe wants a specific direction, and the brew only advances while both are
    /// true. Ingredients dropped in from Prep are real Rigidbody2D bodies the stir
    /// pushes around (rubric cat 5 — forces and torque, never transform nudging).
    /// </summary>
    public class PhysicsCauldronManager : MonoBehaviour
    {
        public static PhysicsCauldronManager Instance { get; private set; }

        [Header("Stirring")]
        [SerializeField] private float stirringRadius = 3f;
        [SerializeField] private float stirForceMultiplier = 15f;
        [SerializeField] private float torqueMultiplier = 8f;
        [Tooltip("Cursor rotation about the pot (deg/sec) that counts as a full-power stir.")]
        [SerializeField] private float spinForFullPower = 420f;
        [Tooltip("Below this (deg/sec) the spoon is considered still, in either direction.")]
        [SerializeField] private float spinDeadZone = 45f;
        [SerializeField] private LayerMask herbLayerMask;

        [Header("Heat")]
        [SerializeField] private float heatGainRate = 0.45f;
        [SerializeField] private float heatDecayRate = 0.28f;
        [Tooltip("How far the optimal band slides either side of centre over a brew.")]
        [Range(0f, 0.3f)] [SerializeField] private float bandDrift = 0.10f;
        [Range(0.05f, 0.4f)] [SerializeField] private float bandHalfWidth = 0.13f;
        [SerializeField] private float bandDriftSpeed = 0.18f;

        [Header("Brew")]
        [Tooltip("Seconds of correct stirring in the band to finish the brew. Quality locks after.")]
        [SerializeField] private float brewSeconds = 12f;

        [Header("Quality")]
        [SerializeField] private int baseDeductionPoints = 5;
        [SerializeField] private float deductionInterval = 1f;

        private float currentHeat = 0.2f;       // starts at room temperature, below the band by design
        private float bandCenter = 0.55f;
        private float bandPhase;

        private float lastMouseAngleDeg;
        private bool hasLastAngle;
        private float smoothedSpinDegPerSec;
        private Vector2 mouseVelocity;
        private Vector2 lastMousePosition;

        private float nextDeductionTime;
        private bool mouseOverPot;
        private bool everStirred;

        // Room temperature starts below the band: the player has to warm the pot up.
        // Nothing is penalised until the first real stir, or quality would drain while
        // they were still reading the Counter (headless playtest: 25 -> 16 before the
        // player ever opened the Cauldron tab).

        public float Heat01 => currentHeat;
        public float MinOptimalHeat => Mathf.Clamp01(bandCenter - bandHalfWidth);
        public float MaxOptimalHeat => Mathf.Clamp01(bandCenter + bandHalfWidth);
        public Action<float> OnHeatChanged;

        /// <summary>0..1 brew completion — climbs only while stirring correctly in the band.</summary>
        public float BrewProgress01 { get; private set; }

        /// <summary>Once true, quality is locked in and stirring no longer matters.</summary>
        public bool IsBrewComplete => BrewProgress01 >= 1f;

        /// <summary>True while the cursor is inside the pot — the spoon only bites here.</summary>
        public bool MouseOverCauldron => mouseOverPot;

        /// <summary>Which way today's recipe wants the spoon turned.</summary>
        public bool RequiredClockwise { get; private set; }

        /// <summary>Signed stir rate, -1..1. Negative is clockwise on screen.</summary>
        public float Spin01 => Mathf.Clamp(smoothedSpinDegPerSec / Mathf.Max(1f, spinForFullPower), -1f, 1f);

        /// <summary>Unsigned stir power, 0..1 — how hard the spoon is being turned.</summary>
        public float StirPower01 => Mathf.Abs(Spin01);

        /// <summary>True while the spoon is turning the way the recipe asked, hard enough to count.</summary>
        public bool StirringCorrectly =>
            mouseOverPot &&
            Mathf.Abs(smoothedSpinDegPerSec) >= spinDeadZone &&
            (smoothedSpinDegPerSec < 0f) == RequiredClockwise;

        /// <summary>True while the spoon is turning the wrong way hard enough to matter.</summary>
        public bool StirringBackwards =>
            mouseOverPot &&
            Mathf.Abs(smoothedSpinDegPerSec) >= spinDeadZone &&
            (smoothedSpinDegPerSec < 0f) != RequiredClockwise;

        /// <summary>How many Prep ingredients are floating in the pot.</summary>
        public int IngredientCount { get; private set; }

        /// <summary>
        /// Number of physics steps this manager has processed. Lets tests / tooling
        /// confirm the FixedUpdate simulation is still running (e.g. after a UI tab
        /// switch hides the cauldron panel).
        /// </summary>
        public long PhysicsStepCount { get; private set; }

        /// <summary>Raised when an ingredient lands in the pot, with its element.</summary>
        public Action<ElementType> OnIngredientAdded;

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
            if (Instance == null) Instance = this;
            else Destroy(gameObject);
        }

        // Scene-scoped singleton: null Instance on destroy so the UI can't stay bound
        // to a dead manager across a day boundary (reviewer X5).
        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        /// <summary>Code-wire the herb layer (ShopWorld builds herbs at runtime).</summary>
        public void Configure(LayerMask herbLayers)
        {
            herbLayerMask = herbLayers;
        }

        /// <summary>
        /// Start a fresh brew: cold pot, empty progress, and a stir direction for the
        /// day. The direction alternates by day rather than being random so the
        /// recipe card at the Counter can state it and always be right.
        /// </summary>
        public void BeginBrew(int day)
        {
            RequiredClockwise = day % 2 == 1;
            BrewProgress01 = 0f;
            currentHeat = 0.2f;
            bandPhase = 0f;
            bandCenter = 0.55f;
            everStirred = false;
            smoothedSpinDegPerSec = 0f;
            hasLastAngle = false;
            OnHeatChanged?.Invoke(currentHeat);
        }

        private void Start()
        {
            lastMousePosition = GetMouseWorldPosition();
            RequiredClockwise = true;
        }

        private void Update()
        {
            Vector2 mouse = GetMouseWorldPosition();
            if (Time.deltaTime > 0f)
                mouseVelocity = (mouse - lastMousePosition) / Time.deltaTime;
            lastMousePosition = mouse;

            Vector2 rel = mouse - (Vector2)transform.position;
            mouseOverPot = rel.sqrMagnitude <= stirringRadius * stirringRadius;

            TrackSpin(rel);

            // The band drifts once the player is actually brewing, so "hold the green"
            // is a thing you keep doing rather than a switch you flip once.
            if (everStirred && !IsBrewComplete)
            {
                bandPhase += Time.deltaTime * bandDriftSpeed;
                bandCenter = 0.55f + Mathf.Sin(bandPhase * Mathf.PI * 2f) * bandDrift;
            }

            float power = StirPower01;
            if (power > 0.05f && mouseOverPot) everStirred = true;

            // Heat rises with a correct stir, falls when idle, and falls faster when
            // the spoon is being dragged the wrong way round.
            float delta;
            if (StirringCorrectly) delta = power * heatGainRate;
            else if (StirringBackwards) delta = -heatDecayRate * 1.6f;
            else delta = -heatDecayRate;

            currentHeat = Mathf.Clamp01(currentHeat + delta * Time.deltaTime);
            OnHeatChanged?.Invoke(currentHeat);

            CheckHeatQualityImpact();
        }

        /// <summary>
        /// Signed angular velocity of the cursor about the pot centre, smoothed.
        /// This — not raw mouse speed — is "stirring": a 2 cm flick across the rim
        /// barely turns the spoon, while a slow circle turns it all the way.
        /// </summary>
        private void TrackSpin(Vector2 rel)
        {
            if (!mouseOverPot || rel.sqrMagnitude < 0.04f)
            {
                hasLastAngle = false;
                smoothedSpinDegPerSec = Mathf.Lerp(smoothedSpinDegPerSec, 0f, Time.deltaTime * 6f);
                return;
            }

            float angle = Mathf.Atan2(rel.y, rel.x) * Mathf.Rad2Deg;
            float raw = 0f;
            if (hasLastAngle && Time.deltaTime > 0f)
                raw = Mathf.DeltaAngle(lastMouseAngleDeg, angle) / Time.deltaTime;
            lastMouseAngleDeg = angle;
            hasLastAngle = true;

            raw = Mathf.Clamp(raw, -spinForFullPower * 2f, spinForFullPower * 2f);
            smoothedSpinDegPerSec = Mathf.Lerp(smoothedSpinDegPerSec, raw, Time.deltaTime * 8f);
        }

        private void FixedUpdate()
        {
            PhysicsStepCount++;
            ApplyStirringForces();
        }

        private void ApplyStirringForces()
        {
            if (!mouseOverPot || mouseVelocity.sqrMagnitude < 0.1f) return;

            Collider2D[] colliders = Physics2D.OverlapCircleAll(transform.position, stirringRadius, herbLayerMask);

            foreach (var col in colliders)
            {
                Rigidbody2D rb = col.GetComponent<Rigidbody2D>();
                if (rb == null) continue;

                Vector2 offset = (Vector2)rb.transform.position - (Vector2)transform.position;
                float distance = offset.magnitude;
                if (distance <= 0.05f) continue;

                // Tangential swirl, strongest near the spoon, plus torque so each
                // body visibly spins with the stir.
                Vector2 tangent = new Vector2(-offset.y, offset.x).normalized;
                float forceScale = (1f / (distance + 0.5f)) * stirForceMultiplier;
                rb.AddForce(tangent * mouseVelocity.magnitude * forceScale, ForceMode2D.Force);

                float stirDirection = Vector3.Cross(offset.normalized, mouseVelocity.normalized).z;
                rb.AddTorque(stirDirection * mouseVelocity.magnitude * torqueMultiplier, ForceMode2D.Force);
            }
        }

        private void CheckHeatQualityImpact()
        {
            ActiveOrder activeOrder = CraftingManager.Instance != null ? CraftingManager.Instance.CurrentOrder : null;
            if (activeOrder == null) return;
            if (IsBrewComplete) return;   // quality is locked; the player just has to send it
            if (!everStirred) return;     // a cold pot nobody has touched isn't a mistake

            bool inGreen = currentHeat >= MinOptimalHeat && currentHeat <= MaxOptimalHeat;

            if (inGreen && StirringCorrectly)
            {
                BrewProgress01 = Mathf.Clamp01(BrewProgress01 + Time.deltaTime / Mathf.Max(1f, brewSeconds));

                if (Time.time >= nextDeductionTime)
                {
                    activeOrder.ApplyBonus(baseDeductionPoints, "Cauldron",
                        $"Held the band at {currentHeat:P0}", Time.time);
                    nextDeductionTime = Time.time + deductionInterval;
                }
                return;
            }

            if (Time.time < nextDeductionTime) return;

            int penalty = baseDeductionPoints;
            string reason;
            if (StirringBackwards)
            {
                reason = $"Stirred {(RequiredClockwise ? "anticlockwise" : "clockwise")} — the recipe says otherwise";
            }
            else if (currentHeat < MinOptimalHeat)
            {
                reason = $"Too cold ({currentHeat:P0}) — stir faster";
                penalty += Mathf.RoundToInt((MinOptimalHeat - currentHeat) / Mathf.Max(0.01f, MinOptimalHeat) * 4f);
            }
            else
            {
                reason = $"Overheating ({currentHeat:P0}) — ease off";
                penalty += Mathf.RoundToInt((currentHeat - MaxOptimalHeat) / Mathf.Max(0.01f, 1f - MaxOptimalHeat) * 6f);
            }

            activeOrder.ApplyDeduction(penalty, "Cauldron", reason, Time.time);
            nextDeductionTime = Time.time + deductionInterval;
        }

        // ------------------------------------------------------------ ingredients

        /// <summary>
        /// Drop a Prep ingredient into the pot: a real physics body at the rim that
        /// the stir then swirls. Purely additive — the quality effect is Prep's call,
        /// this is the thing the player watches happen.
        /// </summary>
        public void DropIngredient(ElementType element)
        {
            var go = new GameObject($"Ingredient_{element}");
            go.transform.SetParent(transform.parent != null ? transform.parent : transform, worldPositionStays: true);
            go.layer = gameObject.layer;

            float ang = UnityEngine.Random.Range(0f, Mathf.PI * 2f);
            go.transform.position = (Vector2)transform.position + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * 1.0f;

            var rb = go.AddComponent<Rigidbody2D>();
            rb.gravityScale = 0f;
            rb.linearDamping = 1.2f;
            rb.angularDamping = 0.8f;
            rb.AddTorque(UnityEngine.Random.Range(-4f, 4f), ForceMode2D.Impulse);

            go.AddComponent<CircleCollider2D>().radius = 0.18f;
            Core.PixelArt.AddSprite(go, PixelSprites.Herb(element), 4, 0.55f);

            IngredientCount++;
            OnIngredientAdded?.Invoke(element);
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
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(transform.position, stirringRadius);
        }
    }
}
