using System;
using System.Collections.Generic;
using UnityEngine;
using AlchemistsArsenal.Art;
using AlchemistsArsenal.Audio;
using AlchemistsArsenal.Core;
using AlchemistsArsenal.Data;
using AlchemistsArsenal.PhysicsKit;
using AlchemistsArsenal.Systems;
using AlchemistsArsenal.Vfx;

namespace AlchemistsArsenal.Crafting
{
    /// <summary>
    /// The Steep bench, first after the Counter: the grain a potion is built on is woken
    /// here the way a brewer wakes barley, then sent on to the Kiln bench to be dried.
    /// Every step only follows the one before (<see cref="SteepStep"/>).
    /// <list type="number">
    /// <item><b>Steep.</b> Hold on the sack: it swings over the jar and barley trickles
    /// down the neck. The water is a <see cref="BuoyancyEffector2D"/>: sound grain sinks,
    /// empty husks float; skim them off (click them) before the soak ends. Pour to the
    /// line, set the lid on, and the soak runs by itself.</item>
    /// <item><b>Germinate.</b> The water drains and the grain sprouts on the clock.
    /// Halfway the bed starts to mat: turn it once (click the jar).</item>
    /// <item><b>Scoop.</b> Sprouted, drag the green malt from the jar into the sack for
    /// the kiln: the order leaves this bench for the Kiln bench.</item>
    /// </list>
    /// This bench holds one order at a time and scores only what happens at it (station
    /// "Steep"). The Steeping Vat runs the steep itself: it seals at the line,
    /// skims the husks and turns the bed, and the player only pours the grain in and
    /// takes the malt out into its basket.
    /// </summary>
    public class SteepBench : MonoBehaviour
    {
        public static SteepBench Instance { get; private set; }

        public const string Station = "Steep";
        public const float BenchTopY = -1.9f;
        public const float JarScale = 1.3f;
        private const float SackScale = 1.1f;
        private static readonly Vector2 JarLocal = new Vector2(-1.2f, BenchTopY);
        private static readonly Vector2 SackHookLocal = new Vector2(-4.2f, 3.0f);
        private static readonly Vector2 KilnSackLocal = new Vector2(3.0f, BenchTopY);

        /// <summary>The steep's water line, in jar art rows.</summary>
        public const int WaterRow = 12;
        private const float PourRate = 7f;
        private const float SoakSeconds = 6f;
        /// <summary>How long the grain sprouts, in seconds: the bed can be turned from <see cref="TurnFrom"/> and mats at <see cref="TurnBy"/> of it.</summary>
        public const float GerminateSeconds = 10f;
        public const float TurnFrom = 0.40f, TurnBy = 0.85f;
        private const float GreenGrace = 10f, OverModifiedEvery = 4f;
        private const int OrderBack = 9, OrderWater = 12, OrderFront = 16;

        private readonly List<Grain> _tub = new List<Grain>();

        private Camera _cam;
        private Transform _jar, _sack, _kilnSack;
        private SpriteRenderer _water;
        private Collider2D _steepCollider;
        private int _waterLevel = WaterRow, _poured;
        private float _pourDebt, _drainT = -1f, _greenFor, _overCharged;
        private int _overCharges;
        private bool _turned, _pointerPour;

        // Hands on the bench: the lid, or a scoop of green malt.
        private enum Carry { None, Lid, Malt }
        private Carry _carry;
        /// <summary>The lid or the malt is in the hand.</summary>
        public bool Carrying => _carry != Carry.None;
        private Transform _lid;
        private SpriteRenderer _carryArt;
        private BenchGauge _jarGauge;

        /// <summary>Where the lid waits beside the jar, and where it sits when the jar is steeping.</summary>
        public Vector2 LidHomeWorld => (Vector2)transform.position + new Vector2(-3.65f, BenchTopY + 0.02f);
        public Vector2 JarMouthWorld => _jar != null ? JP(MaltArt.JarW * 0.5f, 1f) : (Vector2)transform.position;
        public Vector2 LidWorld => _lid != null ? (Vector2)_lid.position : LidHomeWorld;
        /// <summary>The sack the green malt is scooped into, for the kiln.</summary>
        public Vector2 KilnSackWorld => _kilnSack != null ? (Vector2)KilnSackBounds.center
                                                          : (Vector2)transform.position + KilnSackLocal + new Vector2(0f, SackHeight * 0.75f * 0.5f);

        /// <summary>The sack art's height at scale 1, below its hook.</summary>
        private const float SackHeight = 25f / MaltArt.PPU;

