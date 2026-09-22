using System;
using UnityEngine;

namespace AlchemistsArsenal.Art
{
    /// <summary>
    /// The two guardians, as rig parts. Both bosses used to be one 28-px purple blob
    /// with two dots for eyes, drawn at 2.2x and shared by the forest and the peak.
    /// Each is now a figure of separate parts (so it can breathe, sway, raise its
    /// arms before a slam, slump when its ward breaks, and come apart when it dies),
    /// drawn in the game's own style: 1-px ink outline, flat ramps lit from the upper
    /// left, ordered dithering, and at the same 16 px per unit as every other body in
    /// the arena, so a boss pixel is the same size as a hero's.
    ///
    /// <para><b>The Elder Woodwose</b>: a hunched giant of old bark, a mane of moss,
    /// antlers of dead branches, two hollow sockets and a heart-knot in its chest
    /// that glows like a coal. Arms long enough to drag its knuckles, legs that are
    /// roots.</para>
    ///
    /// <para><b>The Coven Matriarch</b>: a robed shape that does not touch the ground,
    /// a hood with a cracked porcelain mask where a face should be, a crown of iron
    /// spires, six long-fingered arms, and a ring of eyes that keeps turning behind
    /// her. The mask is the part that breaks.</para>
    ///
    /// Every part is baked once (with a white silhouette for the hit flash) and
    /// cached. Pivots sit on the joint each part turns about (hips, neck, shoulders);
    /// <see cref="Vfx.BossVisual"/> assembles and animates them.
    /// </summary>
    public static class BossArt
    {
        public const float PPU = 16f;

        private static readonly Color32 K = PixelCanvas.Ink;
        private static Color32 H(uint v, byte a = 255) => PixelCanvas.Hex(v, a);

        private static readonly Color32[] Bark = { H(0x1e140e), H(0x33231a), H(0x4a3423), H(0x6b4a2f), H(0x8f6843) };
        private static readonly Color32[] Moss = { H(0x1f3316), H(0x2f4a1f), H(0x4f7a3a), H(0x6ca24f), H(0x8cc267) };
        private static readonly Color32 Hollow = H(0x0b080d);
        private static readonly Color32[] Robe = { H(0x100818), H(0x1c1024), H(0x2e1a3a), H(0x46285a), H(0x61397d) };
        private static readonly Color32[] Porcelain = { H(0x8f8272), H(0xc2b391), H(0xe2d4b4), H(0xf7eedc) };
        private static readonly Color32[] Limb = { H(0x2c2436), H(0x4a3f58), H(0x6b5d7b), H(0x8e80a0) };
        private static readonly Color32[] Spire = { H(0x16111c), H(0x2a2233), H(0x3f354c), H(0x594b69) };
        private static readonly Color32 White = new Color32(255, 255, 255, 255);

        public static readonly Color32 Amber = H(0xf6c04a), Ember = H(0xe2683a), HexPink = H(0xc451a8), HexHot = H(0xf49ae6);
        public static readonly Color32 Blood = H(0xff4a3a);

        private static bool E(int x, int y, float cx, float cy, float rx, float ry) => PixelCanvas.InEllipse(x, y, cx, cy, rx, ry);
        private static Color32 S(Color32[] ramp, float light, int x, int y) => PixelCanvas.Shade(ramp, light, x, y);
        private static float N(int x, int y, int seed) => PixelCanvas.Hash(x, y, seed);

        /// <summary>Draw, outline and bake one part (with its flash silhouette), once.</summary>
        private static Sprite Part(string key, int w, int h, Vector2 pivot01, Action<PixelCanvas> draw, bool outline = true,
            float ppu = PPU, bool silhouette = true)
        {
            if (PixelCanvas.TryGet(key, out Sprite s)) return s;
            var c = new PixelCanvas(w, h);
            draw(c);
            if (outline) c.Outline(K);
            return c.Bake(key, ppu, pivot01, silhouette);
        }

        // ================================================================ WOODWOSE

        public const int WBodyW = 48, WBodyH = 46;
        /// <summary>Where the heart-knot sits, in world units above the body's pivot.</summary>
        public static readonly Vector2 WHeartLocal = new Vector2(0f, (WBodyH - 22 - 2) / PPU);

