using System.Collections.Generic;
using UnityEngine;
using AlchemistsArsenal.Art;
using AlchemistsArsenal.Combat;
using AlchemistsArsenal.Core;
using AlchemistsArsenal.PhysicsKit;
using AlchemistsArsenal.Vfx;

namespace AlchemistsArsenal.Crafting
{
    /// <summary>
    /// Everything you see of the cauldron, layered back to front:
    /// the far wall and upper rim, the brew's surface, a swirl that turns with the
    /// spoon, the floating herbs (projected from the surface simulation), then the
    /// lower rim and belly, which hide the part of each herb that is below the lip.
    /// Under it: the fire it sits over. Above it: steam and bubbles while it brews.
    ///
    /// What the stir is doing to it is all here to see: stirred too slowly the brew
    /// darkens and smokes as it catches on the bottom, and stirred too fast the
    /// surface heaves and then throws itself over the rim.
    ///
    /// Presentation only: it reads the pot and the liquid, it never changes them.
    /// </summary>
    public class CauldronView : MonoBehaviour
    {
        public const float PotScale = 1.45f;

        private const int OrderBack = 10, OrderLiquid = 11, OrderSwirl = 12, OrderHerbs = 13,
            OrderFire = 19, OrderFront = 20, OrderSpoon = 26, OrderGlow = 4;

        private PhysicsCauldronManager _pot;
        private Camera _cam;
        private SpriteRenderer _liquid, _swirl, _fire, _glow, _spoon;
        private Transform _surface, _swirlT, _spoonT;
        private ElementType _liquidElement = (ElementType)(-1);
        private float _swirlAngle, _fireClock, _steamClock, _bubbleClock, _smokeClock, _sprayClock, _spoonTilt;
        private int _fireFrame;
        private readonly Dictionary<LiquidBody2D.Floater, SpriteRenderer> _herbs = new Dictionary<LiquidBody2D.Floater, SpriteRenderer>();
        private readonly List<LiquidBody2D.Floater> _gone = new List<LiquidBody2D.Floater>();

        public Vector2 MouthCentre { get; private set; }
        public float SurfaceRX { get; private set; }
        public float SurfaceRY { get; private set; }

        /// <summary>Build the pot's art under this object and wire it to <paramref name="pot"/>.</summary>
        public void Build(PhysicsCauldronManager pot, Camera cam)
        {
            _pot = pot;
            _cam = cam;
            Vector2 root = transform.position;
            MouthCentre = root + ShopArt.MouthOffset * PotScale;
            SurfaceRX = ShopArt.LiquidRX / ShopArt.PPU * PotScale;
            SurfaceRY = ShopArt.LiquidRY / ShopArt.PPU * PotScale;

            // The clockwork stirrer rebuilds the pot in copper, with its frame over it.
            bool clockwork = PhysicsCauldronManager.AutoStir;
            Sprite(ShopArt.PotBack(clockwork), OrderBack, "PotBack");
            _liquid = Sprite(ShopArt.Liquid(ElementType.Nature), OrderLiquid, "Liquid");
            Sprite(ShopArt.PotFront(clockwork), OrderFront, "PotFront");
            if (clockwork) BuildClockwork();

            // The swirl lives in "surface space": a unit disc squashed onto the mouth's
            // ellipse, so turning it in its own plane reads as the liquid turning.
            _surface = new GameObject("Surface").transform;
            _surface.SetParent(transform, false);
            _surface.position = MouthCentre;
            _surface.localScale = new Vector3(SurfaceRX / PotScale, SurfaceRY / PotScale, 1f);   // parent is scaled already
            _swirlT = new GameObject("Swirl").transform;
            _swirlT.SetParent(_surface, false);
            _swirl = _swirlT.gameObject.AddComponent<SpriteRenderer>();
            _swirl.sprite = ShopArt.Swirl();
            _swirl.sharedMaterial = SpriteMaterials.For(_swirl.sprite);
            _swirl.sortingOrder = OrderSwirl;
            _swirl.color = new Color(1f, 1f, 1f, 0f);

            // Fire under the belly, behind the legs; a warm glow on the floor.
            var fireGo = new GameObject("Fire");
            fireGo.transform.SetParent(transform, false);
            fireGo.transform.localPosition = new Vector3(0f, -1.52f, 0f);
            _fire = fireGo.AddComponent<SpriteRenderer>();
            _fire.sprite = ShopArt.Fire(0);
            _fire.sharedMaterial = SpriteMaterials.For(_fire.sprite);
            _fire.sortingOrder = OrderFire;

            var glowGo = new GameObject("FireGlow");
            glowGo.transform.SetParent(transform, false);
            glowGo.transform.localPosition = new Vector3(0f, -1.55f, 0f);
            _glow = PixelArt.AddDisc(glowGo, new Color(1f, 0.55f, 0.2f, 0.2f), OrderGlow);

            // The spoon you hold.
            _spoonT = new GameObject("SpoonArt").transform;
            _spoonT.SetParent(transform.parent, false);
            _spoon = PixelArt.AddSprite(_spoonT.gameObject, PixelSprites.Spoon(), OrderSpoon, 0.95f);
            _spoonT.position = RestSpoon();

            pot.Landed += OnLanded;
            pot.OnSplash += OnSplash;
            pot.Spilled += OnSpilled;
            FallingIngredient.OnSpawned += OnFalling;
        }

