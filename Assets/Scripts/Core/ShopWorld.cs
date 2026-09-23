using UnityEngine;
using AlchemistsArsenal.Art;
using AlchemistsArsenal.Crafting;
using AlchemistsArsenal.PhysicsKit;
using AlchemistsArsenal.Vfx;

namespace AlchemistsArsenal.Core
{
    /// <summary>
    /// The morning shop: four physical benches side by side along one wall — the
    /// Counter, the Prep bench, the Cauldron and the Bottling bench — and a camera
    /// that glides to whichever one the player is working at.
    ///
    /// The camera draws into the station column only (a viewport rect above the
    /// HUD strip), so the station HUDs sit beside the world instead of on top of it.
    /// That is what finally stops the gauge deck covering the pot's legs and belly.
    ///
    /// Everything lives under this loop-owned root; destroying it tears the shop down
    /// (DESIGN.md §11).
    /// </summary>
    public class ShopWorld : MonoBehaviour
    {
        public static ShopWorld Instance { get; private set; }

        /// <summary>Bench centres, in rail order: Counter, Malting, Prep, Cauldron, Bottling.</summary>
        public static readonly Vector2[] BenchCentres =
        {
            new Vector2(-48f, 0f), new Vector2(-32f, 0f), new Vector2(-16f, 0f), new Vector2(0f, 0f), new Vector2(16f, 0f),
        };

        /// <summary>The station column above the HUD strip, as a fraction of the screen.</summary>
        public static readonly Rect WorldViewport = new Rect(0.085f, 0.235f, 0.615f, 0.70f);

        public Camera WorldCamera { get; private set; }
        public CameraRig Rig { get; private set; }
        public PhysicsCauldronManager Cauldron { get; private set; }
        public PrepBench Prep { get; private set; }
        public MaltingBench Malting { get; private set; }
        public BottlingBench Bottling { get; private set; }

        private void Awake() => Instance = this;

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        public void Build()
        {
            transform.position = Vector3.zero;

            var camGo = new GameObject("ShopCamera") { tag = "MainCamera" };
            camGo.transform.SetParent(transform, false);
            WorldCamera = camGo.AddComponent<Camera>();
            WorldCamera.orthographic = true;
            WorldCamera.orthographicSize = 3.4f;
            WorldCamera.clearFlags = CameraClearFlags.SolidColor;
            WorldCamera.backgroundColor = new Color(0.07f, 0.05f, 0.09f);
            WorldCamera.depth = -1;
            WorldCamera.rect = WorldViewport;
            Rig = camGo.AddComponent<CameraRig>();
            Rig.Configure(new Vector3(BenchCentres[0].x, BenchCentres[0].y, -10f));

            int day = SaveSystem.Instance != null && SaveSystem.Instance.State != null ? SaveSystem.Instance.State.day : 1;
            VfxWorld.Create(transform, seed: 1000 + day);

            BuildRoom();
            BuildCauldron(BenchCentres[3]);

            Malting = NewBench<MaltingBench>("MaltingBench", BenchCentres[1]);
            Malting.Build(WorldCamera);

            Prep = NewBench<PrepBench>("PrepBench", BenchCentres[2]);
            Prep.Build(WorldCamera);
            Prep.NewDay(day);

            Bottling = NewBench<BottlingBench>("BottlingBench", BenchCentres[4]);
            Bottling.Build(WorldCamera);
        }

        /// <summary>Glide the camera to bench <paramref name="index"/> (rail order). Cuts under the headless playtest.</summary>
        public void Focus(int index, bool instant = false)
        {
            if (Rig == null || index < 0 || index >= BenchCentres.Length) return;
            Vector2 c = BenchCentres[index];
            Rig.PanTo(new Vector3(c.x, c.y, -10f), instant || Application.isBatchMode ? 0f : 0.45f);
        }

        private T NewBench<T>(string name, Vector2 centre) where T : Component
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.transform.position = centre;
            return go.AddComponent<T>();
        }

