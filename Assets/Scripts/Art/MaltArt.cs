using UnityEngine;

namespace AlchemistsArsenal.Art
{
    /// <summary>
    /// The Malting bench, drawn from shapes in the house style (16 px per unit, ink
    /// outline, light from the upper left): a glass steeping jar in the same
    /// back / brew / front layers as the bottling flask, grain and husks that
    /// sprout, a hanging sack of barley, a brick kiln with a drying tray, and logs.
    /// Canvas rows count down from the top; standing objects pivot at their base.
    /// </summary>
    public static class MaltArt
    {
        public const float PPU = 16f;

        private static readonly Color32 K = PixelCanvas.Ink;
        private static Color32 X(uint v, byte a = 255) => PixelCanvas.Hex(v, a);
        private static Color32 S(Color32[] ramp, float light, int x, int y) => PixelCanvas.Shade(ramp, light, x, y);

        private static readonly Color32[] Wood = { X(0x2a1a12), X(0x3b2517), X(0x54361f), X(0x6e4a2a), X(0x8a5f36) };
        private static readonly Color32[] Iron = { X(0x2b2833), X(0x3d3947), X(0x57515f), X(0x7a7383), X(0xa39cab) };
        private static readonly Color32[] Brick = { X(0x3a1a16), X(0x5a2a20), X(0x7a3a2a), X(0x9a5238), X(0xb86c4a) };
        private static readonly Color32[] Copper = { X(0x5a2a14), X(0x9a5a2a), X(0xd08a4a) };
        private static readonly Color32[] Burlap = { X(0x4a3a24), X(0x6a5434), X(0x8a7048), X(0xa88c5c), X(0xc8aa74) };
        private static readonly Color32[] Barley = { X(0x7a5420), X(0xa8742a), X(0xd9a441), X(0xf0c96a) };
        private static readonly Color32[] Sprout = { X(0x4f7a2e), X(0x7fae48), X(0xa9d46a), X(0xd2eca0) };
        private static readonly Color32[] Chaff = { X(0x8a7c60), X(0xb4a482), X(0xd8caa8), X(0xefe4c8) };

        // ------------------------------------------------------------ the jar

        public const int JarW = 36, JarH = 34;
        public const int JarBodyTop = 9, JarNeckX0 = 6, JarNeckX1 = 29;

        /// <summary>The inside of the glass: a wide, round-bottomed body under a short neck.</summary>
        public static bool JarInterior(int x, int y)
        {
            if (y >= 4 && y < JarBodyTop) return x >= JarNeckX0 && x <= JarNeckX1;
            if (y < JarBodyTop || y > JarH - 2) return false;
            // Rounded bottom corners.
            const float r = 6f;
            float cx = x < 2 + r ? 2 + r : x > 33 - r ? 33 - r : x;
            float cy = y > JarH - 2 - r ? JarH - 2 - r : y;
            return x >= 2 && x <= 33 && (x - cx) * (x - cx) + (y - cy) * (y - cy) <= r * r + 0.5f;
        }

        private static bool JarLip(int x, int y) => y >= 1 && y <= 3 && x >= 4 && x <= 31;

        private static bool JarHollow(int x, int y) =>
            JarInterior(x, y) && JarInterior(x - 1, y) && JarInterior(x + 1, y) && JarInterior(x, y - 1) && JarInterior(x, y + 1);

        /// <summary>The glass's back: a faint tint behind what is in it.</summary>
        public static Sprite JarBack()
        {
            const string key = "malt_jar_back";
            if (PixelCanvas.TryGet(key, out Sprite s)) return s;
            var c = new PixelCanvas(JarW, JarH);
            c.Fill(JarInterior, (x, y) => X(0x9fd4f0, 34));
            return c.Bake(key, PPU, new Vector2(0.5f, 0f));
        }