        private static bool WBody(int x, int y) =>
            y <= 44 && (E(x, y, 24, 17, 20, 14) || E(x, y, 24, 31, 14, 13)
                        || (y >= 17 && Mathf.Abs(x + 0.5f - 24) <= 11 - (y - 17) * 0.08f));

        private static bool WHeart(int x, int y) => E(x, y, 24, 22, 4.2f, 5.2f);

        private static readonly (int, int, int, int)[] Splits = { (12, 20, 16, 34), (33, 14, 30, 27), (20, 34, 22, 43), (36, 29, 33, 40) };
        private static readonly (int, int, int, int)[] Burning =
            { (12, 20, 16, 34), (33, 14, 30, 27), (20, 34, 22, 43), (36, 29, 33, 40), (24, 28, 19, 38), (24, 16, 29, 9), (22, 17, 15, 12) };

        private static Color32 BarkAt(int x, int y, float light)
        {
            // Vertical grain that wanders, and knots.
            int wob = Mathf.RoundToInt(Mathf.Sin(y * 0.33f + x * 0.07f) * 1.4f);
            bool streak = ((x + wob) % 5) == 0;
            float n = (N(x / 2, y / 3, 11) - 0.5f) * 0.18f;
            return S(Bark, Mathf.Clamp01(light * (streak ? 0.55f : 1f) + n), x, y);
        }

        public static Sprite WoodwoseBody() => Part("w_body", WBodyW, WBodyH, new Vector2(0.5f, 2f / WBodyH), c =>
        {
            c.Fill(WBody, (x, y) => BarkAt(x, y, PixelCanvas.SphereLight(x, y, 22, 20, 21, 24, 0.14f)));

            // The heart-knot: a hollow with a dark rim (its glow is a separate sprite).
            c.Fill((x, y) => E(x, y, 24, 22, 5.6f, 6.6f) && !WHeart(x, y), (x, y) => Bark[0]);
            c.Fill(WHeart, (x, y) => Hollow);
            // Ribs of root grown over the hollow, like a cage.
            c.Line(20, 18, 28, 26, 1f, Bark[2]);
            c.Line(28, 18, 20, 26, 1f, Bark[2]);

            foreach (var (x0, y0, x1, y1) in Splits) c.Line(x0, y0, x1, y1, 1f, Bark[0]);

            // A mane of moss over the shoulders, ragged along its lower edge, with strands.
            c.Fill((x, y) => WBody(x, y) && y < 9 + (int)(N(x / 2, 3, 5) * 6f),
                (x, y) => S(Moss, PixelCanvas.SphereLight(x, y, 20, 6, 22, 9, 0.2f) + (N(x, y, 9) - 0.5f) * 0.2f, x, y));
            foreach (int sx in new[] { 8, 13, 31, 37, 41 })
            {
                int len = 5 + (int)(N(sx, 1, 4) * 8f);
                c.Line(sx, 10, sx + (sx < 24 ? -1 : 1), 10 + len, 1.4f, (x, y) => Moss[y % 3 == 0 ? 3 : 2]);
            }
            // Fungus shelves on the flank.
            c.Fill((x, y) => E(x, y, 38, 33, 3.2f, 1.4f) || E(x, y, 40, 30, 2.4f, 1.1f), (x, y) => y < 32 ? H(0xd8c49a) : H(0xa08a66));
        });

        /// <summary>Enraged: the splits in its bark burn like a banked fire.</summary>
        public static Sprite WoodwoseCracks() => Part("w_cracks", WBodyW, WBodyH, new Vector2(0.5f, 2f / WBodyH), c =>
        {
            foreach (var (x0, y0, x1, y1) in Burning)
                c.Line(x0, y0, x1, y1, 1f, (x, y) => (x + y) % 3 == 0 ? Amber : Ember);
        }, outline: false, silhouette: false);

        public const int WHeadW = 30, WHeadH = 30;
        /// <summary>The two eye sockets, in world units from the head's pivot.</summary>
        public static readonly Vector2[] WEyesLocal =
            { new Vector2(-4.5f / PPU, (WHeadH - 12.5f - 4f) / PPU), new Vector2(4.5f / PPU, (WHeadH - 12.5f - 4f) / PPU) };