        /// <summary>A stone wall behind everything and a boarded floor under the pot.</summary>
        private void BuildRoom()
        {
            Sprite wall = ShopArt.Wall();
            float tileW = wall.bounds.size.x * 1.5f, tileH = wall.bounds.size.y * 1.5f;
            for (float x = BenchCentres[0].x - 8f; x <= BenchCentres[BenchCentres.Length - 1].x + 8f; x += tileW)
                for (int row = 0; row < 2; row++)
                {
                    var t = new GameObject("Wall");
                    t.transform.SetParent(transform, false);
                    t.transform.position = new Vector3(x, 1.2f - row * tileH, 2f);
                    t.transform.localScale = Vector3.one * 1.5f;
                    var sr = t.AddComponent<SpriteRenderer>();
                    sr.sprite = wall;
                    sr.sharedMaterial = SpriteMaterials.For(wall);
                    sr.sortingOrder = -20;
                }

            // The shop behind each bench: shelves of jars, a candle at the end of each
            // shelf, and herbs hung up to dry, swinging on real joints in the draught.
            for (int i = 1; i < BenchCentres.Length; i++)
            {
                Vector2 c = BenchCentres[i];
                foreach (float side in new[] { -1f, 1f })
                {
                    int v = i + (side > 0f ? 1 : 0);
                    // Beside the bench's work, below the banner line at the top of the view.
                    Vector2 shelfAt = new Vector2(c.x + side * 4.9f, 0.55f);
                    Prop("Shelf", ShopProps.Shelf(v), shelfAt, -15);
                    Vector2 candleAt = shelfAt + new Vector2(1.8f, 1f / ShopProps.PPU);
                    Prop("Candle", ShopProps.Candle(), candleAt, -14);
                    var glow = Prop("CandleGlow", ShopProps.CandleGlow(), candleAt + new Vector2(0f, 0.7f), -13, additive: true);
                    glow.transform.localScale = Vector3.one * 1.1f;
                    glow.color = new Color(1f, 1f, 1f, 0.4f);
                    Vfx.CandleFlicker.Attach(glow, i * 7 + v);
                    HangHerbs(new Vector2(c.x + side * 3.6f, 3.45f), v, i * 13 + v);
                }
            }

            // Floorboards under the cauldron, where there is no bench.
            var floor = new GameObject("Floor");
            floor.transform.SetParent(transform, false);
            floor.transform.position = new Vector3(BenchCentres[3].x, -2.7f, 1f);
            PixelArt.AddSprite(floor, ShopArt.Plank(), -5, 12f).color = new Color(0.62f, 0.55f, 0.52f);
        }

        private SpriteRenderer Prop(string name, Sprite sprite, Vector2 at, int order, bool additive = false)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.transform.position = new Vector3(at.x, at.y, 1.5f);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.sharedMaterial = additive
                ? SpriteMaterials.Particle(SpriteMaterials.ParticleBlend.Additive, sprite.texture)
                : SpriteMaterials.For(sprite);
            sr.sortingOrder = order;
            return sr;
        }

        /// <summary>A bundle of herbs on a hinge at <paramref name="knot"/>: a pendulum the draught keeps moving.</summary>
        private void HangHerbs(Vector2 knot, int variant, int seed)
        {
            var sr = Prop("Herbs", ShopProps.HerbBundle(variant), knot, -12);
            GameObject go = sr.gameObject;
            var rb = go.AddComponent<Rigidbody2D>();
            rb.gravityScale = 1f;
            rb.mass = 0.3f;
            rb.angularDamping = 0.35f;
            rb.linearDamping = 0.5f;
            // Its weight hangs below the knot, so it swings about it.
            var bob = go.AddComponent<CircleCollider2D>();
            bob.radius = 0.25f;
            bob.offset = new Vector2(0f, -1.1f);
            var hinge = go.AddComponent<HingeJoint2D>();
            hinge.anchor = Vector2.zero;
            hinge.autoConfigureConnectedAnchor = true;
            // Nothing else in the shop is on the Debris layer: it touches nothing.
            PhysicsKit.GameLayers.Assign(go, PhysicsKit.GameLayers.Debris);
            PhysicsKit.Draft2D.Attach(go, seed);
            rb.AddTorque((seed % 2 == 0 ? 1f : -1f) * 0.01f, ForceMode2D.Impulse);
        }

        private void BuildCauldron(Vector2 centre)
        {
            var potGo = new GameObject("Cauldron");
            potGo.transform.SetParent(transform, false);
            potGo.transform.position = centre + new Vector2(0f, -0.55f);
            potGo.transform.localScale = Vector3.one * CauldronView.PotScale;
            Cauldron = potGo.AddComponent<PhysicsCauldronManager>();
            var view = potGo.AddComponent<CauldronView>();

            // The surface simulation sits well below the room, out of every camera.
            var surfGo = new GameObject("CauldronSurface");
            surfGo.transform.SetParent(transform, false);
            surfGo.transform.position = centre + new Vector2(0f, -60f);
            var liquid = surfGo.AddComponent<LiquidBody2D>();
            liquid.Build(1.86f);

            // The view knows where the mouth is; the pot needs that to map the pointer.
            Vector2 mouth = (Vector2)potGo.transform.position + ShopArt.MouthOffset * CauldronView.PotScale;
            float rx = ShopArt.LiquidRX / ShopArt.PPU * CauldronView.PotScale;
            float ry = ShopArt.LiquidRY / ShopArt.PPU * CauldronView.PotScale;
            Cauldron.ConfigureSurface(WorldCamera, mouth, rx, ry, liquid);
            view.Build(Cauldron, WorldCamera);
        }
    }
}