        /// <summary>A short line about what the bench wants next, for the station's caption.</summary>
        public string Hint
        {
            get
            {
                ActiveOrder tub = TubOrder;
                SteepStep step = tub != null ? tub.steepStep : SteepStep.Waiting;
                if (TurnDue && !Vat) return "The bed is matting. Click the jar to turn it.";
                if (CanScoop) return Vat ? "Sprouted. Drag the malt from the vat into the basket for the kiln."
                                         : "Sprouted. Drag the malt from the jar into the sack for the kiln.";
                if (Vat && tub != null && step == SteepStep.Filling) return "Hold on the sack to pour. The vat seals itself at the line.";
                if (Vat && tub != null && step == SteepStep.Soaking) return "The vat is skimming the husks off the steep.";
                if (tub != null && step == SteepStep.Germinating)
                    return SproutingHint(Vat, TurnPending && Data.Complexity.TurningOn(Data.Complexity.Level()));
                if (tub != null && step == SteepStep.Filling)
                    return LineReached
                        ? "That's the line. Drag the lid onto the jar."
                        : "Hold on the sack to pour grain into the jar.";
                if (tub != null && step == SteepStep.Soaking)
                    return HusksInJar > 0 ? "Click the floating husks to skim them." : "Soaking.";
                return "";
            }
        }

        /// <summary>What the bench says while the grain sprouts. It never sends the player away from the jar.</summary>
        public static string SproutingHint(bool vat, bool turning) =>
            vat ? "Sprouting. The vat turns the bed itself."
            : turning ? "Sprouting. Stay close: the bed will need turning."
            : "Sprouting.";

        /// <summary>
        /// The pour gauge's range and its green box, as fractions of the bar. A bare jar reads
        /// 0 to 1.6 targets with the good band in the middle; the vat's bar runs 0 to exactly
        /// the grains the machine needs, with a small box at the very end: fill the bar.
        /// </summary>
        public static (float full, float bandMin01, float bandMax01) FillGauge(bool vat)
        {
            float target = QualityBudget.GrainTarget, slack = QualityBudget.GrainSlack;
            if (vat) return (target, (target - 2f) / target, 1f);
            float full = target * 1.6f;
            return (full, (target - slack) / full, (target + slack * 2f) / full);
        }

        /// <summary>Where the green box is on the jar's gauge now (the playtest reads it).</summary>
        public Vector2 JarGaugeBand => _jarGauge != null ? new Vector2(_jarGauge.BandMin, _jarGauge.BandMax) : Vector2.zero;

        /// <summary>The jar has taken all its grain: the line the lid lesson is for.</summary>
        public static bool AtTheLine(SteepStep step, int goodInJar) =>
            step == SteepStep.Filling && goodInJar >= QualityBudget.GrainTarget;

        public bool LineReached => TubOrder != null && AtTheLine(TubOrder.steepStep, GoodInJar);

        // What the tutorial's spotlight is centred on: the art you can see.
        public Bounds SackBounds => MaltShared.FirstArtBounds(_sack);
        public Bounds LidBounds => MaltShared.FirstArtBounds(_lid);
        public Bounds JarBounds => MaltShared.FirstArtBounds(_jar);
        public Bounds KilnSackBounds => MaltShared.FirstArtBounds(_kilnSack);

        public bool Attended { get; set; }
        public bool PourHeld { get; set; }

        /// <summary>The order steeping, sprouting or green in the jar: always one at the Steep stage.</summary>
        public ActiveOrder TubOrder { get; private set; }

        public float SoakProgress01 { get; private set; }
        public float GerminateProgress01 { get; private set; }

        /// <summary>Sound grain in the jar (what "poured to the line" counts).</summary>
        public int GoodInJar { get; private set; }

        /// <summary>Husks still in the jar's water.</summary>
        public int HusksInJar { get; private set; }

        public bool TurnDue => TubOrder != null && TubOrder.steepStep == SteepStep.Germinating
                               && !_turned && GerminateProgress01 >= TurnFrom;

        /// <summary>The bed is sprouting and has not been turned yet (it will be due, or is).</summary>
        public bool TurnPending => TubOrder != null && TubOrder.steepStep == SteepStep.Germinating && !_turned;

        /// <summary>The jar and the gauge over it, in world space (what the turn lesson keeps lit).</summary>
        public Rect JarArea
        {
            get
            {
                if (_jar == null) return default;
                var jar = Rect.MinMaxRect(JarWorld.x - 1.5f, JP(0f, MaltArt.JarH).y, JarWorld.x + 1.5f, JP(0f, 1f).y);
                if (_jarGauge == null) return jar;
                Rect g = _jarGauge.WorldRect;
                return Rect.MinMaxRect(Mathf.Min(jar.xMin, g.xMin), Mathf.Min(jar.yMin, g.yMin),
                                       Mathf.Max(jar.xMax, g.xMax), Mathf.Max(jar.yMax, g.yMax));
            }
        }
        public bool CanSteep => TubOrder != null && TubOrder.steepStep == SteepStep.Filling && GoodInJar >= 4;
        /// <summary>The malt is sprouted and can be scooped for the kiln.</summary>
        public bool CanScoop => TubOrder != null && TubOrder.steepStep == SteepStep.Green;