        /// <summary>The glass's front: edge, rim, the highlights that make it read as glass. The vat's is banded in copper.</summary>
        public static Sprite JarFront(bool upgraded = false)
        {
            string key = upgraded ? "malt_jar_front_vat" : "malt_jar_front";
            if (PixelCanvas.TryGet(key, out Sprite s)) return s;
            var shape = new PixelCanvas(JarW, JarH);
            shape.Fill((x, y) => JarInterior(x, y) || JarLip(x, y), (x, y) => new Color32(1, 1, 1, 1));
            var c = new PixelCanvas(JarW, JarH);
            for (int y = 0; y < JarH; y++)
            for (int x = 0; x < JarW; x++)
            {
                if (!shape.Opaque(x, y)) continue;
                bool edge = !shape.Opaque(x - 1, y) || !shape.Opaque(x + 1, y) || !shape.Opaque(x, y - 1) || !shape.Opaque(x, y + 1);
                if (JarLip(x, y)) c.Set(x, y, upgraded ? S(Copper, y == 1 ? 1f : 0.5f, x, y) : (y == 1 ? Iron[4] : Iron[2]));
                else if (upgraded && (y == 14 || y == 26) && JarInterior(x, y)) c.Set(x, y, S(Copper, 0.6f - (x - 2) * 0.012f, x, y));
                else if (edge) c.Set(x, y, X(0xc8e8f8, 210));
            }
            c.Fill((x, y) => x == 5 && y >= JarBodyTop + 3 && y <= JarH - 8 && !(upgraded && (y == 14 || y == 26)), (x, y) => X(0xffffff, 150));
            c.Fill((x, y) => x == 6 && y >= JarBodyTop + 5 && y <= JarBodyTop + 9, (x, y) => X(0xffffff, 110));
            if (upgraded)   // a brass spigot low on the right: the vat drains itself
                c.Fill((x, y) => y >= 28 && y <= 30 && x >= 33 && x <= 35, (x, y) => S(Copper, y == 28 ? 0.9f : 0.5f, x, y));
            for (int y = 0; y < JarH; y++)
            for (int x = 0; x < JarW; x++)
                if (!shape.Opaque(x, y) && !c.Opaque(x, y)
                    && (shape.Opaque(x - 1, y) || shape.Opaque(x + 1, y) || shape.Opaque(x, y - 1) || shape.Opaque(x, y + 1)))
                    c.Set(x, y, K);
            return c.Bake(key, PPU, new Vector2(0.5f, 0f));
        }

        /// <summary>Steeping water in the jar up to row <paramref name="level"/> (<see cref="JarH"/> is empty).</summary>
        public static Sprite JarWater(int level)
        {
            level = Mathf.Clamp(level, 0, JarH);
            string key = "malt_jar_water_" + level;
            if (PixelCanvas.TryGet(key, out Sprite s)) return s;
            var c = new PixelCanvas(JarW, JarH);
            c.Fill((x, y) => y >= level && JarHollow(x, y), (x, y) =>
                y == level ? X(0xcfeaf6, 170) : X(0x5a9ac8, (byte)(80 + Mathf.Min(60, (y - level) * 3))));
            return c.Bake(key, PPU, new Vector2(0.5f, 0f));
        }

        // ---------------------------------------------------------- grain

        /// <summary>A barleycorn (<paramref name="husk"/>: an empty husk), with a rootlet <paramref name="sprout"/> 0..3 long.</summary>
        public static Sprite Grain(bool husk, int sprout)
        {
            sprout = Mathf.Clamp(sprout, 0, 3);
            string key = $"malt_grain_{(husk ? "h" : "g")}{sprout}";
            if (PixelCanvas.TryGet(key, out Sprite s)) return s;
            const int w = 11, h = 8;
            var ramp = husk ? Chaff : Barley;
            var c = new PixelCanvas(w, h);
            // The corn itself, in the middle.
            c.Fill((x, y) => PixelCanvas.InEllipse(x, y, 5.5f, 4f, husk ? 2.8f : 2.6f, husk ? 1.2f : 1.7f),
                (x, y) => S(ramp, 0.85f - (y - 2) * 0.18f - (x - 3) * 0.04f, x, y));
            // Rootlets: white hairs from the far end, longer as it germinates.
            if (!husk && sprout > 0)
            {
                var root = X(0xf4f0e2);
                for (int i = 0; i < sprout + 1; i++) c.Set(8 + Mathf.Min(i, 2), 4 + (i > 2 ? 1 : 0), root);
                if (sprout >= 2) { c.Set(8, 5, root); c.Set(9, 6, root); }
                if (sprout >= 3) { c.Set(2, 2, X(0xb8e07a)); c.Set(1, 1, X(0xb8e07a)); }   // the acrospire's green tip
            }
            c.Outline(K);
            return c.Bake(key, PPU, new Vector2(0.5f, 0.5f));
        }

        // ----------------------------------------------------------- the sack

