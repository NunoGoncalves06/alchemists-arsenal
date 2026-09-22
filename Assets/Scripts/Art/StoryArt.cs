using System;
using UnityEngine;

namespace AlchemistsArsenal.Art
{
    /// <summary>
    /// The story's illustrations: the places the cutscenes happen (192x108, a 16:9
    /// stage) and the people in them, all on one pixel grid so a character stands
    /// in a room at the room's own scale. Drawn from geometry with
    /// <see cref="PixelCanvas"/> in the same style as the rest of the game: 1-px
    /// ink outline on figures, flat ramps, ordered dithering for light.
    ///
    /// Light is what these scenes are about. The shop at night is lit from the
    /// hearth and a candle; at dawn it is lit from the window; the Peak only from
    /// the spire. Each scene computes a light level per pixel from its sources and
    /// shades its ramps with it, so the pictures read as places rather than
    /// diagrams.
    /// </summary>
    public static class StoryArt
    {
        public const int W = 192, H = 108;
        public const float PPU = 16f;

        private static readonly Color32 K = PixelCanvas.Ink;
        private static Color32 X(uint v, byte a = 255) => PixelCanvas.Hex(v, a);
        private static Color32 S(Color32[] ramp, float light, int x, int y) => PixelCanvas.Shade(ramp, light, x, y);
        private static bool E(int x, int y, float cx, float cy, float rx, float ry) => PixelCanvas.InEllipse(x, y, cx, cy, rx, ry);
        private static float N(int x, int y, int seed) => PixelCanvas.Hash(x, y, seed);

        private static readonly Color32[] Wood = { X(0x160d09), X(0x2a1a12), X(0x3b2517), X(0x54361f), X(0x6e4a2a), X(0x8a5f36) };
        private static readonly Color32[] Stone = { X(0x17141c), X(0x2b2733), X(0x3d3847), X(0x544d61), X(0x6f6780) };
        private static readonly Color32[] Night = { X(0x070912), X(0x0f1426), X(0x1a2240), X(0x2a3560) };
        private static readonly Color32[] Dawn = { X(0x3a2a52), X(0x7a4a6a), X(0xc9707a), X(0xf0a070), X(0xf6d8a0) };
        private static readonly Color32[] Violet = { X(0x100818), X(0x2e1a3a), X(0x46285a), X(0x61397d), X(0x8a55a8) };
        private static readonly Color32[] Fire = { X(0x8a2a1a), X(0xe2683a), X(0xf2a33a), X(0xffd66b), X(0xfff2c0) };
        private static readonly Color32[] Leaf = { X(0x0c140e), X(0x14221a), X(0x1f3326), X(0x2c4a34) };
        private static readonly Color32[] Skin = { X(0xb07852), X(0xd9a178), X(0xf0c9a0) };
        private static readonly Color32[] OldSkin = { X(0x9a7a6c), X(0xc49e84), X(0xe2c3a8) };
        private static readonly Color32 Candle = X(0xe8b64c), Hex = X(0xc451a8), HexHot = X(0xf49ae6), Moon = X(0xe9e2c4);

        private static Sprite Make(string key, int w, int h, Vector2 pivot, Action<PixelCanvas> draw, bool outline)
        {
            if (PixelCanvas.TryGet(key, out Sprite s)) return s;
            var c = new PixelCanvas(w, h);
            draw(c);
            if (outline) c.Outline(K);
            return c.Bake(key, PPU, pivot);
        }

        private static Sprite Scene(string key, Action<PixelCanvas> draw) => Make(key, W, H, new Vector2(0.5f, 0.5f), draw, false);

        /// <summary>A person or thing, pivoted at its feet.</summary>
        private static Sprite Figure(string key, int w, int h, Action<PixelCanvas> draw) => Make(key, w, h, new Vector2(0.5f, 0f), draw, true);

        /// <summary>Darken toward the frame's edges.</summary>
        private static float Vignette(int x, int y)
        {
            float dx = (x + 0.5f - W * 0.5f) / (W * 0.62f), dy = (y + 0.5f - H * 0.5f) / (H * 0.7f);
            return Mathf.Clamp01(1.1f - (dx * dx + dy * dy) * 0.9f);
        }