        /// <summary>The steeping vat: seals at the line, skims and turns the bed by itself.</summary>
        public bool Vat => MaltShared.HasUpgrade(UpgradeCatalog.SteepingVat);

        /// <summary>How often the vat's strainer flicks a husk out.</summary>
        private const float VatSkimEvery = 0.4f;
        private float _vatSkimT;
        private Transform _vatGear, _vatPaddle;

        public Vector2 SackWorld => (Vector2)transform.position + SackHookLocal + new Vector2(0f, -1.2f);

        /// <summary>Where the grain leaves the sack while it is held over the jar: just above the neck, centred.</summary>
        private Vector2 SpoutWorld => JP(MaltArt.JarW * 0.5f, -3f);

        /// <summary>The sack's mouth, from its hook (the sack hangs mouth-down).</summary>
        private static readonly Vector2 SackMouth = new Vector2((20f - 12.5f) / MaltArt.PPU, -23f / MaltArt.PPU) * SackScale;

        /// <summary>The sack has swung over the jar, mouth above the neck: only now does grain run.</summary>
        private bool SackOverJar => _sack != null && Vector2.Distance((Vector2)_sack.position + SackMouth, SpoutWorld + new Vector2(0f, 0.12f)) < 0.15f;
        public Vector2 JarWorld => _jar != null ? JP(MaltArt.JarW * 0.5f, 20f) : (Vector2)transform.position;
        public IReadOnlyList<Grain> TubGrains => _tub;

        public event Action Changed;

        private void Awake() => Instance = this;
        private void OnDestroy() { if (Instance == this) Instance = null; }

        // ------------------------------------------------------------------ build

        public void Build(Camera cam)
        {
            _cam = cam;

            var plank = new GameObject("Plank");
            plank.transform.SetParent(transform, false);
            plank.transform.localPosition = new Vector3(0f, BenchTopY, 0f);
            PixelArt.AddSprite(plank, ShopArt.Plank(), 2, 11.4f);
            var top = plank.AddComponent<EdgeCollider2D>();
            top.points = new[] { new Vector2(-6f, 0f), new Vector2(6f, 0f) };
            top.sharedMaterial = PhysicsMaterials.Wood;
            GameLayers.Assign(plank, GameLayers.ShopStatic);

            var sack = new GameObject("Sack");
            sack.transform.SetParent(transform, false);
            sack.transform.localPosition = SackHookLocal;
            MaltShared.AddArt(sack.transform, MaltArt.Sack(), 10, SackScale);
            _sack = sack.transform;

            BuildJar();

            // The sack the green malt goes into for the kiln, on the bench beside the jar.
            var kilnSack = new GameObject("KilnSack");
            kilnSack.transform.SetParent(transform, false);
            kilnSack.transform.localPosition = KilnSackLocal + new Vector2(0f, SackHeight * 0.75f + 0.02f);   // hangs from its hook: stand it on the bench
            // Sprouted malt waits in its own crate (the vat's wicker basket once it is bought): never the dry-seed sack.
            MaltShared.AddArt(kilnSack.transform, Vat ? MaltArt.SmallBasket() : MaltArt.MaltCrate(), 10, 0.75f);
            _kilnSack = kilnSack.transform;

            var lid = new GameObject("JarLid");
            lid.transform.SetParent(transform, false);
            lid.transform.position = LidHomeWorld;
            MaltShared.AddArt(lid.transform, MaltArt.JarLid(), OrderFront + 1, JarScale);
            _lid = lid.transform;

            var carry = new GameObject("Carried");
            carry.transform.SetParent(transform, false);
            _carryArt = MaltShared.AddArt(carry.transform, MaltArt.Grain(false, 3), 30, 2.4f);
            _carryArt.enabled = false;

            // The instrument: grain against the line above the jar.
            _jarGauge = BenchGauge.Create(transform, JP(MaltArt.JarW * 0.5f, 1f) + new Vector2(0f, 0.75f), 2.2f, false, 30, name: "JarGauge");
        }

        /// <summary>Jar art pixel (x right, y down from its top) to world.</summary>
        private Vector2 JP(float px, float py) => (Vector2)_jar.position + JarOffset(px, py);

        /// <summary>Jar art pixel (x right, y down from its top) as an offset from the jar's pivot (bottom middle), in world units.</summary>
        public static Vector2 JarOffset(float px, float py) =>
            new Vector2((px - MaltArt.JarW * 0.5f) / MaltArt.PPU, (MaltArt.JarH - py) / MaltArt.PPU) * JarScale;