        private void OnDestroy()
        {
            if (_pot != null) { _pot.Landed -= OnLanded; _pot.OnSplash -= OnSplash; _pot.Spilled -= OnSpilled; }
            FallingIngredient.OnSpawned -= OnFalling;
        }

        private SpriteRenderer Sprite(Sprite s, int order, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = s;
            sr.sharedMaterial = SpriteMaterials.For(s);
            sr.sortingOrder = order;
            return sr;
        }

        private Transform _paddle, _gear;
        private float _paddleAngle;
        private Vector2 _crown;

        /// <summary>A brass frame over the pot, a gear at its crown, and a paddle hanging from it into the brew.</summary>
        private void BuildClockwork()
        {
            _crown = MouthCentre + new Vector2(0f, 1.9f);
            var frame = new GameObject("ClockworkFrame");
            frame.transform.SetParent(transform.parent, false);
            frame.transform.position = _crown;
            PixelArt.AddSprite(frame, ShopArt.Gantry(), OrderFront + 1).transform.localScale = Vector3.one * PotScale;

            var gear = new GameObject("Gear");
            gear.transform.SetParent(transform.parent, false);
            gear.transform.position = _crown + new Vector2(0f, 0.55f);
            PixelArt.AddSprite(gear, ShopArt.Gear(), OrderFront + 2).transform.localScale = Vector3.one * PotScale;
            _gear = gear.transform;

            var paddle = new GameObject("Paddle");
            paddle.transform.SetParent(transform.parent, false);
            paddle.transform.position = _crown;
            // Between the brew and the pot's front rim: the blade dips in behind the lip.
            PixelArt.AddSprite(paddle, ShopArt.Paddle(), OrderHerbs + 5).transform.localScale = Vector3.one * PotScale * 0.95f;
            _paddle = paddle.transform;
        }

        /// <summary>The paddle sweeps round with the brew and the gear turns with it.</summary>
        private void MoveClockwork(float dt)
        {
            if (_paddle == null) return;
            _paddleAngle += _pot.SpinDegPerSec * 0.55f * dt * Mathf.Deg2Rad;
            Vector2 blade = MouthCentre + new Vector2(Mathf.Cos(_paddleAngle) * SurfaceRX * 0.55f,
                                                      Mathf.Sin(_paddleAngle) * SurfaceRY * 0.55f + _bob);
            Vector2 d = blade - _crown;
            _paddle.rotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(d.x, -d.y) * Mathf.Rad2Deg);
            _gear.rotation = Quaternion.Euler(0f, 0f, -_paddleAngle * Mathf.Rad2Deg * 2f);
        }

        private Vector3 RestSpoon() => (Vector3)(MouthCentre + new Vector2(SurfaceRX * 0.95f, 0.7f));

