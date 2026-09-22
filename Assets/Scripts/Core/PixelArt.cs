using System.Collections.Generic;
using UnityEngine;
using AlchemistsArsenal.Art;
using AlchemistsArsenal.Combat;

namespace AlchemistsArsenal.Core
{
    /// <summary>
    /// Runtime procedural pixel art for the Phase-0 slice — a thin layer over
    /// <see cref="PlaceholderArt"/> plus a solid sprite for the UI (the arenas'
    /// backdrops are <see cref="BiomeArt"/>'s).
    /// Still not hand-drawn final art; assigning authored <c>Sprite</c>s to the
    /// data SOs takes over (Deepening 6).
    /// </summary>
    public static class PixelArt
    {
        private static Sprite _white;

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
            var sr = PlaceholderArt.AddRenderer(go, PlaceholderArt.Shape.Disc, color, sortingOrder, outlined: false);
            if (diameter > 0f) go.transform.localScale = Vector3.one * diameter;
            return sr;
        }

        /// <summary>Add an authored <see cref="PixelSprites"/> sprite, optionally scaled so its width ≈ <paramref name="worldWidth"/>.</summary>
        public static SpriteRenderer AddSprite(GameObject go, Sprite sprite, int sortingOrder, float worldWidth = 0f)
        {
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.sortingOrder = sortingOrder;
            // Per-texture material — a single shared one makes the whole batch draw
            // with one texture (see PixelSprites.MaterialFor).
            Material mat = PixelSprites.MaterialFor(sprite);
            if (mat != null) sr.sharedMaterial = mat;
            if (worldWidth > 0f && sprite != null && sprite.bounds.size.x > 0.001f)
                go.transform.localScale = Vector3.one * (worldWidth / sprite.bounds.size.x);
            return sr;
        }

        public static Color Element(ElementType e) => PlaceholderArt.ElementColor(e);
    }
}