        private void BuildJar()
        {
            var go = new GameObject("SteepingJar");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = JarLocal;
            _jar = go.transform;

            MaltShared.AddArt(_jar, MaltArt.JarBack(), OrderBack, JarScale);
            _water = MaltShared.AddArt(_jar, MaltArt.JarWater(WaterRow), OrderWater, JarScale);
            MaltShared.AddArt(_jar, MaltArt.JarFront(Vat), OrderFront, JarScale);
            if (Vat)
            {
                var paddle = new GameObject("VatPaddle");
                paddle.transform.SetParent(_jar, false);
                paddle.transform.position = JP(MaltArt.JarW * 0.5f, -1f);
                MaltShared.AddArt(paddle.transform, ShopArt.Paddle(), OrderFront - 1, JarScale * 0.8f);
                _vatPaddle = paddle.transform;
                var gear = new GameObject("VatGear");
                gear.transform.SetParent(_jar, false);
                gear.transform.position = JP(MaltArt.JarW * 0.5f, -2f);
                MaltShared.AddArt(gear.transform, ShopArt.Gear(), OrderFront + 2, JarScale);
                _vatGear = gear.transform;
            }

            // The inside of the glass, lip to lip: down the neck, under the shoulder,
            // down the wall, round the bottom and back up the other side.
            var pts = new List<Vector2> { JP(4f, 1f), JP(6f, 3.5f), JP(6f, 9f), JP(2f, 9f), JP(2f, 26.5f) };
            const float r = 6.5f;
            for (int i = 1; i <= 6; i++)
            {
                float a = Mathf.PI + i / 6f * Mathf.PI * 0.5f;
                pts.Add(JP(8.5f + Mathf.Cos(a) * r, 26.5f - Mathf.Sin(a) * r));
            }
            for (int i = 0; i <= 6; i++)
            {
                float a = Mathf.PI * 1.5f + i / 6f * Mathf.PI * 0.5f;
                pts.Add(JP(27.5f + Mathf.Cos(a) * r, 26.5f - Mathf.Sin(a) * r));
            }
            pts.Add(JP(34f, 9f)); pts.Add(JP(30f, 9f)); pts.Add(JP(30f, 3.5f)); pts.Add(JP(32f, 1f));
            var edge = go.AddComponent<EdgeCollider2D>();
            edge.points = MaltShared.ToLocal(go.transform, pts);
            edge.edgeRadius = 0.03f;
            edge.sharedMaterial = PhysicsMaterials.Glass;
            GameLayers.Assign(go, GameLayers.ShopStatic);

            // The steep: water, as buoyancy. Its surface is the effector's local y = 0.
            var steep = new GameObject("Steep");
            steep.transform.SetParent(_jar, false);
            steep.transform.position = JP(MaltArt.JarW * 0.5f, WaterRow);
            var box = steep.AddComponent<BoxCollider2D>();
            float w = (34f - 2f) / MaltArt.PPU * JarScale, depth = (33f - WaterRow) / MaltArt.PPU * JarScale;
            box.size = new Vector2(w, depth);
            box.offset = new Vector2(0f, -depth * 0.5f);
            box.isTrigger = true;
            box.usedByEffector = true;
            var buoy = steep.AddComponent<BuoyancyEffector2D>();
            buoy.surfaceLevel = 0f;
            buoy.density = 1f;
            buoy.linearDamping = 3f;
            buoy.angularDamping = 1.5f;
            buoy.useColliderMask = true;
            buoy.colliderMask = GameLayers.ShopPropMask;
            GameLayers.Assign(steep, GameLayers.ShopStatic);
            _steepCollider = box;
        }

        // -------------------------------------------------------------- the day

        public void NewDay()
        {
            foreach (var g in _tub) if (g.Body != null) Destroy(g.Body.gameObject);
            _tub.Clear();
            TubOrder = null;
            ResetJar();
            Changed?.Invoke();
        }

        private void ResetJar()
        {
            _poured = 0;
            _pourDebt = 0f;
            _drainT = -1f;
            _turned = false;
            _greenFor = 0f;
            _overCharged = 0f;
            _overCharges = 0;
            SoakProgress01 = GerminateProgress01 = 0f;
            SetWater(WaterRow);
            if (_steepCollider != null) _steepCollider.enabled = true;
            LidOn(false);
        }

        /// <summary>The lid on the jar (steeping) or back beside it.</summary>
        private void LidOn(bool on)
        {
            if (_lid == null) return;
            _lid.position = on ? JarMouthWorld : LidHomeWorld;
            _lid.gameObject.SetActive(!Vat || on);
            if (_carry == Carry.Lid) _carry = Carry.None;
        }