        /// <summary>A burlap sack of barley on a hook, its mouth open and tipped to the right; pivot at the hook.</summary>
        public static Sprite Sack()
        {
            const string key = "malt_sack";
            if (PixelCanvas.TryGet(key, out Sprite s)) return s;
            const int w = 26, h = 30;
            var c = new PixelCanvas(w, h);
            // Rope from the hook.
            c.Fill((x, y) => x == 12 && y < 5, (x, y) => S(Wood, 0.6f, x, y));
            // The body, fat at the bottom, tied at the top.
            c.Fill((x, y) => y >= 5 && PixelCanvas.InEllipse(x, y, 12f, 18f, 9.5f + (y - 5) * 0.12f, 12f),
                (x, y) =>
                {
                    float light = 0.75f - (x - 4) * 0.035f - (y - 8) * 0.012f;
                    bool weave = (x + y) % 4 == 0 || (x - y + 40) % 5 == 0;
                    return S(Burlap, light - (weave ? 0.12f : 0f), x, y);
                });
            // The open mouth at the lower right, showing the grain.
            c.Fill((x, y) => PixelCanvas.InEllipse(x, y, 20f, 23f, 4f, 3f), (x, y) =>
                PixelCanvas.InEllipse(x, y, 20f, 23f, 2.8f, 2f) ? S(Barley, 0.6f + (x - 18) * 0.06f, x, y) : S(Burlap, 0.95f, x, y));
            c.Fill((x, y) => y >= 5 && y <= 6 && x >= 9 && x <= 15, (x, y) => S(Wood, 0.45f, x, y));   // the tie
            c.Outline(K);
            return c.Bake(key, PPU, new Vector2(12.5f / w, 1f));
        }

        /// <summary>
        /// A tall wicker basket with a handle. The Turning Vat's malt now goes to the kiln in
        /// <see cref="SmallBasket"/>; this one stays as the measure the small one is drawn against. Drawn to the sack's size and pivot (the handle's top), so it
        /// stands where the sack stood.
        /// </summary>
        public static Sprite Basket()
        {
            const string key = "malt_basket";
            if (PixelCanvas.TryGet(key, out Sprite s)) return s;
            const int w = 26, h = 30;
            var c = new PixelCanvas(w, h);
            // The handle, an arch of cane over the mouth.
            c.Fill((x, y) => y <= 12 && PixelCanvas.InEllipse(x, y, 12.5f, 12f, 9.5f, 11.5f) && !PixelCanvas.InEllipse(x, y, 12.5f, 12f, 8f, 10f),
                (x, y) => S(Wood, 0.75f, x, y));
            // The body: wider at the rim, woven in rows.
            c.Fill((x, y) => y >= 12 && y <= h - 2 && Mathf.Abs(x + 0.5f - 12.5f) <= 11.5f - (y - 12) * 0.2f,
                (x, y) =>
                {
                    bool row = (y - 12) % 3 == 0;
                    bool weave = ((x + (y / 3) * 2) % 4) < 2;
                    return S(Burlap, (row ? 0.95f : weave ? 0.7f : 0.5f) - (x - 2) * 0.012f, x, y);
                });
            // Green malt heaped at the rim.
            c.Fill((x, y) => y >= 10 && y <= 12 && PixelCanvas.InEllipse(x, y, 12.5f, 12.5f, 9f, 2.5f),
                (x, y) => S(Barley, 0.55f + ((x * 7 + y * 3) % 5) * 0.06f, x, y));
            c.Fill((x, y) => y >= 12 && y <= 13 && x >= 1 && x <= w - 2, (x, y) => S(Wood, 0.55f, x, y));   // the rim
            c.Outline(K);
            return c.Bake(key, PPU, new Vector2(12.5f / w, 1f));
        }

