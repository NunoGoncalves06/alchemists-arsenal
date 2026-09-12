using UnityEngine;
using AlchemistsArsenal.Art;
using AlchemistsArsenal.Combat;
using AlchemistsArsenal.Crafting;

namespace AlchemistsArsenal.Core
{
    /// <summary>
    /// The morning/evening shop world: a dressed backdrop, the physics cauldron with
    /// its spoon and brew glow, and a few herb bodies to swirl. Lives under a
    /// loop-owned root, never under a UI panel (DESIGN.md §11) — destroying the root
    /// tears it all down.
    /// </summary>
    public class ShopWorld : MonoBehaviour
    {
        private const int HerbLayer = 0; // Default — Phase 0 keeps it simple

        public Camera WorldCamera { get; private set; }
        public PhysicsCauldronManager Cauldron { get; private set; }

        private SpriteRenderer _glow;

        public void Build()
        {
            transform.position = Vector3.zero;

            var camGo = new GameObject("ShopCamera") { tag = "MainCamera" }; // PhysicsCauldronManager reads Camera.main
            WorldCamera = camGo.AddComponent<Camera>();
            WorldCamera.transform.SetParent(transform, false);
            WorldCamera.transform.position = new Vector3(0f, 0.5f, -10f);
            WorldCamera.orthographic = true;
            WorldCamera.orthographicSize = 4.2f;
            WorldCamera.clearFlags = CameraClearFlags.SolidColor;
            WorldCamera.backgroundColor = new Color(0.07f, 0.05f, 0.09f);
            WorldCamera.depth = -1;

            BuildBackdrop();

            var potGo = new GameObject("Cauldron");
            potGo.transform.SetParent(transform, false);
            Cauldron = potGo.AddComponent<PhysicsCauldronManager>();
            Cauldron.Configure(1 << HerbLayer);

            // A soft element-tinted glow under the pot: the brew reading its own
            // colour back at the player, brightening as the heat climbs.
            var glowGo = new GameObject("BrewGlow");
            glowGo.transform.SetParent(potGo.transform, false);
            glowGo.transform.localPosition = new Vector3(0f, 0.35f, 0f);
            _glow = PixelArt.AddDisc(glowGo, new Color(0.4f, 0.9f, 0.4f, 0.22f), -1, 3.4f);

            // Every leaf that goes in makes the brew flare in that leaf's colour, so a
            // wrong ingredient is something you SEE react in the pot, not just a line
            // of red text on the bench.
            Cauldron.OnIngredientAdded += FlashMix;

            var potArt = new GameObject("CauldronArt");
            potArt.transform.SetParent(potGo.transform, false);
            PixelArt.AddSprite(potArt, PixelSprites.Cauldron(), 0, 4.6f);

            BuildSpoon(potGo.transform);

            for (int i = 0; i < 4; i++)
            {
                float ang = i / 4f * Mathf.PI * 2f;
                var herb = new GameObject($"Herb_{i}");
                herb.transform.SetParent(transform, false);
                herb.layer = HerbLayer;
                herb.transform.position = new Vector3(Mathf.Cos(ang) * 1.1f, Mathf.Sin(ang) * 1.1f, 0f);

                var rb = herb.AddComponent<Rigidbody2D>();
                rb.gravityScale = 0f;
                rb.linearDamping = 1.2f;
                rb.angularDamping = 0.8f;

                herb.AddComponent<CircleCollider2D>().radius = 0.18f;
                PixelArt.AddSprite(herb, PixelSprites.Herb(i % 2 == 0 ? ElementType.Nature : ElementType.Water), 3, 0.5f);
            }
        }

        private void BuildBackdrop()
        {
            var back = new GameObject("ShopBackdrop");
            back.transform.SetParent(transform, false);
            back.transform.position = new Vector3(0f, 0.5f, 1f);
            var sr = PixelArt.AddSprite(back, PixelArt.Backdrop(
                new Color(0.20f, 0.13f, 0.10f),    // floorboards
                new Color(0.13f, 0.09f, 0.16f)),   // dim shop wall
                -20, 20f);
            sr.color = new Color(1f, 1f, 1f, 0.9f);
        }

        private void BuildSpoon(Transform potParent)
        {
            var spoonGo = new GameObject("Spoon");
            spoonGo.transform.SetParent(transform, false);
            var sr = PixelArt.AddSprite(spoonGo, PixelSprites.Spoon(), 8, 0.55f);
            spoonGo.AddComponent<CauldronSpoon>().Configure(Cauldron, sr);
        }

        private Color _flashColor;
        private float _flashUntil;

        private void FlashMix(Combat.ElementType element)
        {
            _flashColor = PixelArt.Element(element);
            _flashUntil = Time.time + 0.9f;
        }

        private void OnDestroy()
        {
            if (Cauldron != null) Cauldron.OnIngredientAdded -= FlashMix;
        }

        /// <summary>Tint the brew glow to the potion being made, pulse it with the heat,
        /// and flare it in a leaf's colour as that leaf is crushed in.</summary>
        private void Update()
        {
            if (_glow == null || Cauldron == null) return;

            var order = Systems.CraftingManager.Instance != null
                ? Systems.CraftingManager.Instance.CurrentOrder : null;
            Color tint = order != null ? PixelArt.Element(order.element) : new Color(0.4f, 0.8f, 0.4f);

            float heat = Cauldron.Heat01;
            float pulse = 0.14f + heat * 0.34f + Mathf.Sin(Time.time * 3f) * 0.03f;
            float scale = 3.2f + heat * 0.8f;

            float flash = Mathf.Clamp01((_flashUntil - Time.time) / 0.9f);
            if (flash > 0f)
            {
                tint = Color.Lerp(tint, _flashColor, flash);
                pulse += flash * 0.45f;
                scale += flash * 1.1f;
            }

            _glow.color = new Color(tint.r, tint.g, tint.b, Mathf.Clamp01(pulse));
            _glow.transform.localScale = Vector3.one * scale;
        }
    }
}