        private void SetWater(int level)
        {
            if (_water == null || (level == _waterLevel && _water.sprite != null)) return;
            _waterLevel = level;
            _water.sprite = MaltArt.JarWater(level);
            _water.sharedMaterial = SpriteMaterials.For(_water.sprite);
        }

        /// <summary>The jar takes the oldest order still waiting to be malted.</summary>
        private void BindTub()
        {
            if (TubOrder != null) return;
            var cm = CraftingManager.Instance;
            if (cm == null) return;
            foreach (ActiveOrder o in cm.Orders)
            {
                if (o.stage != BrewStage.Steep || o.steepStep != SteepStep.Waiting) continue;
                TubOrder = o;
                o.steepStep = SteepStep.Filling;
                ResetJar();
                Changed?.Invoke();
                return;
            }
        }

        // ---------------------------------------------------------------- input

        private void Update()
        {
            if (AlchemistsArsenal.Core.TimeControl.Paused) return;   // the pause menu: nothing here moves or reads the pointer
            BindTub();
            float dt = Time.deltaTime;

            if (Attended)
            {
                Vector2 p = Pointer.World(_cam);
                if (Pointer.PressedThisFrame && !Pointer.OverUI) Click(p);
                if (_carry != Carry.None)
                {
                    if (Pointer.Held) MoveCarried(p);
                    else Drop(p);
                }
                if (!Pointer.Held) _pointerPour = false;
            }
            else { _pointerPour = false; CancelCarry(); }

            // Held, the sack swings across and tips over the jar; let go, it hangs back on its hook.
            if (_sack != null)
            {
                bool over = Pouring;
                Vector2 to = over ? SpoutWorld + new Vector2(0f, 0.12f) - SackMouth : (Vector2)transform.position + SackHookLocal;
                float k = 1f - Mathf.Exp(-9f * dt);
                _sack.position = Vector2.Lerp(_sack.position, to, k);
                _sack.rotation = Quaternion.Slerp(_sack.rotation, Quaternion.Euler(0f, 0f, over ? -8f : 0f), k);
            }

            UpdateJar(dt);
            DrawGauge();
        }

        private void MoveCarried(Vector2 p)
        {
            if (_carry == Carry.Lid && _lid != null) _lid.position = p;
            else if (_carryArt != null) _carryArt.transform.parent.position = p;
        }

        /// <summary>Let go of what the hand carries: where it lands decides what happens.</summary>
        private void Drop(Vector2 p)
        {
            Carry what = _carry;
            _carry = Carry.None;
            if (_carryArt != null) _carryArt.enabled = false;
            switch (what)
            {
                case Carry.Lid:
                    if (Vector2.Distance(p, JarMouthWorld) < 1.1f && CanSteep) Steep();
                    else LidOn(false);
                    break;
                case Carry.Malt:
                    if (Vector2.Distance(p, KilnSackWorld) < 1.3f) Scoop();
                    break;
            }
        }

        private void CancelCarry()
        {
            if (_carry == Carry.Lid) LidOn(TubOrder != null && TubOrder.steepStep == SteepStep.Soaking);
            _carry = Carry.None;
            if (_carryArt != null) _carryArt.enabled = false;
        }

        private void StartCarry(Carry what, Sprite art, float scale, Vector2 at)
        {
            _carry = what;
            if (what == Carry.Lid || _carryArt == null) return;
            _carryArt.sprite = art;
            _carryArt.sharedMaterial = SpriteMaterials.For(art);
            _carryArt.transform.localScale = Vector3.one * scale;
            _carryArt.transform.parent.position = at;
            _carryArt.enabled = true;
        }