        /// <summary>
        /// The Draught Kiln's log conveyor: a dark belt over a rail, with a roller at each end,
        /// <paramref name="lengthPx"/> long. The belt's ridges step along with
        /// <paramref name="frame"/> (0-3), right to left, toward the fire. Pivot at the left end, mid-height.
        /// </summary>
        public static Sprite Conveyor(int lengthPx, int frame)
        {
            lengthPx = Mathf.Clamp(lengthPx, 12, 160);
            frame = ((frame % 4) + 4) % 4;
            string key = $"kiln_conveyor_{lengthPx}_{frame}";
            if (PixelCanvas.TryGet(key, out Sprite s)) return s;
            int w = lengthPx, h = 8;
            var c = new PixelCanvas(w, h);
            // The rail under the belt, and two legs.
            c.Fill((x, y) => y >= 5 && y <= 6 && x >= 3 && x <= w - 4, (x, y) => S(Iron, 0.45f, x, y));
            c.Fill((x, y) => y == 7 && (x == w / 3 || x == (2 * w) / 3), (x, y) => S(Iron, 0.35f, x, y));
            // The belt: dark rubbered canvas, its ridges marching left.
            c.Fill((x, y) => y >= 1 && y <= 4 && x >= 3 && x <= w - 4, (x, y) =>
            {
                bool ridge = y == 1 && ((x + frame) % 4) == 0;
                return S(Iron, ridge ? 0.75f : y == 1 ? 0.3f : 0.18f, x, y);
            });
            // A roller at each end, its spoke turning with the frame.
            foreach (int cx in new[] { 3, w - 4 })
            {
                c.Fill((x, y) => PixelCanvas.InEllipse(x, y, cx + 0.5f, 3.5f, 3f, 3f), (x, y) =>
                    S(Copper, PixelCanvas.SphereLight(x, y, cx + 0.5f, 3.5f, 3f, 3f, 0.3f), x, y));
                int sx = cx + (frame == 1 ? 1 : frame == 3 ? -1 : 0), sy = 3 + (frame == 0 ? -1 : frame == 2 ? 1 : 0);
                c.Fill((x, y) => x == sx && y == sy, (x, y) => S(Iron, 0.2f, x, y));
            }
            c.Outline(K);
            return c.Bake(key, PPU, new Vector2(0f, 0.5f));
        }

        /// <summary>
        /// A small, low wicker basket of sprouted malt, the Turning Vat's: the vat turns the
        /// malt out into it for the kiln. On the tall basket's canvas and pivot, drawn at the
        /// foot of it, so it stands where the tall one stood; a short loop of cane for a handle.
        /// </summary>
        public static Sprite SmallBasket()
        {
            const string key = "malt_basket_small";
            if (PixelCanvas.TryGet(key, out Sprite s)) return s;
            const int w = 26, h = 30;
            var c = new PixelCanvas(w, h);
            // A short loop of cane over the heap.
            c.Fill((x, y) => y >= 15 && y <= 19 && PixelCanvas.InEllipse(x, y, 12.5f, 19.5f, 5.5f, 4.5f) && !PixelCanvas.InEllipse(x, y, 12.5f, 19.5f, 4.2f, 3.4f),
                (x, y) => S(Wood, 0.8f, x, y));
            // The body: low and round-bellied, woven in rows, a little wider at the rim.
            c.Fill((x, y) => y >= 21 && y <= h - 2 && Mathf.Abs(x + 0.5f - 12.5f) <= 9.5f - (y - 21) * 0.35f,
                (x, y) =>
                {
                    bool row = (y - 21) % 2 == 0;
                    bool weave = ((x + y) % 3) == 0;
                    return S(Burlap, (row ? 0.9f : weave ? 0.62f : 0.48f) - (x - 4) * 0.012f, x, y);
                });
            // Green malt heaped above the rim, rootlets showing.
            c.Fill((x, y) => y >= 18 && y <= 21 && PixelCanvas.InEllipse(x, y, 12.5f, 21f, 8.5f, 3f), (x, y) =>
            {
                int hash = (x * 7 + y * 13) % 9;
                return hash < 3 ? S(Sprout, 0.55f + (y < 20 ? 0.3f : 0f), x, y) : S(Barley, 0.6f + ((x + y) % 3) * 0.08f, x, y);
            });
            c.Fill((x, y) => y == 21 && x >= 3 && x <= w - 4, (x, y) => S(Wood, 0.6f, x, y));   // the rim
            c.Outline(K);
            return c.Bake(key, PPU, new Vector2(12.5f / w, 1f));
        }

