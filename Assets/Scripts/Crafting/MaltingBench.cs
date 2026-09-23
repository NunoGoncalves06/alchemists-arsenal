using System;
using System.Collections.Generic;
using UnityEngine;
using AlchemistsArsenal.Art;
using AlchemistsArsenal.Audio;
using AlchemistsArsenal.Combat;
using AlchemistsArsenal.Core;
using AlchemistsArsenal.Data;
using AlchemistsArsenal.PhysicsKit;
using AlchemistsArsenal.Systems;
using AlchemistsArsenal.Vfx;

namespace AlchemistsArsenal.Crafting
{
    /// <summary>
    /// The Malting bench, first after the Counter: the grain a potion is built on is
    /// malted here the way a brewer malts barley — steeped in water to wake it, left
    /// to germinate, then dried in a kiln to stop it and set its enzymes. Every step
    /// only follows the one before (<see cref="MaltStep"/>), and nothing goes to the
    /// Prep bench until its malt is done.
    /// <list type="number">
    /// <item><b>Steep.</b> Hold on the sack and barley pours out of its mouth on real
    /// arcs into a glass steeping jar. The water in it is a
    /// <see cref="BuoyancyEffector2D"/>: sound grain is dense and sinks, empty husks
    /// are light and float — skim them off (click them) before the soak ends. Pour
    /// to the line, press STEEP, and the soak runs by itself.</item>
    /// <item><b>Germinate.</b> The water drains and the grain sprouts, by itself, on the
    /// clock — while the player is at another bench. Halfway, the bed starts to mat:
    /// turn it once (click the jar), a real shove to every grain in it.</item>
    /// <item><b>Kiln.</b> The green malt is thrown across onto the kiln's tray. Its heat
    /// is fuel: every log thrown into the firebox burns for a while and the heat is
    /// what is burning. Hold it in the band while it dries; too hot cooks the
    /// enzymes, too cool and it just keeps sprouting. Hot air rises through the tray
    /// (an <see cref="AreaEffector2D"/>): the hotter the kiln, the more the grain
    /// dances on it.</item>
    /// </list>
    /// <para>The jar and the kiln each hold their own order, so one fighter's malt can
    /// dry while the next one's steeps — and both run on while the player works
    /// another bench. Good malt is good enzymes: the Cauldron dissolves the herbs
    /// faster on it (<see cref="ActiveOrder.MaltQuality01"/>).</para>
    /// </summary>
    public class MaltingBench : MonoBehaviour
    {
        public static MaltingBench Instance { get; private set; }

        public const float BenchTopY = -1.9f;
        private const float JarScale = 1.3f, KilnScale = 1.2f, SackScale = 1.1f;
        private static readonly Vector2 JarLocal = new Vector2(-1.6f, BenchTopY);
        private static readonly Vector2 KilnLocal = new Vector2(2.0f, BenchTopY);
        private static readonly Vector2 SackHookLocal = new Vector2(-4.2f, 3.0f);
        private static readonly Vector2 PileLocal = new Vector2(4.5f, BenchTopY);

        /// <summary>The steep's water line, in jar art rows.</summary>
        private const int WaterRow = 12;
        private const float GrainRadius = 0.1f;
        private const float PourRate = 7f;
        private const float SoakSeconds = 6f;
        private const float GerminateSeconds = 14f;
        private const float TurnFrom = 0.45f, TurnBy = 0.85f;
        private const float KilnSeconds = 12f;
        public const float HeatLow = 0.42f, HeatHigh = 0.72f;
        private const float LogBurnSeconds = 8f;
        private const float GreenGrace = 10f, OverModifiedEvery = 4f;
        private const int OrderBack = 9, OrderGrain = 11, OrderWater = 12, OrderFront = 16, OrderKiln = 14, OrderFire = 15;

        public sealed class Grain
        {
            public Rigidbody2D Body;
            public CircleCollider2D Collider;
            public SpriteRenderer Art;
            public bool Husk, Skimmed;
            /// <summary>In the air: it passes through the other grains until it lands, so a stream does not scatter itself.</summary>
            public bool Flying;
            public float FlyUntil;
        }

