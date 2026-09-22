using UnityEngine;
using System;
using AlchemistsArsenal.Combat;
using AlchemistsArsenal.Data;
using AlchemistsArsenal.PhysicsKit;
using AlchemistsArsenal.Systems;

namespace AlchemistsArsenal.Crafting
{
    /// <summary>
    /// The cauldron minigame. The spoon is the pointer: it only bites inside the pot's
    /// mouth, and what it measures is how you <b>turn</b> it, not how fast you shove
    /// the mouse: the stir is the signed angular velocity of the pointer about the
    /// centre of the surface, so circles work and flicks do nothing.
    ///
    /// <para>Three things are held at once, which is what makes it a minigame rather
    /// than a button: the heat has to stay inside a band that slowly drifts, the
    /// recipe wants a direction, and the brew only advances while both are true.</para>
    ///
    /// <para><b>Heat.</b> Turning the spoon faster runs the pot hotter and easing off
    /// lets it settle: while you stir the right way, heat eases toward a level set by
    /// how hard you turn. It used to only ever climb while stirring, so "OVERHEATING —
    /// EASE OFF" could not be obeyed (easing off still heated it) and holding the band
    /// meant stopping, which was charged as a mistake.</para>
    ///
    /// <para><b>Physics feeds quality.</b> The leaves from Prep float on the surface as
    /// real bodies (<see cref="LiquidBody2D"/>). They dissolve only while the vortex
    /// carries them, the brew advances faster the more of them have dissolved, and an
    /// over-hard stir flings one over the lip, which costs points and leaves the brew
    /// slower for the rest of the morning.</para>
    /// </summary>
    public class PhysicsCauldronManager : MonoBehaviour
    {
        public static PhysicsCauldronManager Instance { get; private set; }

        [Header("Stirring")]
        [Tooltip("Pointer rotation about the surface centre (deg/sec) that counts as a full-power stir.")]
        [SerializeField] private float spinForFullPower = 420f;
        [Tooltip("Below this (deg/sec) the spoon is considered still, in either direction.")]
        [SerializeField] private float spinDeadZone = 45f;

        [Header("Heat")]
        [Tooltip("How quickly the heat eases toward the level the stir is setting.")]
        [SerializeField] private float heatFollowRate = 0.9f;
        [SerializeField] private float heatDecayRate = 0.28f;
        [Tooltip("How far the optimal band slides either side of centre over a brew.")]
        [Range(0f, 0.3f)] [SerializeField] private float bandDrift = 0.10f;
        [Range(0.05f, 0.4f)] [SerializeField] private float bandHalfWidth = 0.13f;
        [SerializeField] private float bandDriftSpeed = 0.18f;

        [Header("Brew")]
        [Tooltip("Seconds of correct stirring in the band to finish, once everything has dissolved.")]
        [SerializeField] private float brewSeconds = 11f;
        [Tooltip("Brew speed while nothing has dissolved yet, as a fraction of full speed.")]
        [Range(0.1f, 1f)] [SerializeField] private float undissolvedPace = 0.55f;
        [SerializeField] private float deductionInterval = 1f;
        [Tooltip("Breathing room after the heat crosses a band edge, so a player who is already correcting is not charged mid-correction.")]
        [SerializeField] private float bandChangeGraceSeconds = 0.5f;

        /// <summary>The brew pays <see cref="QualityBudget.BrewTotal"/> in this many equal instalments.</summary>
        private const int BonusChunks = 11;

        private float currentHeat = 0.2f;       // starts at room temperature, below the band by design
        private float bandCenter = 0.55f;
        private float bandPhase;

        private float lastPointerAngleDeg;
        private bool hasLastAngle;
        private float smoothedSpinDegPerSec;

        private float nextDeductionTime;
        private bool wasInGreen = true;
        private bool mouseOverPot;
        private bool everStirred;
        private int _paidChunks;

        // --- surface geometry (set by the shop world when it builds the pot) ---
        private Camera _cam;
        private LiquidBody2D _liquid;
        private Vector2 _mouthCentre;
        private float _rx = 1.9f, _ry = 0.45f;

        public float Heat01 => currentHeat;
        public float MinOptimalHeat => Mathf.Clamp01(bandCenter - EffectiveBandHalfWidth);
        public float MaxOptimalHeat => Mathf.Clamp01(bandCenter + EffectiveBandHalfWidth);
        public float BandCentre => bandCenter;

        private float EffectiveBandHalfWidth => bandHalfWidth * _bandScale;
        private float _bandScale = 1f;

