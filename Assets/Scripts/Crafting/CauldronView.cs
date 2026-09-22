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
    /// Under it: fire and a warm glow that grow with the heat. Above it: steam and
    /// bubbles that come faster as it gets hotter.
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
        private float _swirlAngle, _fireClock, _steamClock, _bubbleClock, _spoonTilt;
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

            Sprite(ShopArt.PotBack(), OrderBack, "PotBack");
            _liquid = Sprite(ShopArt.Liquid(ElementType.Nature), OrderLiquid, "Liquid");
            Sprite(ShopArt.PotFront(), OrderFront, "PotFront");

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
            FallingIngredient.OnSpawned += OnFalling;
        }

        private void OnDestroy()
        {
            if (_pot != null) { _pot.Landed -= OnLanded; _pot.OnSplash -= OnSplash; }
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

        private ElementType BrewElement()
        {
            var order = Systems.CraftingManager.Instance != null ? Systems.CraftingManager.Instance.CurrentOrder : null;
            return order != null ? order.element : ElementType.Nature;
        }

        private void LateUpdate()
        {
            if (_pot == null) return;
            float dt = Time.deltaTime;
            float heat = _pot.Heat01;
            float power = _pot.StirPower01;

            ElementType e = BrewElement();
            if (e != _liquidElement)
            {
                _liquidElement = e;
                _liquid.sprite = ShopArt.Liquid(e);
                _liquid.sharedMaterial = SpriteMaterials.For(_liquid.sprite);
            }
            float warm = Mathf.Lerp(0.78f, 1f, heat);
            _liquid.color = new Color(warm, warm, warm, 1f);

            // The swirl turns with the liquid (which lags the spoon).
            _swirlAngle += _pot.SpinDegPerSec * 0.55f * dt;
            _swirlT.localRotation = Quaternion.Euler(0f, 0f, _swirlAngle);
            Color sc = PixelArt.Element(e);
            _swirl.color = new Color(Mathf.Lerp(sc.r, 1f, 0.5f), Mathf.Lerp(sc.g, 1f, 0.5f), Mathf.Lerp(sc.b, 1f, 0.5f),
                0.06f + 0.34f * power);

            // Fire: three frames, bigger and faster the hotter it runs.
            _fireClock += dt * (6f + heat * 8f);
            int frame = (int)_fireClock % 3;
            if (frame != _fireFrame)
            {
                _fireFrame = frame;
                _fire.sprite = ShopArt.Fire(frame);
            }
            float fs = 0.55f + heat * 0.75f;
            _fire.transform.localScale = new Vector3(0.95f, fs, 1f);
            _glow.transform.localScale = new Vector3(2.4f + heat * 1.6f, 0.55f + heat * 0.35f, 1f);
            _glow.color = new Color(1f, 0.55f, 0.2f, 0.12f + heat * 0.25f);

            SyncHerbs();
            Emit(dt, heat, e);
            MoveSpoon(dt);
        }

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
                sr.transform.position = _pot.ToWorld(s);
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

        private void Emit(float dt, float heat, ElementType e)
        {
            var vfx = VfxWorld.Active;
            if (vfx == null) return;

            // Steam off the surface.
            _steamClock += dt * (0.8f + heat * 7f);
            while (_steamClock >= 1f)
            {
                _steamClock -= 1f;
                Vector2 p = MouthCentre + new Vector2((vfx.Random01() * 2f - 1f) * SurfaceRX * 0.8f, 0.1f);
                vfx.Mote(p, new Vector2((vfx.Random01() - 0.5f) * 0.3f, 0.5f + heat * 0.7f),
                    new Color(0.85f, 0.82f, 0.9f, 0.10f + heat * 0.14f), 0.1f + heat * 0.1f, 1.6f, glow: false);
            }

            // Bubbles popping on the surface, faster as it heats.
            _bubbleClock += dt * heat * heat * 14f;
            Color c = PixelArt.Element(e);
            while (_bubbleClock >= 1f)
            {
                _bubbleClock -= 1f;
                float a = vfx.Random01() * Mathf.PI * 2f, r = Mathf.Sqrt(vfx.Random01()) * 0.85f;
                Vector2 p = MouthCentre + new Vector2(Mathf.Cos(a) * r * SurfaceRX, Mathf.Sin(a) * r * SurfaceRY);
                vfx.Flash(p, new Color(Mathf.Lerp(c.r, 1f, 0.6f), Mathf.Lerp(c.g, 1f, 0.6f), Mathf.Lerp(c.b, 1f, 0.6f), 0.7f),
                    0.1f + vfx.Random01() * 0.08f, 0.22f);
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