        private readonly List<Grain> _tub = new List<Grain>(), _kiln = new List<Grain>();
        private readonly List<Grain> _launching = new List<Grain>();
        private readonly List<float> _logs = new List<float>();
        private readonly List<Rigidbody2D> _flyingLogs = new List<Rigidbody2D>();

        private Camera _cam;
        private Transform _jar, _kilnT;
        private SpriteRenderer _water, _fire, _fireGlow;
        private Collider2D _steepCollider;
        private AreaEffector2D _hotAir;
        private int _waterLevel = WaterRow, _poured, _fireFrame;
        private float _pourDebt, _launchDebt, _drainT = -1f, _greenFor, _overCharged, _nextKilnCharge, _hotFor, _coolFor, _kilnFor, _fireClock;
        private float _autoStokeAt;
        private int _kilnChunksPaid, _overCharges;
        private bool _turned, _pointerPour;
        private float _kilnLinger = -1f;

        public bool Attended { get; set; }
        public bool PourHeld { get; set; }

        /// <summary>The order steeping or germinating in the jar, and the one drying in the kiln.</summary>
        public ActiveOrder TubOrder { get; private set; }
        public ActiveOrder KilnOrder { get; private set; }

        public float SoakProgress01 { get; private set; }
        public float GerminateProgress01 { get; private set; }
        public float KilnProgress01 { get; private set; }
        public float Heat01 { get; private set; } = 0.12f;
        public int LogsBurning => _logs.Count;

        /// <summary>Sound grain in the jar (what "poured to the line" counts).</summary>
        public int GoodInJar { get; private set; }

        /// <summary>Husks still in the jar's water.</summary>
        public int HusksInJar { get; private set; }

        public bool TurnDue => TubOrder != null && TubOrder.maltStep == MaltStep.Germinating
                               && !_turned && GerminateProgress01 >= TurnFrom;
        public bool CanSteep => TubOrder != null && TubOrder.maltStep == MaltStep.Filling && GoodInJar >= 4;
        public bool CanLoadKiln => TubOrder != null && TubOrder.maltStep == MaltStep.Green && KilnOrder == null;
        public bool CanStoke => KilnOrder != null && _logs.Count + _flyingLogs.Count < 4;

        /// <summary>The steeping vat soaks and sprouts faster; the draught kiln tends its own fire.</summary>
        public bool Vat => HasUpgrade(UpgradeCatalog.SteepingVat);
        public bool Draught => HasUpgrade(UpgradeCatalog.DraughtKiln);

        public Vector2 SackWorld => (Vector2)transform.position + SackHookLocal + new Vector2(0f, -1.2f);
        public Vector2 JarWorld => _jar != null ? JP(MaltArt.JarW * 0.5f, 20f) : (Vector2)transform.position;
        public Vector2 PileWorld => (Vector2)transform.position + PileLocal + new Vector2(0f, 0.5f);
        public IReadOnlyList<Grain> TubGrains => _tub;

        public event Action Changed;

        private static bool HasUpgrade(string id) =>
            SaveSystem.Instance != null && SaveSystem.Instance.State != null && SaveSystem.Instance.State.HasUpgrade(id);

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
            AddArt(sack.transform, MaltArt.Sack(), 10, SackScale);

            BuildJar();
            BuildKiln();

            var pile = new GameObject("LogPile");
            pile.transform.SetParent(transform, false);
            pile.transform.localPosition = PileLocal;
            AddArt(pile.transform, MaltArt.LogPile(), 10, 1f);
        }

        /// <summary>Jar art pixel (x right, y down from its top) to world.</summary>
        private Vector2 JP(float px, float py) =>
            (Vector2)_jar.position + new Vector2((px - MaltArt.JarW * 0.5f) / MaltArt.PPU, (MaltArt.JarH - py) / MaltArt.PPU) * JarScale;