        /// <summary>
        /// The Prep bench's gate. Stirring an empty pot does nothing at all: you crush
        /// and add the leaves first, then you stir them. A null mixture (a bare test
        /// scene) counts as ready so nothing that predates the recipe system deadlocks.
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

        /// <summary>True while the pointer is over the pot's mouth — the spoon only bites here.</summary>
        public bool MouseOverCauldron => mouseOverPot;

        /// <summary>
        /// True while the player is standing at the Cauldron (the tab is open). The
        /// simulation runs either way — the pot really does cool while you are at
        /// another bench — but nothing is SCORED off it. Set by the Cauldron station.
        /// </summary>
        public bool Attended { get; set; }

        /// <summary>Which way today's recipe wants the spoon turned.</summary>
        public bool RequiredClockwise { get; private set; }

        /// <summary>Signed stir rate, -1..1. Negative is clockwise on screen.</summary>
        public float Spin01 => Mathf.Clamp(smoothedSpinDegPerSec / Mathf.Max(1f, spinForFullPower), -1f, 1f);
        public float SpinDegPerSec => smoothedSpinDegPerSec;

        /// <summary>Unsigned stir power, 0..1 — how hard the spoon is being turned.</summary>
        public float StirPower01 => Mathf.Abs(Spin01);

        public bool StirringCorrectly =>
            mouseOverPot && Mathf.Abs(smoothedSpinDegPerSec) >= spinDeadZone &&
            (smoothedSpinDegPerSec < 0f) == RequiredClockwise;

        public bool StirringBackwards =>
            mouseOverPot && Mathf.Abs(smoothedSpinDegPerSec) >= spinDeadZone &&
            (smoothedSpinDegPerSec < 0f) != RequiredClockwise;

        /// <summary>How many Prep ingredients have gone into the pot today.</summary>
        public int IngredientCount { get; private set; }

        /// <summary>How many herbs over-stirring has thrown out today.</summary>
        public int SplashCount { get; private set; }

        /// <summary>Of everything put in, how much has dissolved (see <see cref="LiquidBody2D.DissolvedFraction"/>).</summary>
        public float DissolvedFraction => _liquid != null ? _liquid.DissolvedFraction : 1f;

        public LiquidBody2D Liquid => _liquid;
        public Vector2 MouthCentre => _mouthCentre;
        public float MouthRadiusX => _rx;
        public float MouthRadiusY => _ry;

        /// <summary>Number of physics steps processed (tooling checks the sim keeps running).</summary>
        public long PhysicsStepCount { get; private set; }

        /// <summary>Raised when an ingredient is dropped toward the pot, with its element.</summary>
        public event Action<ElementType> OnIngredientAdded;

        /// <summary>Raised when over-stirring throws a herb out, with the floater that went.</summary>
        public event Action<LiquidBody2D.Floater> OnSplash;

        /// <summary>A leaf hit the surface (the view splashes and plops).</summary>
        public event Action<ElementType, Vector2> Landed;

        /// <summary>Directly set the brew heat (0..1). For scripted events and simulation tests.</summary>
        public void SetHeat(float value01)
        {
            currentHeat = Mathf.Clamp01(value01);
            OnHeatChanged?.Invoke(currentHeat);
        }

        private void Awake()
        {
            // The newest pot wins. The shop world is destroyed and rebuilt in the same
            // frame at the start of every day, and Destroy() is deferred, so the old
            // pot is still Instance when the new one wakes.
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            if (_liquid != null) _liquid.OnSplashedOut -= HandleSplash;
        }

        /// <summary>Kept for old callers; herbs live in the liquid's own space now.</summary>
        public void Configure(LayerMask herbLayers) { }

        /// <summary>
        /// Wire the pot to its surface: where the mouth is on screen, how big the
        /// ellipse is, and the surface-space simulation that stands in for it.
        /// </summary>
        public void ConfigureSurface(Camera cam, Vector2 mouthCentreWorld, float radiusX, float radiusY, LiquidBody2D liquid)
        {
            _cam = cam;
            _mouthCentre = mouthCentreWorld;
            _rx = Mathf.Max(0.1f, radiusX);
            _ry = Mathf.Max(0.05f, radiusY);
            if (_liquid != null) _liquid.OnSplashedOut -= HandleSplash;
            _liquid = liquid;
            if (_liquid != null) _liquid.OnSplashedOut += HandleSplash;
        }

        /// <summary>World point on the mouth -> surface space (a circle of the liquid's radius).</summary>
        public Vector2 ToSurface(Vector2 world)
        {
            float R = _liquid != null ? _liquid.Radius : 1f;
            Vector2 d = world - _mouthCentre;
            return new Vector2(d.x / _rx, d.y / _ry) * R;
        }

