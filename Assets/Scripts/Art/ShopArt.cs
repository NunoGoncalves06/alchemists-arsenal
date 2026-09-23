using UnityEngine;
using AlchemistsArsenal.Combat;

namespace AlchemistsArsenal.Art
{
    /// <summary>
    /// The shop's geometric props, drawn with <see cref="PixelCanvas"/>: the new
    /// cauldron (split into the parts behind and in front of its contents), its
    /// liquid, swirl and fire; the mortar and pestle; the glass flask, ladle and
    /// cork; the bench and the wall.
    ///
    /// <para><b>The cauldron.</b> The old one was a 28-px pot whose "opening" was a
    /// 22x2 strip seen edge-on, narrower than the belly and sitting above a band of
    /// rim, so the brew read as a slot in the side of the pot. This one is seen
    /// from above at three-quarters: a flared rim, and a mouth that is an ellipse
    /// about 80% of the belly's width, so the liquid surface — and everything
    /// floating in it — is actually visible. It is drawn once as a whole (so the
    /// outline is continuous) and then split at the rim's centre line: the upper
    /// half and the inside back wall draw behind the herbs, the lower half of the
    /// rim and the body draw in front of them, which is what hides the part of a
    /// floating herb that is "below the lip".</para>
    ///
    /// Every sprite here shares its canvas size and pivot with its siblings, so the
    /// layers line up by construction.
    /// </summary>
    public static class ShopArt
    {
        public const float PPU = 16f;

        // Canvas geometry for the pot family (top-left origin, y down).
        public const int PotW = 60, PotH = 52;
        public const float MouthCX = 30f, MouthCY = 14.3f, MouthRX = 21.5f, MouthRY = 5.0f;
        public const float LiquidCY = 14.9f, LiquidRX = 20.5f, LiquidRY = 4.2f;
        private const float RimRX = 27f, RimRY = 7.4f, RimCY = 14f;
        private const float BodyCX = 30f, BodyCY = 29f, BodyRX = 26f, BodyRY = 19f;

        private static readonly Color32 K = PixelCanvas.Ink;
        private static readonly Color32[] Iron =
        {
            PixelCanvas.Hex(0x2a2233), PixelCanvas.Hex(0x3f354c), PixelCanvas.Hex(0x594b69),
            PixelCanvas.Hex(0x7a6b8c), PixelCanvas.Hex(0x9d8fb0),
        };
        private static readonly Color32[] Stone =
        {
            PixelCanvas.Hex(0x3a3d4a), PixelCanvas.Hex(0x565a69), PixelCanvas.Hex(0x767b8c),
            PixelCanvas.Hex(0x9aa0b1), PixelCanvas.Hex(0xc2c6d4),
        };
        private static readonly Color32[] Wood =
        {
            PixelCanvas.Hex(0x2e1f14), PixelCanvas.Hex(0x3f2c1c), PixelCanvas.Hex(0x5a3f28),
            PixelCanvas.Hex(0x6b4a2f), PixelCanvas.Hex(0x8a6340),
        };

        /// <summary>(dark, mid, bright, highlight) for a brew of <paramref name="e"/>.</summary>
        public static Color32[] BrewRamp(ElementType e) => e switch
        {
            ElementType.Fire => new[] { PixelCanvas.Hex(0x7a2418), PixelCanvas.Hex(0xb03c2a), PixelCanvas.Hex(0xe2683a), PixelCanvas.Hex(0xf6c04a) },
            ElementType.Water => new[] { PixelCanvas.Hex(0x173a5a), PixelCanvas.Hex(0x255b86), PixelCanvas.Hex(0x3f8fd0), PixelCanvas.Hex(0x9fd4f0) },
            ElementType.Poison => new[] { PixelCanvas.Hex(0x434a12), PixelCanvas.Hex(0x6b761f), PixelCanvas.Hex(0xb6c33f), PixelCanvas.Hex(0xe0ea7c) },
            ElementType.Arcane => new[] { PixelCanvas.Hex(0x3a1c4c), PixelCanvas.Hex(0x5a2f74), PixelCanvas.Hex(0x9a4a9e), PixelCanvas.Hex(0xe07ad0) },
            _ => new[] { PixelCanvas.Hex(0x234f1c), PixelCanvas.Hex(0x3a7a2e), PixelCanvas.Hex(0x5ea637), PixelCanvas.Hex(0x93d64c) },
        };