        public static Sprite WoodwoseHead() => Part("w_head", WHeadW, WHeadH, new Vector2(0.5f, 4f / WHeadH), c =>
        {
            c.Fill((x, y) => E(x, y, 15, 13, 10, 12) || E(x, y, 15, 21, 8, 6),
                (x, y) => BarkAt(x, y, PixelCanvas.SphereLight(x, y, 13, 11, 11, 14, 0.16f)));
            // A heavy brow over two deep sockets.
            c.Fill((x, y) => (y == 8 || y == 9) && Mathf.Abs(x + 0.5f - 15) < 9, (x, y) => Bark[y == 8 ? 3 : 1]);
            c.Fill((x, y) => E(x, y, 10.5f, 12.5f, 2.6f, 2.3f) || E(x, y, 19.5f, 12.5f, 2.6f, 2.3f), (x, y) => Hollow);
            // A gash of a mouth, with splintered teeth.
            c.Fill((x, y) => y >= 19 && y <= 22 && Mathf.Abs(x + 0.5f - 15) <= 5.5f - (y - 19) * 0.6f, (x, y) => Hollow);
            c.Fill((x, y) => (y == 19 || y == 20) && x % 2 == 0 && Mathf.Abs(x + 0.5f - 15) <= 5f && N(x, y, 3) > 0.25f,
                (x, y) => Bark[4]);
            // A beard of moss hanging off the jaw.
            c.Fill((x, y) => y >= 24 && Mathf.Abs(x + 0.5f - 15) <= 8 - (y - 24) * 1.1f + N(x, 0, 2) * 2f,
                (x, y) => S(Moss, 0.35f + N(x, y, 8) * 0.4f, x, y));
        });

        public const int WAntlerW = 64, WAntlerH = 30;

        public static Sprite WoodwoseAntlers() => Part("w_antlers", WAntlerW, WAntlerH, new Vector2(0.5f, 1f / WAntlerH), c =>
        {
            Func<int, int, Color32> col = (x, y) =>
                S(Bark, 0.45f + (x < 32 ? 0.15f : -0.05f) - y * 0.004f + N(x, y, 6) * 0.15f, x, y);
            for (int side = 0; side < 2; side++)
            {
                int sd = side;
                Vector2 M(float x, float y) => sd == 0 ? new Vector2(x, y) : new Vector2(WAntlerW - x, y);
                c.Stroke(new[] { M(28, 29), M(24, 22), M(17, 15), M(11, 7), M(9, 1) }, 4.5f, 1.8f, col);
                c.Stroke(new[] { M(24, 22), M(15, 20), M(9, 23) }, 2.6f, 1.2f, col);
                c.Stroke(new[] { M(17, 15), M(21, 8), M(23, 3) }, 2.4f, 1.1f, col);
                c.Stroke(new[] { M(12, 9), M(5, 8), M(2, 10) }, 2.0f, 1.0f, col);
                c.Stroke(new[] { M(20, 18), M(18, 25) }, 1.4f, 1.0f, (x, y) => Moss[2]);   // a hanging moss strand
            }
        });

        public const int WArmW = 20, WArmH = 46;

        /// <summary>One arm, hanging from the shoulder (the pivot, top). Mirror it for the other.</summary>
        public static Sprite WoodwoseArm() => Part("w_arm", WArmW, WArmH, new Vector2(0.5f, 1f - 1.5f / WArmH), c =>
        {
            Func<int, int, Color32> col = (x, y) => BarkAt(x, y, 0.35f + (x < 10 ? 0.25f : 0f));
            c.Stroke(new[] { new Vector2(10, 1), new Vector2(9, 12), new Vector2(11, 23), new Vector2(9, 33) }, 7f, 4.5f, col);
            foreach (var tip in new[] { new Vector2(3, 44), new Vector2(7, 45), new Vector2(12, 45), new Vector2(17, 42) })
                c.Stroke(new[] { new Vector2(9, 33), (new Vector2(9, 33) + tip) * 0.5f + new Vector2(0, 1), tip }, 2.2f, 1f,
                    (x, y) => Bark[y > 42 ? 4 : 3]);
            c.Fill((x, y) => E(x, y, 10, 3, 6, 3.5f), (x, y) => S(Moss, 0.4f + N(x, y, 1) * 0.4f, x, y));
        });

        public const int WRootW = 54, WRootH = 18;