        /// <summary>The jar's instrument: the grain against the line, then the soak and the sprouting.</summary>
        private void DrawGauge()
        {
            ActiveOrder tub = TubOrder;
            SteepStep step = tub != null ? tub.steepStep : SteepStep.Waiting;
            if (_jarGauge == null) return;
            _jarGauge.SetVisible(tub != null);
            if (tub != null && step == SteepStep.Filling)
            {
                float target = QualityBudget.GrainTarget, slack = QualityBudget.GrainSlack;
                var (full, lo, hi) = FillGauge(Vat);
                _jarGauge.SetBand(lo, hi);
                // The vat is done at the end of its bar; a bare jar is right anywhere in its band.
                bool inBand = Vat ? GoodInJar >= target : GoodInJar >= target - slack && GoodInJar <= target + slack * 2f;
                _jarGauge.SetValue(Mathf.Clamp01(GoodInJar / full), inBand ? new Color(0.45f, 0.85f, 0.45f) : new Color(0.96f, 0.85f, 0.45f));
                _jarGauge.SetText($"{GoodInJar} / {QualityBudget.GrainTarget}");
            }
            else if (tub != null)
            {
                float t = step == SteepStep.Soaking ? SoakProgress01 : GerminateProgress01;
                // Sprouting: the green box is when to turn the bed (while it still needs a hand).
                bool window = step == SteepStep.Germinating && TurnPending && !Vat && Data.Complexity.TurningOn(Data.Complexity.Level());
                if (window) _jarGauge.SetBand(TurnFrom, TurnBy); else _jarGauge.SetBand(0f, 0f);
                _jarGauge.SetValue(t, window && t >= TurnFrom && t <= TurnBy ? new Color(0.45f, 0.85f, 0.45f) : new Color(0.96f, 0.85f, 0.45f));
                _jarGauge.SetText(step == SteepStep.Soaking ? "soaking" : step == SteepStep.Germinating ? "sprouting" : "sprouted");
            }
        }

        private void Click(Vector2 p)
        {
            if (_lid != null && CanSteep && !Vat && Vector2.Distance(p, (Vector2)_lid.position + new Vector2(0f, 0.15f)) < 0.9f)
            {
                StartCarry(Carry.Lid, null, 1f, p);
                return;
            }
            if (Vector2.Distance(p, SackWorld) < 1.0f && TubOrder != null && TubOrder.steepStep == SteepStep.Filling)
            {
                _pointerPour = true;
                return;
            }

            // A husk under the pointer: skim it.
            foreach (var g in _tub)
                if (g.Husk && !g.Skimmed && g.Body != null && Vector2.Distance(p, g.Body.position) < 0.3f)
                {
                    Skim(g);
                    return;
                }

            bool onJar = Mathf.Abs(p.x - JarWorld.x) < 1.4f && p.y < JP(0f, 1f).y && p.y > JP(0f, MaltArt.JarH).y;
            if (!onJar) return;
            if (TurnDue) Turn();
            else if (CanScoop) StartCarry(Carry.Malt, MaltArt.Grain(false, 3), 2.6f, p);
        }

        private bool Pouring => TubOrder != null && TubOrder.steepStep == SteepStep.Filling && (PourHeld || _pointerPour);

        // ------------------------------------------------------------ the jar

        private void UpdateJar(float dt)
        {
            CountJar();
            ActiveOrder o = TubOrder;
            if (o == null) return;
            if (Vat) RunVat(o, dt);

            switch (o.steepStep)
            {
                case SteepStep.Soaking:
                    SoakProgress01 = Mathf.Min(1f, SoakProgress01 + dt / SoakSeconds);
                    foreach (var g in _tub)
                        if (g.Art != null && !g.Husk) g.Art.transform.localScale = Vector3.one * (1f + 0.15f * SoakProgress01);
                    if (SoakProgress01 >= 1f) EndSoak(o);
                    break;

                case SteepStep.Germinating:
                    if (_drainT >= 0f)
                    {
                        _drainT += dt;
                        SetWater(Mathf.RoundToInt(Mathf.Lerp(WaterRow, MaltArt.JarH, Mathf.Clamp01(_drainT / 1.2f))));
                        if (_drainT >= 1.2f) _drainT = -1f;
                    }
                    GerminateProgress01 = Mathf.Min(1f, GerminateProgress01 + dt / GerminateSeconds);
                    int sprout = GerminateProgress01 < 0.25f ? 0 : GerminateProgress01 < 0.55f ? 1 : GerminateProgress01 < 0.85f ? 2 : 3;
                    foreach (var g in _tub) MaltShared.SetGrainArt(g, sprout);

                    // Before the road teaches turning, the bed turns itself (and earns its share).
                    if (!_turned && GerminateProgress01 >= TurnFrom && !Data.Complexity.TurningOn(Data.Complexity.Level()))
                    {
                        _turned = true;
                        Pay(o, QualityBudget.MaltTurn, "The bed turned itself");
                    }

                    if (!_turned && GerminateProgress01 >= TurnBy)
                    {
                        _turned = true;
                        Charge(o, QualityBudget.MaltMatted, "The rootlets matted — the bed was never turned");
                    }
                    if (GerminateProgress01 >= 1f)
                    {
                        o.steepStep = SteepStep.Green;
                        _greenFor = 0f;
                        Changed?.Invoke();
                    }
                    break;

                case SteepStep.Green:
                    // Green malt that sits waiting for the kiln keeps growing into its own starch.
                    _greenFor += dt;
                    if (_greenFor > GreenGrace && _overCharges < QualityBudget.MaltOverCap)
                    {
                        _overCharged += dt;
                        if (_overCharged >= OverModifiedEvery)
                        {
                            _overCharged = 0f;
                            _overCharges++;
                            Charge(o, QualityBudget.MaltOverModified, "Over-modified — the green malt sat waiting for the kiln");
                        }
                    }
                    break;
            }
        }