        // ------------------------------------------------------------------ cauldron

        private static bool Mouth(int x, int y) => PixelCanvas.InEllipse(x, y, MouthCX, MouthCY, MouthRX, MouthRY);
        private static bool RimOuter(int x, int y) => PixelCanvas.InEllipse(x, y, 30f, RimCY, RimRX, RimRY);
        private static bool Body(int x, int y) => y >= RimCY && PixelCanvas.InEllipse(x, y, BodyCX, BodyCY, BodyRX, BodyRY);

        private static bool Leg(int x, int y)
        {
            if (y < 44 || y > 51) return false;
            int taper = (y - 44) / 3;          // stubby, slightly tapered feet
            foreach (int cx in new[] { 13, 30, 47 })
                if (x >= cx - 3 + taper && x <= cx + 2 - taper) return true;
            return false;
        }

        private static bool HandleRing(int x, int y, out bool top)
        {
            top = false;
            foreach (float cx in new[] { 3.6f, 56.4f })
            {
                bool outer = PixelCanvas.InEllipse(x, y, cx, 18f, 3.6f, 4.2f);
                bool inner = PixelCanvas.InEllipse(x, y, cx, 18.4f, 1.7f, 2.2f);
                if (outer && !inner) { top = y < 17; return true; }
            }
            return false;
        }

        private static PixelCanvas _pot;

        /// <summary>The whole pot, outlined once; split by <see cref="PotBack"/>/<see cref="PotFront"/>.</summary>
        private static PixelCanvas WholePot()
        {
            if (_pot != null) return _pot;
            var c = new PixelCanvas(PotW, PotH);

            // legs first, so the belly's outline sits over their tops
            c.Fill(Leg, (x, y) => PixelCanvas.Shade(Iron, 0.25f + 0.15f * ((x % 17) / 17f), x, y));

            // the belly: an iron sphere lit from the upper left, sooty underneath,
            // with two riveted bands that sag toward the viewer
            c.Fill((x, y) => Body(x, y) && !Mouth(x, y), (x, y) =>
            {
                float light = PixelCanvas.SphereLight(x, y, BodyCX, BodyCY, BodyRX, BodyRY, 0.12f);
                if (y > 40) light *= Mathf.Lerp(1f, 0.55f, (y - 40) / 9f);
                float u = (x + 0.5f - BodyCX) / BodyRX;
                float sag = 2.6f * (1f - u * u);
                foreach (float band in new[] { 24f, 37f })
                {
                    float by = band + sag;
                    if (y >= by - 0.5f && y <= by + 1.5f)
                    {
                        bool rivet = y < by + 0.5f && ((x + 3) % 7 == 0) && Mathf.Abs(u) < 0.92f;
                        return rivet ? Iron[4] : PixelCanvas.Shade(Iron, light * 0.55f, x, y);
                    }
                }
                return PixelCanvas.Shade(Iron, light, x, y);
            });

            // the rim: a flat lip lit on top, brightest at the back-left
            c.Fill((x, y) => RimOuter(x, y) && !Mouth(x, y), (x, y) =>
            {
                float light = PixelCanvas.SphereLight(x, y, 30f, RimCY - 3f, RimRX, RimRY * 2.4f, 0.35f);
                bool frontEdge = y >= RimCY && !RimOuter(x, y + 1);
                if (frontEdge) light *= 0.55f;       // the outer face of the lip, facing us
                return PixelCanvas.Shade(Iron, light, x, y);
            });

            c.Fill((x, y) => HandleRing(x, y, out _), (x, y) =>
            {
                HandleRing(x, y, out bool top);
                return top ? Iron[3] : Iron[1];
            });

            c.Outline(K);

            // inside the mouth: the far wall, dark, catching a little light at its lip
            c.Fill((x, y) => Mouth(x, y) && y < LiquidCY, (x, y) =>
            {
                float t = Mathf.InverseLerp(MouthCY - MouthRY, LiquidCY, y);
                return t < 0.18f ? Iron[1] : PixelCanvas.Shade(new[] { K, Iron[0] }, 1f - t, x, y);
            });
            _pot = c;
            return c;
        }