        /// <summary>
        /// A squat slatted crate heaped with sprouted malt, rootlets showing: where green
        /// malt waits for the kiln before the Turning Vat. Drawn on the sack's canvas and
        /// pivot, so it stands where a sack stood; but not a sack, and not the wicker basket.
        /// </summary>
        public static Sprite MaltCrate()
        {
            const string key = "malt_crate";
            if (PixelCanvas.TryGet(key, out Sprite s)) return s;
            const int w = 26, h = 30;
            var c = new PixelCanvas(w, h);
            // The crate: planks across, posts at the corners, a rim.
            c.Fill((x, y) => y >= 14 && y <= h - 2 && x >= 1 && x <= w - 2, (x, y) =>
            {
                bool post = x <= 3 || x >= w - 4;
                bool seam = (y - 14) % 5 == 4;
                float light = post ? 0.42f : 0.8f - (x - 4) * 0.008f;
                return S(Wood, seam ? light - 0.3f : light, x, y);
            });
            c.Fill((x, y) => y >= 13 && y <= 14 && x >= 0 && x <= w - 1, (x, y) => S(Wood, 0.95f, x, y));   // the rim
            c.Fill((x, y) => (x == 2 || x == w - 3) && (y == 17 || y == 25), (x, y) => S(Iron, 0.7f, x, y));   // nails
            // The heap: pale malt, with the green of its rootlets through it and over it.
            c.Fill((x, y) => PixelCanvas.InEllipse(x, y, 12.5f, 12f, 11f, 6.5f) && y <= 14, (x, y) =>
            {
                int hash = (x * 7 + y * 13) % 11;
                if (hash < 3) return S(Sprout, 0.35f + (y < 10 ? 0.45f : 0.1f), x, y);
                return S(Barley, 0.55f + ((x + y) % 4) * 0.08f - (x - 3) * 0.01f, x, y);
            });
            for (int i = 0; i < 6; i++)   // rootlets curling over the rim
            {
                int rx = 4 + i * 3 + (i % 2);
                c.Fill((x, y) => x == rx && y >= 12 && y <= 15, (x, y) => S(Sprout, y == 12 ? 1f : 0.55f, x, y));
            }
            c.Outline(K);
            return c.Bake(key, PPU, new Vector2(12.5f / w, 1f));
        }

        // ----------------------------------------------------------- the kiln

        public const int KilnW = 44, KilnH = 40;
        public const int KilnTrayY = 10;
        public const int ArchX0 = 14, ArchX1 = 29, ArchTop = 26, ArchBottom = 37;

        private static bool KilnBody(int x, int y) => x >= 4 && x <= 39 && y >= 12 && y <= KilnH - 1;
        private static bool Arch(int x, int y) =>
            x >= ArchX0 && x <= ArchX1 && y <= ArchBottom
            && (y >= ArchTop + 3 || PixelCanvas.InEllipse(x, y, (ArchX0 + ArchX1 + 1) * 0.5f, ArchTop + 3, (ArchX1 - ArchX0 + 1) * 0.5f, 3.5f));

        /// <summary>
        /// The kiln: a brick firebox under a perforated iron drying tray, with a chimney
        /// at the back. The draught kiln is rebuilt so it reads as a different machine: a
        /// taller copper-clad flue under a wide draught cowl, copper corner posts and a
        /// riveted band round the brickwork, and a heat dial on its face. The tray and
        /// the firebox arch stay where they were: the bench's physics depends on them.
        /// </summary>
        public static Sprite Kiln(bool upgraded = false)
        {
            string key = upgraded ? "malt_kiln_draught" : "malt_kiln";
            if (PixelCanvas.TryGet(key, out Sprite s)) return s;
            var c = new PixelCanvas(KilnW, KilnH);
            int chimTop = upgraded ? 0 : 3;
            c.Fill((x, y) => x >= 33 && x <= 38 && y >= chimTop && y < 12, (x, y) =>
                upgraded ? S(Copper, (y % 4 == 3 ? 0.3f : 0.8f) - (x - 33) * 0.08f, x, y)
                    : S(Brick, 0.55f - (x - 33) * 0.06f + (y % 3 == 0 ? -0.15f : 0f), x, y));
            if (upgraded)   // the draught cowl: a wide copper hat on the flue
                c.Fill((x, y) => y <= 1 && x >= 30 && x <= 41, (x, y) => S(Copper, y == 0 ? 0.95f : 0.45f, x, y));
            // Brickwork: courses of 6 x 3, offset every other course.
            c.Fill(KilnBody, (x, y) =>
            {
                int course = (y - 12) / 3;
                bool mortar = (y - 12) % 3 == 0 || (x + (course % 2) * 3) % 6 == 0;
                float light = 0.7f - (x - 4) * 0.012f - (y - 12) * 0.006f;
                return mortar ? S(Brick, 0.1f, x, y) : S(Brick, light + PixelCanvas.Hash(x / 6, course, 5) * 0.15f, x, y);
            });
            // The firebox arch (dark; the bench draws the fire into it).
            c.Fill(Arch, (x, y) => X(0x140c0a));
            // The tray: an iron plate with holes, and lips at either end.
            c.Fill((x, y) => y >= KilnTrayY && y <= KilnTrayY + 1 && x >= 2 && x <= 41, (x, y) =>
                y == KilnTrayY && x % 3 == 0 ? X(0x140c0a) : S(Iron, y == KilnTrayY ? 0.75f : 0.4f, x, y));
            c.Fill((x, y) => y >= KilnTrayY - 3 && y < KilnTrayY && (x == 2 || x == 41), (x, y) => S(Iron, 0.6f, x, y));
            if (upgraded)
            {
                // Copper corner posts, and a riveted band round the brickwork.
                c.Fill((x, y) => y >= 12 && (x == 4 || x == 5 || x == 38 || x == 39), (x, y) =>
                    S(Copper, x == 5 || x == 39 ? 0.45f : 0.85f, x, y));
                c.Fill((x, y) => x >= 4 && x <= 39 && (y == 22 || y == 23), (x, y) =>
                    y == 22 && x % 4 == 1 ? X(0xf0c080) : S(Copper, y == 22 ? 0.8f : 0.4f, x, y));
                // The heat dial: a copper rim, a cream face, a red needle in the band.
                c.Fill((x, y) => PixelCanvas.InEllipse(x, y, 11.5f, 17.5f, 3.6f, 3.6f), (x, y) =>
                    PixelCanvas.InEllipse(x, y, 11.5f, 17.5f, 2.4f, 2.4f)
                        ? ((x == 12 && y >= 15 && y <= 17) || (x == 11 && y == 17) ? X(0xd64550) : X(0xe8e0d0))
                        : S(Copper, 0.75f, x, y));
            }
            c.Outline(K);
            return c.Bake(key, PPU, new Vector2(0.5f, 0f));
        }