        public static Sprite WoodwoseRoots() => Part("w_roots", WRootW, WRootH, new Vector2(0.5f, 1f - 1f / WRootH), c =>
        {
            Func<int, int, Color32> col = (x, y) => BarkAt(x, y, 0.3f + (x < 27 ? 0.15f : 0f));
            c.Stroke(new[] { new Vector2(20, 0), new Vector2(15, 9), new Vector2(8, 16) }, 8f, 3f, col);
            c.Stroke(new[] { new Vector2(34, 0), new Vector2(39, 9), new Vector2(46, 16) }, 8f, 3f, col);
            c.Stroke(new[] { new Vector2(15, 9), new Vector2(5, 13), new Vector2(1, 12) }, 3f, 1.2f, col);
            c.Stroke(new[] { new Vector2(39, 9), new Vector2(49, 12), new Vector2(53, 11) }, 3f, 1.2f, col);
            c.Stroke(new[] { new Vector2(27, 1), new Vector2(27, 9), new Vector2(29, 15) }, 5f, 2f, col);
        });

        /// <summary>A splinter of bark (0..2), for what the Woodwose sheds.</summary>
        public static Sprite BarkChip(int i)
        {
            i = Mathf.Abs(i) % 3;
            int v = i;
            return Part("w_chip" + v, 8, 7, new Vector2(0.5f, 0.5f), c =>
            {
                Vector2[] poly = v == 0
                    ? new[] { new Vector2(0, 2), new Vector2(5, 0), new Vector2(8, 3), new Vector2(3, 7) }
                    : v == 1
                        ? new[] { new Vector2(1, 0), new Vector2(7, 1), new Vector2(6, 6), new Vector2(0, 5) }
                        : new[] { new Vector2(0, 3), new Vector2(4, 0), new Vector2(8, 6), new Vector2(2, 7) };
                c.Fill((x, y) => PixelCanvas.InPoly(x, y, poly), (x, y) => BarkAt(x + v * 7, y, 0.3f + (7 - y) * 0.06f));
            });
        }

        /// <summary>A thorn as long as a forearm, pointing right (rotate it to its flight).</summary>
        public static Sprite Thorn() => Part("w_thorn", 14, 5, new Vector2(0.5f, 0.5f), c =>
        {
            c.Fill((x, y) => Mathf.Abs(y + 0.5f - 2.5f) <= 2.2f * (1f - x / 14f) + 0.3f,
                (x, y) => x > 9 ? H(0xe8dcc0) : S(Bark, 0.3f + (y < 2 ? 0.4f : 0f), x, y));
        });

        // =============================================================== MATRIARCH

        public const int MRobeW = 50, MRobeH = 50;
        private const float RobeHemPivot = 4f / MRobeH;
        /// <summary>The robe's shoulder line, in world units above its pivot (where the hood sits).</summary>
        public static readonly float MShoulderY = (MRobeH - 1 - 4) / PPU;

        private static float RobeHalfWidth(int y) => 7f + Mathf.Pow(Mathf.Clamp01(y / (float)MRobeH), 0.85f) * 17f;
        private static int HemY(int x) => MRobeH - 4 - (int)(N(x / 2, 7, 3) * 4f) - ((x / 3) % 2 == 0 ? 2 : 0);

        private static bool MRobe(int x, int y) =>
            (y >= 3 && y <= HemY(x) && Mathf.Abs(x + 0.5f - 25) <= RobeHalfWidth(y)) || E(x, y, 25, 6, 11, 5);

        private static void Sigil(PixelCanvas c, float cx, float cy, float r, float w, Color32 col)
        {
            c.Fill((x, y) => E(x, y, cx, cy, r, r) && !E(x, y, cx, cy, r - w, r - w), (x, y) => col);
            float top = cy - r * 0.92f, bot = cy + r * 0.5f, half = r * 0.8f;
            c.Line(cx, top, cx - half, bot, w, col);
            c.Line(cx, top, cx + half, bot, w, col);
            c.Line(cx - half, bot, cx + half, bot, w, col);
        }

        private static void HemRunes(PixelCanvas c, Color32 col, bool big)
        {
            for (int x = 6; x < MRobeW - 5; x += 6)
            {
                int yy = HemY(x) - 5;
                if (!MRobe(x, yy)) continue;
                c.Set(x, yy, col); c.Set(x - 1, yy, col); c.Set(x + 1, yy, col); c.Set(x, yy - 1, col);
                if (big) c.Set(x, yy + 1, col);
            }
        }

