using System.Collections.Generic;
using UnityEngine;
using AlchemistsArsenal.Combat;

namespace AlchemistsArsenal.Art
{
    /// <summary>
    /// The ground each road is fought on. The arena used to be one tinted ellipse
    /// on black for all five regions; each is now a place: a textured floor that
    /// fills the whole view, the region's scenery along the top (a tree line,
    /// volcanoes, a cave wall hung with ice, dead mangroves, the foot of the
    /// spire), props in the margins outside the arena walls, and a vignette.
    ///
    /// One 34x20-unit picture at the arena's 16 px per unit, drawn once per region
    /// and cached. Coordinates below are pixels, top-left origin; the arena's
    /// centre is (272, 160), its walls at y 48 and 272.
    /// </summary>
    public static class BiomeArt
    {
        public const float PPU = 16f;
        public const int W = 544, H = 320;
        private const int CX = W / 2, CY = H / 2;

        private static Color32 X(uint v, byte a = 255) => PixelCanvas.Hex(v, a);
        private static Color32 S(Color32[] ramp, float light, int x, int y) => PixelCanvas.Shade(ramp, light, x, y);
        private static float N(int x, int y, int seed) => PixelCanvas.Hash(x, y, seed);
        private static bool E(int x, int y, float cx, float cy, float rx, float ry) => PixelCanvas.InEllipse(x, y, cx, cy, rx, ry);

        /// <summary>Smooth value noise (bilinear over a hashed lattice), 0..1.</summary>
        private static float Smooth(float x, float y, float cell, int seed)
        {
            float fx = x / cell, fy = y / cell;
            int ix = Mathf.FloorToInt(fx), iy = Mathf.FloorToInt(fy);
            float tx = fx - ix, ty = fy - iy;
            tx = tx * tx * (3f - 2f * tx); ty = ty * ty * (3f - 2f * ty);
            float a = N(ix, iy, seed), b = N(ix + 1, iy, seed), c = N(ix, iy + 1, seed), d = N(ix + 1, iy + 1, seed);
            return Mathf.Lerp(Mathf.Lerp(a, b, tx), Mathf.Lerp(c, d, tx), ty);
        }

        private static float Vignette(int x, int y)
        {
            float dx = (x + 0.5f - CX) / (W * 0.5f), dy = (y + 0.5f - CY) / (H * 0.5f);
            return Mathf.Clamp01(1.12f - (dx * dx * 0.55f + dy * dy * 0.75f));
        }

        /// <summary>Pixel <paramref name="p"/> of the picture → world position (the picture is centred on the arena).</summary>
        public static Vector2 ToWorld(Vector2 p) => new Vector2((p.x - CX) / PPU, (CY - p.y) / PPU);

        // ----------------------------------------------------------------- pools

        /// <summary>
        /// The Venom Swamp's bog pools, in world units: drawn into its floor and
        /// given a drag field by the arena (ExpeditionWorld), so the picture and the
        /// physics agree about where the mud is.
        /// </summary>
        public static readonly (Vector2 center, float radius)[] BogPools =
        {
            (new Vector2(-6.5f, 3.2f), 2.1f), (new Vector2(1.5f, -3.6f), 2.4f), (new Vector2(7.5f, 2.4f), 1.8f),
            (new Vector2(-2.5f, -0.8f), 1.3f),
        };

        // ------------------------------------------------------------- backdrops

        public static Sprite Backdrop(ElementType theme)
        {
            string key = "biome_" + theme;
            if (PixelCanvas.TryGet(key, out Sprite s)) return s;
            var c = new PixelCanvas(W, H);
            switch (theme)
            {
                case ElementType.Fire: Cinder(c); break;
                case ElementType.Water: Frost(c); break;
                case ElementType.Poison: Swamp(c); break;
                case ElementType.Arcane: Peak(c); break;
                default: Woods(c); break;
            }
            c.Fill((x, y) => true, (x, y) => Darken(c.Get(x, y), Vignette(x, y)));
            return c.Bake(key, PPU, new Vector2(0.5f, 0.5f));
        }