        /// <summary>Surface space -> the world point on the mouth ellipse that shows it.</summary>
        public Vector2 ToWorld(Vector2 surface)
        {
            float R = _liquid != null ? _liquid.Radius : 1f;
            return _mouthCentre + new Vector2(surface.x / R * _rx, surface.y / R * _ry);
        }

        /// <summary>Called by the Prep bench once the mortar work is done.</summary>
        public void ApplyMix(MixOutcome outcome)
        {
            _bandScale = RecipeBook.BandScale(outcome);
            OnHeatChanged?.Invoke(currentHeat);
        }

        /// <summary>A fresh brew: cold pot, empty progress, and the day's stir direction.</summary>
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
            SplashCount = 0;
            _paidChunks = 0;
            if (_liquid != null) _liquid.ClearAll();
            OnHeatChanged?.Invoke(currentHeat);
        }

        private void Start() => RequiredClockwise = true;

        private void Update()
        {
            Vector2 surface = ToSurface(Pointer.World(_cam != null ? _cam : Camera.main));
            float R = _liquid != null ? _liquid.Radius : 1f;

            // Unattended, the spoon is not in the player's hand at all; and there is
            // nothing to stir until the leaves are crushed and in.
            mouseOverPot = Attended && MixtureReady && surface.sqrMagnitude <= (R * 1.15f) * (R * 1.15f);

            TrackSpin(surface);

            if (_liquid != null)
            {
                _liquid.SpinDegPerSec = mouseOverPot ? smoothedSpinDegPerSec : 0f;
                _liquid.StirringCorrectly = StirringCorrectly;
                _liquid.Spoon = mouseOverPot ? Vector2.ClampMagnitude(surface, R * 0.95f) : (Vector2?)null;
            }

            if (everStirred && !IsBrewComplete)
            {
                bandPhase += Time.deltaTime * bandDriftSpeed;
                bandCenter = 0.55f + Mathf.Sin(bandPhase * Mathf.PI * 2f) * bandDrift;
            }

            float power = StirPower01;
            if (power > 0.05f && mouseOverPot) everStirred = true;

            // Stirring the right way sets the heat: faster runs hotter, slower cools.
            // Idle it drifts down; dragged the wrong way it drops faster.
            float dt = Time.deltaTime;
            if (StirringCorrectly)
            {
                float target = 0.15f + 0.85f * power;
                currentHeat = Mathf.Lerp(currentHeat, target, 1f - Mathf.Exp(-heatFollowRate * dt));
            }
            else if (StirringBackwards) currentHeat -= heatDecayRate * 1.6f * dt;
            else currentHeat -= heatDecayRate * dt;
            currentHeat = Mathf.Clamp01(currentHeat);
            OnHeatChanged?.Invoke(currentHeat);

            CheckHeatQualityImpact();
        }

        /// <summary>Signed angular velocity of the pointer about the surface centre, smoothed.</summary>
        private void TrackSpin(Vector2 surface)
        {
            if (!mouseOverPot || surface.sqrMagnitude < 0.04f)
            {
                hasLastAngle = false;
                smoothedSpinDegPerSec = Mathf.Lerp(smoothedSpinDegPerSec, 0f, Damp(6f));
                return;
            }

            float angle = Mathf.Atan2(surface.y, surface.x) * Mathf.Rad2Deg;
            float raw = 0f;
            if (hasLastAngle && Time.deltaTime > 0f)
                raw = Mathf.DeltaAngle(lastPointerAngleDeg, angle) / Time.deltaTime;
            lastPointerAngleDeg = angle;
            hasLastAngle = true;

            raw = Mathf.Clamp(raw, -spinForFullPower * 2f, spinForFullPower * 2f);
            smoothedSpinDegPerSec = Mathf.Lerp(smoothedSpinDegPerSec, raw, Damp(8f));
        }

        private static float Damp(float rate) => 1f - Mathf.Exp(-rate * Time.deltaTime);

        private void FixedUpdate() => PhysicsStepCount++;

