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
        [SerializeField] private float brewSeconds = 11f;

        [Header("Quality")]
        [Tooltip("Paid per interval while the heat is in the band and the stir is going the right way.")]
        [SerializeField] private int brewBonusPoints = 2;
        [Tooltip("Charged per interval while the heat is out of band or the stir is backwards.")]
        [SerializeField] private int brewPenaltyPoints = 6;
        [SerializeField] private float deductionInterval = 1f;
        [Tooltip("Breathing room after the heat crosses a band edge, so a player who is already correcting is not charged mid-correction.")]
        [SerializeField] private float bandChangeGraceSeconds = 0.5f;

        private float currentHeat = 0.2f;       // starts at room temperature, below the band by design
        private float bandCenter = 0.55f;
        private float bandPhase;

        private float lastMouseAngleDeg;
        private bool hasLastAngle;
        private float smoothedSpinDegPerSec;
        private Vector2 mouseVelocity;
        private Vector2 lastMousePosition;

        private float nextDeductionTime;
        private bool wasInGreen = true;
        private bool mouseOverPot;
        private bool everStirred;

        // Room temperature starts below the band: the player has to warm the pot up.
        // Nothing is penalised until the first real stir, or quality would drain while
        // they were still reading the Counter (headless playtest: 25 -> 16 before the
        // player ever opened the Cauldron tab).

        public float Heat01 => currentHeat;
        public float MinOptimalHeat => Mathf.Clamp01(bandCenter - EffectiveBandHalfWidth);
        public float MaxOptimalHeat => Mathf.Clamp01(bandCenter + EffectiveBandHalfWidth);

        /// <summary>
        /// The band the player has to hold, widened or narrowed by how well the
        /// leaves were prepped. A clean mix is genuinely easier to brew; a ruined one
        /// leaves you chasing a sliver.
        /// </summary>
        private float EffectiveBandHalfWidth => bandHalfWidth * _bandScale;

        private float _bandScale = 1f;

        /// <summary>
        /// The Prep bench's gate. Stirring an empty pot does nothing at all: you crush
        /// and add the leaves first, then you stir them. Null mixture (a bare test
        /// scene, a simulation harness) is treated as ready so nothing that predates
        /// the recipe system deadlocks.
        /// </summary>
        public bool MixtureReady
        {
            get
            {
                var mix = CraftingManager.Instance != null ? CraftingManager.Instance.Mixture : null;
                return mix == null || mix.Ready;
            }
        }
        public event Action<float> OnHeatChanged;

        /// <summary>0..1 brew completion — climbs only while stirring correctly in the band.</summary>
        public float BrewProgress01 { get; private set; }

        /// <summary>Once true, quality is locked in and stirring no longer matters.</summary>
        public bool IsBrewComplete => BrewProgress01 >= 1f;

        /// <summary>True while the cursor is inside the pot — the spoon only bites here.</summary>
        public bool MouseOverCauldron => mouseOverPot;

        /// <summary>
        /// True while the player is actually standing at the Cauldron (the tab is
        /// open). The simulation keeps running either way — the pot really does cool
        /// down while you are at another bench, which is fair and visible when you
        /// come back — but nothing is SCORED off it: docking quality every second
        /// while the player is legitimately picking herbs at the Prep bench is a
        /// penalty for playing the rest of the game. Set by the Cauldron station.
        /// </summary>
        public bool Attended { get; set; }

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
        public event Action<ElementType> OnIngredientAdded;

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
            // The newest pot wins. The shop world is destroyed and rebuilt in the same
            // frame at the start of every day, and Destroy() is deferred, so the old
            // pot is still Instance when the new one wakes: "keep the first one" made
            // the NEW pot destroy itself and left the UI bound to a dying one.
            Instance = this;
        }

        // Scene-scoped singleton: null Instance on destroy so the UI can't stay bound
        // to a dead manager across a day boundary (reviewer X5).
        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        /// <summary>
        /// Called by the Prep bench once the mortar work is done: how good the mix was
        /// decides how forgiving this pot is going to be.
        /// </summary>
        public void ApplyMix(Data.MixOutcome outcome)
        {
            _bandScale = Data.RecipeBook.BandScale(outcome);
            OnHeatChanged?.Invoke(currentHeat);
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
            _bandScale = 1f;
            BrewProgress01 = 0f;
            currentHeat = 0.2f;
            bandPhase = 0f;
            bandCenter = 0.55f;
            everStirred = false;
            wasInGreen = true;
            smoothedSpinDegPerSec = 0f;
            hasLastAngle = false;
            IngredientCount = 0;
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
            // Unattended, the spoon is not in the player's hand at all — otherwise a
            // cursor resting over where the pot happens to be would stir it through
            // whatever panel is covering it. And there is nothing to stir until the
            // leaves are crushed and in.
            mouseOverPot = Attended && MixtureReady
                           && rel.sqrMagnitude <= stirringRadius * stirringRadius;

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
                smoothedSpinDegPerSec = Mathf.Lerp(smoothedSpinDegPerSec, 0f, Damp(6f));
                return;
            }

            float angle = Mathf.Atan2(rel.y, rel.x) * Mathf.Rad2Deg;
            float raw = 0f;
            if (hasLastAngle && Time.deltaTime > 0f)
                raw = Mathf.DeltaAngle(lastMouseAngleDeg, angle) / Time.deltaTime;
            lastMouseAngleDeg = angle;
            hasLastAngle = true;

            raw = Mathf.Clamp(raw, -spinForFullPower * 2f, spinForFullPower * 2f);
            smoothedSpinDegPerSec = Mathf.Lerp(smoothedSpinDegPerSec, raw, Damp(8f));
        }

        /// <summary>Frame-rate independent smoothing factor for a Lerp toward a target.
        /// <c>Lerp(a, b, dt * k)</c> converges at a different speed at 30 and 144 fps.</summary>
        private static float Damp(float rate) => 1f - Mathf.Exp(-rate * Time.deltaTime);

        private void FixedUpdate()
        {
            PhysicsStepCount++;
            ApplyStirringForces();
        }

        private readonly Collider2D[] _stirHits = new Collider2D[32];

        private void ApplyStirringForces()
        {
            float power = StirPower01;
            if (!mouseOverPot || power < 0.02f) return;

            var filter = new ContactFilter2D { useTriggers = false };
            filter.SetLayerMask(herbLayerMask);
            int n = Physics2D.OverlapCircle(transform.position, stirringRadius, filter, _stirHits);

            // The swirl follows the spoon: anticlockwise for a positive spin, clockwise
            // for a negative one. It used to be anticlockwise always, so on a clockwise
            // day the herbs turned against the spoon. Power comes from the smoothed
            // spin, not raw mouse speed, so a flick across the rim does not fling them.
            float turn = Mathf.Sign(smoothedSpinDegPerSec);
            for (int i = 0; i < n; i++)
            {
                Rigidbody2D rb = _stirHits[i].attachedRigidbody;
                if (rb == null) continue;

                Vector2 offset = rb.position - (Vector2)transform.position;
                float distance = offset.magnitude;
                if (distance <= 0.05f) continue;

                Vector2 tangent = new Vector2(-offset.y, offset.x) / distance * turn;
                float forceScale = (1f / (distance + 0.5f)) * stirForceMultiplier;
                rb.AddForce(tangent * power * 6f * forceScale, ForceMode2D.Force);
                // Pull toward the middle so the vortex holds them in rather than
                // throwing them out past the rim.
                rb.AddForce(-offset / distance * power * forceScale * 1.5f, ForceMode2D.Force);
                // Spin with the stir, kept well under the solver's 360°/step cap.
                if (Mathf.Abs(rb.angularVelocity) < 540f)
                    rb.AddTorque(turn * power * torqueMultiplier * rb.inertia * 60f, ForceMode2D.Force);
            }
        }

        private void CheckHeatQualityImpact()
        {
            ActiveOrder activeOrder = CraftingManager.Instance != null ? CraftingManager.Instance.CurrentOrder : null;
            if (activeOrder == null) return;
            if (!Attended) return;        // not at this bench — see Attended
            if (!MixtureReady) return;    // nothing in the pot yet — see MixtureReady
            if (IsBrewComplete) return;   // quality is locked; the player just has to send it
            if (!everStirred) return;     // a cold pot nobody has touched isn't a mistake

            bool inGreen = currentHeat >= MinOptimalHeat && currentHeat <= MaxOptimalHeat;
            if (inGreen != wasInGreen)
            {
                wasInGreen = inGreen;
                nextDeductionTime = Mathf.Max(nextDeductionTime, Time.time + bandChangeGraceSeconds);
            }

            if (inGreen && StirringCorrectly)
            {
                BrewProgress01 = Mathf.Clamp01(BrewProgress01 + Time.deltaTime / Mathf.Max(1f, brewSeconds));

                if (Time.time >= nextDeductionTime)
                {
                    activeOrder.ApplyBonus(brewBonusPoints, "Cauldron",
                        $"Held the band at {currentHeat:P0}", Time.time);
                    nextDeductionTime = Time.time + deductionInterval;
                }
                return;
            }

            if (Time.time < nextDeductionTime) return;

            int penalty = brewPenaltyPoints;
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