        public static Sprite MatriarchRobe() => Part("m_robe", MRobeW, MRobeH, new Vector2(0.5f, RobeHemPivot), c =>
        {
            c.Fill(MRobe, (x, y) =>
            {
                float fold = Mathf.Sin((x - 25) * 0.62f + y * 0.05f);
                float light = PixelCanvas.SphereLight(x, y, 21, 16, 20, 34, 0.2f) * 0.8f + fold * 0.14f;
                return S(Robe, Mathf.Clamp01(light), x, y);
            });
            // Tears in the hem: slits of the dark behind.
            foreach (int tx in new[] { 9, 18, 31, 40 })
                c.Line(tx, HemY(tx) - 6, tx + 1, HemY(tx), 1f, (x, y) => MRobe(x, y) ? Robe[0] : PixelCanvas.Clear);
            Sigil(c, 25, 16, 5.5f, 1f, HexPink);
            HemRunes(c, HexPink, big: false);
        });

        /// <summary>Enraged: every sigil on the robe burns hot.</summary>
        public static Sprite MatriarchSigilsHot() => Part("m_sigils_hot", MRobeW, MRobeH, new Vector2(0.5f, RobeHemPivot), c =>
        {
            Sigil(c, 25, 16, 6f, 1.7f, HexHot);
            HemRunes(c, HexHot, big: true);
        }, outline: false, silhouette: false);

        public const int MHeadW = 28, MHeadH = 32;
        /// <summary>Centre of the mask, in world units above the head's pivot (its bottom).</summary>
        public static readonly float MMaskY = (MHeadH - 19f) / PPU;

        private static readonly Vector2[] Hood =
            { new Vector2(14, 0), new Vector2(24, 10), new Vector2(27, 20), new Vector2(26, 32), new Vector2(2, 32), new Vector2(1, 20), new Vector2(4, 10) };

        private static bool Mask(int x, int y) => E(x, y, 14, 19, 6f, 8.4f);

        private static bool MaskCrack(int x, int y)
        {
            // A jagged split from the brow to the jaw: the one flaw in the face.
            float[] path = { 15.5f, 15f, 14f, 13.5f, 14.5f, 16f, 15.5f, 14.5f, 14f, 14.5f, 15.5f, 16f, 15f, 14f, 13.5f, 14f };
            int i = y - 11;
            return i >= 0 && i < path.Length && Mathf.Abs(x + 0.5f - path[i]) < 0.7f;
        }

        private static Color32 MaskShade(int x, int y) =>
            S(Porcelain, PixelCanvas.SphereLight(x, y, 12.5f, 16.5f, 6.5f, 8.8f, 0.3f), x, y);

        private static void DrawHood(PixelCanvas c, bool withMask)
        {
            c.Fill((x, y) => PixelCanvas.InPoly(x, y, Hood), (x, y) => S(Robe, PixelCanvas.SphereLight(x, y, 11, 12, 13, 18, 0.25f), x, y));
            c.Fill((x, y) => E(x, y, 14, 19.5f, 8.4f, 10.5f), (x, y) => Robe[0]);          // the dark inside the hood
            if (!withMask) return;
            c.Fill(Mask, MaskShade);
            // Eye slits that show nothing behind them.
            c.Fill((x, y) => E(x, y, 11f, 17f, 2.3f, 1.2f) || E(x, y, 17f, 17f, 2.3f, 1.2f), (x, y) => K);
            c.Fill(MaskCrack, (x, y) => K);
            c.Fill((x, y) => y == 24 && Mathf.Abs(x + 0.5f - 14) <= 2f, (x, y) => Porcelain[0]);
            c.Fill((x, y) => (y >= 19 && y <= 22) && (x == 10 || x == 18), (x, y) => HexPink);   // painted tear-lines
        }

        public static Sprite MatriarchHead() => Part("m_head", MHeadW, MHeadH, new Vector2(0.5f, 0f), c => DrawHood(c, true));

        /// <summary>The hood with no mask in it: what is left once the mask is gone.</summary>
        public static Sprite MatriarchHoodEmpty() => Part("m_hood", MHeadW, MHeadH, new Vector2(0.5f, 0f), c => DrawHood(c, false));

