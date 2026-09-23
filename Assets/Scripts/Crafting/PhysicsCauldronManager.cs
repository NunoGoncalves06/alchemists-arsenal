using UnityEngine;
using System;
using AlchemistsArsenal.Combat;
using AlchemistsArsenal.Core;
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
    /// than a button: the stir has to stay inside a band of speeds that slowly drifts,
    /// the recipe wants a direction, and the brew only advances while both are
    /// true.</para>
    ///
    /// <para><b>Too slow and it catches.</b> The pot is over the fire the whole time.
    /// Stir slower than the band — or stop — once the brew is going and it costs a
    /// little every second; keep it up and it starts sticking to the bottom (costs
    /// more) and then burning there (costs most): smoke and a darkening brew, until
    /// the spoon is moving fast enough to scrape it clean again.</para>
    ///
    /// <para><b>Too fast and it slops.</b> Just past the band is fast but holding.
    /// Faster still and the surface starts to heave; hold it there and a wave goes
    /// over the rim. That costs points, and takes the outermost undissolved herb with
    /// it.</para>
    ///
    /// <para>The band sits around one turn a second, and the spoon only grips the
    /// brew in full out toward the wall, so a relaxed circle is on the band and a
    /// twitch near the middle does nothing.</para>
    ///
    /// <para>The band is the stir itself. It used to be a heat meter that the stir only
    /// fed indirectly, so "too cold" and "overheating" were two names for the same
    /// hand, one step removed from what the player was actually doing.</para>
    ///
    /// <para><b>Physics feeds quality.</b> The leaves from Prep float on the surface as
    /// real bodies (<see cref="LiquidBody2D"/>). They dissolve only while the vortex
    /// carries them, the brew advances faster the more of them have dissolved, and a
    /// herb thrown over the lip by a spill is out of the mix for good.</para>
    /// </summary>
    public class PhysicsCauldronManager : MonoBehaviour
    {
        public static PhysicsCauldronManager Instance { get; private set; }

        [Header("Stirring")]
        [Tooltip("Pointer rotation about the surface centre (deg/sec) that reads as a full stir.")]
        [SerializeField] private float spinForFullPower = 720f;
        [Tooltip("Below this (deg/sec) the spoon is considered still, in either direction.")]
        [SerializeField] private float spinDeadZone = 45f;
        [Tooltip("How quickly the stir reading follows the spoon (1/s). Lower is steadier.")]
        [SerializeField] private float spinSmoothing = 4.5f;
        [Tooltip("Inside this fraction of the radius the spoon barely moves the brew; from fullGripRadius out it counts in full.")]
        [Range(0f, 0.5f)] [SerializeField] private float innerDeadRadius = 0.2f;
        [Range(0.2f, 1f)] [SerializeField] private float fullGripRadius = 0.5f;

        [Header("The band of stir speeds")]
        [Tooltip("Middle of the band, as a fraction of a full stir (one turn a second is 0.5).")]
        [Range(0.2f, 0.8f)] [SerializeField] private float bandMid = 0.5f;
        [Tooltip("How far the band slides either side of that over a brew.")]
        [Range(0f, 0.2f)] [SerializeField] private float bandDrift = 0.06f;
        [Range(0.05f, 0.3f)] [SerializeField] private float bandHalfWidth = 0.15f;
        [SerializeField] private float bandDriftSpeed = 0.18f;

        [Header("Sticking and sloshing")]
        [Tooltip("Seconds of stirring too slowly before it starts to cost, and before the bottom starts to catch.")]
        [SerializeField] private float slowGraceSeconds = 0.6f;
        [Tooltip("How fast the bottom catches while the spoon is too slow (per second).")]
        [SerializeField] private float scorchRate = 0.34f;
        [Tooltip("How fast a stir inside the band scrapes the bottom clean again.")]
        [SerializeField] private float scorchClearRate = 0.4f;
        [Tooltip("Past the band's top, this much more (fraction of a full stir) is fast but still holding: no slosh yet.")]
        [SerializeField] private float sloshMargin = 0.06f;
        [Tooltip("How fast the surface builds toward going over the rim while the stir is too fast.")]
        [SerializeField] private float sloshRate = 0.35f;
        [SerializeField] private float sloshSettleRate = 1.0f;
        [Tooltip("Seconds after a spill before the pot can slop again.")]
        [SerializeField] private float spillCooldown = 0.8f;

        [Header("Brew")]
        [Tooltip("Seconds of correct stirring in the band to finish, once everything has dissolved.")]
        [SerializeField] private float brewSeconds = 11f;
        [Tooltip("Brew speed while nothing has dissolved yet, as a fraction of full speed.")]
        [Range(0.1f, 1f)] [SerializeField] private float undissolvedPace = 0.55f;
        [SerializeField] private float deductionInterval = 1f;
        [Tooltip("Breathing room after the stir crosses a band edge, so a player who is already correcting is not charged mid-correction.")]
        [SerializeField] private float bandChangeGraceSeconds = 0.5f;

        /// <summary>The brew pays <see cref="QualityBudget.BrewTotal"/> in this many equal instalments.</summary>
        private const int BonusChunks = 11;

        /// <summary>Where the bottom counts as sticking, and where that has become a burn.</summary>
        public const float StickAt = 0.2f, BurnAt = 0.6f;

        private float bandCenter = 0.5f;
        private float bandPhase;

        private float lastPointerAngleDeg;
        private bool hasLastAngle;
        private float smoothedSpinDegPerSec;

        private float nextDeductionTime;
        private bool wasInBand = true;
        private bool mouseOverPot;
        private bool everStirred;
        private int _paidChunks;
        private float _scorch, _slosh, _slowFor, _spillCooldownLeft;

        // --- surface geometry (set by the shop world when it builds the pot) ---
        private Camera _cam;
        private LiquidBody2D _liquid;
        private Vector2 _mouthCentre;
        private float _rx = 1.9f, _ry = 0.45f;

        /// <summary>How fast the spoon is going, as a fraction of a full stir. Past 1 is faster than the band can ever ask for.</summary>
        public float StirRate => Mathf.Abs(smoothedSpinDegPerSec) / Mathf.Max(1f, spinForFullPower);

        public float MinOptimalStir => Mathf.Clamp(bandCenter - EffectiveBandHalfWidth, 0.15f, 0.9f);
        public float MaxOptimalStir => Mathf.Clamp(bandCenter + EffectiveBandHalfWidth, 0.25f, 0.98f);
        public float StirBandCentre => bandCenter;

        /// <summary>The stir is inside the band: this is where the brew advances.</summary>
        public bool InBand => StirRate >= MinOptimalStir && StirRate <= MaxOptimalStir;
        public bool TooSlow => StirRate < MinOptimalStir;
        public bool TooFast => StirRate > MaxOptimalStir;

        /// <summary>0..1: how badly the brew has caught on the bottom of the pot.</summary>
        public float Scorch01 => _scorch;

        /// <summary>0..1: how close the heaving surface is to going over the rim.</summary>
        public float Slosh01 => _slosh;

        public bool Sticking => _scorch >= StickAt;
        public bool Burning => _scorch >= BurnAt;

        private float EffectiveBandHalfWidth => bandHalfWidth * _bandScale;
        private float _bandScale = 1f;

        /// <summary>
        /// The order in the pot — being brewed, or brewed and still showing until the
        /// next one waiting at the Cauldron is taken up. The pot works the oldest order
        /// that has finished Prep (see <see cref="CraftingManager"/>), and it takes the
        /// mash itself when it takes the order: the band the mix earned, and the leaves
        /// falling in. A second fighter's mash simply waits its turn.
        /// </summary>
        public ActiveOrder Working { get; private set; }

        /// <summary>The order the pot can still act on: <see cref="Working"/>, while it is at the Cauldron.</summary>
        private ActiveOrder Order => Working != null && Working.stage == BrewStage.Cauldron ? Working : null;
        private float _lingerUntil;

        /// <summary>The clockwork stirrer: a copper pot with a wind-up paddle over it.</summary>
        public static bool AutoStir =>
            SaveSystem.Instance != null && SaveSystem.Instance.State != null
            && SaveSystem.Instance.State.HasUpgrade(UpgradeCatalog.ClockworkStirrer);

        /// <summary>How fast the paddle brews, against a good hand in the band.</summary>
        private const float ClockworkPace = 0.8f;
        /// <summary>
        /// What the paddle's brew is worth against a good hand's: it never errs, but it
        /// never works the herbs as well either. Owned-and-ignored is a fine flask; the
        /// best one is still stirred.
        /// </summary>
        public const float ClockworkShare = 0.75f;
        private float _paddleCredit;

        /// <summary>
        /// The paddle is turning the brew: there is a mash in the pot and no hand on the
        /// spoon. It holds the middle of the band, the way the recipe turns, so the brew
        /// goes on while the player works another bench.
        /// </summary>
        public bool ClockworkTurning => AutoStir && Order != null && !IsBrewComplete && !mouseOverPot;

        /// <summary>The element of the brew in the pot (what the liquid is tinted).</summary>
        public ElementType BrewElement => Working != null ? Working.element : ElementType.Nature;

        /// <summary>Raised when the pot takes up an order (the view re-tints the brew).</summary>
        public event Action<ActiveOrder> OrderTaken;

        /// <summary>
        /// The gate: there is a mash in the pot to stir. Stirring an empty pot does
        /// nothing at all — you crush and add the leaves first, then you stir them. A
        /// bare test scene with no crafting at all counts as ready, so nothing that
        /// predates the recipe system deadlocks.
        /// </summary>
        public bool MixtureReady => CraftingManager.Instance == null || Order != null;

        private void BindNext()
        {
            var cm = CraftingManager.Instance;
            if (cm == null || Order != null || Time.time < _lingerUntil) return;
            ActiveOrder next = cm.Waiting(BrewStage.Cauldron);
            if (next == null || next == Working) return;

            Working = next;
            int day = SaveSystem.Instance != null && SaveSystem.Instance.State != null ? SaveSystem.Instance.State.day : 1;
            // A fresh brew, and each fighter's recipe turns its own way.
            BeginBrew(day + next.queueIndex);
            // The malt's enzymes are what break the herbs down: good malt, faster brew.
            if (_liquid != null) _liquid.DissolveMultiplier = 0.7f + 0.6f * Mathf.Clamp01(next.MaltQuality01);
            if (next.Mixture != null)
            {
                ApplyMix(next.Mixture.Evaluate());
                foreach (ElementType e in next.Mixture.Added) DropIngredient(e);
            }
            OrderTaken?.Invoke(next);
        }

        /// <summary>Raised with the stir meter's reading (0..1) every frame it is recomputed.</summary>
        public event Action<float> OnStirChanged;

        /// <summary>0..1 brew completion — climbs only while stirring correctly in the band.</summary>
        public float BrewProgress01 { get; private set; }

        /// <summary>Once true, quality is locked in and stirring no longer matters.</summary>
        public bool IsBrewComplete => BrewProgress01 >= 1f;

        /// <summary>True while the pointer is over the pot's mouth — the spoon only bites here.</summary>
        public bool MouseOverCauldron => mouseOverPot;

        /// <summary>
        /// True while the player is standing at the Cauldron (the tab is open). The
        /// simulation runs either way — the pot really does sit over the fire while you
        /// are at another bench — but nothing is SCORED off it, and the bottom only
        /// catches under the player's own hand. Set by the Cauldron station.
        /// </summary>
        public bool Attended { get; set; }

        /// <summary>Which way today's recipe wants the spoon turned.</summary>
        public bool RequiredClockwise { get; private set; }

        /// <summary>Signed stir rate, -1..1. Negative is clockwise on screen.</summary>
        public float Spin01 => Mathf.Clamp(smoothedSpinDegPerSec / Mathf.Max(1f, spinForFullPower), -1f, 1f);
        public float SpinDegPerSec => smoothedSpinDegPerSec;

        /// <summary>Unsigned stir reading, 0..1 — what the meter shows.</summary>
        public float StirPower01 => Mathf.Abs(Spin01);

        public bool StirringCorrectly =>
            mouseOverPot && Mathf.Abs(smoothedSpinDegPerSec) >= spinDeadZone &&
            (smoothedSpinDegPerSec < 0f) == RequiredClockwise;

        public bool StirringBackwards =>
            mouseOverPot && Mathf.Abs(smoothedSpinDegPerSec) >= spinDeadZone &&
            (smoothedSpinDegPerSec < 0f) != RequiredClockwise;

        /// <summary>How many Prep ingredients have gone into the pot today.</summary>
        public int IngredientCount { get; private set; }

        /// <summary>How many times over-stirring has slopped the pot over its rim today.</summary>
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

        /// <summary>Raised when a spill throws a herb over the lip, with the floater that went.</summary>
        public event Action<LiquidBody2D.Floater> OnSplash;

        /// <summary>Raised when the pot slops over its rim (the view throws brew out of it).</summary>
        public event Action Spilled;

        /// <summary>A leaf hit the surface (the view splashes and plops).</summary>
        public event Action<ElementType, Vector2> Landed;

        /// <summary>Directly set how much the surface is heaving (0..1). For scripted events and simulation tests.</summary>
        public void SetSlosh(float value01) => _slosh = Mathf.Clamp01(value01);

        /// <summary>Directly set how badly the bottom has caught (0..1). For scripted events and simulation tests.</summary>
        public void SetScorch(float value01) => _scorch = Mathf.Clamp01(value01);

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
            OnStirChanged?.Invoke(StirPower01);
        }

        /// <summary>A fresh brew: a clean pot, empty progress, and the day's stir direction.</summary>
        public void BeginBrew(int day)
        {
            RequiredClockwise = day % 2 == 1;
            _bandScale = 1f;
            BrewProgress01 = 0f;
            bandPhase = 0f;
            bandCenter = bandMid;
            everStirred = false;
            wasInBand = true;
            smoothedSpinDegPerSec = 0f;
            hasLastAngle = false;
            IngredientCount = 0;
            SplashCount = 0;
            _paidChunks = 0;
            _paddleCredit = 0f;
            _scorch = _slosh = _slowFor = 0f;
            _spillCooldownLeft = 0f;
            if (_liquid != null) _liquid.ClearAll();
            OnStirChanged?.Invoke(0f);
        }

        private void Start() => RequiredClockwise = true;

        private void Update()
        {
            BindNext();
            Vector2 surface = ToSurface(Pointer.World(_cam != null ? _cam : Camera.main));
            float R = _liquid != null ? _liquid.Radius : 1f;

            // Unattended, the spoon is not in the player's hand at all; and there is
            // nothing to stir until the leaves are crushed and in.
            mouseOverPot = Attended && MixtureReady && surface.sqrMagnitude <= (R * 1.15f) * (R * 1.15f);

            TrackSpin(surface);
            if (ClockworkTurning)
            {
                float want = (RequiredClockwise ? -1f : 1f) * bandCenter * spinForFullPower;
                smoothedSpinDegPerSec = Mathf.Lerp(smoothedSpinDegPerSec, want, Damp(3f));
                everStirred = true;
            }

            if (_liquid != null)
            {
                _liquid.SpinDegPerSec = mouseOverPot || ClockworkTurning ? smoothedSpinDegPerSec : 0f;
                _liquid.StirringCorrectly = StirringCorrectly || ClockworkTurning;
                _liquid.Spoon = mouseOverPot ? Vector2.ClampMagnitude(surface, R * 0.95f) : (Vector2?)null;
            }

            if (everStirred && !IsBrewComplete)
            {
                bandPhase += Time.deltaTime * bandDriftSpeed;
                bandCenter = bandMid + Mathf.Sin(bandPhase * Mathf.PI * 2f) * bandDrift;
            }

            if (StirPower01 > 0.05f && mouseOverPot) everStirred = true;

            UpdateBottomAndSurface(Time.deltaTime);
            OnStirChanged?.Invoke(StirPower01);
            CheckStirQualityImpact();

            // Brewed: on to Bottling. The finished brew stays in view for a moment
            // before the next fighter's mash goes in.
            if (Order != null && IsBrewComplete && CraftingManager.Instance != null)
            {
                CraftingManager.Instance.Advance(Order);
                _lingerUntil = Time.time + 1.5f;
            }
        }

        /// <summary>
        /// What the stir is doing to the pot itself: a bottom that catches while the
        /// spoon is too slow, and a surface that heaves toward the rim while it is too
        /// fast.
        /// </summary>
        private void UpdateBottomAndSurface(float dt)
        {
            bool live = everStirred && !IsBrewComplete && MixtureReady;
            float rate = StirRate;

            // The bottom only catches under the player's own hand: a pot left while you
            // work another bench is not charged for, nor is one nobody has stirred yet.
            if (live && Attended && rate < MinOptimalStir)
            {
                _slowFor += dt;
                if (_slowFor > slowGraceSeconds)
                {
                    float deficit = Mathf.Clamp01((MinOptimalStir - rate) / Mathf.Max(0.01f, MinOptimalStir));
                    _scorch = Mathf.Min(1f, _scorch + dt * scorchRate * (0.55f + 0.45f * deficit));
                }
            }
            else
            {
                _slowFor = 0f;
                if (!live || rate >= MinOptimalStir) _scorch = Mathf.Max(0f, _scorch - dt * scorchClearRate);
            }

            // Just past the band is fast but holding; beyond that the surface heaves,
            // slowly for a small overshoot (a couple of seconds' warning before it
            // goes) and quickly for a frantic one.
            _spillCooldownLeft -= dt;
            float sloshFrom = MaxOptimalStir + sloshMargin;
            if (mouseOverPot && !IsBrewComplete && rate > sloshFrom)
            {
                float over = Mathf.Clamp((rate - sloshFrom) / Mathf.Max(0.05f, EffectiveBandHalfWidth), 0f, 3f);
                _slosh = Mathf.Min(1f, _slosh + dt * sloshRate * (1f + 1.6f * over));
                if (_slosh >= 1f && _spillCooldownLeft <= 0f) Spill();
            }
            else _slosh = Mathf.Max(0f, _slosh - dt * sloshSettleRate);
        }

        /// <summary>A wave goes over the rim: brew on the floor, and whatever was riding the outside of the vortex with it.</summary>
        private void Spill()
        {
            _spillCooldownLeft = spillCooldown;
            _slosh = 0.45f;
            SplashCount++;

            LiquidBody2D.Floater went = _liquid != null ? _liquid.SlopOutermost() : null;
            string with = went != null && went.Tag is ElementType e ? $", and a {e} leaf with it" : "";
            ActiveOrder order = Order;
            if (order != null && !IsBrewComplete)
                order.ApplyDeduction(QualityBudget.Splash, "Cauldron",
                    $"Stirred too fast — the brew slopped over the rim{with}", Time.time);

            Spilled?.Invoke();
        }

        /// <summary>
        /// Signed angular velocity of the pointer about the surface centre, smoothed.
        ///
        /// Angular speed is tangential speed over radius, so the same small wiggle of
        /// the mouse near the middle of the pot read as a furious stir (and the band sat
        /// at barely half a turn a second, slower than anyone circles a mouse). The
        /// spoon now needs to be out toward the wall to move the brew in full — near
        /// the centre it barely grips — and the reading follows it more steadily.
        /// </summary>
        private void TrackSpin(Vector2 surface)
        {
            float R = _liquid != null ? _liquid.Radius : 1f;
            float r01 = surface.magnitude / Mathf.Max(0.01f, R);
            if (!mouseOverPot || r01 < innerDeadRadius)
            {
                hasLastAngle = false;
                if (!ClockworkTurning)
                    smoothedSpinDegPerSec = Mathf.Lerp(smoothedSpinDegPerSec, 0f, Damp(mouseOverPot ? 2.5f : 6f));
                return;
            }

            float angle = Mathf.Atan2(surface.y, surface.x) * Mathf.Rad2Deg;
            float raw = 0f;
            if (hasLastAngle && Time.deltaTime > 0f)
                raw = Mathf.DeltaAngle(lastPointerAngleDeg, angle) / Time.deltaTime;
            lastPointerAngleDeg = angle;
            hasLastAngle = true;

            float grip = Mathf.Clamp01((r01 - innerDeadRadius) / Mathf.Max(0.01f, fullGripRadius - innerDeadRadius));
            raw = Mathf.Clamp(raw * grip, -spinForFullPower * 2f, spinForFullPower * 2f);
            smoothedSpinDegPerSec = Mathf.Lerp(smoothedSpinDegPerSec, raw, Damp(spinSmoothing));
        }

        /// <summary>What a full stir is, in degrees a second (the playtest drives the spoon in these units).</summary>
        public float FullStirDegPerSec => spinForFullPower;

        private static float Damp(float rate) => 1f - Mathf.Exp(-rate * Time.deltaTime);

        private void FixedUpdate() => PhysicsStepCount++;

        private void CheckStirQualityImpact()
        {
            ActiveOrder activeOrder = Order;
            if (activeOrder == null) return;

            // The clockwork paddle brews on by itself, at any bench, and never errs.
            if (ClockworkTurning)
            {
                float cpace = Mathf.Lerp(undissolvedPace, 1f, DissolvedFraction) * ClockworkPace;
                BrewProgress01 = Mathf.Clamp01(BrewProgress01 + Time.deltaTime / Mathf.Max(1f, brewSeconds) * cpace);
                PayChunks(activeOrder, "Held the band (the clockwork paddle)", ClockworkShare);
                return;
            }

            if (!Attended) return;        // not at this bench
            if (!MixtureReady) return;    // nothing in the pot yet
            if (IsBrewComplete) return;   // quality is locked
            if (!everStirred) return;     // a pot nobody has touched isn't a mistake

            bool inBand = InBand;
            if (inBand != wasInBand)
            {
                wasInBand = inBand;
                nextDeductionTime = Mathf.Max(nextDeductionTime, Time.time + bandChangeGraceSeconds);
            }

            if (inBand && StirringCorrectly)
            {
                float pace = Mathf.Lerp(undissolvedPace, 1f, DissolvedFraction);
                BrewProgress01 = Mathf.Clamp01(BrewProgress01 + Time.deltaTime / Mathf.Max(1f, brewSeconds) * pace);

                PayChunks(activeOrder, $"Held the band at {Mathf.RoundToInt(StirRate * 100)}%");
                return;
            }

            if (Time.time < nextDeductionTime) return;

            int penalty;
            string reason;
            if (StirringBackwards)
            {
                penalty = QualityBudget.BrewPenalty;
                reason = $"Stirred {(RequiredClockwise ? "anticlockwise" : "clockwise")} — the recipe says otherwise";
            }
            // Below the band costs every second once it has lasted past a moment's
            // grace, and more the further the brew has caught: slow, then sticking,
            // then burning. (It used to cost nothing until it was already sticking.)
            else if (TooSlow && _slowFor > slowGraceSeconds)
            {
                int pct = Mathf.RoundToInt(_scorch * 100);
                if (Burning)
                {
                    penalty = QualityBudget.BrewBurn + Mathf.RoundToInt(_scorch * 4f);
                    reason = $"Burning on the bottom ({pct}%) — stir faster";
                }
                else if (Sticking)
                {
                    penalty = QualityBudget.BrewStick + Mathf.RoundToInt(_scorch * 3f);
                    reason = $"Sticking to the bottom ({pct}%) — stir faster";
                }
                else
                {
                    penalty = QualityBudget.BrewSlow;
                    reason = "Stirring too slowly — the brew is settling";
                }
            }
            // Stirring too fast is paid for by the spills it causes, not by the second.
            else return;

            activeOrder.ApplyDeduction(penalty, "Cauldron", reason, Time.time);
            nextDeductionTime = Time.time + deductionInterval;
        }

        /// <summary>
        /// The whole brew pays QualityBudget.BrewTotal, in instalments along the bar.
        /// Paying per second (as it used to) made a slower brew worth more.
        /// </summary>
        private void PayChunks(ActiveOrder order, string reason, float share = 1f)
        {
            int due = Mathf.FloorToInt(BrewProgress01 * BonusChunks + 0.0001f);
            while (_paidChunks < due)
            {
                _paidChunks++;
                int chunk = QualityBudget.BrewTotal / BonusChunks
                            + (_paidChunks <= QualityBudget.BrewTotal % BonusChunks ? 1 : 0);
                if (share < 1f)
                {
                    // A share of a chunk, carried over so the whole brew comes to the share.
                    _paddleCredit += chunk * share;
                    chunk = Mathf.FloorToInt(_paddleCredit + 0.0001f);
                    _paddleCredit -= chunk;
                }
                order.ApplyBonus(chunk, "Cauldron", reason, Time.time);
            }
        }

        /// <summary>A herb a spill threw is clear of the pot: the view flies it out. The spill itself is already scored.</summary>
        private void HandleSplash(LiquidBody2D.Floater f) => OnSplash?.Invoke(f);

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