        // ----------------------------------------------------------------- events

        private void OnFalling(FallingIngredient f)
        {
            if (f == null || f.transform.parent != transform.parent) return;
            var art = new GameObject("Art");
            art.transform.SetParent(f.transform, false);
            PixelArt.AddSprite(art, PixelSprites.Herb(f.Element), OrderSpoon - 1, 0.5f);
        }

        private void OnLanded(ElementType e, Vector2 at)
        {
            var vfx = VfxWorld.Active;
            if (vfx == null) return;
            Color c = PixelArt.Element(BrewElement());
            vfx.Burst(at + Vector2.up * 0.05f, Color.Lerp(c, Color.white, 0.4f), 10, 2.2f, 0.09f, 0.45f);
            vfx.Flash(at, new Color(c.r, c.g, c.b, 0.6f), 0.7f, 0.18f);
        }

        /// <summary>The pot slopped over: brew goes over both sides of the rim, runs down the belly, and the shop shakes.</summary>
        private void OnSpilled()
        {
            var vfx = VfxWorld.Active;
            Color c = PixelArt.Element(BrewElement());
            if (vfx != null)
                for (int s = -1; s <= 1; s += 2)
                {
                    Vector2 lip = MouthCentre + new Vector2(SurfaceRX * 0.92f * s, 0.05f);
                    vfx.Burst(lip, Color.Lerp(c, Color.white, 0.45f), 18, 4.2f, 0.17f, 0.8f);
                    // and a wave of it running down the outside of the pot
                    for (int i = 0; i < 4; i++)
                        vfx.Mote(lip + new Vector2(0.1f * s, -0.1f * i), new Vector2(0.5f * s, -1.4f - 0.4f * i),
                            Color.Lerp(c, Color.white, 0.2f), 0.16f, 0.9f, glow: false);
                    vfx.Flash(lip, new Color(c.r, c.g, c.b, 0.55f), 0.7f, 0.22f);
                }
            CameraRig.Shake(0.12f);
        }

        /// <summary>A herb over the lip: it flies out of the pot as a real body and lands on the floor.</summary>
        private void OnSplash(LiquidBody2D.Floater f)
        {
            if (!_herbs.TryGetValue(f, out SpriteRenderer sr) || sr == null) return;
            Vector2 from = sr.transform.position;
            Vector2 dir = (from - MouthCentre).x >= 0f ? new Vector2(1f, 1.4f) : new Vector2(-1f, 1.4f);

            var go = new GameObject("Slopped");
            go.transform.SetParent(transform.parent, false);
            go.transform.position = from;
            var rb = go.AddComponent<Rigidbody2D>();
            rb.gravityScale = 1.4f;
            rb.linearVelocity = dir * 2.6f;
            rb.angularVelocity = dir.x * 600f;
            var art = new GameObject("Art");
            art.transform.SetParent(go.transform, false);
            PixelArt.AddSprite(art, sr.sprite, OrderSpoon - 1, 0.5f);
            Destroy(go, 1.6f);

            var vfx = VfxWorld.Active;
            if (vfx != null)
                vfx.Burst(from, Color.Lerp(PixelArt.Element(BrewElement()), Color.white, 0.3f), 16, 3.2f, 0.1f, 0.6f);
            CameraRig.Shake(0.12f);

            Destroy(sr.gameObject);
            _herbs.Remove(f);
        }

        // ------------------------------------------------------------------ frame

        private ElementType BrewElement() => _pot != null ? _pot.BrewElement : ElementType.Nature;