        /// <summary>Upper rim and inside back wall: draws BEHIND the liquid and herbs.</summary>
        public static Sprite PotBack() => Split("pot_back", back: true);

        /// <summary>Lower rim, belly, legs, handles: draws IN FRONT of the herbs.</summary>
        public static Sprite PotFront() => Split("pot_front", back: false);

        private static Sprite Split(string key, bool back)
        {
            if (PixelCanvas.TryGet(key, out Sprite s)) return s;
            PixelCanvas whole = WholePot();
            var c = new PixelCanvas(PotW, PotH);
            for (int y = 0; y < PotH; y++)
            for (int x = 0; x < PotW; x++)
            {
                bool isFront = y >= RimCY && !Mouth(x, y);
                if (isFront != back) c.Set(x, y, whole.Get(x, y));
            }
            return c.Bake(key, PPU, PotPivot);
        }

        /// <summary>Every pot-family sprite pivots at the canvas centre.</summary>
        public static Vector2 PotPivot => new Vector2(0.5f, 0.5f);

        /// <summary>Mouth centre, relative to the pot pivot, in world units at scale 1.</summary>
        public static Vector2 MouthOffset => new Vector2((MouthCX - PotW * 0.5f) / PPU, (PotH * 0.5f - LiquidCY) / PPU);

        /// <summary>The liquid surface, in the brew's colours.</summary>
        public static Sprite Liquid(ElementType e)
        {
            string key = "liquid_" + e;
            if (PixelCanvas.TryGet(key, out Sprite s)) return s;
            Color32[] ramp = BrewRamp(e);
            var c = new PixelCanvas(PotW, PotH);
            c.Fill((x, y) => PixelCanvas.InEllipse(x, y, MouthCX, LiquidCY, LiquidRX, LiquidRY), (x, y) =>
            {
                float nx = (x + 0.5f - MouthCX) / LiquidRX, ny = (y + 0.5f - LiquidCY) / LiquidRY;
                float edge = nx * nx + ny * ny;
                // darker toward the rim (shadow of the lip), a sheen at the back-left
                float light = 0.62f - 0.4f * edge;
                if (ny < -0.2f && nx < 0.1f && edge > 0.35f && edge < 0.75f) light += 0.35f;
                return PixelCanvas.Shade(ramp, light, x, y);
            });
            return c.Bake(key, PPU, PotPivot);
        }

        /// <summary>A three-armed spiral, white, drawn in circle space and squashed onto the surface.</summary>
        public static Sprite Swirl()
        {
            const string key = "swirl";
            if (PixelCanvas.TryGet(key, out Sprite s)) return s;
            const int n = 48;
            var c = new PixelCanvas(n, n);
            c.Fill((x, y) =>
            {
                float dx = x + 0.5f - n / 2f, dy = y + 0.5f - n / 2f;
                float r = Mathf.Sqrt(dx * dx + dy * dy) / (n / 2f);
                if (r > 0.96f || r < 0.08f) return false;
                float a = Mathf.Atan2(dy, dx);
                return Mathf.Cos(3f * (a - r * 5.2f)) > 0.72f;
            }, (x, y) =>
            {
                float dx = x + 0.5f - n / 2f, dy = y + 0.5f - n / 2f;
                float r = Mathf.Sqrt(dx * dx + dy * dy) / (n / 2f);
                return new Color32(255, 255, 255, (byte)(200 * (1f - r * 0.7f)));
            });
            return c.Bake(key, n / 2f, new Vector2(0.5f, 0.5f));   // 2 world units across at scale 1
        }