        /// <summary>Kiln art pixel to world.</summary>
        private Vector2 KP(float px, float py) =>
            (Vector2)_kilnT.position + new Vector2((px - MaltArt.KilnW * 0.5f) / MaltArt.PPU, (MaltArt.KilnH - py) / MaltArt.PPU) * KilnScale;

        private void BuildJar()
        {
            var go = new GameObject("SteepingJar");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = JarLocal;
            _jar = go.transform;

            AddArt(_jar, MaltArt.JarBack(), OrderBack, JarScale);
            _water = AddArt(_jar, MaltArt.JarWater(WaterRow), OrderWater, JarScale);
            AddArt(_jar, MaltArt.JarFront(Vat), OrderFront, JarScale);

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
            edge.points = ToLocal(go.transform, pts);
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

        private void BuildKiln()
        {
            var go = new GameObject("Kiln");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = KilnLocal;
            _kilnT = go.transform;
            AddArt(_kilnT, MaltArt.Kiln(Draught), OrderKiln, KilnScale);

            // The drying tray and its two lips.
            var tray = new GameObject("Tray");
            tray.transform.SetParent(_kilnT, false);
            var edge = tray.AddComponent<EdgeCollider2D>();
            edge.points = ToLocal(tray.transform, new List<Vector2>
            {
                KP(2.5f, MaltArt.KilnTrayY - 3f), KP(2.5f, MaltArt.KilnTrayY),
                KP(41.5f, MaltArt.KilnTrayY), KP(41.5f, MaltArt.KilnTrayY - 3f),
            });
            edge.edgeRadius = 0.03f;
            edge.sharedMaterial = PhysicsMaterials.Iron;
            GameLayers.Assign(tray, GameLayers.ShopStatic);

            // Hot air up through the perforated tray.
            var air = new GameObject("HotAir");
            air.transform.SetParent(_kilnT, false);
            Vector2 a = KP(2.5f, MaltArt.KilnTrayY), b = KP(41.5f, MaltArt.KilnTrayY - 9f);
            air.transform.position = (a + b) * 0.5f;
            var abox = air.AddComponent<BoxCollider2D>();
            abox.size = new Vector2(Mathf.Abs(b.x - a.x), Mathf.Abs(b.y - a.y));
            abox.isTrigger = true;
            abox.usedByEffector = true;
            _hotAir = air.AddComponent<AreaEffector2D>();
            _hotAir.useGlobalAngle = true;
            _hotAir.forceAngle = 90f;
            _hotAir.forceMagnitude = 0f;
            _hotAir.useColliderMask = true;
            _hotAir.colliderMask = GameLayers.ShopPropMask;
            GameLayers.Assign(air, GameLayers.ShopStatic);

            // The fire in the arch, and its glow on the floor.
            var fire = new GameObject("Fire");
            fire.transform.SetParent(transform, false);
            fire.transform.position = KP((MaltArt.ArchX0 + MaltArt.ArchX1 + 1) * 0.5f, MaltArt.ArchBottom + 0.5f);
            _fire = fire.AddComponent<SpriteRenderer>();
            _fire.sprite = ShopArt.Fire(0);
            _fire.sharedMaterial = SpriteMaterials.For(_fire.sprite);
            _fire.sortingOrder = OrderFire;
            var glow = new GameObject("FireGlow");
            glow.transform.SetParent(transform, false);
            glow.transform.position = fire.transform.position + new Vector3(0f, -0.1f, 0f);
            _fireGlow = PixelArt.AddDisc(glow, new Color(1f, 0.55f, 0.2f, 0.1f), 4);
            glow.transform.localScale = new Vector3(2.2f, 0.6f, 1f);
        }

        private static Vector2[] ToLocal(Transform t, List<Vector2> world)
        {
            var local = new Vector2[world.Count];
            for (int i = 0; i < world.Count; i++) local[i] = t.InverseTransformPoint(world[i]);
            return local;
        }

        private static SpriteRenderer AddArt(Transform parent, Sprite s, int order, float scale)
        {
            var go = new GameObject("Art");
            go.transform.SetParent(parent, false);
            go.transform.localScale = Vector3.one * scale;
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = s;
            sr.sharedMaterial = SpriteMaterials.For(s);
            sr.sortingOrder = order;
            return sr;
        }

        // -------------------------------------------------------------- the day

        public void NewDay()
        {
            foreach (var g in _tub) if (g.Body != null) Destroy(g.Body.gameObject);
            foreach (var g in _kiln) if (g.Body != null) Destroy(g.Body.gameObject);
            foreach (var l in _flyingLogs) if (l != null) Destroy(l.gameObject);
            _tub.Clear(); _kiln.Clear(); _launching.Clear(); _logs.Clear(); _flyingLogs.Clear();
            TubOrder = KilnOrder = null;
            Heat01 = 0.12f;
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
                if (o.stage != BrewStage.Malting || o.maltStep != MaltStep.Waiting) continue;
                TubOrder = o;
                o.maltStep = MaltStep.Filling;
                ResetJar();
                Changed?.Invoke();
                return;
            }
        }

