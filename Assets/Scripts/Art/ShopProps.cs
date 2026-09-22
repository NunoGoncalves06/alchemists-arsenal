using UnityEngine;

namespace AlchemistsArsenal.Art
{
    /// <summary>
    /// The shop behind the benches: shelves of stoppered jars, stubby candles,
    /// bundles of drying herbs. The benches used to stand in front of a bare stone
    /// wall. Same style and scale as <see cref="ShopArt"/> (16 px per unit, ink
    /// outline, light from the upper left).
    /// </summary>
    public static class ShopProps
    {
        public const float PPU = 16f;

        private static readonly Color32 K = PixelCanvas.Ink;
        private static Color32 X(uint v, byte a = 255) => PixelCanvas.Hex(v, a);
        private static Color32 S(Color32[] ramp, float light, int x, int y) => PixelCanvas.Shade(ramp, light, x, y);
        private static float N(int x, int y, int seed) => PixelCanvas.Hash(x, y, seed);

        private static readonly Color32[] Wood = { X(0x2a1a12), X(0x3b2517), X(0x54361f), X(0x6e4a2a), X(0x8a5f36) };
        private static readonly Color32[][] Brews =
        {
            new[] { X(0x1f3a1f), X(0x4f7a3a), X(0x8cc267) },
            new[] { X(0x3a1a12), X(0x9a3a22), X(0xe2683a) },
            new[] { X(0x1a2a4a), X(0x3a5a9a), X(0x7aa0e0) },
            new[] { X(0x2a3a0a), X(0x6a8a1a), X(0xb6c33f) },
            new[] { X(0x2e1a3a), X(0x61397d), X(0xc451a8) },
        };

        /// <summary>A shelf of jars, 72 px wide, pivot at the middle of its board.</summary>
        public static Sprite Shelf(int variant)
        {
            int v = Mathf.Abs(variant) % 3;
            string key = "prop_shelf" + v;
            if (PixelCanvas.TryGet(key, out Sprite s)) return s;
            const int w = 72, h = 24;
            var c = new PixelCanvas(w, h);
            // The jars first, standing on the board's top edge (y 19).
            int x0 = 3;
            for (int i = 0; x0 < w - 16; i++)   // the last 14 px are left clear for a candle
            {
                int jw = 5 + (int)(N(i, v, 11) * 5f), jh = 7 + (int)(N(v, i, 12) * 9f);
                var brew = Brews[(i + v * 2) % Brews.Length];
                int top = 19 - jh, jx = x0;
                bool round = N(i, v, 13) > 0.6f;
                c.Fill((x, y) => x >= jx && x < jx + jw && y >= top && y < 19 &&
                                 (!round || PixelCanvas.InEllipse(x, y, jx + jw * 0.5f, 19 - jh * 0.45f, jw * 0.55f, jh * 0.6f) || y < top + 2),
                    (x, y) =>
                    {
                        if (y < top + 2) return S(Wood, 0.55f, x, y);                                  // the stopper
                        bool glint = x == jx + 1 && y > top + 2 && y < 17;
                        bool liquid = y > top + 2 + jh / 3;
                        return glint ? X(0xe8e0d0) : liquid ? S(brew, 0.65f - (x - jx) * 0.05f, x, y) : X(0x3a3448);
                    });
                x0 += jw + 2 + (int)(N(i, 3, 14) * 3f);
            }
            // The board, with its brackets.
            c.Fill((x, y) => y >= 19 && y <= 21, (x, y) => S(Wood, y == 19 ? 0.85f : 0.4f, x, y));
            c.Fill((x, y) => ((x >= 8 && x <= 10) || (x >= w - 11 && x <= w - 9)) && y > 21, (x, y) => S(Wood, 0.3f, x, y));
            c.Outline(K);
            return c.Bake(key, PPU, new Vector2(0.5f, 1f - 20f / h));
        }

        /// <summary>A stub of candle in a dish, pivot at its base.</summary>
        public static Sprite Candle()
        {
            const string key = "prop_candle";
            if (PixelCanvas.TryGet(key, out Sprite s)) return s;
            var c = new PixelCanvas(8, 12);
            c.Fill((x, y) => x >= 2 && x <= 5 && y >= 3 && y <= 9, (x, y) => x == 2 ? X(0xf4ecd8) : X(0xd8ccb0));
            c.Fill((x, y) => y >= 10, (x, y) => S(Wood, y == 10 ? 0.8f : 0.35f, x, y));   // the dish
            c.Set(3, 2, X(0x2a1a12));                                                   // the wick
            c.Fill((x, y) => PixelCanvas.InEllipse(x, y, 3.5f, 1.2f, 1.1f, 1.8f), (x, y) => y < 1 ? X(0xfff2c0) : X(0xf6c04a));
            c.Outline(K);
            return c.Bake(key, PPU, new Vector2(0.5f, 0f));
        }

        /// <summary>A bundle of drying herbs tied with twine, pivot at the knot (top).</summary>
        public static Sprite HerbBundle(int variant)
        {
            int v = Mathf.Abs(variant) % 3;
            string key = "prop_herbs" + v;
            if (PixelCanvas.TryGet(key, out Sprite s)) return s;
            const int w = 14, h = 26;
            var c = new PixelCanvas(w, h);
            var leaf = v == 0 ? new[] { X(0x1f3a1f), X(0x4f7a3a), X(0x7aa25a) }
                     : v == 1 ? new[] { X(0x3a3a1a), X(0x6a6a2a), X(0x9a9a4a) }
                              : new[] { X(0x3a1a2a), X(0x6a3a5a), X(0x9a6a8a) };
            // Stems fanning down from the knot, leaves along them.
            for (int k = 0; k < 6; k++)
            {
                float ex = 2 + k * 2f, ey = 20 + N(k, v, 3) * 5f;
                c.Line(7, 4, ex, ey, 1f, X(0x4a3a22));
                for (int t = 6; t < (int)ey; t += 3)
                {
                    float lx = 7 + (ex - 7) * (t - 4) / (ey - 4);
                    c.Set((int)lx - 1, t, S(leaf, 0.3f + N(k, t, 5) * 0.6f, (int)lx, t));
                    c.Set((int)lx + 1, t + 1, S(leaf, 0.2f + N(t, k, 6) * 0.6f, (int)lx, t));
                }
            }
            c.Fill((x, y) => y >= 3 && y <= 5 && x >= 5 && x <= 9, (x, y) => X(0xc9a25a));   // the twine
            c.Line(7, 0, 7, 3, 1f, X(0xa07b3a));
            c.Outline(K);
            return c.Bake(key, PPU, new Vector2(0.5f, 1f));
        }

        public static Sprite CandleGlow() => PixelCanvas.Glow("prop_candle_glow", 16, new Color32(246, 192, 74, 255), PPU);
    }
}