        /// <summary>One of three flame frames for under the pot.</summary>
        public static Sprite Fire(int frame)
        {
            frame = Mathf.Abs(frame) % 3;
            string key = "fire_" + frame;
            if (PixelCanvas.TryGet(key, out Sprite s)) return s;
            const int w = 30, h = 14;
            var yellow = PixelCanvas.Hex(0xf6c04a); var orange = PixelCanvas.Hex(0xe2683a);
            var red = PixelCanvas.Hex(0xb03c2a); var core = PixelCanvas.Hex(0xfff0b8);
            var c = new PixelCanvas(w, h);
            for (int x = 0; x < w; x++)
            {
                float envelope = 1f - Mathf.Abs(x - w * 0.5f + 0.5f) / (w * 0.5f);
                float flick = Mathf.Abs(Mathf.Sin(x * 0.72f + frame * 2.1f)) * 0.6f
                              + Mathf.Abs(Mathf.Sin(x * 0.31f - frame * 1.3f)) * 0.4f;
                int height = Mathf.RoundToInt((3f + 10f * flick) * Mathf.Sqrt(Mathf.Max(0f, envelope)));
                for (int k = 0; k < height; k++)
                {
                    float t = k / (float)Mathf.Max(1, height);
                    int y = h - 1 - k;
                    Color32 col = t < 0.2f && envelope > 0.4f ? core : t < 0.45f ? yellow : t < 0.78f ? orange : red;
                    c.Set(x, y, col);
                }
            }
            return c.Bake(key, PPU, new Vector2(0.5f, 0f));
        }

        // ----------------------------------------------------------------- mortar

        public const int MortarW = 36, MortarH = 22;
        private const float MortarRimY = 5f;

        private static bool MortarBody(int x, int y) =>
            y >= MortarRimY && PixelCanvas.InEllipse(x, y, 18f, MortarRimY, 17f, 16.5f);
        private static bool MortarRim(int x, int y) => PixelCanvas.InEllipse(x, y, 18f, MortarRimY, 17f, 3.6f);
        private static bool MortarCavity(int x, int y) => PixelCanvas.InEllipse(x, y, 18f, MortarRimY + 0.4f, 13.5f, 2.6f);

        private static PixelCanvas _mortar;

        private static PixelCanvas WholeMortar()
        {
            if (_mortar != null) return _mortar;
            var c = new PixelCanvas(MortarW, MortarH);
            c.Fill((x, y) => MortarBody(x, y) && y < MortarH - 1, (x, y) =>
                PixelCanvas.Shade(Stone, PixelCanvas.SphereLight(x, y, 18f, MortarRimY + 2f, 17f, 16f, 0.18f), x, y));
            c.Fill((x, y) => MortarRim(x, y) && !MortarCavity(x, y), (x, y) =>
                PixelCanvas.Shade(Stone, PixelCanvas.SphereLight(x, y, 18f, MortarRimY - 2f, 17f, 7f, 0.4f), x, y));
            c.Fill(MortarCavity, (x, y) => y < MortarRimY ? Stone[0] : K);
            c.Outline(K);
            _mortar = c;
            return c;
        }

        /// <summary>The far half of the rim and the hollow: draws behind whatever is in the bowl.</summary>
        public static Sprite MortarBack() => MortarPart("mortar_back", back: true);

        /// <summary>The near lip and the whole belly: draws over whatever is in the bowl.</summary>
        public static Sprite MortarFront() => MortarPart("mortar_front", back: false);

        /// <summary>
        /// Height of the hollow's middle line above the mortar's base, in its unscaled
        /// local units. What is in the bowl shows above this line; the near lip hides
        /// it below.
        /// </summary>
        public const float MortarLipLocalY = (MortarH - (MortarRimY + 0.4f)) / PPU;