        // ---------------------------------------------------------------- input

        private void Update()
        {
            BindTub();
            float dt = Time.deltaTime;

            if (Attended)
            {
                Vector2 p = Pointer.World(_cam);
                if (Pointer.PressedThisFrame && !Pointer.OverUI) Click(p);
                if (!Pointer.Held) _pointerPour = false;
            }
            else _pointerPour = false;

            UpdateJar(dt);
            UpdateKiln(dt);
            DrawKiln(dt);
        }

        private void Click(Vector2 p)
        {
            if (Vector2.Distance(p, SackWorld) < 1.0f && TubOrder != null && TubOrder.maltStep == MaltStep.Filling)
            {
                _pointerPour = true;
                return;
            }
            if (Vector2.Distance(p, PileWorld) < 1.0f) { Stoke(); return; }

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
            else if (CanLoadKiln) LoadKiln();
        }

        private bool Pouring => TubOrder != null && TubOrder.maltStep == MaltStep.Filling && (PourHeld || _pointerPour);

        // ------------------------------------------------------------ the jar

        private void UpdateJar(float dt)
        {
            CountJar();
            ActiveOrder o = TubOrder;
            if (o == null) return;

            switch (o.maltStep)
            {
                case MaltStep.Soaking:
                    SoakProgress01 = Mathf.Min(1f, SoakProgress01 + dt / (SoakSeconds * (Vat ? 0.7f : 1f)));
                    foreach (var g in _tub)
                        if (g.Art != null && !g.Husk) g.Art.transform.localScale = Vector3.one * (1f + 0.15f * SoakProgress01);
                    if (SoakProgress01 >= 1f) EndSoak(o);
                    break;

                case MaltStep.Germinating:
                    if (_drainT >= 0f)
                    {
                        _drainT += dt;
                        SetWater(Mathf.RoundToInt(Mathf.Lerp(WaterRow, MaltArt.JarH, Mathf.Clamp01(_drainT / 1.2f))));
                        if (_drainT >= 1.2f) _drainT = -1f;
                    }
                    GerminateProgress01 = Mathf.Min(1f, GerminateProgress01 + dt / (GerminateSeconds * (Vat ? 0.65f : 1f)));
                    int sprout = GerminateProgress01 < 0.25f ? 0 : GerminateProgress01 < 0.55f ? 1 : GerminateProgress01 < 0.85f ? 2 : 3;
                    foreach (var g in _tub) SetGrainArt(g, sprout);

                    if (!_turned && GerminateProgress01 >= TurnBy)
                    {
                        _turned = true;
                        Charge(o, QualityBudget.MaltMatted, "The rootlets matted — the bed was never turned");
                    }
                    if (GerminateProgress01 >= 1f)
                    {
                        o.maltStep = MaltStep.Green;
                        _greenFor = 0f;
                        Changed?.Invoke();
                    }
                    break;

                case MaltStep.Green:
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
            o.maltStep = MaltStep.Soaking;
            SoakProgress01 = 0f;
            int good = GoodInJar, target = QualityBudget.GrainTarget, slack = QualityBudget.GrainSlack;
            if (good < target - slack)
                Charge(o, QualityBudget.MaltShort, $"Short of grain in the steep ({good} of {target})");
            else if (good > target + slack * 2)
                Charge(o, QualityBudget.MaltOver, $"Crowded the steep ({good} for {target})");
            else
                Pay(o, QualityBudget.MaltFill, $"Steeped to the line ({good} grains)");
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

            // Drain it: the water goes, and the grain is left to sprout.
            if (_steepCollider != null) _steepCollider.enabled = false;
            _drainT = 0f;
            o.maltStep = MaltStep.Germinating;
            GerminateProgress01 = 0f;
            AudioManager.Play(Sfx.Tab);
            Changed?.Invoke();
        }

        /// <summary>Flick a floating husk up and out over the rim.</summary>
        public void Skim(Grain g)
        {
            if (g == null || !g.Husk || g.Skimmed || g.Body == null || TubOrder == null) return;
            if (TubOrder.maltStep != MaltStep.Filling && TubOrder.maltStep != MaltStep.Soaking) return;
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
                g.Body.AddForce(new Vector2(k * 2.4f, 2.6f + Mathf.Abs(k)) * g.Body.mass, ForceMode2D.Impulse);
                g.Body.AddTorque(k * 0.02f, ForceMode2D.Impulse);
            }
            Pay(o, QualityBudget.MaltTurn, "Turned the bed before the rootlets matted");
            CameraRig.Shake(0.05f);
            AudioManager.Play(Sfx.Tab);
            Changed?.Invoke();
        }

