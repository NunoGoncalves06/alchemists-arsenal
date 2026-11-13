using System;
using System.Collections.Generic;
using UnityEngine;
using AlchemistsArsenal.Art;
using AlchemistsArsenal.Audio;
using AlchemistsArsenal.Core;
using AlchemistsArsenal.PhysicsKit;
using AlchemistsArsenal.Systems;
using AlchemistsArsenal.Vfx;

namespace AlchemistsArsenal.Crafting
{
    /// <summary>
    /// The Delivery bench, last on the rail: the fighters who ordered today stand at
    /// it, and every sealed flask from Bottling waits on its tray. Drag a flask to the
    /// fighter it was brewed for and it is handed over: it rises into their hand and
    /// goes onto their belt, and that is the flask they take into the arena. Let it go
    /// anywhere else (another fighter, the bench) and it goes back to the tray.
    /// Nothing is scored here; it only decides who carries what.
    /// </summary>
    public class DeliveryBench : MonoBehaviour
    {
        public static DeliveryBench Instance { get; private set; }

        public const float BenchTopY = -1.9f;
        private static readonly Vector2 TrayLocal = new Vector2(-3.6f, BenchTopY);
        private const float FighterX0 = -0.6f, FighterGap = 1.75f, FighterWidth = 1.7f;
        private const float HandOverSeconds = 0.6f;

        private sealed class Fighter
        {
            public string HeroId;
            public string Name;
            public Transform Root;
            public Transform Belt;
            public float BobUntil;
        }

        private sealed class Flask
        {
            public ActiveOrder Order;
            public Rigidbody2D Body;
            public Grabbable2D Grab;
            public Vector2 Home;
            // the hand-over in flight: from where, to whom, since when
            public Fighter To;
            public Vector2 From;
            public float Started = -1f;
        }

        private readonly List<Fighter> _fighters = new List<Fighter>();
        private readonly List<Flask> _flasks = new List<Flask>();
        private Camera _cam;
        private PointerGrabber _grabber;

        public bool Attended { get; set; }

        /// <summary>The last thing that happened, for the station's caption (a refused drop says whose flask it was).</summary>
        public string Reaction { get; private set; } = "";

        /// <summary>A short line about what the bench wants next, for the station's caption.</summary>
        public string Hint
        {
            get
            {
                if (!string.IsNullOrEmpty(Reaction)) return Reaction;
                Flask next = FirstWaiting();
                return next != null ? $"Drag the {next.Order.potionName} to {next.Order.heroName}." : "";
            }
        }

        /// <summary>The order whose flask is on the tray, oldest first (the station follows it).</summary>
        public ActiveOrder Waiting => FirstWaiting()?.Order;

        public event Action Changed;

        private void Awake() => Instance = this;
        private void OnDestroy() { if (Instance == this) Instance = null; }

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

            _grabber = gameObject.AddComponent<PointerGrabber>();
            _grabber.Configure(cam, GameLayers.ShopPropMask, 0.4f);
            _grabber.Enabled = () => Attended;
        }

        public void NewDay()
        {
            foreach (var f in _flasks) if (f.Body != null) Destroy(f.Body.gameObject);
            foreach (var f in _fighters) if (f.Root != null) Destroy(f.Root.gameObject);
            _flasks.Clear();
            _fighters.Clear();
            Reaction = "";
            Changed?.Invoke();
        }

        // ------------------------------------------------------------ public API

        /// <summary>Where the flask for <paramref name="order"/> is, or the tray if it is not out yet.</summary>
        public Vector2 FlaskWorld(ActiveOrder order)
        {
            Flask f = FlaskOf(order);
            return f != null && f.Body != null ? f.Body.position : (Vector2)transform.position + TrayLocal;
        }

        /// <summary>Where <paramref name="heroId"/> stands: their hand, where a flask is given.</summary>
        public Vector2 FighterWorld(string heroId)
        {
            Fighter f = FighterOf(heroId);
            return f != null ? (Vector2)f.Root.position + new Vector2(0.45f, 1.0f) : (Vector2)transform.position;
        }

        /// <summary>A hand-over is playing (the flask on its way into a hand).</summary>
        public bool HandingOver
        {
            get { foreach (var f in _flasks) if (f.Started >= 0f) return true; return false; }
        }