        private static Color32 Darken(Color32 c, float k) =>
            new Color32((byte)(c.r * k), (byte)(c.g * k), (byte)(c.b * k), 255);

        /// <summary>A floor from a ramp, patched with noise and lit a little from the top of the view.</summary>
        private static void Floor(PixelCanvas c, Color32[] ramp, float bias, int seed)
        {
            c.Fill((x, y) => true, (x, y) =>
            {
                float n = Smooth(x, y, 26f, seed) * 0.6f + Smooth(x, y, 7f, seed + 1) * 0.4f;
                return S(ramp, bias + (n - 0.5f) * 0.5f, x, y);
            });
        }

        private static void Scatter(PixelCanvas c, int count, int seed, System.Action<int, int, int> draw, int top = 50, int bottom = H)
        {
            for (int i = 0; i < count; i++)
                draw((int)(N(i, 1, seed) * W), top + (int)(N(i, 2, seed) * (bottom - top)), i);
        }

        // ------------------------------------------------------------ the woods

        private static readonly Color32[] Moss = { X(0x0f1a12), X(0x172a1c), X(0x1f3826), X(0x2b4a30), X(0x3a5e3a) };
        private static readonly Color32[] Bark = { X(0x140d0a), X(0x24170f), X(0x3a2718), X(0x523822) };
        private static readonly Color32[] Canopy = { X(0x08100b), X(0x0f1d14), X(0x17291c), X(0x223826) };

        private static void Woods(PixelCanvas c)
        {
            Floor(c, Moss, 0.45f, 3);
            // Fallen leaves, grass tufts and stones.
            Scatter(c, 420, 11, (x, y, i) =>
            {
                Color32 leaf = i % 3 == 0 ? X(0x8a5a2a) : i % 3 == 1 ? X(0x6a3a1a) : X(0xa07a30);
                c.Set(x, y, leaf); c.Set(x + 1, y, leaf);
            });
            Scatter(c, 520, 12, (x, y, i) =>
            {
                Color32 g = Moss[3 + (i % 2)];
                c.Set(x, y, g); c.Set(x - 1, y + 1, g); c.Set(x + 1, y + 1, g); c.Set(x, y - 1, Moss[4]);
            });
            Scatter(c, 40, 13, (x, y, i) =>
                c.Fill((px, py) => E(px, py, x, y, 2.5f + i % 2, 1.6f), (px, py) => S(new[] { X(0x2b2733), X(0x4a4555), X(0x6a6478) }, py < y ? 0.9f : 0.3f, px, py)));
            // A tree line behind the arena.
            for (int t = 0; t < 30; t++)
            {
                float tx = t * 19 + N(t, 0, 5) * 12 - 6, r = 13 + N(t, 1, 5) * 9, cy = 22 + N(t, 2, 5) * 12;
                c.Fill((x, y) => y < 64 && (E(x, y, tx, cy, r, r * 0.9f) || E(x, y, tx - r * 0.7f, cy + 8, r * 0.7f, r * 0.6f) ||
                                            (Mathf.Abs(x - tx) < 2.5f && y > cy)),
                    (x, y) => S(Canopy, 0.35f + (Smooth(x, y, 5f, 7) - 0.5f) * 0.5f - (y > 50 ? 0.1f : 0f), x, y));
            }
            // Undergrowth along the foot of the tree line.
            for (int b = 0; b < 46; b++)
            {
                float bx = b * 12 + N(b, 0, 8) * 10, by = 52 + N(b, 1, 8) * 8, br = 7 + N(b, 2, 8) * 6;
                c.Fill((x, y) => E(x, y, bx, by, br, br * 0.6f),
                    (x, y) => S(Moss, 0.25f + (y < by ? 0.2f : 0f) + (Smooth(x, y, 4f, 9) - 0.5f) * 0.4f, x, y));
            }
            // Trunks in the margins, outside the arena walls.
            foreach (int tx in new[] { 18, 40, 504, 528 })
                c.Fill((x, y) => Mathf.Abs(x - tx - Mathf.Sin(y * 0.05f) * 2f) < 6f && y > 30,
                    (x, y) => S(Bark, (x < tx ? 0.7f : 0.35f) + (N(x / 2, y / 6, 3) - 0.5f) * 0.2f, x, y));
        }