        private static Sprite MortarPart(string key, bool back)
        {
            if (PixelCanvas.TryGet(key, out Sprite s)) return s;
            PixelCanvas whole = WholeMortar();
            var c = new PixelCanvas(MortarW, MortarH);
            for (int y = 0; y < MortarH; y++)
            for (int x = 0; x < MortarW; x++)
            {
                // A three-quarter bowl: the hollow and the far rim are behind the
                // contents, and everything nearer than the hollow's middle line (the
                // near lip, the belly) is in front of them. A fixed row split used to
                // leave the upper belly behind the leaves, so they were drawn over it.
                bool isFront = !MortarCavity(x, y) && y + 0.5f > MortarRimY + 0.4f;
                if (isFront != back) c.Set(x, y, whole.Get(x, y));
            }
            return c.Bake(key, PPU, new Vector2(0.5f, 0f));
        }

        /// <summary>A chunky gold arrow pointing down: "press here".</summary>
        public static Sprite PressArrow()
        {
            const string key = "press_arrow";
            if (PixelCanvas.TryGet(key, out Sprite s)) return s;
            const int w = 13, h = 14;
            var gold = new[] { PixelCanvas.Hex(0xb07a26), PixelCanvas.Hex(0xe8b64c), PixelCanvas.Hex(0xffe39a) };
            var c = new PixelCanvas(w, h);
            // shaft, then a head that widens toward its point at the bottom
            c.Fill((x, y) => y >= 1 && y <= 6 && x >= 4 && x <= 8, (x, y) => x == 4 ? gold[2] : gold[1]);
            c.Fill((x, y) => y >= 7 && y <= 12 && Mathf.Abs(x - 6) <= 12 - y, (x, y) =>
                Mathf.Abs(x - 6) == 12 - y ? gold[0] : x < 6 ? gold[2] : gold[1]);
            c.Outline(K);
            return c.Bake(key, PPU, new Vector2(0.5f, 0f));
        }

        /// <summary>A gold ring the shape of the mortar's mouth, drawn on it to say "here".</summary>
        public static Sprite PressRing()
        {
            const string key = "press_ring";
            if (PixelCanvas.TryGet(key, out Sprite s)) return s;
            const int w = 32, h = 10;
            var c = new PixelCanvas(w, h);
            c.Fill((x, y) => PixelCanvas.InEllipse(x, y, w * 0.5f, h * 0.5f, w * 0.5f - 0.5f, h * 0.5f - 0.5f)
                             && !PixelCanvas.InEllipse(x, y, w * 0.5f, h * 0.5f, w * 0.5f - 2.5f, h * 0.5f - 2.2f),
                (x, y) => new Color32(0xff, 0xd9, 0x7a, 220));
            return c.Bake(key, PPU, new Vector2(0.5f, 0.5f));
        }

        /// <summary>A stone-headed, wooden-handled pestle, head down.</summary>
        public static Sprite Pestle()
        {
            const string key = "pestle";
            if (PixelCanvas.TryGet(key, out Sprite s)) return s;
            const int w = 9, h = 30;
            var c = new PixelCanvas(w, h);
            c.Fill((x, y) => y >= 18 && PixelCanvas.InEllipse(x, y, 4.5f, 23f, 4.4f, 6.8f), (x, y) =>
                PixelCanvas.Shade(Stone, PixelCanvas.SphereLight(x, y, 4.5f, 23f, 4.4f, 6.8f, 0.2f), x, y));
            c.Fill((x, y) => y < 19 && y > 1 && x >= 3 && x <= 5, (x, y) => x == 3 ? Wood[4] : x == 4 ? Wood[3] : Wood[1]);
            c.Fill((x, y) => PixelCanvas.InEllipse(x, y, 4.5f, 2f, 2.2f, 2f), (x, y) => y < 2 ? Wood[4] : Wood[2]);
            c.Outline(K);
            return c.Bake(key, PPU, new Vector2(0.5f, 0.5f));
        }

