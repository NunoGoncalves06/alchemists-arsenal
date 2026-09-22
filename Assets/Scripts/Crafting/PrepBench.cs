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
        private const float MaxLift = 2.4f, LiftRate = 1.6f;
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
        private int _rolledForDay = -1;
        private Grabbable2D _pressed;
        private float _pressTime;
        private Vector2 _pressAt;

        public bool Attended { get; set; }
        public IReadOnlyList<Leaf> Leaves => _leaves;
        public int StrikesLeft { get; private set; } = QualityBudget.GrindStrikes;
        public float LastStrikeSpeed { get; private set; } = -1f;
        public float CurrentBand => Bands[Mathf.Clamp(QualityBudget.GrindStrikes - StrikesLeft, 0, Bands.Length - 1)];
        public string Reaction { get; private set; } = "";
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
                // A drop from where the head actually is: v = sqrt(2 g h).
                float h = Mathf.Max(0f, _pestle.position.y - PestleHalfLength - BowlFloorY);
                return Mathf.Sqrt(2f * Mathf.Abs(Physics2D.gravity.y) * h);
            }
        }

        private const float PestleHalfLength = 1.3f;
        private float RimTopY => transform.position.y + MortarLocal.y + 1.08f * MortarScale;
        private float BowlFloorY => transform.position.y + MortarLocal.y + 0.2f * MortarScale;

        public Vector2 MortarWorld => (Vector2)transform.position + MortarLocal + new Vector2(0f, 0.55f);
        public Vector2 PestleWorld => _pestle != null ? _pestle.position : MortarWorld;

        /// <summary>Anything that moved quality or the bench's state (the HUD redraws).</summary>
        public event Action Changed;

        private static BrewMixture Mix => CraftingManager.Instance != null ? CraftingManager.Instance.Mixture : null;
        private static ActiveOrder Order => CraftingManager.Instance != null ? CraftingManager.Instance.CurrentOrder : null;

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
            AddArt(m.transform, ShopArt.MortarBack(), 8, "Back");
            AddArt(m.transform, ShopArt.MortarFront(), 14, "Front");

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

        private void BuildPestle()
        {
            var p = new GameObject("Pestle");
            p.transform.SetParent(transform, false);
            p.transform.localPosition = MortarLocal + new Vector2(2.1f, 1.3f);
            AddArt(p.transform, ShopArt.Pestle(), 12, "Art").transform.localScale = Vector3.one * 1.45f;
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
            foreach (var l in _leaves) if (l.Body != null) Destroy(l.Body.gameObject);
            _leaves.Clear();
            StrikesLeft = QualityBudget.GrindStrikes;
            LastStrikeSpeed = -1f;
            SetReaction("", Color.white);

            var rng = new System.Random(day * 7717 + 31);
            var stock = new List<HerbData>();
            foreach (ElementType e in new[]
                     { ElementType.Nature, ElementType.Fire, ElementType.Water, ElementType.Poison, ElementType.Arcane })
                stock.Add(Roll(e, rng));
            stock.Add(Roll((ElementType)rng.Next(0, 5), rng));
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
                _pestle.position = (Vector2)transform.position + MortarLocal + new Vector2(2.1f, 1.3f);
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

            var rng = new System.Random(_rolledForDay * 131 + (int)mix.Recipe.Result);
            foreach (var kv in need)
            {
                int short_ = kv.Value - (have.TryGetValue(kv.Key, out int h) ? h : 0);
                for (int i = 0; i < short_; i++)
                    _leaves.Add(SpawnLeaf(Roll(kv.Key, rng), SlotLocal(_leaves.Count)));
            }
            Changed?.Invoke();
        }

        private static HerbData Roll(ElementType element, System.Random rng)
        {
            string name = HerbData.NameFor(element, rng.Next(0, 2));
            int potency = rng.Next(1, 4);
            bool wilted = rng.Next(0, 100) < 25;
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
            if (data.Wilted)
            {
                sr.color = new Color(0.62f, 0.58f, 0.52f);
                art.transform.localRotation = Quaternion.Euler(0f, 0f, -25f);
            }

            var grab = go.AddComponent<Grabbable2D>();
            grab.OnReleased += OnLeafReleased;
            GameLayers.Assign(go, GameLayers.ShopProp);
            return new Leaf { Data = data, Body = rb, Grab = grab, Art = sr, Home = (Vector2)transform.position + local };
        }

        // ------------------------------------------------------------------ input

        private void Update()
        {
            float dt = Time.deltaTime;
            _strikeCooldown -= dt;
            if (!Attended) { EndLift(); Hovered = null; return; }

            Vector2 p = Pointer.World(_cam);
            Grabbable2D under = _grabber != null ? _grabber.Pick(p) : null;
            Hovered = null;
            foreach (var l in _leaves) if (l.Grab == under) Hovered = l;

            // Hold on the mortar (not on something grabbable) to lift the pestle.
            bool onMortar = Vector2.Distance(p, MortarWorld) < 1.35f;
            if (Pointer.PressedThisFrame && onMortar && under == null && _grabber.Held == null) BeginLift();
            if (_lift != null)
            {
                if (!Pointer.Held) EndLift();
                else
                {
                    // First bring the pestle over the bowl and let it steady; only then
                    // does the held press start raising it. Lifting while it was still
                    // swinging in from the side put that sideways speed into the strike.
                    float hold = MortarWorld.y + HangAbove;
                    if (!LiftReady)
                    {
                        // Up first, then across: pulled diagonally, the pestle dragged
                        // along the mortar's rim and friction pinned it there.
                        float head = _pestle.position.y - PestleHalfLength;
                        bool clear = head > RimTopY + 0.1f;
                        _lift.target = new Vector2(clear ? MortarWorld.x : _pestle.position.x, hold);
                        bool over = Mathf.Abs(_pestle.position.x - MortarWorld.x) < 0.12f;
                        _steadyFor = over && _pestle.linearVelocity.sqrMagnitude < 0.3f * 0.3f ? _steadyFor + dt : 0f;
                        if (_steadyFor > 0.15f) LiftReady = true;
                    }
                    else
                    {
                        _liftHeight = Mathf.Min(MaxLift, _liftHeight + LiftRate * dt);
                        _lift.target = new Vector2(MortarWorld.x, hold + _liftHeight);
                    }
                }
            }
        }

        private float _steadyFor;

        /// <summary>True once a held press has the pestle steady over the bowl and rising.</summary>
        public bool LiftReady { get; private set; }

        private void BeginLift()
        {
            if (_pestle == null || _pestleGrab.IsHeld) return;
            _pestle.rotation = 0f;
            _pestle.angularVelocity = 0f;
            _lift = _pestle.gameObject.AddComponent<TargetJoint2D>();
            _lift.autoConfigureTarget = false;
            _lift.anchor = new Vector2(0f, 0.9f);
            _lift.frequency = 5f;
            _lift.dampingRatio = 1f;
            _lift.maxForce = 250f;
            _liftHeight = 0f;
            _steadyFor = 0f;
            LiftReady = false;
            _lift.target = new Vector2(_pestle.position.x, MortarWorld.y + HangAbove);
        }

        /// <summary>Let go: the joint goes and gravity takes the pestle down into the bowl.</summary>
        private void EndLift()
        {
            if (_lift == null) return;
            Destroy(_lift);
            _lift = null;
            LiftReady = false;
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
            leaf.Body.linearVelocity = Vector2.zero;
            leaf.Body.AddForce(v * leaf.Body.mass, ForceMode2D.Impulse);
            leaf.Body.AddTorque(-0.05f * leaf.Body.mass, ForceMode2D.Impulse);
            AudioManager.Play(Sfx.Tab);
        }

        // --------------------------------------------------------------- physics

        private void FixedUpdate()
        {
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
                SetReaction(order == null ? "Take a job at the Counter first — nothing to prep yet." : "The mortar is full already.",
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
                VfxWorld.Active.Burst(leaf.Body.position, PixelArt.Element(ing.Element), 8, 1.6f, 0.08f, 0.4f);
            AudioManager.Play(onCue || inRecipe ? Sfx.Confirm : Sfx.Deny);
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
            if (StrikesLeft <= 0) return;

            _strikeCooldown = 0.4f;   // the pestle bounces; one strike per blow
            float band = CurrentBand;
            LastStrikeSpeed = hit.Speed;
            StrikesLeft--;
            float off = Mathf.Abs(hit.Speed - IdealStrike);
            bool clean = off <= band;

            if (clean)
            {
                int gain = Mathf.RoundToInt(Mathf.Lerp(QualityBudget.StrikeMax, QualityBudget.StrikeMin, off / band));
                order.ApplyBonus(gain, "Prep", $"Clean strike at {hit.Speed:0.0} m/s (+{gain})");
                SetReaction($"Clean strike — {hit.Speed:0.0} m/s.", UI.UITheme.Ok);
                AudioManager.Play(Sfx.Seal);
                CrushBurst(hit.Point, 12);
                CameraRig.Shake(0.08f);
            }
            else if (hit.Speed < IdealStrike)
            {
                order.ApplyDeduction(QualityBudget.StrikeMiss, "Prep", $"Too soft ({hit.Speed:0.0} m/s) — barely bruised it");
                SetReaction($"Too soft — {hit.Speed:0.0} m/s. Lift it higher before you let go.", UI.UITheme.Danger);
                AudioManager.Play(Sfx.Deny);
                CrushBurst(hit.Point, 4);
            }
            else
            {
                order.ApplyDeduction(QualityBudget.StrikeMiss, "Prep", $"Too hard ({hit.Speed:0.0} m/s) — the mix jumped out");
                SetReaction($"Too hard — {hit.Speed:0.0} m/s. Some of it jumped the bowl.", UI.UITheme.Danger);
                AudioManager.Play(Sfx.Deny);
                PopOut();
                CameraRig.Shake(0.18f);
            }

            foreach (var l in _leaves)
                if (l.InBowl && l.Art != null) l.Art.transform.localScale *= 0.86f;   // crushed a little more

            if (StrikesLeft <= 0) FinishMix(order, mix);
            Changed?.Invoke();
        }

        private void CrushBurst(Vector2 at, int n)
        {
            if (VfxWorld.Active == null || Mix == null) return;
            Color c = PixelArt.Element(Mix.Recipe.Result);
            VfxWorld.Active.Burst(at + Vector2.up * 0.2f, c, n, 2.4f, 0.08f, 0.45f);
        }

        /// <summary>A too-hard strike throws a crushed leaf clean out of the bowl.</summary>
        private void PopOut()
        {
            foreach (var l in _leaves)
            {
                if (!l.InBowl || l.Body == null) continue;
                l.Body.AddForce(new Vector2(-1.2f, 4.6f) * l.Body.mass, ForceMode2D.Impulse);
                break;
            }
        }

        /// <summary>
        /// Settle the mixture: score it, hand the pot the band width it has earned, and
        /// tip the mash into the cauldron (the leaves go into the pot as falling bodies).
        /// </summary>
        private void FinishMix(ActiveOrder order, BrewMixture mix)
        {
            mix.Ground = true;
            MixOutcome outcome = mix.Evaluate();
            int delta = RecipeBook.QualityDelta(outcome);
            if (delta > 0) order.ApplyBonus(delta, "Prep", $"{outcome} mix — {mix.Recipe.Name}");
            else if (delta < 0) order.ApplyDeduction(-delta, "Prep", $"{outcome} mix — {mix.Recipe.Name}");

            var pot = PhysicsCauldronManager.Instance;
            if (pot != null)
            {
                pot.ApplyMix(outcome);
                foreach (ElementType e in mix.Added) pot.DropIngredient(e);
            }

            foreach (var l in _leaves)
            {
                if (!l.InBowl || l.Body == null) continue;
                if (VfxWorld.Active != null) VfxWorld.Active.Puff(l.Body.position, new Color(0.7f, 0.65f, 0.6f, 0.35f), 3, 0.2f, 0.3f);
                Destroy(l.Body.gameObject);
                l.Body = null;
            }

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