        /// <summary>Hand <paramref name="order"/>'s flask to the fighter who ordered it, as a drop on them does.</summary>
        public bool HandOver(ActiveOrder order)
        {
            Flask f = FlaskOf(order);
            Fighter to = order != null ? FighterOf(order.heroId) : null;
            if (f == null || to == null || f.Started >= 0f || !order.CanHandTo(to.HeroId)) return false;
            f.Grab.CanGrab = false;
            if (f.Grab.IsHeld) f.Grab.Release();
            f.Body.bodyType = RigidbodyType2D.Kinematic;
            f.Body.linearVelocity = Vector2.zero;
            f.Body.angularVelocity = 0f;
            f.To = to;
            f.From = f.Body.position;
            f.Started = Time.unscaledTime;
            Reaction = "";
            AudioManager.Play(Sfx.Confirm);
            return true;
        }

        // --------------------------------------------------------------- update

        private void Update()
        {
            if (AlchemistsArsenal.Core.TimeControl.Paused) return;   // the pause menu: nothing here moves or reads the pointer
            SyncFighters();
            SyncFlasks();
            float now = Time.unscaledTime;
            bool cut = SettingsService.ReduceMotion;   // the playtest photographs it mid-air, so it plays in batchmode too

            for (int i = _flasks.Count - 1; i >= 0; i--)
            {
                Flask f = _flasks[i];
                if (f.Body == null) continue;
                if (f.Started < 0f)
                {
                    // Off the bench (dropped past its end, or over the edge): back on the tray.
                    Vector2 local = (Vector2)transform.InverseTransformPoint(f.Body.position);
                    if (!f.Grab.IsHeld && (local.y < BenchTopY - 0.6f || Mathf.Abs(local.x) > 6.2f))
                    {
                        f.Body.linearVelocity = Vector2.zero;
                        f.Body.position = f.Home;
                    }
                    continue;
                }
                float k = cut ? 1f : Mathf.Clamp01((now - f.Started) / HandOverSeconds);
                // a short arc up and into the hand
                Vector2 to = FighterWorld(f.To.HeroId);
                Vector2 p = Vector2.Lerp(f.From, to, Mathf.SmoothStep(0f, 1f, k)) + Vector2.up * (Mathf.Sin(k * Mathf.PI) * 0.6f);
                f.Body.MovePosition(p);
                if (k < 1f) continue;
                Finish(f);
                _flasks.RemoveAt(i);
            }

            foreach (var fighter in _fighters)
            {
                if (fighter.Root == null) continue;
                float bob = now < fighter.BobUntil ? Mathf.Abs(Mathf.Sin((fighter.BobUntil - now) * 14f)) * 0.12f : 0f;
                fighter.Root.GetChild(0).localPosition = new Vector3(0f, bob, 0f);
            }
        }

        /// <summary>The flask is in the hand: it goes onto the belt and the order is done.</summary>
        private void Finish(Flask f)
        {
            ActiveOrder o = f.Order;
            o.deliveredTo = f.To.HeroId;
            // The same flask now hangs at their belt: it is what they carry into the arena.
            f.Body.simulated = false;
            f.Body.transform.SetParent(f.To.Belt, true);
            f.Body.transform.localPosition = Vector3.zero;
            f.Body.transform.localScale = Vector3.one * 0.6f;
            f.To.BobUntil = Time.unscaledTime + 0.35f;
            if (VfxWorld.Active != null) VfxWorld.Active.Burst(f.To.Belt.position, new Color(1f, 0.9f, 0.55f), 10, 1.4f, 0.06f, 0.4f);
            AudioManager.Play(Sfx.Chime);
            if (CraftingManager.Instance != null) CraftingManager.Instance.Advance(o);   // Delivery -> Done
            Changed?.Invoke();
        }