        // -------------------------------------------------------------- bottling

        public const int FlaskW = 30, FlaskH = 38;
        public const float FlaskCX = 15f, FlaskBodyCY = 25f, FlaskBodyR = 11.5f;
        public const int NeckX0 = 11, NeckX1 = 18, NeckY0 = 5, NeckY1 = 15;

        private static bool FlaskInterior(int x, int y) =>
            PixelCanvas.InEllipse(x, y, FlaskCX, FlaskBodyCY, FlaskBodyR, FlaskBodyR)
            || (x >= NeckX0 && x <= NeckX1 && y >= NeckY0 && y <= NeckY1);

        private static bool FlaskLip(int x, int y) => y >= 2 && y <= 4 && x >= 9 && x <= 20;

        /// <summary>The glass's back: a faint tint behind the liquid.</summary>
        public static Sprite FlaskBack()
        {
            const string key = "flask_back";
            if (PixelCanvas.TryGet(key, out Sprite s)) return s;
            var c = new PixelCanvas(FlaskW, FlaskH);
            c.Fill(FlaskInterior, (x, y) => new Color32(0x9f, 0xd4, 0xf0, 38));
            return c.Bake(key, PPU, new Vector2(0.5f, 0f));
        }

        /// <summary>The inside of the glass the brew can fill: the interior less its 1-px glass edge.</summary>
        private static bool FlaskHollow(int x, int y) =>
            FlaskInterior(x, y) && FlaskInterior(x - 1, y) && FlaskInterior(x + 1, y)
            && FlaskInterior(x, y - 1) && FlaskInterior(x, y + 1);

        private static int[] _hollowRow;

        private static int HollowRow(int y)
        {
            if (_hollowRow == null)
            {
                _hollowRow = new int[FlaskH];
                for (int yy = 0; yy < FlaskH; yy++)
                for (int x = 0; x < FlaskW; x++)
                    if (FlaskHollow(x, yy)) _hollowRow[yy]++;
            }
            return y >= 0 && y < FlaskH ? _hollowRow[y] : 0;
        }

        /// <summary>The bulb's volume "to the line" in square art pixels: what a fill of 1 means.</summary>
        public static float FlaskBulbVolumePx => Mathf.PI * (FlaskBodyR - 1f) * (FlaskBodyR - 1f);

        /// <summary>
        /// The top row a volume of brew (square art pixels) reaches, filling the hollow
        /// from the bottom up. <see cref="FlaskH"/> is an empty flask; it stops at the
        /// top of the neck however much is poured.
        /// </summary>
        public static int FlaskLevelRow(float volumePx)
        {
            int level = FlaskH;
            float left = volumePx;
            for (int y = FlaskH - 1; y >= 0; y--)
            {
                int n = HollowRow(y);
                if (n == 0)
                {
                    if (level < FlaskH) break;   // above the neck: brimful
                    continue;                    // below the bulb: nothing to fill yet
                }
                if (left < n * 0.5f) break;
                left -= n;
                level = y;
            }
            return level;
        }

        /// <summary>The top row of the neck the brew can reach (a brimful flask).</summary>
        public static int FlaskBrimRow => FlaskLevelRow(float.MaxValue);

        /// <summary>
        /// The brew in the glass, filled up to row <paramref name="level"/>, in greys to
        /// be tinted with the element colour: a light surface line, darkening with depth.
        /// </summary>
        public static Sprite FlaskLiquid(int level)
        {
            level = Mathf.Clamp(level, 0, FlaskH);
            string key = "flask_liquid_" + level;
            if (PixelCanvas.TryGet(key, out Sprite s)) return s;
            var ramp = new[]
            {
                PixelCanvas.Hex(0x8a8a8a), PixelCanvas.Hex(0xa8a8a8), PixelCanvas.Hex(0xc6c6c6),
                PixelCanvas.Hex(0xe2e2e2), PixelCanvas.Hex(0xffffff),
            };
            var c = new PixelCanvas(FlaskW, FlaskH);
            float depth = Mathf.Max(1f, FlaskH - 1 - level);
            c.Fill((x, y) => y >= level && FlaskHollow(x, y), (x, y) =>
            {
                if (y == level) return ramp[4];
                float light = 0.72f - 0.5f * (y - level) / depth + (x < FlaskCX - 4f ? 0.12f : 0f);
                Color32 k = PixelCanvas.Shade(ramp, light, x, y);
                return new Color32(k.r, k.g, k.b, 235);
            });
            return c.Bake(key, PPU, new Vector2(0.5f, 0f));
        }