        /// <summary>Throw the green malt across onto the kiln's tray: the kiln takes the order.</summary>
        public void LoadKiln()
        {
            if (!CanLoadKiln) return;
            ActiveOrder o = TubOrder;
            o.maltStep = MaltStep.Kilning;
            KilnOrder = o;
            TubOrder = null;
            KilnProgress01 = 0f;
            _kilnChunksPaid = 0;
            _kilnFor = 0f;
            _hotFor = _coolFor = 0f;
            _nextKilnCharge = 0f;
            _kilnLinger = -1f;
            foreach (var g in _tub)
            {
                if (g.Body == null || g.Skimmed) continue;
                _launching.Add(g);
            }
            _tub.Clear();
            _launchDebt = 0f;
            AudioManager.Play(Sfx.Confirm);
            Changed?.Invoke();
        }

        /// <summary>Throw a log from the pile into the firebox.</summary>
        public void Stoke()
        {
            if (!CanStoke) return;
            var go = new GameObject("Log");
            go.transform.SetParent(transform, false);
            Vector2 from = PileWorld + new Vector2(-0.2f, 0.3f);
            go.transform.position = from;
            var rb = go.AddComponent<Rigidbody2D>();
            rb.mass = 0.6f;
            var col = go.AddComponent<BoxCollider2D>();
            col.size = new Vector2(0.6f, 0.26f);
            AddArt(go.transform, MaltArt.Log(), OrderFire + 1, 1f);
            GameLayers.Assign(go, GameLayers.ShopProp);
            col.excludeLayers = ~0;   // it flies clean into the arch
            rb.linearVelocity = Launch(from, ArchCentre, 0.7f);
            rb.angularVelocity = 360f;
            _flyingLogs.Add(rb);
            AudioManager.Play(Sfx.Tab);
        }

        private Vector2 ArchCentre => KP((MaltArt.ArchX0 + MaltArt.ArchX1 + 1) * 0.5f, MaltArt.ArchTop + 6f);