        private void CheckHeatQualityImpact()
        {
            ActiveOrder activeOrder = CraftingManager.Instance != null ? CraftingManager.Instance.CurrentOrder : null;
            if (activeOrder == null) return;
            if (!Attended) return;        // not at this bench
            if (!MixtureReady) return;    // nothing in the pot yet
            if (IsBrewComplete) return;   // quality is locked
            if (!everStirred) return;     // a cold pot nobody has touched isn't a mistake

            bool inGreen = currentHeat >= MinOptimalHeat && currentHeat <= MaxOptimalHeat;
            if (inGreen != wasInGreen)
            {
                wasInGreen = inGreen;
                nextDeductionTime = Mathf.Max(nextDeductionTime, Time.time + bandChangeGraceSeconds);
            }

            if (inGreen && StirringCorrectly)
            {
                float pace = Mathf.Lerp(undissolvedPace, 1f, DissolvedFraction);
                BrewProgress01 = Mathf.Clamp01(BrewProgress01 + Time.deltaTime / Mathf.Max(1f, brewSeconds) * pace);

                // The whole brew pays QualityBudget.BrewTotal, in instalments along the
                // bar. Paying per second (as it used to) made a slower brew worth more.
                int due = Mathf.FloorToInt(BrewProgress01 * BonusChunks + 0.0001f);
                while (_paidChunks < due)
                {
                    _paidChunks++;
                    int chunk = QualityBudget.BrewTotal / BonusChunks
                                + (_paidChunks <= QualityBudget.BrewTotal % BonusChunks ? 1 : 0);
                    activeOrder.ApplyBonus(chunk, "Cauldron", $"Held the band at {Mathf.RoundToInt(currentHeat * 100)}%", Time.time);
                }
                return;
            }

            // In the band but not turning: the brew just waits. It is not a mistake.
            if (inGreen && !StirringBackwards) return;
            if (Time.time < nextDeductionTime) return;

            int penalty = QualityBudget.BrewPenalty;
            string reason;
            if (StirringBackwards)
                reason = $"Stirred {(RequiredClockwise ? "anticlockwise" : "clockwise")} — the recipe says otherwise";
            else if (currentHeat < MinOptimalHeat)
            {
                reason = $"Too cold ({Mathf.RoundToInt(currentHeat * 100)}%) — stir faster";
                penalty += Mathf.RoundToInt((MinOptimalHeat - currentHeat) / Mathf.Max(0.01f, MinOptimalHeat) * 4f);
            }
            else
            {
                reason = $"Overheating ({Mathf.RoundToInt(currentHeat * 100)}%) — ease off";
                penalty += Mathf.RoundToInt((currentHeat - MaxOptimalHeat) / Mathf.Max(0.01f, 1f - MaxOptimalHeat) * 6f);
            }

            activeOrder.ApplyDeduction(penalty, "Cauldron", reason, Time.time);
            nextDeductionTime = Time.time + deductionInterval;
        }

        private void HandleSplash(LiquidBody2D.Floater f)
        {
            SplashCount++;
            ActiveOrder order = CraftingManager.Instance != null ? CraftingManager.Instance.CurrentOrder : null;
            string what = f.Tag is ElementType e ? $"a {e} leaf" : "a leaf";
            if (order != null && !IsBrewComplete)
                order.ApplyDeduction(QualityBudget.Splash, "Cauldron", $"Stirred too hard — {what} slopped out of the pot", Time.time);
            OnSplash?.Invoke(f);
        }

        // ------------------------------------------------------------ ingredients

        /// <summary>
        /// Drop a Prep ingredient into the pot: a real body that falls from above the
        /// rim under gravity and, when it reaches the surface, becomes a floater in the
        /// liquid. The quality effect is Prep's call; this is the part you watch.
        /// </summary>
        public void DropIngredient(ElementType element)
        {
            IngredientCount++;
            var go = new GameObject($"Falling_{element}");
            go.transform.SetParent(transform.parent != null ? transform.parent : transform, worldPositionStays: true);

            float t = (IngredientCount * 0.37f) % 1f;              // deterministic spread across the mouth
            Vector2 start = _mouthCentre + new Vector2(Mathf.Lerp(-_rx * 0.6f, _rx * 0.6f, t), 2.4f);
            go.transform.position = start;

            var rb = go.AddComponent<Rigidbody2D>();
            rb.gravityScale = 1.2f;
            rb.angularVelocity = (t - 0.5f) * 540f;
            var fall = go.AddComponent<FallingIngredient>();
            fall.Init(this, element, _mouthCentre.y + _ry * 0.2f);

            OnIngredientAdded?.Invoke(element);
        }

        /// <summary>The falling leaf reached the surface: it floats from here.</summary>
        internal void Land(ElementType element, Vector2 world, float spin)
        {
            if (_liquid == null) return;
            Vector2 surface = ToSurface(world);
            surface.y = Mathf.Clamp(surface.y, -_liquid.Radius * 0.4f, _liquid.Radius * 0.4f);
            _liquid.Add(surface, 0.2f, 0.18f, element, spin);
            Landed?.Invoke(element, world);
        }
    }
}
