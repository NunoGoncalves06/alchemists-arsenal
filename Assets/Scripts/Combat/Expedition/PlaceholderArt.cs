using UnityEngine;

namespace AlchemistsArsenal.Combat
{
    /// <summary>
    /// Bakes simple, readable placeholder sprites at runtime (a tinted disc / diamond
    /// / star per role) so the expedition has something visible before the art team's
    /// pixel art lands. NOT final art — every sprite here is meant to be replaced by a
    /// hand-drawn <c>Sprite</c> asset on the corresponding prefab.
    /// </summary>
    public static class PlaceholderArt
    {
        public enum Shape { Disc, Diamond, Star }

        private const int Size = 32;
        private const float PixelsPerUnit = 32f;

        /// <summary>The per-texture unlit material (see <see cref="Art.SpriteMaterials"/>).</summary>
        public static Material MaterialFor(Sprite sprite) => Art.SpriteMaterials.For(sprite);

        /// <summary>Add a SpriteRenderer with a placeholder sprite + unlit material.</summary>
        /// <param name="outlined">False for glows, rings and ground: an opaque dark
        /// outline around a faint glow read as a dark ring rather than light.</param>
        public static SpriteRenderer AddRenderer(GameObject go, Shape shape, Color fill, int sortingOrder,
            bool outlined = true)
        {
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = Make(shape, fill, outlined ? new Color(0.10f, 0.10f, 0.12f) : fill);
            Material mat = MaterialFor(sr.sprite);
            if (mat != null) sr.sharedMaterial = mat;
            sr.sortingOrder = sortingOrder;
            return sr;
        }

        public static Color ElementColor(ElementType e) => e switch
        {
            ElementType.Fire => new Color(0.93f, 0.35f, 0.18f),
            ElementType.Water => new Color(0.30f, 0.62f, 0.95f),
            ElementType.Nature => new Color(0.36f, 0.74f, 0.36f),
            ElementType.Poison => new Color(0.71f, 0.76f, 0.25f), // #b6c33f, as in UITheme
            ElementType.Arcane => new Color(0.86f, 0.42f, 0.86f),
            _ => new Color(0.7f, 0.7f, 0.7f),
        };

        private static readonly System.Collections.Generic.Dictionary<string, Sprite> _cache =
            new System.Collections.Generic.Dictionary<string, Sprite>();

        public static Sprite Make(Shape shape, Color fill, Color outline)
        {
            // Cache by shape + both colours in full (alpha included) so each distinct
            // sprite is baked once. The old key XOR-folded a few channels together,
            // ignored alpha, and only looked at the outline's red, so two different
            // discs could come back as the same sprite.
            string key = $"{shape}:{ColorUtility.ToHtmlStringRGBA(fill)}:{ColorUtility.ToHtmlStringRGBA(outline)}";
            if (_cache.TryGetValue(key, out Sprite cached) && cached != null)
                return cached;

            var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                name = "Placeholder",
            };

            var px = new Color32[Size * Size];
            float c = (Size - 1) * 0.5f;

            for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
            {
                float dx = x - c, dy = y - c;
                float t = Metric(shape, dx, dy) / c;
                Color col;
                if (t > 1.02f) col = new Color(0, 0, 0, 0);
                else if (t > 0.80f) col = outline;
                else col = fill;
                px[y * Size + x] = col;
            }

            tex.SetPixels32(px);
            tex.Apply(false, true);

            Sprite sprite = Sprite.Create(tex, new Rect(0, 0, Size, Size), new Vector2(0.5f, 0.5f), PixelsPerUnit);
            _cache[key] = sprite;
            return sprite;
        }

        private static float Metric(Shape shape, float dx, float dy)
        {
            switch (shape)
            {
                case Shape.Diamond: return Mathf.Abs(dx) + Mathf.Abs(dy);
                case Shape.Star:
                {
                    float ang = Mathf.Atan2(dy, dx);
                    float spikes = 5f;
                    float wobble = 0.72f + 0.28f * Mathf.Cos(ang * spikes);
                    return Mathf.Sqrt(dx * dx + dy * dy) / wobble;
                }
                default: return Mathf.Sqrt(dx * dx + dy * dy);
            }
        }
    }
}
