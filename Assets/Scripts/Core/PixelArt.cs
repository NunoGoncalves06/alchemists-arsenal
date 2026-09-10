using System.Collections.Generic;
using UnityEngine;
using AlchemistsArsenal.Art;
using AlchemistsArsenal.Combat;

namespace AlchemistsArsenal.Core
{
    /// <summary>
    /// Runtime procedural pixel art for the Phase-0 slice — a thin layer over
    /// <see cref="PlaceholderArt"/> plus solid + backdrop sprites for the UI.
    /// Still not hand-drawn final art; assigning authored <c>Sprite</c>s to the
    /// data SOs takes over (Deepening 6).
    /// </summary>
    public static class PixelArt
    {
        private static Sprite _white;
        private static readonly Dictionary<int, Sprite> _backdrops = new Dictionary<int, Sprite>();

        /// <summary>A 4×4 white sprite for tinting UI <c>Image</c>s.</summary>
        public static Sprite White
        {
            get
            {
                if (_white != null) return _white;
                var tex = new Texture2D(4, 4, TextureFormat.RGBA32, false)
                { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, name = "px_white" };
                var px = new Color32[16];
                for (int i = 0; i < px.Length; i++) px[i] = new Color32(255, 255, 255, 255);
                tex.SetPixels32(px); tex.Apply(false, true);
                _white = Sprite.Create(tex, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 4f, 0, SpriteMeshType.FullRect, new Vector4(1, 1, 1, 1));
                return _white;
            }
        }

        /// <summary>Add a tinted disc SpriteRenderer. <paramref name="diameter"/> ≤ 0 leaves the transform scale alone.</summary>
        public static SpriteRenderer AddDisc(GameObject go, Color color, int sortingOrder, float diameter = 0f)
        {
            var sr = PlaceholderArt.AddRenderer(go, PlaceholderArt.Shape.Disc, color, sortingOrder);
            if (diameter > 0f) go.transform.localScale = Vector3.one * diameter;
            return sr;
        }

        /// <summary>Add an authored <see cref="PixelSprites"/> sprite, optionally scaled so its width ≈ <paramref name="worldWidth"/>.</summary>
        public static SpriteRenderer AddSprite(GameObject go, Sprite sprite, int sortingOrder, float worldWidth = 0f)
        {
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.sortingOrder = sortingOrder;
            if (PixelSprites.Unlit != null) sr.sharedMaterial = PixelSprites.Unlit;
            if (worldWidth > 0f && sprite != null && sprite.bounds.size.x > 0.001f)
                go.transform.localScale = Vector3.one * (worldWidth / sprite.bounds.size.x);
            return sr;
        }

        public static Color Element(ElementType e) => PlaceholderArt.ElementColor(e);

        /// <summary>A low-res two-tone backdrop (sky wash + ground band) keyed by biome tint.</summary>
        public static Sprite Backdrop(Color groundTint, Color skyTint)
        {
            int key = QuantKey(groundTint) ^ (QuantKey(skyTint) << 12);
            if (_backdrops.TryGetValue(key, out var cached) && cached != null) return cached;

            const int w = 64, h = 36;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false)
            { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, name = "px_backdrop" };
            var px = new Color32[w * h];
            for (int y = 0; y < h; y++)
            {
                float t = y / (float)h;
                Color row = y < h * 0.34f
                    ? Color.Lerp(groundTint * 0.7f, groundTint, y / (h * 0.34f))
                    : Color.Lerp(skyTint * 0.8f, skyTint * 1.1f, (t - 0.34f) / 0.66f);
                for (int x = 0; x < w; x++)
                {
                    // faint dither so flat fills don't band
                    float n = ((x * 7 + y * 13) % 5) * 0.012f;
                    px[y * w + x] = row * (1f + n - 0.024f);
                }
            }
            tex.SetPixels32(px); tex.Apply(false, true);
            var sprite = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 8f);
            _backdrops[key] = sprite;
            return sprite;
        }

        private static int QuantKey(Color c) =>
            (Mathf.RoundToInt(c.r * 15)) | (Mathf.RoundToInt(c.g * 15) << 4) | (Mathf.RoundToInt(c.b * 15) << 8);
    }
}