        // ------------------------------------------------------------ the peaks

        private static readonly Color32[] Basalt = { X(0x120c0c), X(0x1e1515), X(0x2b1f1d), X(0x3b2b27), X(0x4e3a33) };
        private static readonly Color32[] Lava = { X(0x6a1a0a), X(0xb03a12), X(0xe2683a), X(0xf6a53a), X(0xffd66b) };

        private static void Cinder(PixelCanvas c)
        {
            Floor(c, Basalt, 0.45f, 21);
            // Cracks glowing with lava: thin wandering lines, brighter at their hearts.
            for (int k = 0; k < 14; k++)
            {
                float x0 = N(k, 1, 23) * W, y0 = 60 + N(k, 2, 23) * 240, a = N(k, 3, 23) * 6.28f;
                float px = x0, py = y0;
                for (int s = 0; s < 22; s++)
                {
                    a += (N(k, s, 24) - 0.5f) * 1.2f;
                    float nx = px + Mathf.Cos(a) * 4f, ny = py + Mathf.Sin(a) * 4f;
                    float heat = 1f - Mathf.Abs(s - 11) / 11f;
                    c.Line(px, py, nx, ny, 1f, (x, y) => S(Lava, 0.3f + heat * 0.6f, x, y));
                    px = nx; py = ny;
                }
            }
            Scatter(c, 300, 25, (x, y, i) => { c.Set(x, y, X(0x5a4a44)); if (i % 2 == 0) c.Set(x + 1, y, X(0x4a3a36)); });   // ash
            // Volcanoes against a red sky.
            c.Fill((x, y) => y < 60, (x, y) => S(new[] { X(0x1a0808), X(0x3a120c), X(0x6a2412), X(0x9a3a1a) }, 0.2f + y / 60f * 0.6f, x, y));
            foreach (var (vx, vh, vw) in new[] { (120f, 44f, 110f), (330f, 58f, 150f), (480f, 36f, 90f) })
                c.Fill((x, y) => y < 64 && y > 60 - vh + Mathf.Abs(x - vx) * (vh / vw) + (Mathf.Abs(x - vx) < 8 ? 6 : 0),
                    (x, y) => Mathf.Abs(x - vx) < 7 && y < 64 - vh + 12 ? S(Lava, 0.6f, x, y) : S(Basalt, 0.25f + (x < vx ? 0.15f : 0f), x, y));
            // Jagged rocks in the margins.
            foreach (int rx in new[] { 20, 44, 500, 526 })
                c.Fill((x, y) => y > 70 && Mathf.Abs(x - rx) < 10 - ((y / 7) % 3) * 2 && N(rx, y / 9, 3) > 0.25f,
                    (x, y) => S(Basalt, x < rx ? 0.8f : 0.4f, x, y));
        }

        // -------------------------------------------------------- the caverns

        private static readonly Color32[] Ice = { X(0x121a26), X(0x1c2a3c), X(0x2a4058), X(0x3e5a78), X(0x6a8cae) };
        private static readonly Color32[] CaveRock = { X(0x0c0f16), X(0x151a24), X(0x1f2634), X(0x2c3446) };