        /// <summary>The launch velocity that carries a body from <paramref name="from"/> to <paramref name="to"/> in <paramref name="t"/> seconds.</summary>
        private static Vector2 Launch(Vector2 from, Vector2 to, float t)
        {
            float g = -Physics2D.gravity.y;
            return new Vector2((to.x - from.x) / t, (to.y - from.y + 0.5f * g * t * t) / t);
        }

        // ------------------------------------------------------------ physics

        private void FixedUpdate()
        {
            float dt = Time.fixedDeltaTime;

            // The pour: barley off the sack's mouth on real arcs into the jar.
            if (Pouring)
            {
                _pourDebt += dt * PourRate;
                Vector2 mouth = (Vector2)transform.position + SackHookLocal
                                + new Vector2((20f - 12.5f) / MaltArt.PPU, -23f / MaltArt.PPU) * SackScale;
                int day = SaveSystem.Instance != null && SaveSystem.Instance.State != null ? SaveSystem.Instance.State.day : 1;
                while (_pourDebt >= 1f)
                {
                    _pourDebt -= 1f;
                    int n = _poured++;
                    bool husk = (n * 7 + day * 3) % 9 < 2;
                    float j = ((n * 37) % 11) / 11f - 0.5f;
                    Grain g = SpawnGrain(mouth + new Vector2(0f, j * 0.06f), husk);
                    g.Body.linearVelocity = Launch(g.Body.position, JarWorld + new Vector2(j * 1.2f, 1.35f), 0.5f);
                    Fly(g, 0.75f);
                    _tub.Add(g);
                }
            }

            // Green malt on its way to the kiln, a few grains a step.
            if (_launching.Count > 0)
            {
                _launchDebt += dt * 30f;
                Vector2 lip = JP(MaltArt.JarW * 0.5f, 2f);
                while (_launchDebt >= 1f && _launching.Count > 0)
                {
                    _launchDebt -= 1f;
                    Grain g = _launching[0];
                    _launching.RemoveAt(0);
                    if (g.Body == null) continue;
                    float j = ((_kiln.Count * 29) % 13) / 13f;
                    Vector2 to = Vector2.Lerp(KP(6f, MaltArt.KilnTrayY - 1f), KP(38f, MaltArt.KilnTrayY - 1f), j);
                    g.Body.position = lip;
                    g.Body.linearVelocity = Launch(lip, to, 0.55f);
                    Fly(g, 0.6f);
                    _kiln.Add(g);
                }
            }

            // Grains that have come down rejoin the pile.
            float neck = _jar != null ? JP(0f, 5f).y : 0f;
            LandFlyers(_tub, neck);
            LandFlyers(_kiln, float.MaxValue);

            // Logs into the firebox.
            Vector2 a0 = KP(MaltArt.ArchX0, MaltArt.ArchTop), a1 = KP(MaltArt.ArchX1 + 1, MaltArt.ArchBottom + 1);
            for (int i = _flyingLogs.Count - 1; i >= 0; i--)
            {
                Rigidbody2D log = _flyingLogs[i];
                if (log == null) { _flyingLogs.RemoveAt(i); continue; }
                Vector2 p = log.position;
                bool inArch = p.x >= Mathf.Min(a0.x, a1.x) && p.x <= Mathf.Max(a0.x, a1.x)
                              && p.y <= Mathf.Max(a0.y, a1.y) && p.y >= Mathf.Min(a0.y, a1.y);
                if (inArch || p.y < transform.position.y + BenchTopY - 1f)
                {
                    if (inArch)
                    {
                        _logs.Add(LogBurnSeconds);
                        if (VfxWorld.Active != null) VfxWorld.Active.Burst(p, new Color(1f, 0.6f, 0.2f), 12, 2.2f, 0.08f, 0.5f);
                    }
                    Destroy(log.gameObject);
                    _flyingLogs.RemoveAt(i);
                }
            }

            // The hot air through the tray. A grain weighs about 0.68 N: in the band it
            // shivers, too hot and it visibly bounces.
            if (_hotAir != null)
            {
                _hotAir.forceMagnitude = Heat01 * 0.45f;
                _hotAir.forceVariation = Heat01 * 0.35f;
            }
        }