        /// <summary>
        /// The vat's part: seal once the good grain is at the line, flick the husks out
        /// one at a time, turn the bed as soon as it is due. The paddle and gear show it working.
        /// </summary>
        private void RunVat(ActiveOrder o, float dt)
        {
            if (o.steepStep == SteepStep.Filling && GoodInJar >= QualityBudget.GrainTarget)
            {
                PourHeld = false;
                _pointerPour = false;
                Steep();
            }
            if ((o.steepStep == SteepStep.Filling || o.steepStep == SteepStep.Soaking) && HusksInJar > 0)
            {
                _vatSkimT += dt;
                if (_vatSkimT >= VatSkimEvery) { _vatSkimT = 0f; SkimNext(); }
            }
            if (TurnDue) Turn();
            bool working = o.steepStep == SteepStep.Soaking || o.steepStep == SteepStep.Germinating;
            if (_vatGear != null && working) _vatGear.Rotate(0f, 0f, -90f * dt);
            if (_vatPaddle != null)
                _vatPaddle.localRotation = Quaternion.Euler(0f, 0f, working ? Mathf.Sin(Time.time * 2.2f) * 14f : 0f);
        }

        private void CountJar()
        {
            int good = 0, husks = 0;
            if (_jar == null) return;
            float top = JP(0f, 4f).y, bottom = JP(0f, MaltArt.JarH).y, half = 1.45f;
            foreach (var g in _tub)
            {
                if (g.Body == null || g.Skimmed) continue;
                Vector2 p = g.Body.position;
                if (p.y > top || p.y < bottom || Mathf.Abs(p.x - JarWorld.x) > half) continue;
                if (g.Husk) husks++; else good++;
            }
            GoodInJar = good;
            HusksInJar = husks;
        }

        /// <summary>Stop pouring and start the soak. Scored on how close to the line the good grain came.</summary>
        public void Steep()
        {
            ActiveOrder o = TubOrder;
            if (!CanSteep) return;
            o.steepStep = SteepStep.Soaking;
            SoakProgress01 = 0f;
            int good = GoodInJar, target = QualityBudget.GrainTarget, slack = QualityBudget.GrainSlack;
            if (good < target - slack)
                Charge(o, QualityBudget.MaltShort, $"Short of grain in the steep ({good} of {target})");
            else if (good > target + slack * 2)
                Charge(o, QualityBudget.MaltOver, $"Crowded the steep ({good} for {target})");
            else
                Pay(o, QualityBudget.MaltFill, $"Steeped to the line ({good} grains)");
            LidOn(true);
            AudioManager.Play(Sfx.Confirm);
            Changed?.Invoke();
        }

        private void EndSoak(ActiveOrder o)
        {
            int left = HusksInJar;
            int husksPoured = 0;
            foreach (var g in _tub) if (g.Husk) husksPoured++;
            if (left > 0)
                Charge(o, Mathf.Min(QualityBudget.MaltHuskCap, left * QualityBudget.MaltHuskLeft),
                    $"{left} husk{(left == 1 ? "" : "s")} left in the steep");
            else if (husksPoured > 0)
                Pay(o, QualityBudget.MaltSkim, "Skimmed the steep clean");
            else if (!Data.Complexity.HusksOn(Data.Complexity.Level()))
                Pay(o, QualityBudget.MaltSkim, "A clean steep");   // before the road teaches skimming

            // Drain it: the water goes, and the grain is left to sprout.
            LidOn(false);
            if (_steepCollider != null) _steepCollider.enabled = false;
            _drainT = 0f;
            o.steepStep = SteepStep.Germinating;
            GerminateProgress01 = 0f;
            AudioManager.Play(Sfx.Tab);
            Changed?.Invoke();
        }

        /// <summary>Flick a floating husk up and out over the rim.</summary>
        public void Skim(Grain g)
        {
            if (g == null || !g.Husk || g.Skimmed || g.Body == null || TubOrder == null) return;
            if (TubOrder.steepStep != SteepStep.Filling && TubOrder.steepStep != SteepStep.Soaking) return;
            g.Skimmed = true;
            float side = g.Body.position.x < JarWorld.x ? -1f : 1f;
            g.Collider.excludeLayers = ~0;   // clear of the glass: it goes over the rim and away
            g.Body.linearVelocity = Vector2.zero;
            g.Body.AddForce(new Vector2(side * 1.6f, 5.2f) * g.Body.mass, ForceMode2D.Impulse);
            g.Body.angularVelocity = side * 720f;
            if (VfxWorld.Active != null) VfxWorld.Active.Burst(g.Body.position, new Color(0.8f, 0.9f, 1f, 0.8f), 5, 1.2f, 0.06f, 0.3f);
            Destroy(g.Body.gameObject, 1.4f);
            AudioManager.Play(Sfx.Tab);
            Changed?.Invoke();
        }

