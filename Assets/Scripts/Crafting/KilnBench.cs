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
    /// The Kiln bench, after the Steep bench: green malt is dried here to stop it
    /// sprouting and set its enzymes. Each order arrives in its own sack on the rack
    /// (<see cref="KilnStep.Waiting"/>); drag the oldest sack onto the tray and it dries
    /// (<see cref="KilnStep.Drying"/>). The heat is fuel: every log thrown into the
    /// firebox burns for a while and the heat is what is burning. Hold it in the band;
    /// too hot cooks the enzymes, too cool and it keeps sprouting. Hot air rises through
    /// the tray (an <see cref="AreaEffector2D"/>): the hotter the kiln, the more the
    /// grain dances on it.
    ///
    /// <para>This bench holds one order at a time and scores only what happens at it
    /// (station "Kiln"). Dried, the order goes on to Prep with its malt quality worked
    /// out from both benches' points. The Draught Kiln tends its own fire here.</para>
    /// </summary>
    public class KilnBench : MonoBehaviour
    {
        public static KilnBench Instance { get; private set; }

        public const string Station = "Kiln";
        public const float BenchTopY = -1.9f;
        private const float KilnScale = 1.2f;
        private static readonly Vector2 KilnLocal = new Vector2(0.6f, BenchTopY);
        private static readonly Vector2 PileLocal = new Vector2(4.6f, BenchTopY);
        private static readonly Vector2 RackLocal = new Vector2(-2.5f, BenchTopY);

        private const float RackSpacing = 0.75f;

        /// <summary>
        /// Where basket <paramref name="slot"/> stands (bench-local x) when <paramref name="count"/> are
        /// waiting: the group is centred on the rack, so it never runs off the bench's left and the
        /// first basket (the one a hand takes) is wherever the group puts it.
        /// </summary>
        public static float RackSlotX(int slot, int count) => RackLocal.x + (slot - (count - 1) * 0.5f) * RackSpacing;

        private const float KilnSeconds = 12f;
        public const float HeatLow = 0.42f, HeatHigh = 0.72f;
        private const float LogBurnSeconds = 8f;
        private const int OrderKiln = 14, OrderFire = 15;

        private readonly List<Grain> _kiln = new List<Grain>();
        private readonly List<float> _logs = new List<float>();
        private readonly List<Rigidbody2D> _flyingLogs = new List<Rigidbody2D>();
        private readonly List<SpriteRenderer> _rackSacks = new List<SpriteRenderer>();

        private Camera _cam;
        private Transform _kilnT;
        private SpriteRenderer _fire, _fireGlow;
        private AreaEffector2D _hotAir;
        private int _fireFrame, _toDrop;
        private float _launchDebt, _nextKilnCharge, _hotFor, _coolFor, _kilnFor, _fireClock;
        private float _autoStokeAt;
        private int _kilnChunksPaid, _kilnSpotSkip;
        /// <summary>The last batch, dried and cooling on the tray until it is swept off.</summary>
        private readonly List<Grain> _dried = new List<Grain>();
        private float _driedUntil;

        // Hands on the bench: a sack of green malt, or a log.
        private enum Carry { None, Malt, Log }
        private Carry _carry;
        /// <summary>A sack of malt or a log is in the hand.</summary>
        public bool Carrying => _carry != Carry.None;
        private SpriteRenderer _carryArt;
        private BenchGauge _thermo;

        public Vector2 KilnTrayWorld => _kilnT != null ? KP(22f, MaltArt.KilnTrayY - 1f) : (Vector2)transform.position;
        public Vector2 FireboxWorld => _kilnT != null ? ArchCentre : (Vector2)transform.position;
        /// <summary>The rack of sacks of green malt waiting for the kiln.</summary>
        public Vector2 RackWorld => (Vector2)transform.position + RackLocal + new Vector2(0f, SackHeight * 0.3f);

        /// <summary>The basket a hand takes next: the first on the rack (the oldest order waiting).</summary>
        public Bounds NextBasketBounds => _rackSacks.Count > 0 && _rackSacks[0].enabled
            ? _rackSacks[0].bounds : new Bounds(RackWorld, Vector3.zero);

        /// <summary>The sack art's height at scale 1, below its hook.</summary>
        private const float SackHeight = 25f / MaltArt.PPU;

        /// <summary>A short line about what the bench wants next, for the station's caption.</summary>
        public string Hint
        {
            get
            {
                ActiveOrder kiln = KilnOrder;
                if (kiln != null && Heat01 > HeatHigh) return "Too hot. Let the fire burn down.";
                if (kiln != null && Heat01 < HeatLow && !Draught) return "The kiln is cooling. Drag a log into the fire.";
                if (CanLoadKiln) return "Green malt is waiting. Drag its sack onto the kiln tray.";
                if (kiln != null) return "Drying. Keep the heat in the green.";
                return "";
            }
        }

        public bool Attended { get; set; }

        /// <summary>The order drying on the tray: always one at the Kiln stage.</summary>
        public ActiveOrder KilnOrder { get; private set; }

        public float KilnProgress01 { get; private set; }
        public float Heat01 { get; private set; } = 0.12f;
        public int LogsBurning => _logs.Count;

        /// <summary>The oldest order whose green malt is waiting on the rack, or null.</summary>
        public ActiveOrder NextWaiting
        {
            get
            {
                var cm = CraftingManager.Instance;
                if (cm == null) return null;
                foreach (ActiveOrder o in cm.Orders)
                    if (o.stage == BrewStage.Kiln && o.kilnStep == KilnStep.Waiting && o != KilnOrder) return o;
                return null;
            }
        }

        public bool CanLoadKiln => KilnOrder == null && _dried.Count == 0 && NextWaiting != null;
        public bool CanStoke => KilnOrder != null && _logs.Count + _flyingLogs.Count < 4;

        /// <summary>The draught kiln tends its own fire.</summary>
        public bool Draught => MaltShared.HasUpgrade(UpgradeCatalog.DraughtKiln);

        public Vector2 PileWorld => (Vector2)transform.position + PileLocal + new Vector2(0f, 0.5f);

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

            BuildKiln();

            var pile = new GameObject("LogPile");
            pile.transform.SetParent(transform, false);
            pile.transform.localPosition = PileLocal;
            MaltShared.AddArt(pile.transform, MaltArt.LogPile(), 10, 1f);

            // The rack: one crate of sprouted malt (a wicker basket with the vat) per order waiting for the kiln.
            for (int i = 0; i < RunState.MaxParty; i++)   // one per fighter at most
            {
                var s = new GameObject("RackSack" + i);
                s.transform.SetParent(transform, false);
                // The art hangs from its pivot: raise it by its own height to stand on the bench.
                s.transform.localPosition = new Vector2(RackSlotX(i, 1), RackLocal.y + SackHeight * 0.6f + 0.02f);
                var art = MaltShared.AddArt(s.transform, MaltShared.HasUpgrade(UpgradeCatalog.SteepingVat) ? MaltArt.SmallBasket() : MaltArt.MaltCrate(), 10 + i, 0.6f);
                art.enabled = false;
                _rackSacks.Add(art);
            }

            var carry = new GameObject("Carried");
            carry.transform.SetParent(transform, false);
            _carryArt = MaltShared.AddArt(carry.transform, MaltArt.Grain(false, 3), 30, 2.4f);
            _carryArt.enabled = false;

            // The instrument: a thermometer by the kiln.
            _thermo = BenchGauge.Create(transform, KP(MaltArt.KilnW + 5f, MaltArt.KilnH * 0.45f), 2.2f, true, 30, fillMode: true, name: "Thermometer");
            _thermo.SetBand(HeatLow, HeatHigh);
        }

        /// <summary>Kiln art pixel to world.</summary>
        private Vector2 KP(float px, float py) =>
            (Vector2)_kilnT.position + new Vector2((px - MaltArt.KilnW * 0.5f) / MaltArt.PPU, (MaltArt.KilnH - py) / MaltArt.PPU) * KilnScale;

        private void BuildKiln()
        {
            var go = new GameObject("Kiln");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = KilnLocal;
            _kilnT = go.transform;
            MaltShared.AddArt(_kilnT, MaltArt.Kiln(Draught), OrderKiln, KilnScale);

            // The drying tray and its two lips.
            var tray = new GameObject("Tray");
            tray.transform.SetParent(_kilnT, false);
            var edge = tray.AddComponent<EdgeCollider2D>();
            edge.points = MaltShared.ToLocal(tray.transform, new List<Vector2>
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

        // -------------------------------------------------------------- the day

        public void NewDay()
        {
            foreach (var g in _kiln) if (g.Body != null) Destroy(g.Body.gameObject);
            foreach (var g in _dried) if (g.Body != null) Destroy(g.Body.gameObject);
            _dried.Clear();
            foreach (var l in _flyingLogs) if (l != null) Destroy(l.gameObject);
            _kiln.Clear(); _logs.Clear(); _flyingLogs.Clear();
            _toDrop = 0;
            KilnOrder = null;
            Heat01 = 0.12f;
            Changed?.Invoke();
        }

        // ---------------------------------------------------------------- input

        private void Update()
        {
            if (AlchemistsArsenal.Core.TimeControl.Paused) return;   // the pause menu: nothing here moves or reads the pointer
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
            }
            else CancelCarry();

            DrawRack();
            UpdateKiln(dt);
            DrawKiln(dt);
            DrawGauge();
        }

        /// <summary>One sack on the rack per order waiting for the kiln.</summary>
        private void DrawRack()
        {
            int waiting = 0;
            var cm = CraftingManager.Instance;
            if (cm != null)
                foreach (ActiveOrder o in cm.Orders)
                    if (o.stage == BrewStage.Kiln && o.kilnStep == KilnStep.Waiting) waiting++;
            for (int i = 0; i < _rackSacks.Count; i++)
            {
                _rackSacks[i].enabled = i < waiting;
                Vector3 at = _rackSacks[i].transform.parent.localPosition;
                _rackSacks[i].transform.parent.localPosition = new Vector3(RackSlotX(i, Mathf.Max(1, waiting)), at.y, at.z);
            }
        }

        private void MoveCarried(Vector2 p)
        {
            if (_carryArt != null) _carryArt.transform.parent.position = p;
        }

        /// <summary>Let go of what the hand carries: where it lands decides what happens.</summary>
        private void Drop(Vector2 p)
        {
            Carry what = _carry;
            _carry = Carry.None;
            if (_carryArt != null) _carryArt.enabled = false;
            switch (what)
            {
                case Carry.Malt:
                    if (Vector2.Distance(p, KilnTrayWorld) < 1.8f) LoadKiln();
                    break;
                case Carry.Log:
                    if (Vector2.Distance(p, FireboxWorld) < 1.4f) Stoke(p);
                    break;
            }
        }

        private void CancelCarry()
        {
            _carry = Carry.None;
            if (_carryArt != null) _carryArt.enabled = false;
        }

        private void StartCarry(Carry what, Sprite art, float scale, Vector2 at)
        {
            _carry = what;
            if (_carryArt == null) return;
            _carryArt.sprite = art;
            _carryArt.sharedMaterial = SpriteMaterials.For(art);
            _carryArt.transform.localScale = Vector3.one * scale;
            _carryArt.transform.parent.position = at;
            _carryArt.enabled = true;
        }

        private void DrawGauge()
        {
            if (_thermo == null) return;
            _thermo.SetValue(Heat01, Heat01 > HeatHigh ? new Color(0.84f, 0.27f, 0.31f)
                : Heat01 < HeatLow ? new Color(0.25f, 0.56f, 0.82f) : new Color(0.31f, 0.68f, 0.35f));
            _thermo.SetText(KilnOrder != null ? $"dry {Mathf.RoundToInt(KilnProgress01 * 100f)}%" : "");
        }

        private void Click(Vector2 p)
        {
            if (Vector2.Distance(p, PileWorld) < 1.0f)
            {
                if (CanStoke) StartCarry(Carry.Log, MaltArt.Log(), 1.2f, p);
                return;
            }
            if (Vector2.Distance(p, NextBasketBounds.center) < 1.3f && CanLoadKiln)
                StartCarry(Carry.Malt, MaltArt.Grain(false, 3), 2.6f, p);
        }

        /// <summary>Tip the oldest waiting sack of green malt onto the tray: the kiln takes that order.</summary>
        public void LoadKiln()
        {
            if (!CanLoadKiln) return;
            ActiveOrder o = NextWaiting;
            o.kilnStep = KilnStep.Drying;
            KilnOrder = o;
            KilnProgress01 = 0f;
            _kilnChunksPaid = 0;
            _kilnFor = 0f;
            _hotFor = _coolFor = 0f;
            _nextKilnCharge = 0f;
            _toDrop = Mathf.Max(1, o.steepedGrains);
            _launchDebt = 0f;
            _kilnSpotSkip = 0;
            AudioManager.Play(Sfx.Confirm);
            Changed?.Invoke();
        }

        /// <summary>A log let go at <paramref name="from"/> drops into the firebox.</summary>
        public void Stoke(Vector2 from)
        {
            if (!CanStoke) return;
            var go = new GameObject("Log");
            go.transform.SetParent(transform, false);
            go.transform.position = from;
            var rb = go.AddComponent<Rigidbody2D>();
            rb.mass = 0.6f;
            var col = go.AddComponent<BoxCollider2D>();
            col.size = new Vector2(0.6f, 0.26f);
            MaltShared.AddArt(go.transform, MaltArt.Log(), OrderFire + 1, 1f);
            GameLayers.Assign(go, GameLayers.ShopProp);
            col.excludeLayers = ~0;   // it flies clean into the arch
            rb.linearVelocity = MaltShared.Launch(from, ArchCentre, Mathf.Clamp(Vector2.Distance(from, ArchCentre) * 0.18f, 0.25f, 0.7f));
            rb.angularVelocity = 360f;
            _flyingLogs.Add(rb);
            AudioManager.Play(Sfx.Tab);
        }

        /// <summary>Throw a log from the pile into the firebox (the draught kiln's stoker, and the playtest).</summary>
        public void Stoke() => Stoke(PileWorld + new Vector2(-0.2f, 0.3f));

        private Vector2 ArchCentre => KP((MaltArt.ArchX0 + MaltArt.ArchX1 + 1) * 0.5f, MaltArt.ArchTop + 6f);

        // ------------------------------------------------------------ physics

        private void FixedUpdate()
        {
            float dt = Time.fixedDeltaTime;

            // Green malt tipped from its sack onto the tray: a grain a step, each dropped a
            // hand's breadth above the tray into clear air, spread along it.
            if (_toDrop > 0)
            {
                _launchDebt = Mathf.Min(1f, _launchDebt + dt * 30f);
                float j = (((_kiln.Count + _kilnSpotSkip) * 29) % 13) / 13f;
                Vector2 to = Vector2.Lerp(KP(6f, MaltArt.KilnTrayY - 8f), KP(38f, MaltArt.KilnTrayY - 8f), j);
                if (_launchDebt >= 1f)
                {
                    if (Physics2D.OverlapCircle(to, MaltShared.GrainRadius * 1.2f, GameLayers.ShopPropMask) != null) _kilnSpotSkip++;   // taken: try the next spot
                    else
                    {
                        _launchDebt -= 1f;
                        _toDrop--;
                        Grain g = MaltShared.SpawnGrain(transform, to, false);
                        MaltShared.SetGrainArt(g, 3);
                        g.Body.linearVelocity = new Vector2(0f, -0.5f);
                        _kiln.Add(g);
                    }
                }
            }

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

            // The hot air through the tray. A grain weighs about 0.68 N: the draught at
            // full heat peaks just under that, so the malt shivers and dances on the tray
            // the hotter it runs, but never lifts off it onto the bench.
            if (_hotAir != null)
            {
                _hotAir.forceMagnitude = Heat01 * 0.32f;
                _hotAir.forceVariation = Heat01 * 0.28f;
            }
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
                // A log in its last two seconds is already fading (see burning, above): count
                // it as gone, or the heat dips out of the band before the next one catches.
                int strong = _flyingLogs.Count;
                foreach (float left in _logs) if (left > 2f) strong++;
                if (strong < 2 && CanStoke && Time.time >= _autoStokeAt)
                {
                    _autoStokeAt = Time.time + 1.2f;
                    Stoke();
                }
            }
            Heat01 = Mathf.Lerp(Heat01, target, 1f - Mathf.Exp(-0.55f * dt));

            // The last batch cools on the tray a moment, then is swept off for the next.
            if (_dried.Count > 0 && Time.time >= _driedUntil)
            {
                foreach (var g in _dried) if (g.Body != null) Destroy(g.Body.gameObject);
                _dried.Clear();
                Changed?.Invoke();
            }

            ActiveOrder o = KilnOrder;
            if (o == null) return;

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
            o.kilnStep = KilnStep.Dried;
            o.MaltQuality01 = Mathf.Clamp01(o.maltPoints / (float)QualityBudget.MaltMax);
            if (VfxWorld.Active != null)
                VfxWorld.Active.Puff(KP(22f, MaltArt.KilnTrayY - 2f), new Color(0.9f, 0.8f, 0.6f, 0.4f), 6, 0.8f, 0.6f);
            AudioManager.Play(Sfx.Chime);
            // The order leaves the bench the moment it is dried (an order is only ever at one
            // bench); its malt stays on the tray to cool, and nothing else is loaded until
            // the tray has been swept.
            KilnOrder = null;
            _dried.AddRange(_kiln);
            _kiln.Clear();
            _toDrop = 0;
            _driedUntil = Time.time + 1.2f;
            if (CraftingManager.Instance != null) CraftingManager.Instance.Advance(o);   // on to Prep
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

        private static void Pay(ActiveOrder o, int points, string reason) => MaltShared.Pay(o, points, Station, reason);
        private static void Charge(ActiveOrder o, int points, string reason) => MaltShared.Charge(o, points, Station, reason);
    }
}