        /// <summary>The mask, broken along its crack: 0 = left piece, 1 = right, 2 = the chin.</summary>
        public static Sprite MaskShard(int piece)
        {
            int p = Mathf.Clamp(piece, 0, 2);
            // Centred on the mask's own middle (14, 19), so the shard spins about itself.
            return Part("m_shard" + p, MHeadW, MHeadH, new Vector2(14f / MHeadW, 1f - 19f / MHeadH), c =>
            {
                c.Fill((x, y) =>
                {
                    if (!Mask(x, y) || MaskCrack(x, y)) return false;
                    bool chin = y > 23;
                    if (p == 2) return chin;
                    if (chin) return false;
                    bool left = x < 14.5f + (y - 18) * 0.1f;
                    return p == 0 ? left : !left;
                }, MaskShade);
                c.Fill((x, y) => (E(x, y, 11f, 17f, 2.3f, 1.2f) || E(x, y, 17f, 17f, 2.3f, 1.2f)) && c.Opaque(x, y), (x, y) => K);
            });
        }

        public const int MCrownW = 40, MCrownH = 20;

        public static Sprite MatriarchCrown() => Part("m_crown", MCrownW, MCrownH, new Vector2(0.5f, 0f), c =>
        {
            int[] xs = { 5, 12, 20, 28, 35 };
            int[] hs = { 8, 13, 19, 13, 8 };
            for (int i = 0; i < xs.Length; i++)
            {
                int top = MCrownH - 1 - hs[i];
                int cx = xs[i];
                c.Stroke(new[] { new Vector2(cx, MCrownH - 1), new Vector2(cx + (i - 2) * 0.6f, top) }, 3.6f, 1.2f,
                    (x, y) => y <= top + 2 ? HexHot : S(Spire, 0.3f + (x < cx ? 0.3f : 0f), x, y));
            }
            // The band that holds them.
            c.Fill((x, y) => y >= MCrownH - 3 && x >= 4 && x <= 36, (x, y) => S(Spire, y == MCrownH - 3 ? 0.9f : 0.4f, x, y));
        });

        public const int MArmW = 12, MArmH = 36;

        /// <summary>One long arm, from the shoulder (the pivot, top) to spread fingers.</summary>
        public static Sprite MatriarchArm() => Part("m_arm", MArmW, MArmH, new Vector2(0.5f, 1f - 1f / MArmH), c =>
        {
            // A ragged sleeve, then a bare grey forearm far too long, then the fingers.
            c.Fill((x, y) => y <= 10 - (int)(N(x, 1, 2) * 3f) && Mathf.Abs(x + 0.5f - 6) <= 3.2f + y * 0.12f,
                (x, y) => S(Robe, 0.4f + (x < 6 ? 0.25f : 0f), x, y));
            c.Line(6, 8, 6, 27, 2.2f, (x, y) => S(Limb, x < 6 ? 0.8f : 0.45f, x, y));
            c.Line(6, 18, 6.5f, 19, 3f, Limb[2]);   // the elbow
            foreach (var tip in new[] { new Vector2(1, 35), new Vector2(4, 35.5f), new Vector2(8, 35.5f), new Vector2(11, 33) })
                c.Line(6, 27, tip.x, tip.y, 1.1f, Limb[3]);
        });

        public static Sprite Eye() => Part("m_eye", 11, 7, new Vector2(0.5f, 0.5f), c =>
        {
            c.Fill((x, y) => E(x, y, 5.5f, 3.5f, 5f, 2.7f), (x, y) => Porcelain[3]);
            c.Fill((x, y) => E(x, y, 5.5f, 3.5f, 2f, 2f), (x, y) => HexPink);
            c.Fill((x, y) => E(x, y, 5.5f, 3.5f, 0.9f, 1.3f), (x, y) => K);
        });