        /// <summary>Skim the husk nearest the surface (the HUD's button, and the playtest).</summary>
        public bool SkimNext()
        {
            Grain best = null;
            foreach (var g in _tub)
                if (g.Husk && !g.Skimmed && g.Body != null && (best == null || g.Body.position.y > best.Body.position.y)) best = g;
            if (best == null) return false;
            Skim(best);
            return true;
        }

        /// <summary>Turn the germinating bed: a real shove to every grain in it.</summary>
        public void Turn()
        {
            ActiveOrder o = TubOrder;
            if (!TurnDue) return;
            _turned = true;
            int i = 0;
            foreach (var g in _tub)
            {
                if (g.Body == null || g.Skimmed) continue;
                float k = ((i++ * 37) % 11) / 11f - 0.5f;
                // A turn of the bed, not a throw: enough to lift and roll each grain a
                // few centimetres, never enough to clear the glass.
                g.Body.AddForce(new Vector2(k * 0.9f, 0.9f + Mathf.Abs(k) * 0.6f) * g.Body.mass, ForceMode2D.Impulse);
                g.Body.AddTorque(k * 0.02f, ForceMode2D.Impulse);
            }
            Pay(o, QualityBudget.MaltTurn, "Turned the bed before the rootlets matted");
            CameraRig.Shake(0.05f);
            AudioManager.Play(Sfx.Tab);
            Changed?.Invoke();
        }

        /// <summary>
        /// Scoop the green malt into the kiln's sack: the order leaves this bench for the
        /// Kiln bench, carrying how much sound grain it is. Any husk still in the jar was
        /// already charged for and is chaff; it does not go to the kiln.
        /// </summary>
        public void Scoop()
        {
            if (!CanScoop) return;
            ActiveOrder o = TubOrder;
            CountJar();
            int good = GoodInJar;   // only what is in the jar goes to the kiln
            foreach (var g in _tub) if (g.Body != null) Destroy(g.Body.gameObject);
            _tub.Clear();
            o.steepedGrains = good;
            o.kilnStep = KilnStep.Waiting;
            TubOrder = null;
            if (CraftingManager.Instance != null) CraftingManager.Instance.Advance(o);   // on to the Kiln
            if (VfxWorld.Active != null) VfxWorld.Active.Puff(KilnSackWorld, new Color(0.8f, 0.9f, 0.6f, 0.4f), 5, 0.6f, 0.4f);
            AudioManager.Play(Sfx.Confirm);
            ResetJar();
            Changed?.Invoke();
        }

        // ------------------------------------------------------------ physics

        private void FixedUpdate()
        {
            float dt = Time.fixedDeltaTime;

            // The pour: a trickle off the sack's mouth, straight down the neck. A grain only
            // leaves the sack into clear air, so it never appears inside another one (the
            // overlap the solver used to fling out over the rim), and a jar filled to the
            // neck simply stops taking grain, the way a real one does.
            if (Pouring && SackOverJar)
            {
                _pourDebt = Mathf.Min(1f, _pourDebt + dt * PourRate);
                Vector2 spout = SpoutWorld;
                if (_pourDebt >= 1f && Physics2D.OverlapCircle(spout, MaltShared.GrainRadius * 1.2f, GameLayers.ShopPropMask) == null)
                {
                    _pourDebt -= 1f;
                    int n = _poured++;
                    int day = SaveSystem.Instance != null && SaveSystem.Instance.State != null ? SaveSystem.Instance.State.day : 1;
                    // Husks only turn up once the road is far enough along to teach skimming.
                    bool husk = Data.Complexity.HusksOn(Data.Complexity.Level()) && (n * 7 + day * 3) % 9 < 2;
                    float j = ((n * 37) % 11) / 11f - 0.5f;
                    Grain g = SpawnGrain(spout + new Vector2(j * 0.16f, 0f), husk);
                    g.Body.linearVelocity = new Vector2(j * 0.25f, -0.8f);
                    _tub.Add(g);
                }
            }
        }

        private Grain SpawnGrain(Vector2 at, bool husk) => MaltShared.SpawnGrain(transform, at, husk);
        private static void Pay(ActiveOrder o, int points, string reason) => MaltShared.Pay(o, points, Station, reason);
        private static void Charge(ActiveOrder o, int points, string reason) => MaltShared.Charge(o, points, Station, reason);
    }
}