        /// <summary>Two small ticks etched on the glass at rows <paramref name="low"/> and <paramref name="high"/>: the line to pour to.</summary>
        public static Sprite FlaskMarks(int low, int high)
        {
            string key = $"flask_marks_{low}_{high}";
            if (PixelCanvas.TryGet(key, out Sprite s)) return s;
            var c = new PixelCanvas(FlaskW, FlaskH);
            foreach (int row in new[] { low, high })
            {
                int right = -1;
                for (int x = 0; x < FlaskW; x++) if (FlaskHollow(x, row)) right = x;
                for (int x = right - 2; right >= 0 && x <= right; x++)
                    c.Set(x, row, new Color32(255, 255, 255, 150));
            }
            return c.Bake(key, PPU, new Vector2(0.5f, 0f));
        }

        /// <summary>The glass's front: outline, rim, and the highlights that make it read as glass.</summary>
        public static Sprite FlaskFront()
        {
            const string key = "flask_front";
            if (PixelCanvas.TryGet(key, out Sprite s)) return s;
            var shape = new PixelCanvas(FlaskW, FlaskH);
            shape.Fill((x, y) => FlaskInterior(x, y) || FlaskLip(x, y), (x, y) => new Color32(1, 1, 1, 1));
            var c = new PixelCanvas(FlaskW, FlaskH);
            // 1-px glass edge just inside the silhouette
            for (int y = 0; y < FlaskH; y++)
            for (int x = 0; x < FlaskW; x++)
            {
                if (!shape.Opaque(x, y)) continue;
                bool edge = !shape.Opaque(x - 1, y) || !shape.Opaque(x + 1, y) || !shape.Opaque(x, y - 1) || !shape.Opaque(x, y + 1);
                if (FlaskLip(x, y)) c.Set(x, y, y == 2 ? Iron[4] : Iron[3]);
                else if (edge) c.Set(x, y, new Color32(0xc8, 0xe8, 0xf8, 210));
            }
            // a highlight streak down the left of the bulb, and a glint on the neck
            c.Fill((x, y) => PixelCanvas.InEllipse(x, y, FlaskCX, FlaskBodyCY, FlaskBodyR - 2.2f, FlaskBodyR - 2.2f)
                             && !PixelCanvas.InEllipse(x, y, FlaskCX + 1.4f, FlaskBodyCY + 0.4f, FlaskBodyR - 2.6f, FlaskBodyR - 2.2f)
                             && x < FlaskCX - 3 && y < FlaskBodyCY + 4,
                (x, y) => new Color32(255, 255, 255, 170));
            c.Fill((x, y) => x == NeckX0 + 1 && y >= NeckY0 + 2 && y <= NeckY1 - 2, (x, y) => new Color32(255, 255, 255, 150));
            // outline around the whole flask
            for (int y = 0; y < FlaskH; y++)
            for (int x = 0; x < FlaskW; x++)
                if (!shape.Opaque(x, y) && (shape.Opaque(x - 1, y) || shape.Opaque(x + 1, y) || shape.Opaque(x, y - 1) || shape.Opaque(x, y + 1)))
                    c.Set(x, y, K);
            return c.Bake(key, PPU, new Vector2(0.5f, 0f));
        }