        private static void Frost(PixelCanvas c)
        {
            Floor(c, Ice, 0.5f, 31);
            // The sheen of ice: long diagonal streaks, and cracks.
            // Sparse and short, or the streaks read as falling rain.
            c.Fill((x, y) => y > 56 && ((x + y * 2) % 53 == 0) && Smooth(x, y, 30f, 33) > 0.62f && Smooth(x, y, 6f, 32) > 0.5f,
                (x, y) => S(Ice, 0.9f, x, y));
            for (int k = 0; k < 10; k++)
            {
                float px = N(k, 1, 34) * W, py = 70 + N(k, 2, 34) * 220, a = N(k, 3, 34) * 6.28f;
                for (int s = 0; s < 8; s++)
                {
                    float nx = px + Mathf.Cos(a) * 6f, ny = py + Mathf.Sin(a) * 6f;
                    c.Line(px, py, nx, ny, 1f, Ice[0]);
                    a += (N(k, s, 35) - 0.5f) * 1.8f; px = nx; py = ny;
                }
            }
            Scatter(c, 60, 36, (x, y, i) => c.Fill((px, py) => E(px, py, x, y, 5 + i % 4, 2), (px, py) => S(Ice, 0.95f, px, py)));   // snow drifts
            // The cave wall, hung with ice.
            c.Fill((x, y) => y < 60 + Smooth(x, 0, 18f, 37) * 10f, (x, y) => S(CaveRock, 0.2f + (Smooth(x, y, 9f, 38) - 0.5f) * 0.5f, x, y));
            for (int t = 0; t < 34; t++)
            {
                float tx = t * 16 + N(t, 0, 39) * 10, len = 10 + N(t, 1, 39) * 22;
                c.Fill((x, y) => y >= 50 && y < 50 + len && Mathf.Abs(x - tx) < 3.5f * (1f - (y - 50) / len),
                    (x, y) => S(new[] { X(0x3e5a78), X(0x7aa8d0), X(0xc8e6ff) }, x < tx ? 0.9f : 0.4f, x, y));
            }
            // Crystals in the margins, lit from within.
            foreach (int cx in new[] { 24, 516 })
                c.Fill((x, y) => y > 80 && y < 250 && Mathf.Abs(x - cx) < 9 - (y % 40) * 0.2f,
                    (x, y) => S(new[] { X(0x2a4058), X(0x5aa0d0), X(0xb8f0ff) }, 0.3f + ((x + y) % 7) / 10f, x, y));
        }

        // ---------------------------------------------------------- the swamp

        private static readonly Color32[] Mud = { X(0x12120a), X(0x1e1e10), X(0x2c2a16), X(0x3c3a1e), X(0x4e4a26) };
        private static readonly Color32[] Toxic = { X(0x1f2a0a), X(0x3a5210), X(0x6a8a1a), X(0xa4c43a), X(0xd8f07a) };

        private static void Swamp(PixelCanvas c)
        {
            Floor(c, Mud, 0.45f, 41);
            // The bog pools: the same ones the arena gives drag to.
            foreach (var (center, radius) in BogPools)
            {
                float px = CX + center.x * PPU, py = CY - center.y * PPU, r = radius * PPU;
                c.Fill((x, y) => E(x, y, px, py, r * 1.08f, r * 0.62f), (x, y) =>
                {
                    bool rim = !E(x, y, px, py, r, r * 0.55f);
                    if (rim) return S(Mud, 0.15f, x, y);
                    bool bubble = N(x / 2, y / 2, 44) > 0.93f;
                    return bubble ? Toxic[4] : S(Toxic, 0.35f + (Smooth(x, y, 6f, 45) - 0.5f) * 0.5f, x, y);
                });
            }
            // Reeds grow in clumps at the water's edge, not evenly (which read as rain).
            Scatter(c, 40, 46, (x, y, i) =>
            {
                for (int r = 0; r < 4; r++)
                {
                    int rx = x + r * 2 - 3, h = 3 + (i + r) % 4;
                    c.Line(rx, y, rx + (r % 3) - 1, y - h, 1f, Toxic[1 + (r % 2)]);
                }
            });
            // Dead mangroves in a sickly fog.
            c.Fill((x, y) => y < 60, (x, y) => S(new[] { X(0x10140a), X(0x1c2410), X(0x2c3a18), X(0x3e5020) }, 0.25f + y / 60f * 0.5f, x, y));
            for (int t = 0; t < 16; t++)
            {
                float tx = t * 36 + N(t, 0, 47) * 20;
                c.Stroke(new[] { new Vector2(tx, 64), new Vector2(tx + 2, 40), new Vector2(tx - 4, 20), new Vector2(tx - 12, 8) }, 5f, 1.5f,
                    (x, y) => S(Mud, 0.2f, x, y));
                c.Stroke(new[] { new Vector2(tx + 2, 40), new Vector2(tx + 12, 28), new Vector2(tx + 18, 22) }, 3f, 1f, (x, y) => S(Mud, 0.2f, x, y));
                c.Stroke(new[] { new Vector2(tx, 64), new Vector2(tx - 8, 72), new Vector2(tx - 14, 76) }, 3f, 1f, (x, y) => S(Mud, 0.2f, x, y));
            }
            c.Fill((x, y) => y > 44 && y < 70 && ((x + y) % 3 == 0) && Smooth(x, y, 20f, 48) > 0.45f, (x, y) => X(0x6a8a3a, 120));
        }

