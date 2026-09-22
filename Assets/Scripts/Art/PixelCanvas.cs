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
        private static readonly Dictionary<Sprite, Sprite> _silhouettes = new Dictionary<Sprite, Sprite>();
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

        /// <summary>
        /// Bake to a point-filtered sprite and cache it under <paramref name="key"/>.
        /// With <paramref name="silhouette"/>, also bake a flat white copy of its shape
        /// (see <see cref="SilhouetteOf"/>): the hit flash. A tint can only darken a
        /// sprite, so flashing white needs a white sprite drawn over it. The texture
        /// is made unreadable on upload, so this is the only moment to make one.
        /// </summary>
        public Sprite Bake(string key, float ppu, Vector2 pivot01, bool silhouette = false)
        {
            if (TryGet(key, out Sprite cached)) return cached;
            Export(key);
            var s = Upload(key, _px, ppu, pivot01);
            _baked[key] = s;
            if (silhouette)
            {
                var white = new Color32[_px.Length];
                for (int i = 0; i < _px.Length; i++)
                    white[i] = _px[i].a > 0 ? new Color32(255, 255, 255, _px[i].a) : Clear;
                _silhouettes[s] = Upload(key + "_white", white, ppu, pivot01);
            }
            return s;
        }

        /// <summary>
        /// When set (the headless harness does, for the design document), every
        /// sprite is also written here as "&lt;key&gt;.png" the moment it is baked:
        /// the procedural art has no source image, so this is the only way to get
        /// the exact in-game pixels out.
        /// </summary>
        public static string ExportDir;

        private void Export(string key)
        {
            if (string.IsNullOrEmpty(ExportDir)) return;
            try
            {
                var tex = new Texture2D(W, H, TextureFormat.RGBA32, false);
                var flipped = new Color32[W * H];
                for (int y = 0; y < H; y++) Array.Copy(_px, y * W, flipped, (H - 1 - y) * W, W);
                tex.SetPixels32(flipped);
                tex.Apply(false, false);
                System.IO.Directory.CreateDirectory(ExportDir);
                System.IO.File.WriteAllBytes(System.IO.Path.Combine(ExportDir, key + ".png"), tex.EncodeToPNG());
                UnityEngine.Object.Destroy(tex);
            }
            catch (Exception e) { Debug.LogWarning($"[PixelCanvas] export of '{key}' failed: {e.Message}"); }
        }

        /// <summary>The white silhouette baked alongside <paramref name="sprite"/>, or null.</summary>
        public static Sprite SilhouetteOf(Sprite sprite) =>
            sprite != null && _silhouettes.TryGetValue(sprite, out Sprite s) ? s : null;

        private Sprite Upload(string key, Color32[] px, float ppu, Vector2 pivot01)
        {
            var tex = new Texture2D(W, H, TextureFormat.RGBA32, false)
            { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, name = "px_" + key };
            var flipped = new Color32[W * H];
            for (int y = 0; y < H; y++)
                Array.Copy(px, y * W, flipped, (H - 1 - y) * W, W);   // row 0 is the top here, the bottom in a texture
            tex.SetPixels32(flipped);
            tex.Apply(false, true);
            return Sprite.Create(tex, new Rect(0, 0, W, H), pivot01, ppu);
        }

        /// <summary>A line <paramref name="thickness"/> pixels wide (round caps), coloured per pixel.</summary>
        public void Line(float x0, float y0, float x1, float y1, float thickness, Func<int, int, Color32> color)
        {
            float r = thickness * 0.5f;
            int minX = Mathf.FloorToInt(Mathf.Min(x0, x1) - r - 1), maxX = Mathf.CeilToInt(Mathf.Max(x0, x1) + r + 1);
            int minY = Mathf.FloorToInt(Mathf.Min(y0, y1) - r - 1), maxY = Mathf.CeilToInt(Mathf.Max(y0, y1) + r + 1);
            Vector2 a = new Vector2(x0, y0), b = new Vector2(x1, y1), ab = b - a;
            float len2 = Mathf.Max(0.0001f, ab.sqrMagnitude);
            for (int y = minY; y <= maxY; y++)
            for (int x = minX; x <= maxX; x++)
            {
                Vector2 p = new Vector2(x + 0.5f, y + 0.5f);
                float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / len2);
                if ((p - (a + ab * t)).sqrMagnitude <= r * r) Set(x, y, color(x, y));
            }
        }

        public void Line(float x0, float y0, float x1, float y1, float thickness, Color32 c) =>
            Line(x0, y0, x1, y1, thickness, (x, y) => c);

        /// <summary>A tapering stroke along a polyline: thick at the first point, thin at the last.</summary>
        public void Stroke(Vector2[] pts, float startWidth, float endWidth, Func<int, int, Color32> color)
        {
            for (int i = 0; i < pts.Length - 1; i++)
            {
                float t = pts.Length <= 2 ? 0f : i / (float)(pts.Length - 2);
                Line(pts[i].x, pts[i].y, pts[i + 1].x, pts[i + 1].y, Mathf.Lerp(startWidth, endWidth, t), color);
            }
        }

        /// <summary>Is (x, y) inside the polygon (even-odd rule)?</summary>
        public static bool InPoly(float x, float y, Vector2[] poly)
        {
            bool inside = false;
            float px = x + 0.5f, py = y + 0.5f;
            for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
            {
                if ((poly[i].y > py) != (poly[j].y > py) &&
                    px < (poly[j].x - poly[i].x) * (py - poly[i].y) / (poly[j].y - poly[i].y) + poly[i].x)
                    inside = !inside;
            }
            return inside;
        }

        /// <summary>Deterministic 0..1 value noise per integer cell, for bark, moss and tatters.</summary>
        public static float Hash(int x, int y, int seed = 0)
        {
            unchecked
            {
                int h = x * 374761393 + y * 668265263 + seed * 1442695041;
                h = (h ^ (h >> 13)) * 1274126177;
                return ((h ^ (h >> 16)) & 0x7fffffff) / (float)0x7fffffff;
            }
        }

        /// <summary>A soft round glow (alpha falls off from the centre), for eyes, hearts and runes.</summary>
        public static Sprite Glow(string key, int size, Color32 color, float ppu)
        {
            if (TryGet(key, out Sprite s)) return s;
            var c = new PixelCanvas(size, size);
            float r = size * 0.5f;
            c.Fill((x, y) => true, (x, y) =>
            {
                float dx = x + 0.5f - r, dy = y + 0.5f - r;
                float k = Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy) / r);
                // Stepped, not smooth: three hard rings read as pixel art, a gradient does not.
                float a = k > 0.66f ? 1f : k > 0.33f ? 0.55f : k > 0.05f ? 0.2f : 0f;
                return new Color32(color.r, color.g, color.b, (byte)(color.a * a));
            });
            return c.Bake(key, ppu, new Vector2(0.5f, 0.5f));
        }

        public static Color32 WithAlpha(Color32 c, byte a) => new Color32(c.r, c.g, c.b, a);
        public static Color32 Hex(uint rgb, byte a = 0xff) =>
            new Color32((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb, a);
    }
}