        private void LateUpdate()
        {
            if (_pot == null) return;
            float dt = Time.deltaTime;
            float power = _pot.StirPower01;
            float scorch = _pot.Scorch01, slosh = _pot.Slosh01;
            bool brewing = _pot.InBand && _pot.StirringCorrectly;

            ElementType e = BrewElement();
            if (e != _liquidElement)
            {
                _liquidElement = e;
                _liquid.sprite = ShopArt.Liquid(e);
                _liquid.sharedMaterial = SpriteMaterials.For(_liquid.sprite);
            }
            // Catching on the bottom drags the whole brew dark and brown.
            _liquid.color = Color.Lerp(Color.white, new Color(0.42f, 0.33f, 0.28f), scorch);

            // A surface stirred past the band heaves up and down; over the top of that
            // it throws itself over the rim (see OnSpilled).
            _bob = Mathf.Sin(Time.time * 13f) * 0.05f * slosh;
            _liquid.transform.localPosition = new Vector3(0f, _bob / PotScale, 0f);
            _surface.position = MouthCentre + new Vector2(0f, _bob);

            // The swirl turns with the liquid (which lags the spoon).
            _swirlAngle += _pot.SpinDegPerSec * 0.55f * dt;
            _swirlT.localRotation = Quaternion.Euler(0f, 0f, _swirlAngle);
            Color sc = PixelArt.Element(e);
            _swirl.color = new Color(Mathf.Lerp(sc.r, 1f, 0.5f), Mathf.Lerp(sc.g, 1f, 0.5f), Mathf.Lerp(sc.b, 1f, 0.5f),
                0.06f + 0.34f * power);

            // The fire is just the fire now: the pot sits over it all morning.
            _fireClock += dt * 9f;
            int frame = (int)_fireClock % 3;
            if (frame != _fireFrame)
            {
                _fireFrame = frame;
                _fire.sprite = ShopArt.Fire(frame);
            }
            float flicker = 0.88f + 0.08f * Mathf.Sin(Time.time * 3.3f);
            _fire.transform.localScale = new Vector3(0.95f, flicker, 1f);
            _glow.transform.localScale = new Vector3(3.2f, 0.72f, 1f);
            _glow.color = new Color(1f, 0.55f, 0.2f, 0.22f + 0.03f * Mathf.Sin(Time.time * 2.1f));

            SyncHerbs();
            Emit(dt, e, brewing || _pot.ClockworkTurning, scorch, slosh);
            MoveSpoon(dt);
            MoveClockwork(dt);
        }

        private float _bob;

        private void SyncHerbs()
        {
            LiquidBody2D liquid = _pot.Liquid;
            if (liquid == null) return;
            float R = liquid.Radius;

            foreach (var f in liquid.Floaters)
            {
                if (f.Splashed) continue;
                if (!_herbs.TryGetValue(f, out SpriteRenderer sr))
                {
                    if (f.Body == null) continue;
                    var go = new GameObject("Herb");
                    go.transform.SetParent(transform.parent, false);
                    sr = PixelArt.AddSprite(go, PixelSprites.Herb(f.Tag is ElementType te ? te : ElementType.Nature),
                        OrderHerbs, 0.5f);
                    _herbs[f] = sr;
                }

                if (f.Body == null)
                {
                    // Dissolved: a little bloom of its colour, then it is part of the brew.
                    if (f.Dissolved && VfxWorld.Active != null)
                        VfxWorld.Active.Flash(sr.transform.position,
                            (Color)PixelArt.Element(f.Tag is ElementType de ? de : ElementType.Nature) * new Color(1, 1, 1, 0.55f), 0.5f, 0.3f);
                    _gone.Add(f);
                    continue;
                }

                Vector2 s = f.Local;
                sr.transform.position = _pot.ToWorld(s) + Vector2.up * _bob;   // ride the heaving surface
                sr.transform.rotation = Quaternion.Euler(0f, 0f, f.Spin);
                float k = 1f - 0.65f * f.Dissolve01;
                sr.transform.localScale = Vector3.one * (0.5f / sr.sprite.bounds.size.x) * k;
                sr.color = new Color(1f, 1f, 1f, Mathf.Lerp(1f, 0.35f, f.Dissolve01));
                // Further back on the surface draws behind nearer herbs.
                sr.sortingOrder = OrderHerbs + Mathf.Clamp(Mathf.RoundToInt((1f - s.y / R) * 2f), 0, 4);
            }

            foreach (var f in _gone)
            {
                if (_herbs.TryGetValue(f, out SpriteRenderer sr) && sr != null) Destroy(sr.gameObject);
                _herbs.Remove(f);
            }
            _gone.Clear();
        }