        // ------------------------------------------------------------- the peak

        private static readonly Color32[] Flag = { X(0x100c16), X(0x1c1624), X(0x2a2236), X(0x3a3048), X(0x4e4260) };

        private static void Peak(PixelCanvas c)
        {
            // Flagstones, each its own shade, with dark seams.
            c.Fill((x, y) => true, (x, y) =>
            {
                int row = y / 14, off = row % 2 == 0 ? 0 : 11;
                int col = (x + off) / 22;
                bool seam = y % 14 == 0 || (x + off) % 22 == 0;
                float l = 0.35f + (N(col, row, 51) - 0.5f) * 0.35f - (seam ? 0.3f : 0f);
                return S(Flag, l, x, y);
            });
            // The rune circle at the centre of the summit, and its spokes.
            c.Fill((x, y) => E(x, y, CX, CY, 150, 84) && !E(x, y, CX, CY, 147, 82) && N(x / 6, 1, 52) > 0.2f, (x, y) => X(0xc451a8));
            c.Fill((x, y) => E(x, y, CX, CY, 118, 66) && !E(x, y, CX, CY, 116, 64.6f) && N(x / 5, 2, 52) > 0.3f, (x, y) => X(0x8a55a8));
            for (int i = 0; i < 8; i++)
            {
                float a = i / 8f * Mathf.PI * 2f;
                c.Line(CX + Mathf.Cos(a) * 120, CY + Mathf.Sin(a) * 67, CX + Mathf.Cos(a) * 146, CY + Mathf.Sin(a) * 81, 1f, X(0xc451a8));
            }
            // A storm, and the spire's foot behind the arena.
            c.Fill((x, y) => y < 58, (x, y) => S(new[] { X(0x0a0610), X(0x1c1026), X(0x2e1a3a), X(0x46285a) }, 0.15f + y / 58f * 0.5f + (Smooth(x, y, 16f, 53) - 0.5f) * 0.3f, x, y));
            c.Fill((x, y) => y < 64 && Mathf.Abs(x + 0.5f - CX) < 40 - y * 0.2f, (x, y) =>
                S(Flag, 0.3f + (x < CX ? 0.2f : 0f) - ((y % 10 == 0) ? 0.15f : 0f), x, y));
            c.Fill((x, y) => E(x, y, CX, 30, 5, 9), (x, y) => X(0xf49ae6));   // its door, lit
            foreach (int px in new[] { 26, 518 })
                c.Fill((x, y) => Mathf.Abs(x - px) < 10 && y > 20, (x, y) => S(Flag, (x < px ? 0.7f : 0.35f) - (y % 12 == 0 ? 0.2f : 0f), x, y));
        }
    }
}