        private static float Glow(int x, int y, float cx, float cy, float r) =>
            Mathf.Clamp01(1f - Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy)) / r);

        // ================================================================ scenes

        public static Sprite ScenePicture(string id) => id switch
        {
            "shop_night" => ShopInterior(dawn: false),
            "shop_dawn" => ShopInterior(dawn: true),
            "bedroom" => Bedroom(awake: false),
            "bedroom_awake" => Bedroom(awake: true),
            "woods" => Woods(),
            "peak" => Peak(),
            "summit" => Summit(),
            "doorway" => Doorway(),
            _ => Black(),
        };

        private static Sprite Black() => Scene("st_black", c =>
            c.Fill((x, y) => true, (x, y) => N(x, y, 3) > 0.992f ? X(0x2e1a3a) : X(0x0b0810)));

        /// <summary>Vertical boards, lit by <paramref name="light"/>.</summary>
        private static void Boards(PixelCanvas c, int top, int bottom, Func<int, int, float> light)
        {
            c.Fill((x, y) => y >= top && y < bottom, (x, y) =>
            {
                int col = x / 12;
                bool seam = x % 12 == 0;
                float grain = (N(col, y / 5, 21) - 0.5f) * 0.12f + (N(col, 0, 5) - 0.5f) * 0.16f;
                float l = light(x, y) + grain - (seam ? 0.25f : 0f);
                return S(Wood, l, x, y);
            });
        }

        private static Sprite ShopInterior(bool dawn) => Scene(dawn ? "st_shop_dawn" : "st_shop_night", c =>
        {
            // Where the light comes from: the hearth and a candle at night; the window at dawn.
            Func<int, int, float> light = (x, y) =>
            {
                float warm = dawn ? 0.25f * Glow(x, y, 50, 78, 70) : 0.75f * Glow(x, y, 50, 78, 95) + 0.35f * Glow(x, y, 124, 64, 40);
                float cold = dawn ? 0.8f * Glow(x, y, 156, 30, 120) : 0.25f * Glow(x, y, 156, 30, 60);
                return (0.08f + warm + cold) * Vignette(x, y);
            };

            Boards(c, 0, 96, light);
            // A beam across the top.
            c.Fill((x, y) => y >= 6 && y < 11, (x, y) => S(Wood, light(x, y) * 0.8f - (y == 10 ? 0.2f : 0f), x, y));

            // The window.
            c.Fill((x, y) => x >= 134 && x <= 178 && y >= 14 && y <= 52, (x, y) =>
            {
                bool frame = x <= 136 || x >= 176 || y <= 16 || y >= 50 || x == 156 || y == 33;
                if (frame) return S(Wood, light(x, y) + 0.1f, x, y);
                if (dawn) return S(Dawn, 1f - (y - 16) / 34f * 0.9f, x, y);
                float sky = 0.35f + (y - 16) / 34f * 0.5f;
                bool rain = N(x, (y + x) / 3, 17) > 0.93f && ((x + y) % 3 == 0);
                if (E(x, y, 166, 25, 5.5f, 5.5f)) return E(x, y, 164.5f, 23.5f, 1.4f, 1.2f) ? X(0xc9c09c) : Moon;
                return rain ? X(0x6a7aa0) : S(Night, sky, x, y);
            });

            // Shelves of jars.
            foreach (int sy in new[] { 26, 44 })
            {
                c.Fill((x, y) => x >= 8 && x <= 74 && y >= sy && y <= sy + 2, (x, y) => S(Wood, light(x, y) + 0.2f - (y == sy + 2 ? 0.3f : 0f), x, y));
                for (int i = 0; i < 7; i++)
                {
                    int jx = 11 + i * 9, jw = 4 + (int)(N(i, sy, 2) * 3f), jh = 5 + (int)(N(sy, i, 4) * 4f);
                    Color32[] tint = i % 3 == 0 ? new[] { X(0x1f3a1f), X(0x4f7a3a), X(0x8cc267) }
                                   : i % 3 == 1 ? new[] { X(0x3a1a12), X(0x9a3a22), X(0xe2683a) }
                                                : new[] { X(0x1a2a4a), X(0x3a5a9a), X(0x7aa0e0) };
                    int top = sy - jh;
                    c.Fill((x, y) => x >= jx && x < jx + jw && y >= top && y < sy, (x, y) =>
                    {
                        if (y == top) return S(Wood, 0.5f, x, y);                    // the cork
                        bool hi = x == jx + 1 && y > top + 1 && y < sy - 1;
                        return hi ? X(0xe8e0d0) : S(tint, 0.3f + light(x, y) * 0.9f, x, y);
                    });
                    c.Set(jx - 1, top + 1, K); c.Set(jx + jw, top + 1, K);
                }
            }

            // The hearth: a stone arch, the fire (low embers at dawn), a hook for the kettle.
            c.Fill((x, y) => x >= 20 && x <= 80 && y >= 54 && y <= 96 && !E(x, y, 50, 96, 22, 32), (x, y) =>
            {
                bool mortar = (y % 6 == 0) || ((x + (y / 6) * 5) % 11 == 0);
                return S(Stone, light(x, y) * 0.9f + 0.1f - (mortar ? 0.25f : 0f), x, y);
            });
            c.Fill((x, y) => E(x, y, 50, 96, 22, 32) && y <= 96, (x, y) => S(Stone, 0.05f + (1f - (96 - y) / 32f) * 0.2f, x, y));
            c.Fill((x, y) => y >= 80 && y <= 95 && Mathf.Abs(x + 0.5f - 50) < 16 - (95 - y) * 0.3f, (x, y) =>
            {
                float h = (95 - y) / 15f;
                float f = N(x / 2, y / 2, dawn ? 3 : 9) * 0.5f + (1f - h) * 0.7f - Mathf.Abs(x - 50) / 30f;
                if (dawn) return f > 0.55f ? S(Fire, 0.1f + f * 0.3f, x, y) : S(Stone, 0.1f, x, y);
                return f > 0.25f ? S(Fire, Mathf.Clamp01(f), x, y) : S(Stone, 0.15f, x, y);
            });
            c.Line(50, 64, 50, 72, 1f, S(Stone, 0.1f, 50, 64));   // the pot-hook

            // The table and a candle.
            c.Fill((x, y) => x >= 96 && x <= 170 && y >= 72 && y <= 76, (x, y) => S(Wood, light(x, y) + 0.25f - (y == 76 ? 0.3f : 0f), x, y));
            foreach (int lx in new[] { 100, 164 })
                c.Fill((x, y) => x >= lx && x <= lx + 2 && y > 76 && y <= 96, (x, y) => S(Wood, light(x, y), x, y));
            if (!dawn)
            {
                c.Fill((x, y) => x >= 123 && x <= 125 && y >= 64 && y <= 71, (x, y) => X(0xe8e0d0));
                c.Fill((x, y) => E(x, y, 124, 61, 1.5f, 2.6f), (x, y) => y < 61 ? Fire[4] : Fire[3]);
            }

            // Floorboards.
            c.Fill((x, y) => y >= 96, (x, y) => S(Wood, light(x, y) * 0.8f - ((y - 96) % 4 == 0 ? 0.2f : 0f) - ((x + (y / 4) * 23) % 37 == 0 ? 0.2f : 0f), x, y));
        });

        private static Sprite Bedroom(bool awake) => Scene(awake ? "st_bed_awake" : "st_bed", c =>
        {
            Func<int, int, float> light = (x, y) =>
                (0.06f + (awake ? 0.7f : 0.35f) * Glow(x, y, 40, 30, 90) + 0.3f * Glow(x, y, 120, 70, 50)) * Vignette(x, y);
            Boards(c, 0, 92, light);

            // A small window, moonlit (dawn once Tam is awake).
            c.Fill((x, y) => x >= 24 && x <= 56 && y >= 14 && y <= 44, (x, y) =>
            {
                bool frame = x <= 26 || x >= 54 || y <= 16 || y >= 42 || x == 40;
                if (frame) return S(Wood, light(x, y) + 0.1f, x, y);
                return awake ? S(Dawn, 1f - (y - 16) / 26f * 0.8f, x, y) : S(Night, 0.3f + (y - 16) / 26f * 0.5f, x, y);
            });

            // The bed.
            c.Fill((x, y) => x >= 92 && x <= 176 && y >= 58 && y <= 92, (x, y) =>
            {
                if (x <= 96) return S(Wood, light(x, y) + 0.15f, x, y);                      // headboard post
                if (y <= 64) return S(Wood, light(x, y) + 0.05f, x, y);                      // frame rail
                // A patchwork quilt: soft squares with a stitched seam between them.
                bool seam = (x - 97) % 8 == 0 || (y - 65) % 8 == 0;
                bool blue = (((x - 97) / 8) + ((y - 65) / 8)) % 2 == 0;
                Color32[] q = blue ? new[] { X(0x1a2238), X(0x2e3e62), X(0x4a5e88) } : new[] { X(0x2a1a22), X(0x4e2e3a), X(0x74485a) };
                return S(q, light(x, y) + 0.25f - (seam ? 0.3f : 0f), x, y);
            });
            c.Fill((x, y) => x >= 92 && x <= 97 && y >= 40 && y <= 92, (x, y) => S(Wood, light(x, y) + 0.2f, x, y));
            // Pillow (propped up against the headboard once Tam is sitting).
            c.Fill((x, y) => awake ? E(x, y, 104, 52, 6, 9) : E(x, y, 110, 60, 12, 5),
                (x, y) => S(new[] { X(0x6a6474), X(0x9a94a8), X(0xc9c3d6) }, light(x, y) + 0.3f, x, y));

            if (!awake)
            {
                // Tam asleep: sandy hair on the pillow, eyes shut, the quilt to the chin.
                c.Fill((x, y) => E(x, y, 110, 57, 6, 4.5f), (x, y) => y < 56 ? S(new[] { X(0x8a6a2a), X(0xc9a25a) }, 0.6f, x, y) : S(Skin, 0.6f, x, y));
                c.Line(106, 58, 108, 58, 1f, K); c.Line(112, 58, 114, 58, 1f, K);
                // The ward: a faint violet ring over the sleeper.
                c.Fill((x, y) => E(x, y, 118, 64, 30, 14) && !E(x, y, 118, 64, 28.5f, 12.8f) && ((x + y) & 1) == 0, (x, y) => HexHot);
            }
            else
            {
                // Tam sitting up, rubbing an eye.
                c.Fill((x, y) => x >= 104 && x <= 118 && y >= 50 && y <= 64, (x, y) => S(new[] { X(0x1a2a4a), X(0x3a5a9a), X(0x5a7fb0) }, 0.7f, x, y));
                c.Fill((x, y) => E(x, y, 111, 44, 6, 6.5f), (x, y) => y < 41 ? S(new[] { X(0x8a6a2a), X(0xc9a25a) }, 0.8f, x, y) : S(Skin, 0.8f, x, y));
                c.Set(109, 45, K); c.Set(113, 45, K); c.Line(110, 48, 112, 48, 1f, X(0xb07852));
                c.Fill((x, y) => x >= 104 && x <= 107 && y >= 46 && y <= 51, (x, y) => S(Skin, 0.7f, x, y));   // the hand at the eye
            }

            // A stool with a candle.
            c.Fill((x, y) => x >= 64 && x <= 80 && y >= 74 && y <= 77, (x, y) => S(Wood, light(x, y) + 0.2f, x, y));
            c.Fill((x, y) => (x == 66 || x == 78) && y > 77 && y <= 92, (x, y) => S(Wood, light(x, y), x, y));
            c.Fill((x, y) => x >= 71 && x <= 73 && y >= 67 && y <= 73, (x, y) => X(0xe8e0d0));
            c.Fill((x, y) => E(x, y, 72, 64, 1.4f, 2.4f), (x, y) => Fire[3]);

            c.Fill((x, y) => y > 92, (x, y) => S(Wood, light(x, y) * 0.7f - ((y - 92) % 4 == 0 ? 0.2f : 0f), x, y));
        });

        private static Sprite Woods() => Scene("st_woods", c =>
        {
            c.Fill((x, y) => true, (x, y) => S(Night, 0.15f + y / (float)H * 0.35f, x, y));
            c.Fill((x, y) => E(x, y, 150, 18, 6, 6), (x, y) => Moon);
            for (int i = 0; i < 40; i++)
                c.Set((int)(N(i, 1, 70) * W), (int)(N(i, 2, 70) * 40), X(0xc9d0e8));   // stars
            // Three rows of trees, further ones paler in the fog.
            for (int layer = 0; layer < 3; layer++)
            {
                int baseY = 70 + layer * 12, seed = 40 + layer;
                float fog = 0.55f - layer * 0.2f;
                for (int t = 0; t < 9; t++)
                {
                    float tx = t * 24 + N(t, layer, seed) * 16 - 4;
                    float trunkW = 2 + layer * 1.5f, crownR = 11 + layer * 3 + N(t, 7, seed) * 5;
                    float cy = baseY - 30 - layer * 6 + N(t, 3, seed) * 8;
                    c.Fill((x, y) => (Mathf.Abs(x - tx) <= trunkW && y > cy && y <= baseY + 10) || E(x, y, tx, cy, crownR, crownR * 1.1f)
                                     || E(x, y, tx - crownR * 0.6f, cy + 5, crownR * 0.7f, crownR * 0.6f)
                                     || E(x, y, tx + crownR * 0.6f, cy + 4, crownR * 0.7f, crownR * 0.6f),
                        (x, y) => S(Leaf, fog * 0.6f + (N(x / 2, y / 2, seed) - 0.5f) * 0.12f, x, y));
                }
            }
            // The clearing floor, and mist along it.
            c.Fill((x, y) => y >= 90, (x, y) => S(Leaf, 0.3f + (N(x, y / 2, 8) > 0.8f ? 0.2f : 0f), x, y));
            c.Fill((x, y) => y >= 90 && ((x * 7 + y * 3) % 11 == 0), (x, y) => S(Leaf, 0.9f, x, y));   // grass
            c.Fill((x, y) => y >= 84 && y <= 90 && (y % 2 == 0) && ((x + y) % 4 != 0) && N(x / 9, y, 12) > 0.35f, (x, y) => X(0x5a6a80, 90));   // low mist
        });

        private static Sprite Peak() => Scene("st_peak", c =>
        {
            // A storm the colour of the Coven, lit only by the spire.
            c.Fill((x, y) => true, (x, y) =>
            {
                float band = Mathf.Sin(y * 0.35f + x * 0.03f) * 0.08f + (N(x / 8, y / 3, 5) - 0.5f) * 0.1f;
                return S(Violet, 0.1f + y / (float)H * 0.25f + band + 0.35f * Glow(x, y, 96, 22, 60), x, y);
            });
            // The mountain.
            c.Fill((x, y) => y > 40 + Mathf.Abs(x - 96) * 0.62f + N(x / 4, 0, 3) * 4f, (x, y) =>
            {
                float snow = y < 58 + N(x / 3, 1, 6) * 6f ? 0.35f : 0f;
                return S(Stone, 0.12f + snow + 0.3f * Glow(x, y, 96, 30, 50) - (x > 96 ? 0.08f : 0f), x, y);
            });
            // The spire, and its lit window.
            c.Fill((x, y) => y >= 8 && y <= 44 && Mathf.Abs(x + 0.5f - 96) <= 1.5f + (y - 8) * 0.12f, (x, y) => S(Stone, 0.2f + (x < 96 ? 0.15f : 0f), x, y));
            c.Fill((x, y) => E(x, y, 96, 20, 1.2f, 2f), (x, y) => HexHot);
            // Lightning, frozen mid-stroke.
            c.Line(140, 0, 134, 14, 1f, X(0xd8c8f0)); c.Line(134, 14, 142, 22, 1f, X(0xd8c8f0)); c.Line(142, 22, 136, 36, 1f, X(0xd8c8f0));
        });

        private static Sprite Summit() => Scene("st_summit", c =>
        {
            c.Fill((x, y) => y < 50, (x, y) => S(Violet, 0.12f + y / 50f * 0.25f, x, y));
            // The spire's foot: two columns framing the view.
            foreach (int cx in new[] { 18, 174 })
                c.Fill((x, y) => Mathf.Abs(x - cx) <= 9 && y < 70, (x, y) => S(Stone, 0.15f + (x < cx ? 0.12f : 0f) - ((y % 9) == 0 ? 0.1f : 0f), x, y));
            // Flagstones running away from us.
            c.Fill((x, y) => y >= 50, (x, y) =>
            {
                float depth = (y - 50) / 58f;
                int row = (int)(Mathf.Pow(depth, 0.7f) * 9f);
                bool seam = ((int)((x - 96) / (8f + depth * 24f) + 100) % 2 == 0) ^ (row % 2 == 0);
                return S(Stone, 0.15f + depth * 0.3f - (seam ? 0.06f : 0f) + 0.25f * Glow(x, y, 96, 80, 60), x, y);
            });
            // The broken circle she stood in.
            c.Fill((x, y) => E(x, y, 96, 84, 52, 14) && !E(x, y, 96, 84, 50.5f, 12.8f) && N(x / 4, 1, 9) > 0.25f, (x, y) => Hex);
            // Haze.
            c.Fill((x, y) => y >= 60 && ((x + y) % 4 == 0) && N(x / 5, y / 3, 13) > 0.5f, (x, y) => X(0x8a55a8, 110));
        });

        private static Sprite Doorway() => Scene("st_doorway", c =>
        {
            Func<int, int, float> light = (x, y) => (0.05f + 0.8f * Glow(x, y, 96, 70, 80)) * Vignette(x, y);
            Boards(c, 0, 96, (x, y) => light(x, y) * 0.5f);
            // The open door, and the morning in it.
            c.Fill((x, y) => x >= 66 && x <= 126 && y >= 12 && y <= 96, (x, y) =>
            {
                bool frame = x <= 69 || x >= 123 || y <= 15;
                if (frame) return S(Wood, light(x, y) + 0.2f, x, y);
                if (y > 76 + (x - 96) * 0.05f) return S(new[] { X(0x4a3a2a), X(0x7a6a4a), X(0xa89060) }, 0.5f + (y - 76) / 40f, x, y);  // the path
                if (y > 56 && N(x / 3, 2, 5) > 0.45f && y > 64 - N(x / 4, 3, 5) * 12f) return S(Leaf, 0.7f, x, y);                   // hedges
                return S(Dawn, 1f - (y - 16) / 60f * 0.85f, x, y);
            });
            // The light that spills in across the floor.
            c.Fill((x, y) => y >= 96, (x, y) =>
            {
                float spread = 30f + (y - 96) * 2.5f;
                bool lit = Mathf.Abs(x - 96) < spread;
                return S(Wood, (lit ? 0.75f : 0.1f) - ((y - 96) % 4 == 0 ? 0.15f : 0f), x, y);
            });
        });

        // ================================================================ people

        public static Sprite ActorPicture(string id)
        {
            switch (id)
            {
                case "nell": return Nell(false);
                case "nell_flask": return Nell(true);
                case "tam": return Tam();
                case "tam_asleep": return TamAsleep();
                case "veil": return Veil();
                case "ysolde": return Ysolde();
                case "kettle": return Kettle(false);
                case "kettle_glow": return Kettle(true);
                case "cure": return PixelSprites.Flask(Combat.ElementType.Arcane);
                case "woodwose_head": return BossArt.WoodwoseHead();
                case "woodwose_body": return BossArt.WoodwoseBody();
                case "woodwose_arm": return BossArt.WoodwoseArm();
                case "woodwose_antlers": return BossArt.WoodwoseAntlers();
                case "matriarch_crown": return BossArt.MatriarchCrown();
                case "mask_l": return BossArt.MaskShard(0);
                case "mask_r": return BossArt.MaskShard(1);
                case "mask_chin": return BossArt.MaskShard(2);
                case "hero_rookie": return PixelSprites.Fighter("rookie");
                case "hero_knight": return PixelSprites.Fighter("knight");
                case "hero_herbalist": return PixelSprites.Fighter("herbalist");
                case "hero_merchant": return PixelSprites.Fighter("merchant");
                default: return null;
            }
        }

        private static readonly Color32[] HatGreen = { X(0x142214), X(0x1f3a22), X(0x2f5a32), X(0x4f7a4a) };
        private static readonly Color32[] Dress = { X(0x3a1420), X(0x6b2a35), X(0x8e3a45), X(0xb05a60) };
        private static readonly Color32[] DarkHair = { X(0x1a1210), X(0x2a1d1a), X(0x4a3328) };

        /// <summary>Nell Ashgrove: a witch's hat, a moss shawl, a wine-dark dress. Optionally holding the cure.</summary>
        private static Sprite Nell(bool flask) => Figure(flask ? "st_nell_flask" : "st_nell", 30, 48, c =>
        {
            float L(int x, int y) => x < 15 ? 0.75f : 0.45f;
            // Dress, flaring to the floor.
            c.Fill((x, y) => y >= 26 && y <= 47 && Mathf.Abs(x + 0.5f - 15) <= 5f + (y - 26) * 0.32f, (x, y) =>
                S(Dress, L(x, y) - ((x + y / 3) % 5 == 0 ? 0.2f : 0f), x, y));
            // Hair down the back.
            c.Fill((x, y) => y >= 12 && y <= 30 && Mathf.Abs(x + 0.5f - 15) <= 6.5f - Mathf.Max(0, y - 26) * 0.5f, (x, y) => S(DarkHair, L(x, y) - 0.1f, x, y));
            // Face.
            c.Fill((x, y) => E(x, y, 15, 18, 4.2f, 5f), (x, y) => S(Skin, L(x, y) + 0.1f, x, y));
            c.Set(13, 18, K); c.Set(17, 18, K);
            c.Set(15, 21, Skin[0]);
            // Shawl over the shoulders.
            c.Fill((x, y) => y >= 23 && y <= 30 && Mathf.Abs(x + 0.5f - 15) <= 8f - (y - 23) * 0.3f && !(y > 27 && Mathf.Abs(x + 0.5f - 15) < 2f), (x, y) =>
                S(HatGreen, L(x, y) + 0.1f, x, y));
            // Hands.
            c.Fill((x, y) => (E(x, y, 7.5f, 32, 1.5f, 1.8f) || E(x, y, 22.5f, 32, 1.5f, 1.8f)), (x, y) => S(Skin, 0.7f, x, y));
            // The hat: a wide brim, a cone that bends at the tip.
            c.Fill((x, y) => E(x, y, 15, 12, 13, 2.2f), (x, y) => S(HatGreen, L(x, y), x, y));
            c.Fill((x, y) => y >= 1 && y <= 11 && Mathf.Abs(x + 0.5f - (15 + (11 - y) * 0.35f)) <= 5f - (11 - y) * 0.42f, (x, y) =>
                S(HatGreen, L(x, y) + 0.1f, x, y));
            c.Fill((x, y) => y == 10 && Mathf.Abs(x + 0.5f - 15) <= 5f, (x, y) => Candle);   // the band
            if (flask)
            {
                // The cure, held up to the light.
                c.Fill((x, y) => E(x, y, 24, 29, 2.6f, 2.8f), (x, y) => y < 29 ? X(0xd8c8f0) : Hex);
                c.Fill((x, y) => x >= 23 && x <= 24 && y >= 24 && y <= 26, (x, y) => X(0xd8c8f0));
            }
        });

        private static readonly Color32[] Sandy = { X(0x6a4a1a), X(0xa07b3a), X(0xc9a25a) };
        private static readonly Color32[] Tunic = { X(0x1a2a4a), X(0x3a5a8a), X(0x5a7fb0) };

        /// <summary>Tam: younger, a head shorter, sandy-haired, a red scarf.</summary>
        private static Sprite Tam() => Figure("st_tam", 22, 36, c =>
        {
            float L(int x) => x < 11 ? 0.75f : 0.45f;
            c.Fill((x, y) => y >= 28 && y <= 35 && (Mathf.Abs(x + 0.5f - 8.5f) <= 1.8f || Mathf.Abs(x + 0.5f - 13.5f) <= 1.8f), (x, y) => S(Wood, 0.35f, x, y));
            c.Fill((x, y) => y >= 16 && y <= 29 && Mathf.Abs(x + 0.5f - 11) <= 5f, (x, y) => S(Tunic, L(x), x, y));
            c.Fill((x, y) => E(x, y, 11, 10, 5f, 5.5f), (x, y) => S(Skin, L(x) + 0.1f, x, y));
            c.Fill((x, y) => y <= 8 && E(x, y, 11, 8, 6f, 4.5f), (x, y) => S(Sandy, L(x), x, y));
            c.Set(9, 10, K); c.Set(13, 10, K);
            c.Fill((x, y) => y >= 15 && y <= 17 && Mathf.Abs(x + 0.5f - 11) <= 5.5f, (x, y) => S(new[] { X(0x6a1a1a), X(0xb0403a) }, 0.7f, x, y));
            c.Fill((x, y) => x >= 14 && x <= 15 && y >= 17 && y <= 22, (x, y) => X(0xb0403a));
        });

        /// <summary>Tam slumped over the table, asleep, the ward a faint violet halo.</summary>
        private static Sprite TamAsleep() => Figure("st_tam_asleep", 30, 16, c =>
        {
            c.Fill((x, y) => E(x, y, 15, 12, 12, 4), (x, y) => S(Tunic, 0.5f + (x < 15 ? 0.2f : 0f), x, y));
            c.Fill((x, y) => E(x, y, 12, 7, 5f, 4.5f), (x, y) => y < 7 ? S(Sandy, 0.7f, x, y) : S(Skin, 0.7f, x, y));
            c.Line(10, 9, 11, 9, 1f, Skin[0]);   // a closed eye
            c.Fill((x, y) => (E(x, y, 15, 9, 14.5f, 7.5f) && !E(x, y, 15, 9, 13f, 6.2f)) && ((x + y) & 1) == 0 && !c.Opaque(x, y), (x, y) => HexHot);
        });

        /// <summary>Sister Veil: tall, hooded in Coven violet, her mouth behind a veil, a letter in her hand.</summary>
        private static Sprite Veil() => Figure("st_veil", 26, 52, c =>
        {
            float L(int x) => x < 13 ? 0.7f : 0.4f;
            c.Fill((x, y) => y >= 10 && y <= 51 && Mathf.Abs(x + 0.5f - 13) <= 5f + (y - 10) * 0.16f, (x, y) =>
                S(Violet, L(x) + ((x + y / 4) % 6 == 0 ? -0.15f : 0f), x, y));
            c.Fill((x, y) => y <= 16 && E(x, y, 13, 10, 7f, 9f), (x, y) => S(Violet, L(x) + 0.1f, x, y));   // hood
            c.Fill((x, y) => E(x, y, 13, 12, 3.8f, 4.5f), (x, y) => S(Skin, 0.8f, x, y));
            // A gauze veil of Coven violet over the mouth, pinned at the ears.
            c.Fill((x, y) => E(x, y, 13, 14.5f, 4.2f, 2.8f) && y >= 13, (x, y) => ((x + y) & 1) == 0 ? X(0x8a55a8) : X(0x61397d));
            c.Line(11, 11, 12, 11, 1f, K); c.Line(14, 11, 15, 11, 1f, K);
            c.Set(10, 10, K); c.Set(16, 10, K);   // lashes
            // The Coven's sigil, and the letter.
            c.Fill((x, y) => E(x, y, 13, 24, 2.5f, 2.5f) && !E(x, y, 13, 24, 1.4f, 1.4f), (x, y) => Hex);
            c.Fill((x, y) => x >= 17 && x <= 21 && y >= 28 && y <= 32, (x, y) => y == 30 && x == 19 ? Hex : X(0xe8e0d0));
        });

        /// <summary>The Matriarch unmasked: Grandmother Ysolde's face in the Coven's hood, the painted tears now her own.</summary>
        private static Sprite Ysolde() => Figure("st_ysolde", 46, 56, c =>
        {
            // The Coven's hood, folded, over the shoulders of the robe.
            Vector2[] hood = { new Vector2(23, 0), new Vector2(37, 12), new Vector2(43, 28), new Vector2(45, 55), new Vector2(1, 55), new Vector2(3, 28), new Vector2(9, 12) };
            c.Fill((x, y) => PixelCanvas.InPoly(x, y, hood), (x, y) =>
                S(Violet, PixelCanvas.SphereLight(x, y, 18, 18, 22, 28, 0.25f) + (Mathf.Sin(x * 0.9f + y * 0.1f) > 0.85f ? -0.15f : 0f), x, y));
            c.Fill((x, y) => E(x, y, 23, 27, 14f, 17f), (x, y) => Violet[0]);
            // Grey hair, parted in the middle, falling either side of the face.
            c.Fill((x, y) => E(x, y, 23, 24, 13f, 14f) && !E(x, y, 23, 31, 9.5f, 12f), (x, y) =>
                S(new[] { X(0x6f6878), X(0x8f889a), X(0xbdb6c6), X(0xdcd6e2) }, 0.45f + (x < 23 ? 0.3f : 0f) + (N(x, y / 2, 4) - 0.5f) * 0.3f, x, y));
            c.Line(23, 11, 23, 16, 1f, X(0x6f6878));
            // The face: old, tired, kind. Lit from the left, like everything else.
            c.Fill((x, y) => E(x, y, 23, 31, 9f, 11.5f), (x, y) => S(OldSkin, PixelCanvas.SphereLight(x, y, 20, 27, 10, 12, 0.35f), x, y));
            c.Line(17, 27, 21, 26, 1f, X(0x8f889a)); c.Line(25, 26, 29, 27, 1f, X(0x8f889a));   // grey brows
            c.Line(18, 29, 20, 29, 1f, K); c.Line(26, 29, 28, 29, 1f, K);                       // eyes, heavy-lidded
            c.Set(19, 30, X(0x5a4a6a)); c.Set(27, 30, X(0x5a4a6a));
            c.Line(18, 31, 20, 31, 1f, OldSkin[0]); c.Line(26, 31, 28, 31, 1f, OldSkin[0]);     // the bags under them
            c.Line(23, 31, 22, 35, 1f, OldSkin[0]);                                             // the nose
            c.Line(21, 38, 25, 38, 1f, X(0x8a5a5a)); c.Set(20, 37, X(0x8a5a5a)); c.Set(26, 37, X(0x8a5a5a));   // the smile
            c.Line(16, 32, 17, 36, 1f, OldSkin[0]); c.Line(30, 32, 29, 36, 1f, OldSkin[0]);     // lines of age
            c.Line(20, 40, 26, 40, 1f, OldSkin[0]);                                             // the chin
            // The mask's painted tear-lines, faded into her own skin.
            c.Set(19, 33, X(0xb05a9a)); c.Set(19, 35, X(0xb05a9a)); c.Set(27, 33, X(0xb05a9a)); c.Set(27, 35, X(0xb05a9a));
        });

        private static readonly Color32[] Copper = { X(0x3a1a0e), X(0x6b3a1f), X(0x9a5a2a), X(0xc47a3a), X(0xe8a55a) };

        /// <summary>Grandmother's kettle. Lit, the charm she hid in it shows on the copper.</summary>
        private static Sprite Kettle(bool glow) => Figure(glow ? "st_kettle_glow" : "st_kettle", 26, 20, c =>
        {
            c.Fill((x, y) => E(x, y, 12, 13, 9f, 6.5f) && y <= 19, (x, y) => S(Copper, PixelCanvas.SphereLight(x, y, 10, 11, 9, 7, 0.2f), x, y));
            c.Stroke(new[] { new Vector2(20, 12), new Vector2(23, 8), new Vector2(25, 6) }, 2.4f, 1.4f, (x, y) => S(Copper, 0.6f, x, y));   // spout
            c.Fill((x, y) => E(x, y, 12, 7, 4f, 1.6f), (x, y) => S(Copper, 0.8f, x, y));                                                  // lid
            c.Set(12, 5, S(Copper, 0.9f, 12, 5));
            c.Fill((x, y) => E(x, y, 12, 5, 7f, 5f) && !E(x, y, 12, 5, 5.6f, 3.8f) && y <= 5, (x, y) => S(Stone, 0.4f, x, y));                // handle
            if (glow)
                foreach (int rx in new[] { 7, 12, 17 })
                {
                    c.Set(rx, 13, HexHot); c.Set(rx - 1, 13, Hex); c.Set(rx + 1, 13, Hex); c.Set(rx, 12, Hex); c.Set(rx, 14, Hex);
                }
        });
    }
}