        private static void Fly(Grain g, float seconds)
        {
            g.Flying = true;
            g.FlyUntil = Time.time + seconds;
            g.Collider.excludeLayers |= GameLayers.ShopPropMask;
        }

        /// <summary>A grain in the air lands once it is below <paramref name="landBelow"/> (in the jar) or its flight is up.</summary>
        private static void LandFlyers(List<Grain> grains, float landBelow)
        {
            foreach (var g in grains)
            {
                if (!g.Flying || g.Body == null || g.Skimmed) continue;
                if (g.Body.position.y < landBelow || Time.time >= g.FlyUntil)
                {
                    g.Flying = false;
                    g.Collider.excludeLayers = ~GameLayers.ContactsOf(GameLayers.ShopProp);
                }
            }
        }

        private Grain SpawnGrain(Vector2 at, bool husk)
        {
            var go = new GameObject(husk ? "Husk" : "Grain");
            go.transform.SetParent(transform, false);
            go.transform.position = at;
            var rb = go.AddComponent<Rigidbody2D>();
            rb.useAutoMass = true;
            rb.linearDamping = 0.1f;
            rb.angularDamping = 0.6f;
            rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            var col = go.AddComponent<CircleCollider2D>();
            col.radius = GrainRadius;
            // Sound barley is dense and sinks; an empty husk floats. The buoyancy does the sorting.
            col.density = husk ? 0.35f : 2.2f;
            col.sharedMaterial = PhysicsMaterials.Herb;
            var art = AddArt(go.transform, MaltArt.Grain(husk, 0), OrderGrain, 1f);
            GameLayers.Assign(go, GameLayers.ShopProp);
            return new Grain { Body = rb, Collider = col, Art = art, Husk = husk };
        }

        private static void SetGrainArt(Grain g, int sprout)
        {
            if (g.Art == null || g.Husk) return;
            Sprite s = MaltArt.Grain(false, sprout);
            if (g.Art.sprite == s) return;
            g.Art.sprite = s;
            g.Art.sharedMaterial = SpriteMaterials.For(s);
        }

        // ------------------------------------------------------------- the kiln

        private void UpdateKiln(float dt)
        {
            // Every log burning adds its heat, fading over its last two seconds.
            float burning = 0f;
            for (int i = _logs.Count - 1; i >= 0; i--)
            {
                _logs[i] -= dt;
                if (_logs[i] <= 0f) { _logs.RemoveAt(i); continue; }
                burning += Mathf.Clamp01(_logs[i] / 2f);
            }
            float target = 0.12f + 0.21f * burning;

            // The draught kiln tends itself: a damper that keeps it off the top of the
            // band, and a stoker that keeps two logs burning while there is malt in it.
            if (Draught && KilnOrder != null)
            {
                target = Mathf.Min(target, HeatHigh - 0.05f);
                if (_logs.Count + _flyingLogs.Count < 2 && CanStoke && Time.time >= _autoStokeAt)
                {
                    _autoStokeAt = Time.time + 1.2f;
                    Stoke();
                }
            }
            Heat01 = Mathf.Lerp(Heat01, target, 1f - Mathf.Exp(-0.55f * dt));

            ActiveOrder o = KilnOrder;
            if (o == null) return;

            if (_kilnLinger >= 0f)
            {
                _kilnLinger += dt;
                if (_kilnLinger > 1.2f)
                {
                    foreach (var g in _kiln) if (g.Body != null) Destroy(g.Body.gameObject);
                    _kiln.Clear();
                    KilnOrder = null;
                    _kilnLinger = -1f;
                    Changed?.Invoke();
                }
                return;
            }

            _kilnFor += dt;
            float speed = 1f / (KilnSeconds * (Draught ? 0.75f : 1f));
            bool hot = Heat01 > HeatHigh, cool = Heat01 < HeatLow;
            if (!hot && !cool)
            {
                _hotFor = _coolFor = 0f;
                KilnProgress01 = Mathf.Min(1f, KilnProgress01 + dt * speed);
                int due = Mathf.FloorToInt(KilnProgress01 * QualityBudget.KilnTotal + 0.0001f);
                while (_kilnChunksPaid < due)
                {
                    _kilnChunksPaid++;
                    Pay(o, 1, $"Kilned in the band ({Mathf.RoundToInt(Heat01 * 100)}°)");
                }
            }
            else if (hot)
            {
                KilnProgress01 = Mathf.Min(1f, KilnProgress01 + dt * speed * 0.5f);
                _hotFor += dt;
                _coolFor = 0f;
                if (_hotFor > 0.8f && Time.time >= _nextKilnCharge)
                {
                    _nextKilnCharge = Time.time + 1f;
                    Charge(o, QualityBudget.KilnScorch, "Too hot in the kiln — the enzymes are cooking");
                }
            }
            else
            {
                _coolFor += dt;
                _hotFor = 0f;
                if (_kilnFor > 4f && _coolFor > 2f && Time.time >= _nextKilnCharge)
                {
                    _nextKilnCharge = Time.time + 2f;
                    Charge(o, QualityBudget.KilnCool, "The kiln has gone cool — it is still sprouting");
                }
            }

            // Drying darkens the malt toward its toasted colour.
            Color toast = Color.Lerp(Color.white, new Color(0.85f, 0.68f, 0.5f), KilnProgress01);
            foreach (var g in _kiln) if (g.Art != null) g.Art.color = toast;

            if (KilnProgress01 >= 1f) FinishMalt(o);
        }

