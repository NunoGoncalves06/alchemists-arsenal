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
    /// The Prep bench as a physical bench. The day's six leaves lie on the plank as
    /// real bodies. You get them into the mortar by dragging them there (a
    /// <see cref="TargetJoint2D"/> hold, so a flick throws them) or, as a one-click
    /// alternative, by clicking one, which tosses it along a ballistic arc into the
    /// bowl. A leaf counts once it has actually settled in the bowl.
    ///
    /// Then the grind. The pestle is a body too: hold the pointer on the mortar and
    /// the pestle rises over the bowl; let go and it falls under gravity. What scores
    /// a strike is how fast the pestle is going when it
    /// hits the bowl, measured at the contact: too soft bruises nothing, too hard
    /// smashes the mix out of the bowl. Three clean strikes, each asking a little
    /// more precision, settle the mix, and the mash is tipped into the pot.
    ///
    /// The recipe logic and every point are unchanged from the button version
    /// (<see cref="QualityBudget"/>): what changed is that each is earned with a
    /// physical action rather than a click on a timing bar.
    /// </summary>
    public class PrepBench : MonoBehaviour
    {
        public static PrepBench Instance { get; private set; }

        public sealed class Leaf
        {
            public HerbData Data;
            public Rigidbody2D Body;
            public Grabbable2D Grab;
            public SpriteRenderer Art;
            public Vector2 Home;
            public bool InBowl;
            public float StillFor;
        }

        // Layout, local to the bench root.
        public const float BenchTopY = -1.9f;
        public static readonly Vector2 MortarLocal = new Vector2(2.3f, BenchTopY);
        private const float MortarScale = 1.45f;
        /// <summary>
        /// How far the pestle rises from resting on the mix. A full lift lands at
        /// sqrt(2 g MaxLift), about 8.9 m/s: past the top of the widest clean band (the
        /// brass mortar's first strike, 6.5 + 2.1), so every stroke rises through the
        /// green and out of it rather than stopping inside it.
        /// </summary>
        public const float MaxLift = 4.0f;
        private const float LiftRate = 1.6f;
        /// <summary>The brass mortar widens every clean band by a third.</summary>
        private const float BrassBandScale = 1.33f;
        /// <summary>The widest clean band any strike gets (the first one, in the brass mortar).</summary>
        public static float WidestBand => Bands[0] * BrassBandScale;
        /// <summary>
        /// Where the hold point sits above the bowl's middle at zero lift, chosen so the
        /// pestle's head hangs just clear of the rim (the joint holds it 0.9 above its
        /// centre and the head is 1.3 below that). Any lower and pulling it over from
        /// the bench dragged it through the mortar's wall, where it stuck.
        /// </summary>
        private const float HangAbove = 3.4f;

        /// <summary>Pestle speed at impact (m/s) that scores best, and how far off still counts, per strike.</summary>
        public const float IdealStrike = 6.5f;
        private static readonly float[] Bands = { 1.6f, 1.3f, 1.0f };
        private const float MinStrikeSpeed = 1.4f;

        private readonly List<Leaf> _leaves = new List<Leaf>();
        private Rigidbody2D _pestle;
        private Collider2D _pestleCol;
        private Grabbable2D _pestleGrab;
        private PointerGrabber _grabber;
        private Camera _cam;
        private TargetJoint2D _lift;
        private float _liftHeight;
        private float _strikeCooldown;
        /// <summary>
        /// One strike per blow: a lift arms the pestle and the first blow it lands in
        /// the bowl spends it. Without this a soft blow that first met a leaf, then
        /// settled onto the bowl a moment later, counted as two strikes.
        /// </summary>
        private bool _strikeArmed;
        private int _rolledForDay = -1;
        private Grabbable2D _pressed;
        private float _pressTime;
        private Vector2 _pressAt;

        public bool Attended { get; set; }
        public IReadOnlyList<Leaf> Leaves => _leaves;
        public int StrikesLeft { get; private set; } = QualityBudget.GrindStrikes;
        public float LastStrikeSpeed { get; private set; } = -1f;
        /// <summary>What the last strike landed on, and where (for the playtest's log).</summary>
        public string LastStrikeWith { get; private set; } = "";
        public float CurrentBand => Bands[Mathf.Clamp(QualityBudget.GrindStrikes - StrikesLeft, 0, Bands.Length - 1)]
                                    * (Brass ? BrassBandScale : 1f);

        private static bool HasUpgrade(string id) =>
            SaveSystem.Instance != null && SaveSystem.Instance.State != null && SaveSystem.Instance.State.HasUpgrade(id);

        /// <summary>The brass mortar: a heavier pestle in a brass bowl, and a wider clean band.</summary>
        public static bool Brass => HasUpgrade(UpgradeCatalog.BrassMortar);

        /// <summary>The drying rack: no wilted leaves, and every one sound.</summary>
        public static bool Rack => HasUpgrade(UpgradeCatalog.DryingRack);

        /// <summary>
        /// The auto crusher (the Brass Mortar upgrade): a brass ram on a frame over the
        /// mortar that lifts and drops itself, three strikes, with no hand on it.
        /// </summary>
        public static bool Crusher => Brass;

        /// <summary>The height the crusher lets its ram fall from: v = sqrt(2gh) is the ideal strike.</summary>
        public static float CrusherDrop => IdealStrike * IdealStrike / (2f * Mathf.Abs(Physics2D.gravity.y));

        /// <summary>Where the crusher parks its ram between strikes: high enough that a tossed leaf passes under it.</summary>
        private const float ParkAbove = 1.6f;
        private bool _parked;
        private float _crusherWait;
        public string Reaction { get; private set; } = "";

        private BenchGauge _strikeDial;
        private const float DialMax = 9.5f;   // m/s at the top of the dial, just over a full lift

        private void DrawStrikeDial()
        {
            if (_strikeDial == null) return;
            BrewMixture mix = Mix;
            bool show = Order != null && mix != null && mix.AllLeavesIn && !mix.Ground;
            _strikeDial.SetVisible(show);
            if (!show) return;
            float band = CurrentBand;
            _strikeDial.SetBand((IdealStrike - band) / DialMax, (IdealStrike + band) / DialMax);
            // Empty, at the bottom, until the pestle is resting on the mix and rising:
            // every stroke reads up from nothing. The last blow is told in words.
            float v = LiftReady ? PredictedStrikeSpeed : 0f;
            bool clean = Mathf.Abs(v - IdealStrike) <= band;
            _strikeDial.SetValue(v / DialMax, LiftReady ? (clean ? new Color(0.45f, 0.85f, 0.45f) : new Color(0.96f, 0.85f, 0.45f)) : new Color(0.95f, 0.91f, 0.81f));
            _strikeDial.SetText(LiftReady ? Mps(v) : LastStrikeSpeed >= 0f ? $"last {Mps(LastStrikeSpeed)}" : $"{StrikesLeft} strikes");
        }

        /// <summary>A short line about what the bench wants next, for the station's caption.</summary>
        public string Hint
        {
            get
            {
                BrewMixture mix = Mix;
                if (Order == null || mix == null) return "";
                if (!mix.AllLeavesIn) return $"Drag or click a {mix.NextStep} leaf into the mortar. {mix.Remaining} to go.";
                if (mix.Ground) return "Ground. It goes to the cauldron.";
                if (Crusher) return "The crusher is working the leaves.";
                if (LiftReady) return "Let go when the needle is in the green.";
                if (Lifting) return _overBowl ? "Hold. The pestle is settling into the bowl." : "Hold. The pestle is coming over the bowl.";
                return "Press and hold on the bowl, then let go to smash the leaves.";
            }
        }
        public Color ReactionColor { get; private set; } = Color.white;
        public Leaf Hovered { get; private set; }

        /// <summary>0..1: how high the pestle is being lifted by a held press on the mortar.</summary>
        public float Lift01 => _lift != null ? _liftHeight / MaxLift : 0f;

        /// <summary>True while a held press on the mortar is lifting the pestle.</summary>
        public bool Lifting => _lift != null;

        /// <summary>
        /// How fast the pestle will be going when it lands if you let go now: a fall
        /// from the current lift under gravity, v = sqrt(2gh). The HUD shows it live,
        /// which is what turns "let go at the right moment" into something you can read.
        /// </summary>
        public float PredictedStrikeSpeed
        {
            get
            {
                if (_lift == null || _pestle == null || !LiftReady) return 0f;
                return FallSpeed(FirstContactDrop(), _pestle.linearVelocity.y, _pestle.linearDamping, Time.fixedDeltaTime);
            }
        }

        /// <summary>
        /// How fast a body that is let go <paramref name="drop"/> metres above what it will
        /// meet, moving up at <paramref name="vyUp"/> (negative: already falling), is going
        /// when it touches. The physics step done by hand (gravity, then drag, then the move)
        /// so the dial and the measured blow are the same number; a plain sqrt(2 g h) missed
        /// the drag and the last of the rise, and read soft.
        /// </summary>
        public static float FallSpeed(float drop, float vyUp, float linearDamping, float dt)
        {
            float g = Mathf.Abs(Physics2D.gravity.y);
            float v = -vyUp;          // downward is positive from here
            float fallen = 0f;
            // The engine starts a contact a hair before the two touch (its speculative
            // margin), measured against dropped bodies: 0.01 to 0.02 m all fit.
            const float ContactMargin = 0.015f;
            for (int i = 0; i < 4000 && fallen < drop - ContactMargin; i++)
            {
                v += g * dt;
                v /= 1f + linearDamping * dt;
                fallen += v * dt;
            }
            return Mathf.Max(0f, v);
        }

        private readonly RaycastHit2D[] _castHits = new RaycastHit2D[8];

        /// <summary>
        /// How far the pestle can fall straight down before something stops it: its own
        /// shape cast onto the leaves and the bowl, which is what the blow really lands on
        /// (the head is rounded and a leaf is a disc). The crusher uses the same cast.
        /// </summary>
        private float FirstContactDrop()
        {
            var filter = new ContactFilter2D { useTriggers = false };
            filter.SetLayerMask(GameLayers.ShopPropMask | GameLayers.ShopStaticMask);
            int n = _pestle.Cast(Vector2.down, filter, _castHits, MaxLift + 2f);
            float d = float.MaxValue;
            for (int i = 0; i < n; i++) d = Mathf.Min(d, _castHits[i].distance);
            return n == 0 ? Mathf.Max(0f, _pestle.position.y - PestleHalfLength - LandingY) : d;
        }

        /// <summary>One way to write a strike speed: the dial, the reaction line and the ledger all use it.</summary>
        public static string Mps(float v) => $"{v:0.0} m/s";

        /// <summary>
        /// Where the pestle's head will land: the top of the highest leaf under it, or
        /// the bowl's floor if none is. Measuring to the floor alone read every blow
        /// fast, and a leaf that settled on top of the others (so the blow met it
        /// half a hand early) landed soft while the gauge still showed it clean.
        /// </summary>
        private float LandingY
        {
            get
            {
                float y = BowlFloorY;
                float px = _pestle != null ? _pestle.position.x : MortarWorld.x;
                foreach (var l in _leaves)
                {
                    if (!l.InBowl || l.Body == null) continue;
                    var col = l.Body.GetComponent<Collider2D>();
                    if (col == null) continue;
                    Bounds b = col.bounds;
                    if (b.max.x < px - PestleHeadHalfWidth || b.min.x > px + PestleHeadHalfWidth) continue;
                    y = Mathf.Max(y, b.max.y);
                }
                return y;
            }
        }

        private const float PestleHeadHalfWidth = 0.3f;

        private const float PestleHalfLength = 1.3f;
        private float RimTopY => transform.position.y + MortarLocal.y + 1.08f * MortarScale;
        private float BowlFloorY => transform.position.y + MortarLocal.y + 0.2f * MortarScale;

        public Vector2 MortarWorld => (Vector2)transform.position + MortarLocal + new Vector2(0f, 0.55f);
        public Vector2 PestleWorld => _pestle != null ? _pestle.position : MortarWorld;

        /// <summary>Anything that moved quality or the bench's state (the HUD redraws).</summary>
        public event Action Changed;

        /// <summary>
        /// The order this bench is working — its leaves, its strikes — or showing, once
        /// finished, until the next one waiting at Prep is taken up. The bench works
        /// the oldest order waiting at its stage (see <see cref="CraftingManager"/>).
        /// </summary>
        public ActiveOrder Working { get; private set; }

        /// <summary>The order the bench can still act on: <see cref="Working"/>, while it is at Prep.</summary>
        private ActiveOrder Order => Working != null && Working.stage == BrewStage.Prep ? Working : null;
        private BrewMixture Mix => Order?.Mixture;
        private float _lingerUntil;

        /// <summary>
        /// Take up the next order waiting at Prep once the last one is done (and has
        /// been seen done for a moment): fresh strikes, and its leaves on the bench.
        /// </summary>
        private void BindNext()
        {
            var cm = CraftingManager.Instance;
            if (cm == null || Order != null || Time.time < _lingerUntil) return;
            ActiveOrder next = cm.Waiting(BrewStage.Prep);
            if (next == null || next == Working) return;

            Working = next;
            StrikesLeft = QualityBudget.GrindStrikes;
            LastStrikeSpeed = -1f;
            _strikeArmed = false;
            if (next.Mixture != null)
            {
                EnsureRecipeStock(next.Mixture);
                SetReaction($"{next.heroName}'s {next.Mixture.Recipe.Name} — {next.Mixture.Recipe.Shorthand}.", UI.UITheme.TextMid);
            }
            Changed?.Invoke();
        }

        private void Awake() => Instance = this;

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        // ----------------------------------------------------------------- build

        public void Build(Camera cam)
        {
            _cam = cam;
            Vector2 root = transform.position;

            // The plank the leaves lie on, and the bench's two ends.
            var plank = new GameObject("Plank");
            plank.transform.SetParent(transform, false);
            plank.transform.localPosition = new Vector3(0f, BenchTopY, 0f);
            PixelArt.AddSprite(plank, ShopArt.Plank(), 2, 11.4f);
            var top = plank.AddComponent<EdgeCollider2D>();
            top.points = new[] { new Vector2(-6f, 0f), new Vector2(6f, 0f) };
            top.sharedMaterial = PhysicsMaterials.Wood;
            AddWall(new Vector2(-5.55f, 0f), new Vector2(0.4f, 12f));
            AddWall(new Vector2(5.55f, 0f), new Vector2(0.4f, 12f));
            GameLayers.Assign(plank, GameLayers.ShopStatic);

            BuildMortar();
            BuildPestle();

            _grabber = gameObject.AddComponent<PointerGrabber>();
            _grabber.Configure(cam, GameLayers.ShopPropMask, 0.34f);
            _grabber.Enabled = () => Attended;
            _grabber.OnPicked += g => { _pressed = g; _pressTime = Time.time; _pressAt = Pointer.World(_cam); };
        }

        private void AddWall(Vector2 local, Vector2 size)
        {
            var w = new GameObject("BenchEnd");
            w.transform.SetParent(transform, false);
            w.transform.localPosition = local;
            w.AddComponent<BoxCollider2D>().size = size;
            GameLayers.Assign(w, GameLayers.ShopStatic);
        }

        private void BuildMortar()
        {
            var m = new GameObject("Mortar");
            m.transform.SetParent(transform, false);
            m.transform.localPosition = MortarLocal;
            m.transform.localScale = Vector3.one * MortarScale;
            AddArt(m.transform, ShopArt.MortarBack(Brass), 8, "Back");
            AddArt(m.transform, ShopArt.MortarFront(Brass), 14, "Front");
            if (Rack) BuildRack();
            // The strike dial, on the bench beside the mortar: the clean band around the
            // ideal speed; while the pestle is lifted the needle shows where it would land.
            _strikeDial = BenchGauge.Create(transform, (Vector2)transform.position + new Vector2(0.55f, -0.75f), 2.1f, true, 30, name: "StrikeDial");

            // The bowl, as one solid lip-to-lip line (in the sprite's local units).
            var edge = m.AddComponent<EdgeCollider2D>();
            // A deep, wide bowl: three leaves have to sit in it side by side and on top
            // of each other without the last one sliding back out over the rim.
            var pts = new List<Vector2> { new Vector2(-1.02f, 0.02f), new Vector2(-1.1f, 1.05f), new Vector2(-0.97f, 1.08f) };
            for (int i = 0; i <= 12; i++)
            {
                float a = Mathf.PI + i / 12f * Mathf.PI;          // the inside of the bowl, a half-ellipse
                pts.Add(new Vector2(Mathf.Cos(a) * 0.95f, 1.06f + Mathf.Sin(a) * 0.86f));
            }
            pts.Add(new Vector2(0.97f, 1.08f)); pts.Add(new Vector2(1.1f, 1.05f)); pts.Add(new Vector2(1.02f, 0.02f));
            edge.points = pts.ToArray();
            edge.edgeRadius = 0.03f;
            edge.sharedMaterial = PhysicsMaterials.Stone;
            GameLayers.Assign(m, GameLayers.ShopStatic);
        }

        /// <summary>The drying rack over the bench, hung with bundles of herbs.</summary>
        private void BuildRack()
        {
            var rack = new GameObject("DryingRack");
            rack.transform.SetParent(transform, false);
            // On the open wall under the recipe card, above the leaves: hung any higher,
            // the card covered it and the upgrade could not be seen at all.
            rack.transform.localPosition = new Vector2(-2.6f, 0.15f);
            AddArt(rack.transform, ShopArt.DryingRack(), -11, "Bar").transform.localScale = Vector3.one * 1.1f;
            for (int i = 0; i < 5; i++)
            {
                var bundle = AddArt(rack.transform, ShopProps.HerbBundle(i + 3), -12, "Herbs");
                bundle.transform.localPosition = new Vector2((-26f + i * 13f) / ShopArt.PPU * 1.1f, -0.35f);
                bundle.transform.localScale = Vector3.one * 0.8f;
            }
        }

        /// <summary>
        /// The leaves are in and the pestle is waiting to be used. Nothing is drawn on
        /// the bowl for it any more: the tutorial's spotlight shows where to press.
        /// </summary>
        public bool PressHintShowing { get; private set; }

        private void UpdatePressHint()
        {
            BrewMixture mix = Mix;
            PressHintShowing = !Crusher && Attended && mix != null && mix.AllLeavesIn && !mix.Ground
                               && StrikesLeft > 0 && _lift == null && Order != null;
        }

        private void BuildPestle()
        {
            var p = new GameObject("Pestle");
            p.transform.SetParent(transform, false);
            p.transform.localPosition = MortarLocal + new Vector2(2.1f, 1.3f);
            if (Crusher)
            {
                p.transform.localPosition = MortarLocal + new Vector2(0f, 0.55f + HangAbove + ParkAbove - PestleAnchor);
                var frame = new GameObject("CrusherFrame");
                frame.transform.SetParent(transform, false);
                frame.transform.localPosition = MortarLocal;   // standing on the bench, astride the mortar
                AddArt(frame.transform, ShopArt.CrusherFrame(), 7, "Art").transform.localScale = Vector3.one * 1.45f;
            }
            AddArt(p.transform, Crusher ? ShopArt.CrusherRam() : ShopArt.Pestle(Brass), 12, "Art").transform.localScale = Vector3.one * 1.45f;
            _pestle = p.AddComponent<Rigidbody2D>();
            _pestle.mass = 2f;
            _pestle.linearDamping = 0.05f;
            _pestle.freezeRotation = true;   // a pestle comes down head first
            var cap = p.AddComponent<CapsuleCollider2D>();
            cap.size = new Vector2(0.5f, 2.6f);
            cap.sharedMaterial = PhysicsMaterials.Wood;
            _pestleCol = cap;
            // Worked only by holding on the mortar (one clear control), never dragged:
            // with the pestle lying in the bowl under the pointer, a press used to pick
            // it up instead of lifting it.
            _pestleGrab = p.AddComponent<Grabbable2D>();
            _pestleGrab.CanGrab = false;
            GameLayers.Assign(p, GameLayers.ShopProp);

            // The sensor rides on the pestle: whatever its head meets inside the bowl
            // (the bowl itself, or the leaves lying in it) is the strike.
            var sensor = p.AddComponent<ImpactSensor2D>();
            sensor.Configure(GameLayers.ShopPropMask | GameLayers.ShopStaticMask, MinStrikeSpeed);
            sensor.OnImpact += OnBowlImpact;
        }

        private static SpriteRenderer AddArt(Transform parent, Sprite s, int order, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = s;
            sr.sharedMaterial = SpriteMaterials.For(s);
            sr.sortingOrder = order;
            return sr;
        }

        // --------------------------------------------------------------- the day

        /// <summary>Roll the day's six leaves and lay them out; reset the mortar.</summary>
        public void NewDay(int day)
        {
            _rolledForDay = day;
            Working = null;
            foreach (var l in _leaves) if (l.Body != null) Destroy(l.Body.gameObject);
            _leaves.Clear();
            StrikesLeft = QualityBudget.GrindStrikes;
            LastStrikeSpeed = -1f;
            _strikeArmed = false;
            SetReaction("", Color.white);

            var rng = new System.Random(day * 7717 + 31);
            var stock = new List<HerbData>();
            foreach (ElementType e in new[]
                     { ElementType.Nature, ElementType.Fire, ElementType.Water, ElementType.Poison, ElementType.Arcane })
                stock.Add(Roll(e, rng, Rack));
            stock.Add(Roll((ElementType)rng.Next(0, 5), rng, Rack));
            for (int i = stock.Count - 1; i > 0; i--)
            {
                int j = rng.Next(0, i + 1);
                (stock[i], stock[j]) = (stock[j], stock[i]);
            }

            for (int i = 0; i < stock.Count; i++)
                _leaves.Add(SpawnLeaf(stock[i], SlotLocal(i)));
            _toppedUpFor = null;

            if (_pestle != null)
            {
                _pestle.position = (Vector2)transform.position + MortarLocal
                                   + (Crusher ? new Vector2(0f, 0.55f + HangAbove + ParkAbove - PestleAnchor) : new Vector2(2.1f, 1.3f));
                _pestle.rotation = 0f;
                _pestle.linearVelocity = Vector2.zero;
                _pestle.angularVelocity = 0f;
            }
            Changed?.Invoke();
        }

        public bool RolledFor(int day) => _rolledForDay == day;

        private static Vector2 SlotLocal(int i) => new Vector2(-5.0f + i * 0.7f, BenchTopY + 0.32f);

        private BrewMixture _toppedUpFor;

        /// <summary>
        /// Make sure today's recipe can actually be made from the bench.
        ///
        /// Every recipe doubles its base element (Fireblood is 2 x Fire + 1 x Nature),
        /// but the daily stock is one leaf of each element plus one wildcard, rolled
        /// before the customer has even ordered. So a clean mix was only possible on
        /// days the wildcard happened to match, and nothing ever said so. Once the order
        /// is known the bench adds whatever the recipe is short of (deterministically,
        /// from the day), in the free slot at the end of the row.
        /// </summary>
        public void EnsureRecipeStock(BrewMixture mix)
        {
            if (mix == null || mix == _toppedUpFor) return;
            _toppedUpFor = mix;

            var have = new Dictionary<ElementType, int>();
            foreach (var l in _leaves)
                if (l.Body != null && !l.InBowl) have[l.Data.Element] = (have.TryGetValue(l.Data.Element, out int n) ? n : 0) + 1;
            var need = new Dictionary<ElementType, int>();
            foreach (ElementType e in mix.Recipe.Steps) need[e] = (need.TryGetValue(e, out int n) ? n : 0) + 1;

            var rng = new System.Random(_rolledForDay * 131 + (int)mix.Recipe.Result + 17 * _leaves.Count);
            foreach (var kv in need)
            {
                int short_ = kv.Value - (have.TryGetValue(kv.Key, out int h) ? h : 0);
                for (int i = 0; i < short_; i++)
                    _leaves.Add(SpawnLeaf(Roll(kv.Key, rng, Rack), FreeSlotLocal()));
            }
            Changed?.Invoke();
        }

        /// <summary>
        /// The first place on the plank no leaf is lying in. Several orders' leaves
        /// pass over the bench in a day, so slots are reused rather than counted up
        /// (counting up walked the extras along the plank and into the mortar).
        /// </summary>
        private Vector2 FreeSlotLocal()
        {
            const int Slots = 8;
            for (int i = 0; i < Slots; i++)
            {
                Vector2 home = (Vector2)transform.position + SlotLocal(i);
                bool taken = false;
                foreach (var l in _leaves)
                    if (l.Body != null && !l.InBowl && (l.Home - home).sqrMagnitude < 0.01f) { taken = true; break; }
                if (!taken) return SlotLocal(i);
            }
            return SlotLocal(Slots - 1);
        }

        /// <summary>
        /// One leaf off the shelf. Hung on the drying rack, herbs keep: none wilts and
        /// every one is sound (potency 2-3). Left in a heap they don't: two in five come
        /// wilted and none is better than middling (potency 1-2).
        /// </summary>
        public static HerbData Roll(ElementType element, System.Random rng, bool rack)
        {
            string name = HerbData.NameFor(element, rng.Next(0, 2));
            int potency = rack ? rng.Next(2, 4) : rng.Next(1, 3);
            bool wilted = !rack && rng.Next(0, 100) < 40;
            return HerbData.Create(name, element, potency, wilted);
        }

        private Leaf SpawnLeaf(HerbData data, Vector2 local)
        {
            var go = new GameObject("Leaf_" + data.DisplayName);
            go.transform.SetParent(transform, false);
            go.transform.localPosition = local;

            var rb = go.AddComponent<Rigidbody2D>();
            rb.mass = data.Mass;
            rb.linearDamping = 0.02f;   // no air drag: the toss is solved as a pure ballistic arc
            rb.angularDamping = 1.2f;
            var col = go.AddComponent<CircleCollider2D>();
            col.radius = 0.24f;
            col.sharedMaterial = PhysicsMaterials.Herb;

            float size = 0.55f + 0.08f * data.Potency;       // potent leaves are plumper
            var art = new GameObject("Art");
            art.transform.SetParent(go.transform, false);
            SpriteRenderer sr = PixelArt.AddSprite(art, PixelSprites.Herb(data.Element), 11, size);
            // A thin leaf is a little pale and yellowed, a potent one deep and standing up.
            sr.color = data.Potency <= 1 ? new Color(0.9f, 0.92f, 0.74f)
                     : data.Potency >= 3 ? Color.white : new Color(0.96f, 0.97f, 0.9f);
            if (data.Potency >= 3) art.transform.localScale = new Vector3(1f, 1.08f, 1f);
            if (data.Wilted)
            {
                sr.color = new Color(0.58f, 0.52f, 0.42f);
                art.transform.localRotation = Quaternion.Euler(0f, 0f, -35f);
                art.transform.localScale = new Vector3(0.9f, 0.78f, 1f);
            }

            var grab = go.AddComponent<Grabbable2D>();
            grab.OnReleased += OnLeafReleased;
            GameLayers.Assign(go, GameLayers.ShopProp);
            return new Leaf { Data = data, Body = rb, Grab = grab, Art = sr, Home = (Vector2)transform.position + local };
        }

        // ------------------------------------------------------------------ input

        private void Update()
        {
            if (AlchemistsArsenal.Core.TimeControl.Paused) return;   // the pause menu: nothing here moves or reads the pointer
            float dt = Time.deltaTime;
            _strikeCooldown -= dt;
            BindNext();
            UpdatePressHint();
            DrawStrikeDial();
            if (Crusher) DriveCrusher(dt);
            if (!Attended) { if (!Crusher) EndLift(); Hovered = null; return; }

            Vector2 p = Pointer.World(_cam);
            Grabbable2D under = _grabber != null ? _grabber.Pick(p) : null;
            Hovered = null;
            foreach (var l in _leaves) if (l.Grab == under) Hovered = l;

            // Hold on the mortar (not on something grabbable) to lift the pestle.
            bool onMortar = Vector2.Distance(p, MortarWorld) < 1.35f;
            if (Crusher) return;   // the machine's ram, not a hand's pestle
            if (Pointer.PressedThisFrame && onMortar && under == null && _grabber.Held == null) BeginLift();
            if (_lift != null)
            {
                if (!Pointer.Held) EndLift();
                else StepLift(dt);
            }
        }

        /// <summary>
        /// The crusher's cycle, the hand's press done by the machine: bring the ram over
        /// the bowl, set it on the mix, raise it until it would land at the ideal speed,
        /// drop it, wait for the blow; three times. Between orders it hangs parked.
        /// </summary>
        private void DriveCrusher(float dt)
        {
            BrewMixture mix = Mix;
            bool work = Order != null && mix != null && mix.AllLeavesIn && !mix.Ground && StrikesLeft > 0;
            if (_crusherWait > 0f)
            {
                _crusherWait -= dt;
                // The blow has landed (it spent the arming): let the leaves settle, then go again.
                if (!_strikeArmed && _crusherWait > 0.8f) _crusherWait = 0.8f;
                return;
            }
            if (!work) { Park(); return; }
            if (_lift == null || _parked) { if (_lift != null) Destroy(_lift); _lift = null; BeginLift(); return; }
            if (LiftReady && PredictedStrikeSpeed >= IdealStrike) { EndLift(); _crusherWait = 1.5f; return; }
            StepLift(dt);
        }

        /// <summary>Hang the ram above the bowl, clear of the leaves going in.</summary>
        private void Park()
        {
            if (_pestle == null) return;
            if (_lift == null) { BeginLift(); _strikeArmed = false; }
            if (_lift == null) return;
            _parked = true;
            _lift.target = new Vector2(MortarWorld.x, MortarWorld.y + HangAbove + ParkAbove);
        }

        /// <summary>One frame of a held lift: over the bowl, down onto the mix, then up.</summary>
        private void StepLift(float dt)
        {
                {
                    // First bring the pestle over the bowl and let it steady; only then
                    // does the held press start raising it. Lifting while it was still
                    // swinging in from the side put that sideways speed into the strike.
                    float hold = MortarWorld.y + HangAbove;
                    bool steady = Mathf.Abs(_pestle.position.x - MortarWorld.x) < 0.12f
                                  && _pestle.linearVelocity.sqrMagnitude < 0.3f * 0.3f;
                    if (!_overBowl)
                    {
                        // Up first, then across: pulled diagonally, the pestle dragged
                        // along the mortar's rim and friction pinned it there.
                        float head = _pestle.position.y - PestleHalfLength;
                        bool clear = head > RimTopY + 0.1f;
                        _lift.target = new Vector2(clear ? MortarWorld.x : _pestle.position.x, hold);
                        _steadyFor = steady ? _steadyFor + dt : 0f;
                        if (_steadyFor > 0.15f)
                        {
                            _overBowl = true;
                            _steadyFor = 0f;
                            _restY = RestOnMixY;
                        }
                    }
                    else if (!LiftReady)
                    {
                        // Then down onto the mix, the way a hand sets a pestle in the bowl
                        // before raising it: the stroke starts from rest, at no speed at all.
                        // The rest point follows leaves that settle lower, never ones the
                        // head nudges up, so it cannot chase them or stop short of them.
                        _restY = Mathf.Min(_restY, RestOnMixY);
                        _lift.target = new Vector2(MortarWorld.x, _restY);
                        bool resting = Mathf.Abs(_pestle.position.y + PestleAnchor - _restY) < 0.06f;
                        _steadyFor = steady && resting ? _steadyFor + dt : 0f;
                        if (_steadyFor > 0.1f) LiftReady = true;
                    }
                    else
                    {
                        _liftHeight = Mathf.Min(MaxLift, _liftHeight + LiftRate * dt);
                        _lift.target = new Vector2(MortarWorld.x, _restY + _liftHeight);
                    }
                }
        }

        private float _steadyFor, _restY;
        /// <summary>The held pestle has come over the bowl and is being set down onto the mix.</summary>
        private bool _overBowl;
        /// <summary>The lift joint's anchor, above the pestle's centre.</summary>
        private const float PestleAnchor = 0.9f;
        /// <summary>
        /// The lift joint's target that rests the pestle's head just on top of the mix: down
        /// by however far the head can still fall (the same cast the dial reads), so a stroke
        /// starts from nothing on the needle.
        /// </summary>
        private float RestOnMixY => _pestle.position.y + PestleAnchor - Mathf.Max(0f, FirstContactDrop() - 0.02f);

        /// <summary>True once a held press has the pestle steady over the bowl and rising.</summary>
        public bool LiftReady { get; private set; }

        private void BeginLift()
        {
            if (_pestle == null || _pestleGrab.IsHeld) return;
            _pestle.rotation = 0f;
            _pestle.angularVelocity = 0f;
            _lift = _pestle.gameObject.AddComponent<TargetJoint2D>();
            _lift.autoConfigureTarget = false;
            _lift.anchor = new Vector2(0f, PestleAnchor);
            _lift.frequency = 5f;
            _lift.dampingRatio = 1f;
            _lift.maxForce = 250f;
            _liftHeight = 0f;
            _steadyFor = 0f;
            _overBowl = false;
            LiftReady = false;
            _strikeArmed = true;
            _parked = false;
            _lift.target = new Vector2(_pestle.position.x, MortarWorld.y + HangAbove);
        }

        /// <summary>Let go: the joint goes and gravity takes the pestle down into the bowl.</summary>
        /// <summary>What the dial read the moment the pestle was let go (0 if it was not rising yet).</summary>
        public float ReleasePrediction { get; private set; }

        /// <summary>How far the dial expected the pestle to fall from where it was let go (m).</summary>
        public float ReleaseDrop { get; private set; }

        /// <summary>How far the pestle really fell before the last strike (m); differs from <see cref="ReleaseDrop"/> when a leaf moved under it. NaN for a strike that did not follow a lift.</summary>
        public float LastStrikeFall { get; private set; }

        private float _releaseY = float.NaN;

        private void EndLift()
        {
            if (_lift == null) return;
            ReleasePrediction = LiftReady ? PredictedStrikeSpeed : 0f;
            ReleaseDrop = LiftReady ? FirstContactDrop() : 0f;
            _releaseY = _pestle.position.y;
            Destroy(_lift);
            _lift = null;
            LiftReady = false;
            _overBowl = false;
            _liftHeight = 0f;
            _pestle.angularVelocity = 0f;
        }

        private void OnLeafReleased(Grabbable2D g)
        {
            // A quick click with no drag tosses the leaf into the mortar instead: the
            // one-click way to play, and exactly the same physics as a throw.
            if (g != _pressed) return;
            _pressed = null;
            bool click = Time.time - _pressTime < 0.3f && Vector2.Distance(Pointer.World(_cam), _pressAt) < 0.2f;
            if (!click) return;
            foreach (var l in _leaves)
                if (l.Grab == g && !l.InBowl) Toss(l);
        }

        /// <summary>Throw <paramref name="leaf"/> on a ballistic arc that lands in the bowl.</summary>
        public void Toss(Leaf leaf)
        {
            if (leaf == null || leaf.Body == null || leaf.InBowl) return;
            Vector2 from = leaf.Body.position;
            // Aim at a different part of the bowl for each leaf so they sit side by
            // side instead of landing on each other.
            int inBowl = 0;
            foreach (var l in _leaves) if (l.InBowl) inBowl++;
            float[] spread = { -0.45f, 0.45f, 0f, -0.2f, 0.2f };
            Vector2 to = MortarWorld + new Vector2(spread[inBowl % spread.Length], 0.1f);
            Vector2 g = Physics2D.gravity * leaf.Body.gravityScale;
            // Fast enough to reach from anywhere on the bench: the slowest throw that
            // carries a distance d is sqrt(g d) at 45 degrees; a margin over that lets
            // the solver pick the high arc, which drops into the bowl from above.
            float d = Vector2.Distance(from, to);
            float speed = Mathf.Max(7.2f, Mathf.Sqrt(g.magnitude * d) * 1.2f);
            Vector2 v = BallisticSolver.TrySolveArc(from, to, speed, g, true, out Vector2 arc)
                ? arc : BallisticSolver.SolveLob(from, to, speed, g);
            // Thrown on the next physics step, not from the click's frame.
            _impulses.Add(leaf.Body, v * leaf.Body.mass, -0.05f * leaf.Body.mass, resetVelocity: true);
            AudioManager.Play(Sfx.Tab);
        }

        // --------------------------------------------------------------- physics

        private readonly ImpulseQueue _impulses = new ImpulseQueue();

        private void FixedUpdate()
        {
            _impulses.Flush();
            float dt = Time.fixedDeltaTime;
            Vector2 bowl = MortarWorld;
            foreach (var l in _leaves)
            {
                if (l.Body == null) continue;
                Vector2 p = l.Body.position;

                // Off the bench entirely: it goes back where it was.
                if (p.y < transform.position.y + BenchTopY - 2.5f)
                {
                    l.Body.position = l.Home;
                    l.Body.linearVelocity = Vector2.zero;
                    l.Body.angularVelocity = 0f;
                    continue;
                }
                if (l.InBowl) continue;

                // Anywhere inside the bowl up to its rim counts, including resting on
                // top of the leaves already in it (the third leaf never touches the floor).
                bool inside = Mathf.Abs(p.x - bowl.x) < 1.25f && p.y > bowl.y - 0.6f && p.y < bowl.y + 1.25f;
                // The bowl's grit stops a round leaf rocking back and forth in it like
                // a pendulum (it never counted as settled otherwise).
                l.Body.linearDamping = inside ? 3.5f : 0.02f;
                l.Body.angularDamping = inside ? 4f : 1.2f;
                bool still = l.Body.linearVelocity.sqrMagnitude < 0.45f * 0.45f && !l.Grab.IsHeld;
                l.StillFor = inside && still ? l.StillFor + dt : 0f;
                if (l.StillFor >= 0.25f) Settle(l);
            }
        }

        // ------------------------------------------------------------------ view

        /// <summary>
        /// How much of the bowl's physical depth shows through its mouth. The mortar is
        /// drawn in three-quarter view, so its inside is foreshortened: a leaf lying on
        /// the floor of the (side-on, physical) bowl is seen just inside the mouth,
        /// its lower half behind the near lip. Drawn where the body really was, it hung
        /// in the middle of the belly, painted over the bowl's front.
        /// </summary>
        private const float DepthShown = 0.22f;

        /// <summary>Where something at physical point <paramref name="world"/> is drawn (the bowl's inside is foreshortened).</summary>
        public Vector2 ToView(Vector2 world)
        {
            float rim = RimTopY;
            float bowlX = transform.position.x + MortarLocal.x;
            if (world.y >= rim || Mathf.Abs(world.x - bowlX) > 1.1f * MortarScale) return world;
            return new Vector2(world.x, rim - (rim - world.y) * DepthShown);
        }

        /// <summary>True when <paramref name="leaf"/> is drawn inside the bowl's mouth (the playtest checks it).</summary>
        public bool ShowsInBowl(Leaf leaf)
        {
            if (leaf == null || leaf.Art == null) return false;
            Vector2 at = leaf.Art.transform.position;
            float baseY = transform.position.y + MortarLocal.y;
            float lip = baseY + ShopArt.MortarLipLocalY * MortarScale;
            float halfMouth = 13.5f / ShopArt.PPU * MortarScale;
            return Mathf.Abs(at.y - lip) < 0.3f && Mathf.Abs(at.x - (transform.position.x + MortarLocal.x)) < halfMouth;
        }

        private void LateUpdate()
        {
            foreach (var l in _leaves)
                if (l.Body != null && l.Art != null)
                    l.Art.transform.position = ToView(l.Body.position);
        }

        /// <summary>A leaf came to rest in the bowl: it is in the mix now, for better or worse.</summary>
        private void Settle(Leaf leaf)
        {
            leaf.InBowl = true;
            leaf.Grab.Release();
            leaf.Grab.CanGrab = false;

            var order = Order;
            BrewMixture mix = Mix;
            if (order == null || mix == null || mix.AllLeavesIn)
            {
                SetReaction(order == null ? "No order is waiting at this bench yet — take a job at the Counter." : "The mortar is full already.",
                    UI.UITheme.TextMid);
                Changed?.Invoke();
                return;
            }

            HerbData ing = leaf.Data;
            bool onCue = mix.IsNextStep(ing.Element);
            bool inRecipe = Array.IndexOf(mix.Recipe.Steps, ing.Element) >= 0;
            mix.Added.Add(ing.Element);

            if (onCue)
            {
                int gain = QualityBudget.LeafOnCue(ing.Potency, ing.Wilted);
                order.ApplyBonus(gain, "Prep", $"{ing.DisplayName} in on cue (+{gain})");
                SetReaction($"{ing.DisplayName} goes in cleanly.", UI.UITheme.Ok);
            }
            else if (inRecipe)
            {
                int gain = QualityBudget.LeafOutOfOrder(ing.Potency, ing.Wilted);
                order.ApplyBonus(gain, "Prep", $"{ing.DisplayName} — right leaf, out of order (+{gain})");
                SetReaction($"{ing.DisplayName} belongs here, just not yet.", UI.UITheme.Candle);
            }
            else
            {
                int loss = QualityBudget.WrongLeaf(ing.Potency);
                string reaction = RecipeBook.Reaction(mix.Recipe.Result, ing.Element);
                order.ApplyDeduction(loss, "Prep", $"{ing.DisplayName} — {reaction}");
                SetReaction($"{ing.DisplayName}: {reaction}", UI.UITheme.Danger);
            }

            if (VfxWorld.Active != null)
                VfxWorld.Active.Burst(ToView(leaf.Body.position), PixelArt.Element(ing.Element), 8, 1.6f, 0.08f, 0.4f);
            AudioManager.Play(onCue || inRecipe ? Sfx.Confirm : Sfx.Deny);

            // The last leaf in: say what comes next, loudly, on the bench itself.
            if (mix.AllLeavesIn)
            {
                SetReaction("All the leaves are in. Now PRESS AND HOLD on the bowl to lift the pestle — let go to smash them.",
                    UI.UITheme.CandleHot);
                if (VfxWorld.Active != null)
                    VfxWorld.Active.Flash(MortarWorld + Vector2.up * 0.9f, new Color(1f, 0.85f, 0.45f, 0.6f), 1.6f, 0.35f);
                AudioManager.Play(Sfx.Chime);
            }
            Changed?.Invoke();
        }

        /// <summary>The pestle (or anything) hit the bowl. Only the pestle's hits are strikes.</summary>
        private void OnBowlImpact(ImpactSensor2D sensor, Impact hit)
        {
            if (_strikeCooldown > 0f) return;
            // Only a blow landed in the bowl counts, not the pestle knocking the bench.
            bool inBowl = Mathf.Abs(hit.Point.x - MortarWorld.x) < 1.3f && hit.Point.y < RimTopY + 0.15f
                          && hit.Point.y > transform.position.y + MortarLocal.y;
            if (!inBowl) return;
            // Knocks while the held press is still lifting it are not strikes: a
            // pestle let go of (or swung down by hand) is.
            if (_lift != null) return;
            var order = Order;
            BrewMixture mix = Mix;
            if (order == null || mix == null || mix.Ground) return;
            if (!mix.AllLeavesIn)
            {
                SetReaction("Add every leaf before you start grinding.", UI.UITheme.TextMid);
                Changed?.Invoke();
                return;
            }
            if (StrikesLeft <= 0 || !_strikeArmed) return;

            _strikeArmed = false;     // the next strike needs the next lift
            _strikeCooldown = 0.4f;
            LastStrikeWith = $"{(hit.Other != null ? hit.Other.name : "?")} at ({hit.Point.x:0.00}, {hit.Point.y:0.00}), " +
                             $"pestle v {_pestle.linearVelocity.y:0.00}, {CountInBowl()} leaves in the bowl";
            float band = CurrentBand;
            float speed = hit.Vertical;   // how fast the pestle was falling when it touched: the one number
            LastStrikeSpeed = speed;
            LastStrikeFall = _releaseY - _pestle.position.y;   // NaN unless this strike follows a lift
            _releaseY = float.NaN;
            StrikesLeft--;
            float off = Mathf.Abs(speed - IdealStrike);
            bool clean = off <= band;

            if (clean)
            {
                int gain = Mathf.RoundToInt(Mathf.Lerp(QualityBudget.StrikeMax, QualityBudget.StrikeMin, off / band));
                order.ApplyBonus(gain, "Prep", $"Clean strike at {Mps(speed)} (+{gain})");
                SetReaction($"Clean strike — {Mps(speed)}.", UI.UITheme.Ok);
                AudioManager.Play(Sfx.Seal);
                CrushBurst(hit.Point, 12);
                CameraRig.Shake(0.08f);
            }
            else if (speed < IdealStrike)
            {
                order.ApplyDeduction(QualityBudget.StrikeMiss, "Prep", $"Too soft ({Mps(speed)}) — barely bruised it");
                SetReaction($"Too soft — {Mps(speed)}. Lift it higher before you let go.", UI.UITheme.Danger);
                AudioManager.Play(Sfx.Deny);
                CrushBurst(hit.Point, 4);
            }
            else
            {
                order.ApplyDeduction(QualityBudget.StrikeMiss, "Prep", $"Too hard ({Mps(speed)}) — the mix jumped out");
                SetReaction($"Too hard — {Mps(speed)}. Some of it jumped the bowl.", UI.UITheme.Danger);
                AudioManager.Play(Sfx.Deny);
                PopOut();
                CameraRig.Shake(0.18f);
            }

            foreach (var l in _leaves)
                if (l.InBowl && l.Art != null) l.Art.transform.localScale *= 0.86f;   // crushed a little more

            if (StrikesLeft <= 0) FinishMix(order, mix);
            Changed?.Invoke();
        }

        private int CountInBowl()
        {
            int n = 0;
            foreach (var l in _leaves) if (l.InBowl && l.Body != null) n++;
            return n;
        }

        private void CrushBurst(Vector2 at, int n)
        {
            if (VfxWorld.Active == null || Mix == null) return;
            Color c = PixelArt.Element(Mix.Recipe.Result);
            VfxWorld.Active.Burst(ToView(at) + Vector2.up * 0.2f, c, n, 2.4f, 0.08f, 0.45f);
        }

        /// <summary>A too-hard strike throws a crushed leaf clean out of the bowl.</summary>
        private void PopOut()
        {
            foreach (var l in _leaves)
            {
                if (!l.InBowl || l.Body == null) continue;
                _impulses.Add(l.Body, new Vector2(-1.2f, 4.6f) * l.Body.mass);
                break;
            }
        }

        /// <summary>
        /// Settle the mixture: score it and send the order on to the Cauldron. The mash
        /// waits for the pot there: the cauldron takes it up (the band width it earned,
        /// the leaves falling in) when it takes up this order, which may be after
        /// someone else's brew is done with it.
        /// </summary>
        private void FinishMix(ActiveOrder order, BrewMixture mix)
        {
            mix.Ground = true;
            MixOutcome outcome = mix.Evaluate();
            int delta = RecipeBook.QualityDelta(outcome);
            if (delta > 0) order.ApplyBonus(delta, "Prep", $"{outcome} mix — {mix.Recipe.Name}");
            else if (delta < 0) order.ApplyDeduction(-delta, "Prep", $"{outcome} mix — {mix.Recipe.Name}");

            foreach (var l in _leaves)
            {
                if (!l.InBowl || l.Body == null) continue;
                if (VfxWorld.Active != null) VfxWorld.Active.Puff(ToView(l.Body.position), new Color(0.7f, 0.65f, 0.6f, 0.35f), 3, 0.2f, 0.3f);
                Destroy(l.Body.gameObject);
                l.Body = null;
            }
            _leaves.RemoveAll(l => l.Body == null);

            if (CraftingManager.Instance != null) CraftingManager.Instance.Advance(order);
            _lingerUntil = Time.time + 1.5f;

            SetReaction(RecipeBook.Describe(outcome),
                outcome == MixOutcome.Perfect ? UI.UITheme.Ok
                : outcome == MixOutcome.Close ? UI.UITheme.Candle : UI.UITheme.Danger);
            AudioManager.Play(outcome >= MixOutcome.Close ? Sfx.Chime : Sfx.Deny);
        }

        private void SetReaction(string text, Color color)
        {
            Reaction = text;
            ReactionColor = color;
        }

        /// <summary>The best leaf on the bench for <paramref name="element"/> (for hints and the playtest).</summary>
        public Leaf BestLeafFor(ElementType element)
        {
            Leaf best = null;
            foreach (var l in _leaves)
            {
                if (l.Body == null || l.InBowl || l.Data.Element != element) continue;
                if (best == null || Score(l) > Score(best)) best = l;
            }
            return best;
            int Score(Leaf x) => x.Data.Potency * 2 - (x.Data.Wilted ? 3 : 0);
        }
    }
}
