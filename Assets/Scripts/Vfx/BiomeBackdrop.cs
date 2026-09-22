using UnityEngine;
using AlchemistsArsenal.Art;
using AlchemistsArsenal.Combat;

namespace AlchemistsArsenal.Vfx
{
    /// <summary>
    /// The region an arena is in: its floor and scenery (<see cref="BiomeArt"/>),
    /// under everything, and the air above it: leaves coming down through the
    /// Whispering Woods, embers rising off the Cinder Peaks, snow in the Frostbite
    /// Caverns, spores drifting over the Venom Swamp, motes of the Coven's magic
    /// round the Peak. Presentation only; its particles come from the arena's own
    /// <see cref="VfxWorld"/> on its own seeded stream.
    /// </summary>
    public class BiomeBackdrop : MonoBehaviour
    {
        public const int SortingOrder = -10;

        private ElementType _theme;
        private float _clock;
        private System.Random _rng;

        public static BiomeBackdrop Create(Transform world, ElementType theme, int seed = 77)
        {
            var go = new GameObject("Backdrop");
            go.transform.SetParent(world, false);
            go.transform.localPosition = new Vector3(0f, 0f, 1f);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = BiomeArt.Backdrop(theme);
            Material m = SpriteMaterials.For(sr.sprite);
            if (m != null) sr.sharedMaterial = m;
            sr.sortingOrder = SortingOrder;

            var b = go.AddComponent<BiomeBackdrop>();
            b._theme = theme;
            b._rng = new System.Random(seed);
            return b;
        }

        private float R(float a, float b) => a + (float)_rng.NextDouble() * (b - a);

        private void Update()
        {
            var vfx = VfxWorld.Active;
            if (vfx == null) return;
            _clock += Time.deltaTime * 7f;   // motes per second
            while (_clock >= 1f)
            {
                _clock -= 1f;
                switch (_theme)
                {
                    case ElementType.Fire:
                        vfx.Mote(new Vector2(R(-15f, 15f), R(-8f, 4f)), new Vector2(R(-0.2f, 0.2f), R(0.6f, 1.4f)),
                            new Color(1f, R(0.35f, 0.6f), 0.15f, 0.9f), R(0.05f, 0.1f), R(1.5f, 2.6f), glow: true);
                        break;
                    case ElementType.Water:
                        vfx.Mote(new Vector2(R(-15f, 15f), R(0f, 9f)), new Vector2(R(0.1f, 0.5f), R(-1.2f, -0.6f)),
                            new Color(0.9f, 0.95f, 1f, 0.8f), R(0.06f, 0.12f), R(4f, 6f), glow: false);
                        break;
                    case ElementType.Poison:
                        vfx.Mote(new Vector2(R(-15f, 15f), R(-8f, 6f)), new Vector2(R(-0.15f, 0.15f), R(0.15f, 0.45f)),
                            new Color(0.75f, 0.9f, 0.35f, 0.7f), R(0.05f, 0.09f), R(3f, 5f), glow: true);
                        break;
                    case ElementType.Arcane:
                        vfx.Mote(new Vector2(R(-15f, 15f), R(-8f, 8f)), new Vector2(R(-0.3f, 0.3f), R(0.1f, 0.5f)),
                            new Color(0.96f, 0.6f, 0.9f, 0.8f), R(0.05f, 0.1f), R(2.5f, 4f), glow: true);
                        break;
                    default:
                        // Leaves in autumn colours, drifting down across the clearing.
                        Color leaf = _rng.NextDouble() < 0.5 ? new Color(0.62f, 0.4f, 0.16f, 0.9f) : new Color(0.45f, 0.55f, 0.2f, 0.9f);
                        vfx.Mote(new Vector2(R(-15f, 15f), R(2f, 9f)), new Vector2(R(0.3f, 0.9f), R(-1.1f, -0.5f)), leaf,
                            R(0.07f, 0.12f), R(4f, 6f), glow: false);
                        break;
                }
            }
        }
    }
}