        private void FinishMalt(ActiveOrder o)
        {
            o.maltStep = MaltStep.Malted;
            o.MaltQuality01 = Mathf.Clamp01(o.maltPoints / (float)QualityBudget.MaltMax);
            if (VfxWorld.Active != null)
                VfxWorld.Active.Puff(KP(22f, MaltArt.KilnTrayY - 2f), new Color(0.9f, 0.8f, 0.6f, 0.4f), 6, 0.8f, 0.6f);
            AudioManager.Play(Sfx.Chime);
            if (CraftingManager.Instance != null) CraftingManager.Instance.Advance(o);   // on to Prep
            _kilnLinger = 0f;
            Changed?.Invoke();
        }

        private void DrawKiln(float dt)
        {
            if (_fire == null) return;
            _fireClock += dt * (5f + Heat01 * 8f);
            int frame = (int)_fireClock % 3;
            if (frame != _fireFrame)
            {
                _fireFrame = frame;
                _fire.sprite = ShopArt.Fire(frame);
            }
            float h = Mathf.Clamp01((Heat01 - 0.12f) / 0.8f);
            _fire.enabled = _logs.Count > 0;
            _fire.transform.localScale = new Vector3(0.5f, 0.25f + 0.55f * h, 1f);
            _fireGlow.color = new Color(1f, 0.55f, 0.2f, 0.06f + 0.3f * h);

            // Heat shimmer off the tray while it is hot.
            if (VfxWorld.Active != null && Heat01 > 0.35f && VfxWorld.Active.Random01() < dt * 10f * Heat01)
                VfxWorld.Active.Mote(KP(6f + VfxWorld.Active.Random01() * 32f, MaltArt.KilnTrayY - 1f), new Vector2(0f, 0.8f),
                    new Color(1f, 0.85f, 0.6f, 0.18f), 0.12f, 0.8f, glow: false);
        }

        // ---------------------------------------------------------- scoring

        private static void Pay(ActiveOrder o, int points, string reason)
        {
            if (o == null || points <= 0) return;
            o.ApplyBonus(points, "Malting", reason);
            o.maltPoints += points;
        }

        private static void Charge(ActiveOrder o, int points, string reason)
        {
            if (o == null || points <= 0) return;
            o.ApplyDeduction(points, "Malting", reason);
            o.maltPoints -= points;
            AudioManager.Play(Sfx.Deny);
        }
    }
}
