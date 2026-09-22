using System;
using System.Collections.Generic;
using UnityEngine;

namespace AlchemistsArsenal.Art
{
    /// <summary>
    /// A small raster for drawing pixel art in code: shapes, lit shading with ordered
    /// dithering, and a 1-px outline, in the same style as the hand-authored grids in
    /// <see cref="PixelSprites"/> (dark #17111c outline, flat ramps, top-left light).
    ///
    /// Hand-typing a 60x52 cauldron as string rows is how the old one ended up with
    /// rows of two different lengths; shapes that are geometric (a pot's belly, a
    /// glass flask, a mortar bowl) are drawn from their geometry here instead, and
    /// the organic ones stay hand-authored grids.
    ///
    /// Coordinates are top-left origin with y down, like the grids.
    /// </summary>
    public sealed class PixelCanvas
    {
        public readonly int W, H;
        private readonly Color32[] _px;

        private static readonly Dictionary<string, Sprite> _baked = new Dictionary<string, Sprite>();
        public static readonly Color32 Clear = new Color32(0, 0, 0, 0);
        public static readonly Color32 Ink = new Color32(0x17, 0x11, 0x1c, 0xff);

        // 4x4 Bayer matrix, 0..15, for ordered dithering between ramp steps.
        private static readonly int[] Bayer = { 0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5 };

        public PixelCanvas(int w, int h)
        {
            W = w; H = h;
            _px = new Color32[w * h];
        }

        public bool In(int x, int y) => x >= 0 && y >= 0 && x < W && y < H;
        public Color32 Get(int x, int y) => In(x, y) ? _px[y * W + x] : Clear;
        public bool Opaque(int x, int y) => In(x, y) && _px[y * W + x].a > 0;

        public void Set(int x, int y, Color32 c)
        {
            if (In(x, y)) _px[y * W + x] = c;
        }

        /// <summary>A cached, already-baked sprite, if one exists under <paramref name="key"/>.</summary>
        public static bool TryGet(string key, out Sprite sprite) =>
            _baked.TryGetValue(key, out sprite) && sprite != null;

        /// <summary>Colour every pixel <paramref name="inside"/> says is in the shape.</summary>
        public void Fill(Func<int, int, bool> inside, Func<int, int, Color32> color)
        {
            for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
                if (inside(x, y)) Set(x, y, color(x, y));
        }

        public void FillEllipse(float cx, float cy, float rx, float ry, Color32 c) =>
            Fill((x, y) => InEllipse(x, y, cx, cy, rx, ry), (x, y) => c);

        public static bool InEllipse(float x, float y, float cx, float cy, float rx, float ry)
        {
            float dx = (x + 0.5f - cx) / rx, dy = (y + 0.5f - cy) / ry;
            return dx * dx + dy * dy <= 1f;
        }

        /// <summary>Wrap every opaque region in a 1-px outline (on the transparent side).</summary>
        public void Outline(Color32 ink)
        {
            var add = new List<int>();
            for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                if (Opaque(x, y)) continue;
                if (Opaque(x - 1, y) || Opaque(x + 1, y) || Opaque(x, y - 1) || Opaque(x, y + 1)) add.Add(y * W + x);
            }
            foreach (int i in add) _px[i] = ink;
        }

        /// <summary>
        /// Pick a colour from <paramref name="ramp"/> (dark to light) for a 0..1 light
        /// level, dithering between neighbouring steps with a Bayer pattern so the
        /// shading reads as pixel art rather than banding.
        /// </summary>
        public static Color32 Shade(Color32[] ramp, float light01, int x, int y)
        {
            float t = Mathf.Clamp01(light01) * (ramp.Length - 1);
            int lo = Mathf.FloorToInt(t);
            float frac = t - lo;
            float threshold = (Bayer[(y & 3) * 4 + (x & 3)] + 0.5f) / 16f;
            int idx = frac > threshold ? lo + 1 : lo;
            return ramp[Mathf.Clamp(idx, 0, ramp.Length - 1)];
        }

        /// <summary>Lambert light from the upper left on an ellipsoid, 0..1.</summary>
        public static float SphereLight(float x, float y, float cx, float cy, float rx, float ry, float ambient = 0.18f)
        {
            float nx = (x + 0.5f - cx) / rx, ny = (y + 0.5f - cy) / ry;
            float nz2 = 1f - nx * nx - ny * ny;
            float nz = nz2 > 0f ? Mathf.Sqrt(nz2) : 0f;
            // light from up-left and toward the viewer (y is down here)
            Vector3 l = new Vector3(-0.55f, -0.6f, 0.58f).normalized;
            float lambert = Mathf.Max(0f, nx * l.x + ny * l.y + nz * l.z);
            return Mathf.Clamp01(ambient + (1f - ambient) * lambert);
        }

        /// <summary>Bake to a point-filtered sprite and cache it under <paramref name="key"/>.</summary>
        public Sprite Bake(string key, float ppu, Vector2 pivot01)
        {
            if (TryGet(key, out Sprite cached)) return cached;
            var tex = new Texture2D(W, H, TextureFormat.RGBA32, false)
            { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, name = "px_" + key };
            var flipped = new Color32[W * H];
            for (int y = 0; y < H; y++)
                Array.Copy(_px, y * W, flipped, (H - 1 - y) * W, W);   // row 0 is the top here, the bottom in a texture
            tex.SetPixels32(flipped);
            tex.Apply(false, true);
            var s = Sprite.Create(tex, new Rect(0, 0, W, H), pivot01, ppu);
            _baked[key] = s;
            return s;
        }

        public static Color32 WithAlpha(Color32 c, byte a) => new Color32(c.r, c.g, c.b, a);
        public static Color32 Hex(uint rgb, byte a = 0xff) =>
            new Color32((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb, a);
    }
}