        private void Emit(float dt, ElementType e, bool brewing, float scorch, float slosh)
        {
            var vfx = VfxWorld.Active;
            if (vfx == null) return;

            // Steam off the surface: more of it while the brew is actually working.
            _steamClock += dt * (1.6f + (brewing ? 4.5f : 0f));
            while (_steamClock >= 1f)
            {
                _steamClock -= 1f;
                Vector2 p = MouthCentre + new Vector2((vfx.Random01() * 2f - 1f) * SurfaceRX * 0.8f, 0.1f + _bob);
                vfx.Mote(p, new Vector2((vfx.Random01() - 0.5f) * 0.3f, 0.6f + (brewing ? 0.5f : 0f)),
                    new Color(0.85f, 0.82f, 0.9f, brewing ? 0.2f : 0.12f), 0.12f, 1.6f, glow: false);
            }

            // Bubbles popping on the surface while it brews.
            _bubbleClock += dt * (brewing ? 9f : 1.5f);
            Color c = PixelArt.Element(e);
            while (_bubbleClock >= 1f)
            {
                _bubbleClock -= 1f;
                float a = vfx.Random01() * Mathf.PI * 2f, r = Mathf.Sqrt(vfx.Random01()) * 0.85f;
                Vector2 p = MouthCentre + new Vector2(Mathf.Cos(a) * r * SurfaceRX, Mathf.Sin(a) * r * SurfaceRY + _bob);
                vfx.Flash(p, new Color(Mathf.Lerp(c.r, 1f, 0.6f), Mathf.Lerp(c.g, 1f, 0.6f), Mathf.Lerp(c.b, 1f, 0.6f), 0.7f),
                    0.1f + vfx.Random01() * 0.08f, 0.22f);
            }

            // Catching on the bottom: dirty smoke off the surface, thicker as it burns.
            // It is a pale ash grey on purpose — real soot-black is invisible against
            // the shop's dark wall, which is where this has to read from.
            _smokeClock += dt * scorch * 11f;
            while (_smokeClock >= 1f)
            {
                _smokeClock -= 1f;
                Vector2 p = MouthCentre + new Vector2((vfx.Random01() * 2f - 1f) * SurfaceRX * 0.7f, 0.05f + _bob);
                vfx.Puff(p, new Color(0.42f, 0.37f, 0.34f, 0.35f + 0.35f * scorch), 1, 0.3f, 0.7f + 0.6f * scorch);
            }

            // A surface close to going over throws spray off the rim.
            _sprayClock += dt * Mathf.Max(0f, slosh - 0.45f) * 14f;
            while (_sprayClock >= 1f)
            {
                _sprayClock -= 1f;
                float side = vfx.Random01() < 0.5f ? -1f : 1f;
                Vector2 p = MouthCentre + new Vector2(side * SurfaceRX * 0.9f, _bob);
                vfx.Mote(p, new Vector2(side * 1.1f, 1.3f), Color.Lerp(c, Color.white, 0.35f), 0.09f, 0.5f, glow: false);
            }
        }

        private void MoveSpoon(float dt)
        {
            bool over = _pot.MouseOverCauldron;
            Vector3 target = over
                ? (Vector3)_pot.ToWorld(Vector2.ClampMagnitude(_pot.ToSurface(Pointer.World(_cam)), (_pot.Liquid != null ? _pot.Liquid.Radius : 1f) * 0.95f)) + new Vector3(0f, 0.35f, 0f)
                : RestSpoon();
            _spoonT.position = Vector3.Lerp(_spoonT.position, target, 1f - Mathf.Exp(-18f * dt));
            float wanted = over ? -_pot.Spin01 * 28f : -18f;
            _spoonTilt = Mathf.Lerp(_spoonTilt, wanted, 1f - Mathf.Exp(-10f * dt));
            _spoonT.rotation = Quaternion.Euler(0f, 0f, _spoonTilt);
            _spoon.color = new Color(1f, 1f, 1f, over ? 1f : 0.85f);
        }
    }
}