        /// <summary>The ladle, pivoting at the end of its handle (right).</summary>
        public static Sprite Ladle()
        {
            const string key = "ladle";
            if (PixelCanvas.TryGet(key, out Sprite s)) return s;
            const int w = 34, h = 12;
            var c = new PixelCanvas(w, h);
            c.Fill((x, y) => y >= 3 && PixelCanvas.InEllipse(x, y, 7f, 3f, 6.6f, 7.4f), (x, y) =>
                PixelCanvas.Shade(Iron, PixelCanvas.SphereLight(x, y, 7f, 2f, 6.6f, 7.4f, 0.2f), x, y));
            c.Fill((x, y) => y >= 2 && y <= 4 && x >= 12 && x <= 32, (x, y) => y == 2 ? Wood[4] : y == 3 ? Wood[3] : Wood[1]);
            c.Fill((x, y) => y >= 2 && y <= 3 && x >= 1 && x <= 13, (x, y) => Iron[3]);   // the bowl's lip
            c.Outline(K);
            // Pivot at the middle of the bowl's rim: the ladle tips about its bowl,
            // so the pouring lip stays over the flask's neck however far it tilts.
            return c.Bake(key, PPU, new Vector2(7f / w, 1f - 3f / h));
        }

        public static Sprite Cork()
        {
            const string key = "cork";
            if (PixelCanvas.TryGet(key, out Sprite s)) return s;
            var ramp = new[] { PixelCanvas.Hex(0x6b4a2f), PixelCanvas.Hex(0x9a7248), PixelCanvas.Hex(0xc49a64) };
            var c = new PixelCanvas(9, 8);
            c.Fill((x, y) => x >= 1 && x <= 7 && y >= 1 && y <= 6, (x, y) =>
                PixelCanvas.Shade(ramp, 1f - (x - 1) / 7f * 0.8f - (y > 4 ? 0.2f : 0f), x, y));
            c.Outline(K);
            return c.Bake(key, PPU, new Vector2(0.5f, 0.5f));
        }

        // ------------------------------------------------------------- the room

        /// <summary>A long workbench top, grained, lit along its front edge.</summary>
        public static Sprite Plank()
        {
            const string key = "plank";
            if (PixelCanvas.TryGet(key, out Sprite s)) return s;
            const int w = 176, h = 14;
            var c = new PixelCanvas(w, h);
            c.Fill((x, y) => true, (x, y) =>
            {
                if (y == 0) return Wood[4];
                if (y == 1) return Wood[3];
                if (y == h - 1) return K;
                float grain = Mathf.Sin(x * 0.09f + y * 1.7f + Mathf.Sin(x * 0.021f) * 3f);
                bool seam = x % 44 == 0;
                return seam ? Wood[0] : PixelCanvas.Shade(Wood, 0.45f + 0.18f * grain - y * 0.02f, x, y);
            });
            return c.Bake(key, PPU, new Vector2(0.5f, 1f));
        }

        /// <summary>Dim stone wall, a seamless tile.</summary>
        public static Sprite Wall()
        {
            const string key = "wall";
            if (PixelCanvas.TryGet(key, out Sprite s)) return s;
            const int w = 64, h = 48;
            var ramp = new[] { PixelCanvas.Hex(0x16111b), PixelCanvas.Hex(0x1f1826), PixelCanvas.Hex(0x281f31), PixelCanvas.Hex(0x31273b) };
            var c = new PixelCanvas(w, h);
            c.Fill((x, y) => true, (x, y) =>
            {
                int row = y / 12;
                int off = row % 2 == 0 ? 0 : 16;
                bool mortar = y % 12 == 0 || (x + off) % 32 == 0;
                if (mortar) return ramp[0];
                int stone = ((x + off) / 32) * 7 + row * 13;
                float t = 0.35f + ((stone * 37) % 11) / 30f - (y % 12) * 0.012f;
                return PixelCanvas.Shade(ramp, t, x, y);
            });
            return c.Bake(key, PPU, new Vector2(0.5f, 0.5f));
        }
    }
}