        /// <summary>The steeping jar's wooden lid, with a knob; pivot at its underside centre.</summary>
        public static Sprite JarLid()
        {
            const string key = "malt_jar_lid";
            if (PixelCanvas.TryGet(key, out Sprite s)) return s;
            const int w = 32, h = 7;
            var c = new PixelCanvas(w, h);
            c.Fill((x, y) => y >= 3 && y <= 5 && x >= 1 && x <= w - 2, (x, y) =>
                S(Wood, y == 3 ? 0.85f : 0.45f - (x - 1) * 0.004f, x, y));
            c.Fill((x, y) => y >= 1 && y <= 2 && x >= w / 2 - 3 && x <= w / 2 + 2, (x, y) => S(Wood, y == 1 ? 0.9f : 0.6f, x, y));
            c.Outline(K);
            return c.Bake(key, PPU, new Vector2(0.5f, 1f / h));
        }

        /// <summary>A split log: bark along its length, rings at the cut end.</summary>
        public static Sprite Log()
        {
            const string key = "malt_log";
            if (PixelCanvas.TryGet(key, out Sprite s)) return s;
            const int w = 12, h = 6;
            var c = new PixelCanvas(w, h);
            c.Fill((x, y) => y >= 1 && y <= 4 && x >= 1 && x <= 10, (x, y) =>
                x >= 9 ? (PixelCanvas.InEllipse(x, y, 9.5f, 2.5f, 1.4f, 1.4f) ? X(0xc89a64) : X(0x8a5f36))
                    : S(Wood, 0.55f - (y - 1) * 0.1f + (x % 3 == 0 ? -0.1f : 0f), x, y));
            c.Outline(K);
            return c.Bake(key, PPU, new Vector2(0.5f, 0.5f));
        }

        /// <summary>A little pyramid of logs for the kiln; pivot at its base.</summary>
        public static Sprite LogPile()
        {
            const string key = "malt_logpile";
            if (PixelCanvas.TryGet(key, out Sprite s)) return s;
            const int w = 26, h = 14;
            var c = new PixelCanvas(w, h);
            void LogAt(int lx, int ly) =>
                c.Fill((x, y) => y >= ly && y <= ly + 3 && x >= lx && x <= lx + 10, (x, y) =>
                    x >= lx + 9 ? (PixelCanvas.InEllipse(x, y, lx + 9.5f, ly + 1.5f, 1.4f, 1.4f) ? X(0xc89a64) : X(0x8a5f36))
                        : S(Wood, 0.55f - (y - ly) * 0.1f, x, y));
            LogAt(1, 9); LogAt(13, 9); LogAt(7, 5);
            c.Outline(K);
            return c.Bake(key, PPU, new Vector2(0.5f, 0f));
        }
    }
}