        /// <summary>A shred of the robe, for what the Matriarch sheds.</summary>
        public static Sprite Tatter(int i)
        {
            int v = Mathf.Abs(i) % 2;
            return Part("m_tatter" + v, 8, 8, new Vector2(0.5f, 0.5f), c =>
            {
                Vector2[] poly = v == 0
                    ? new[] { new Vector2(0, 0), new Vector2(8, 1), new Vector2(6, 8), new Vector2(3, 5), new Vector2(1, 8) }
                    : new[] { new Vector2(1, 0), new Vector2(7, 0), new Vector2(8, 7), new Vector2(4, 4), new Vector2(0, 7) };
                c.Fill((x, y) => PixelCanvas.InPoly(x, y, poly), (x, y) => S(Robe, 0.35f + (8 - y) * 0.05f, x, y));
            });
        }

        /// <summary>A hex orb: a hot core in a violet shell.</summary>
        public static Sprite HexOrb() => Part("m_orb", 9, 9, new Vector2(0.5f, 0.5f), c =>
        {
            var shell = new[] { Robe[3], HexPink, HexHot };
            c.Fill((x, y) => E(x, y, 4.5f, 4.5f, 4.2f, 4.2f), (x, y) => S(shell, PixelCanvas.SphereLight(x, y, 4, 4, 4, 4, 0.2f), x, y));
            c.Fill((x, y) => E(x, y, 3.5f, 3.5f, 1.2f, 1.2f), (x, y) => White);
        });

        // ================================================================= shared

        public static Sprite GlowAmber() => PixelCanvas.Glow("g_amber", 12, Amber, PPU);
        public static Sprite GlowHex() => PixelCanvas.Glow("g_hex", 12, HexHot, PPU);
        public static Sprite GlowRed() => PixelCanvas.Glow("g_red", 12, Blood, PPU);

        /// <summary>A soft dark ellipse for the ground under a boss, 2 units wide at scale 1.</summary>
        public static Sprite Shadow() => Part("boss_shadow", 32, 10, new Vector2(0.5f, 0.5f), c =>
            c.Fill((x, y) => E(x, y, 16, 5, 16, 5), (x, y) => E(x, y, 16, 5, 12, 3.4f) ? H(0x000000, 120) : H(0x000000, 60)),
            outline: false, silhouette: false);

        /// <summary>A thin white ring, 2 world units across at scale 1 (tint it; scale it to a radius).</summary>
        public static Sprite Ring() => Part("boss_ring", 48, 48, new Vector2(0.5f, 0.5f), c =>
            c.Fill((x, y) => E(x, y, 24, 24, 24, 24) && !E(x, y, 24, 24, 21.5f, 21.5f), (x, y) => White),
            outline: false, ppu: 24f, silhouette: false);

        /// <summary>A filled white disc, 2 world units across at scale 1.</summary>
        public static Sprite Disc() => Part("boss_disc", 48, 48, new Vector2(0.5f, 0.5f), c =>
            c.Fill((x, y) => E(x, y, 24, 24, 24, 24), (x, y) => White),
            outline: false, ppu: 24f, silhouette: false);

        /// <summary>A white bar 1 unit long and 1/4 unit thick, pivoted on its left end: telegraph lines.</summary>
        public static Sprite Bar() => Part("boss_bar", 4, 1, new Vector2(0f, 0.5f), c =>
            c.Fill((x, y) => true, (x, y) => White), outline: false, ppu: 4f, silhouette: false);

        /// <summary>A ward: a ring of runes, white (tint it the warded element), 2 units across.</summary>
        public static Sprite WardRunes() => Part("boss_ward", 64, 64, new Vector2(0.5f, 0.5f), c =>
        {
            c.Fill((x, y) => E(x, y, 32, 32, 31, 31) && !E(x, y, 32, 32, 29.5f, 29.5f), (x, y) => new Color32(255, 255, 255, 220));
            c.Fill((x, y) => E(x, y, 32, 32, 29.5f, 29.5f), (x, y) => new Color32(255, 255, 255, 30));
            for (int i = 0; i < 8; i++)
            {
                float a = i / 8f * Mathf.PI * 2f;
                float cx = 32 + Mathf.Cos(a) * 26f, cy = 32 + Mathf.Sin(a) * 26f;
                c.Line(cx - 2, cy, cx + 2, cy, 1f, White);
                c.Line(cx, cy - 2, cx, cy + 2, 1f, White);
                c.Set((int)cx - 2, (int)cy - 2, White);
                c.Set((int)cx + 2, (int)cy + 2, White);
            }
        }, outline: false, ppu: 32f, silhouette: false);
    }
}