        /// <summary>One fighter at the bench for every order taken today, in the order they were served.</summary>
        private void SyncFighters()
        {
            var cm = CraftingManager.Instance;
            if (cm == null) return;
            foreach (ActiveOrder o in cm.Orders)
            {
                if (string.IsNullOrEmpty(o.heroId) || FighterOf(o.heroId) != null) continue;
                RunState s = SaveSystem.Instance != null ? SaveSystem.Instance.State : null;
                string portrait = s?.FindHero(o.heroId)?.portraitId ?? "rookie";

                var root = new GameObject("Fighter_" + o.heroName);
                root.transform.SetParent(transform, false);
                root.transform.localPosition = new Vector2(FighterX0 + _fighters.Count * FighterGap, BenchTopY + 0.02f);
                var art = new GameObject("Art");
                art.transform.SetParent(root.transform, false);
                SpriteRenderer sr = PixelArt.AddSprite(art, PixelSprites.Fighter(portrait), 8, FighterWidth);
                sr.transform.localPosition = Vector3.zero;
                var belt = new GameObject("Belt");
                belt.transform.SetParent(art.transform, false);
                belt.transform.localPosition = new Vector3(0.35f, 0.75f, 0f);
                _fighters.Add(new Fighter { HeroId = o.heroId, Name = o.heroName, Root = root.transform, Belt = belt.transform });
            }
        }

        /// <summary>Every sealed flask from Bottling appears on the tray, one beside the other.</summary>
        private void SyncFlasks()
        {
            var cm = CraftingManager.Instance;
            if (cm == null) return;
            foreach (ActiveOrder o in cm.Orders)
            {
                if (o.stage != BrewStage.Delivery || FlaskOf(o) != null) continue;
                // An order nobody ordered (the demo, the sims) has no fighter to take it: done as it is.
                if (string.IsNullOrEmpty(o.heroId)) { cm.Advance(o); continue; }
                Vector2 home = (Vector2)transform.position + TrayLocal + new Vector2((_flasks.Count % 4) * 0.7f - 1.05f, 0.45f);
                var go = new GameObject("Flask_" + o.heroName);
                go.transform.SetParent(transform, false);
                go.transform.position = home;
                var rb = go.AddComponent<Rigidbody2D>();
                rb.mass = 0.6f;
                rb.freezeRotation = true;
                rb.linearDamping = 0.4f;
                var col = go.AddComponent<BoxCollider2D>();
                col.size = new Vector2(0.45f, 0.7f);
                col.sharedMaterial = PhysicsMaterials.Glass;
                // The flasks on the tray pass through each other: carrying one across the
                // tray must not shove the others off the bench.
                col.excludeLayers = GameLayers.ShopPropMask;
                var art = new GameObject("Art");
                art.transform.SetParent(go.transform, false);
                PixelArt.AddSprite(art, PixelSprites.Flask(o.element), 12, 0.7f);
                var grab = go.AddComponent<Grabbable2D>();
                GameLayers.Assign(go, GameLayers.ShopProp);
                var flask = new Flask { Order = o, Body = rb, Grab = grab, Home = home };
                grab.OnReleased += _ => OnFlaskReleased(flask);
                _flasks.Add(flask);
                Changed?.Invoke();
            }
        }

        /// <summary>Let go over the fighter it was brewed for: handed over. Anywhere else: back to the tray.</summary>
        private void OnFlaskReleased(Flask f)
        {
            if (f.Body == null || f.Started >= 0f) return;
            Fighter over = FighterUnder(f.Body.position);
            if (over != null && f.Order.CanHandTo(over.HeroId)) { HandOver(f.Order); return; }
            Reaction = over != null ? $"That's {f.Order.heroName}'s flask, not {over.Name}'s." : "";
            f.Body.linearVelocity = Vector2.zero;
            f.Body.position = f.Home;
            AudioManager.Play(over != null ? Sfx.Deny : Sfx.Tab);
            Changed?.Invoke();
        }

        private Fighter FighterUnder(Vector2 p)
        {
            foreach (var f in _fighters)
                if (f.Root != null && Mathf.Abs(p.x - f.Root.position.x) < FighterWidth * 0.5f
                    && p.y > f.Root.position.y - 0.2f && p.y < f.Root.position.y + 2.2f) return f;
            return null;
        }

        private Flask FirstWaiting()
        {
            foreach (var f in _flasks) if (f.Started < 0f) return f;
            return null;
        }

        private Flask FlaskOf(ActiveOrder o)
        {
            foreach (var f in _flasks) if (f.Order == o) return f;
            return null;
        }

        private Fighter FighterOf(string heroId)
        {
            foreach (var f in _fighters) if (f.HeroId == heroId) return f;
            return null;
        }
    }
}
