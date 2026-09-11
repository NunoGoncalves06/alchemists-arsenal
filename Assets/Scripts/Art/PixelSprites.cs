using System.Collections.Generic;
using UnityEngine;
using AlchemistsArsenal.Combat;

namespace AlchemistsArsenal.Art
{
    /// <summary>
    /// Hand-authored pixel sprites, drawn pixel-by-pixel as string grids (each glyph
    /// = a palette entry). Baked to point-filtered <see cref="Sprite"/>s at load and
    /// cached. Style reference: chunky 1px black outline, small flat-shaded palette,
    /// slight top-light — the witch-cauldron / 16px-character look.
    ///
    /// This is the Phase-0 art set. It is authored content (every pixel is in this
    /// file, nothing from an asset store) — the art team can redraw any sprite in
    /// Aseprite at the same dimensions and assign it to the matching data SO to
    /// take over, no code change (see docs/ART_BRIEF.md).
    /// </summary>
    public static class PixelSprites
    {
        private const float PPU = 16f;

        // ---- palette ---------------------------------------------------------
        // '.' = transparent. One glyph per colour; reused across every sprite.
        private static readonly Dictionary<char, Color32> Pal = new Dictionary<char, Color32>
        {
            ['.'] = new Color32(0, 0, 0, 0),
            ['K'] = new Color32(0x17, 0x11, 0x1c, 0xff), // outline / darkest
            ['1'] = new Color32(0x2a, 0x22, 0x33, 0xff), // iron dark
            ['2'] = new Color32(0x3f, 0x35, 0x4c, 0xff), // iron
            ['3'] = new Color32(0x59, 0x4b, 0x69, 0xff), // iron light
            ['4'] = new Color32(0x8b, 0x7d, 0x99, 0xff), // metal shine
            ['g'] = new Color32(0x3a, 0x7a, 0x2e, 0xff), // brew green dark
            ['G'] = new Color32(0x5e, 0xa6, 0x37, 0xff), // brew green
            ['H'] = new Color32(0x93, 0xd6, 0x4c, 0xff), // brew green bright
            ['r'] = new Color32(0xb0, 0x3c, 0x2a, 0xff), // fire dark
            ['R'] = new Color32(0xe2, 0x68, 0x3a, 0xff), // fire
            ['Y'] = new Color32(0xf6, 0xc0, 0x4a, 0xff), // fire bright / gold
            ['b'] = new Color32(0x25, 0x5b, 0x86, 0xff), // water dark
            ['B'] = new Color32(0x3f, 0x8f, 0xd0, 0xff), // water
            ['C'] = new Color32(0x9f, 0xd4, 0xf0, 0xff), // water bright / ice
            ['p'] = new Color32(0x5a, 0x2f, 0x74, 0xff), // arcane/witch dark
            ['P'] = new Color32(0x7b, 0x4d, 0x9e, 0xff), // witch purple
            ['M'] = new Color32(0xc4, 0x51, 0xa8, 0xff), // arcane bright
            ['n'] = new Color32(0x4f, 0x7a, 0x3a, 0xff), // treant/leaf mid
            ['N'] = new Color32(0x6c, 0xa2, 0x4f, 0xff), // leaf light
            ['w'] = new Color32(0x3f, 0x2c, 0x1c, 0xff), // wood dark
            ['W'] = new Color32(0x6b, 0x4a, 0x2f, 0xff), // wood
            ['s'] = new Color32(0xd2, 0xa0, 0x6b, 0xff), // skin
            ['S'] = new Color32(0xf0, 0xc9, 0x9a, 0xff), // skin light
            ['c'] = new Color32(0xef, 0xe2, 0xc4, 0xff), // parchment / cloth light
            ['o'] = new Color32(0xc9, 0xb8, 0x92, 0xff), // parchment shade
            ['t'] = new Color32(0x93, 0x2e, 0x2e, 0xff), // tunic red
            ['T'] = new Color32(0xc6, 0x46, 0x3a, 0xff), // tunic red light
            ['l'] = new Color32(0x8a, 0x8f, 0xa0, 0xff), // steel / grey mid
            ['L'] = new Color32(0xc2, 0xc6, 0xd4, 0xff), // steel light
            ['y'] = new Color32(0xe8, 0xb6, 0x4c, 0xff), // candle gold
            ['#'] = new Color32(0x1b, 0x14, 0x1f, 0xff), // ui panel fill
            ['+'] = new Color32(0x33, 0x26, 0x3c, 0xff), // ui panel inner
        };

        private static readonly Dictionary<string, Sprite> _cache = new Dictionary<string, Sprite>();
        private static Material _unlit;

        public static Material Unlit
        {
            get
            {
                if (_unlit == null)
                {
                    var s = Shader.Find("Sprites/Default");
                    _unlit = s != null ? new Material(s) : null;
                }
                return _unlit;
            }
        }

        // ---- public accessors ----------------------------------------------

        public static Sprite Cauldron() => Bake("cauldron", CAULDRON);
        public static Sprite BootLogo() => Bake("logo", LOGO);
        public static Sprite Coin() => Bake("coin", COIN);
        public static Sprite Rookie() => Bake("rookie", ROOKIE);
        public static Sprite Boss() => Bake("boss", BOSS);
        public static Sprite CatSleeping() => Bake("cat", CAT);

        public static Sprite Monster(string name)
        {
            string key = (name ?? "").ToLowerInvariant();
            if (key.Contains("ember") || key.Contains("cinder") || key.Contains("hound")) return Bake("m_ember", EMBERLING);
            if (key.Contains("frost") || key.Contains("rime")) return Bake("m_frost", FROSTKIN);
            if (key.Contains("treant") || key.Contains("thorn") || key.Contains("moss") || key.Contains("bog")) return Bake("m_treant", TREANT);
            if (key.Contains("acolyte") || key.Contains("effigy") || key.Contains("coven")) return Bake("m_acolyte", ACOLYTE);
            if (key.Contains("mire") || key.Contains("maw")) return Bake("m_mire", MIREMAW);
            return Bake("m_treant", TREANT);
        }

        /// <summary>Herb jar / leaf tinted to an element (uses the element's greens/blues/oranges swap).</summary>
        public static Sprite Herb(ElementType e) => BakeTinted("herb_" + e, HERB, ElementSwap(e));

        public static Sprite Flask(ElementType e) => BakeTinted("flask_" + e, FLASK, ElementSwap(e));

        public static Sprite ElementIcon(ElementType e) => e switch
        {
            ElementType.Fire => Bake("ei_fire", EI_FIRE),
            ElementType.Water => Bake("ei_water", EI_WATER),
            ElementType.Nature => Bake("ei_nature", EI_NATURE),
            ElementType.Poison => Bake("ei_poison", EI_POISON),
            _ => Bake("ei_arcane", EI_ARCANE),
        };

        /// <summary>9-slice pixel frame for UI panels (4px border).</summary>
        public static Sprite Panel9()
        {
            const string key = "panel9";
            if (_cache.TryGetValue(key, out var s) && s != null) return s;
            var tex = BakeTexture(PANEL9);
            s = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f),
                PPU, 0, SpriteMeshType.FullRect, new Vector4(4, 4, 4, 4));
            _cache[key] = s;
            return s;
        }

        // ---- baking -------------------------------------------------------

        private static Sprite Bake(string key, string[] rows)
        {
            if (_cache.TryGetValue(key, out var s) && s != null) return s;
            var tex = BakeTexture(rows);
            s = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), PPU);
            _cache[key] = s;
            return s;
        }

        private static Sprite BakeTinted(string key, string[] rows, Dictionary<char, char> swap)
        {
            if (_cache.TryGetValue(key, out var s) && s != null) return s;
            var tex = BakeTexture(rows, swap);
            s = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), PPU);
            _cache[key] = s;
            return s;
        }

        private static Texture2D BakeTexture(string[] rows, Dictionary<char, char> swap = null)
        {
            int h = rows.Length;
            int w = 0;
            foreach (var r in rows) if (r.Length > w) w = r.Length;

            // Every row must be the same width — a ragged constant is an authoring
            // typo that would skew the sprite. Warn loudly; render it padded.
            foreach (var r in rows)
                if (r.Length != w)
                {
                    Debug.LogWarning($"[PixelSprites] ragged sprite: a row is {r.Length} wide, expected {w}. Fix the string grid.");
                    break;
                }

            var tex = new Texture2D(Mathf.Max(1, w), Mathf.Max(1, h), TextureFormat.RGBA32, false)
            { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, name = "px" };
            var px = new Color32[w * h];

            for (int y = 0; y < h; y++)
            {
                string row = rows[h - 1 - y]; // string row 0 = top → texture row h-1
                for (int x = 0; x < w; x++)
                {
                    char c = x < row.Length ? row[x] : '.';
                    if (swap != null && swap.TryGetValue(c, out char sc)) c = sc;
                    px[y * w + x] = Pal.TryGetValue(c, out var col) ? col : new Color32(0, 0, 0, 0);
                }
            }
            tex.SetPixels32(px);
            tex.Apply(false, true);
            return tex;
        }

        /// <summary>Remap the "green" brew glyphs to another element's colour ramp.</summary>
        private static Dictionary<char, char> ElementSwap(ElementType e)
        {
            return e switch
            {
                ElementType.Fire   => new Dictionary<char, char> { ['g'] = 'r', ['G'] = 'R', ['H'] = 'Y', ['n'] = 'r', ['N'] = 'Y' },
                ElementType.Water  => new Dictionary<char, char> { ['g'] = 'b', ['G'] = 'B', ['H'] = 'C', ['n'] = 'b', ['N'] = 'C' },
                ElementType.Poison => new Dictionary<char, char> { ['g'] = 'p', ['G'] = 'M', ['H'] = 'H', ['n'] = 'p', ['N'] = 'M' },
                ElementType.Arcane => new Dictionary<char, char> { ['g'] = 'p', ['G'] = 'P', ['H'] = 'M', ['n'] = 'p', ['N'] = 'M' },
                _ => new Dictionary<char, char>(), // Nature = the default greens
            };
        }

        // ================================================================
        //  SPRITE DATA  (top row = top of image)
        // ================================================================

        // 28 x 28 — the bubbling cauldron (reference: fat body, 3 legs, side
        // handles, lighter rim, green brew + rising bubbles)
        private static readonly string[] CAULDRON =
        {
            ".............................",
            "..............H..............",
            ".............HHH.............",
            "..............H..............",
            "..........H.......H..........",
            ".........HHH.....HHH.........",
            "..........H.......H..........",
            "...KKKKKKKKKKKKKKKKKKKKKK....",
            "..K33333333333333333333K3K...",
            ".K3KKKKKKKKKKKKKKKKKKKKKK3K..",
            ".K3KgGHGgGHGgGHGgGHGgGHK3K...",
            ".K3KGgGHGgGHGgGHGgGHGgGK3K...",
            "KK31KKKKKKKKKKKKKKKKKK13KK...",
            "K3K111111111111111111111K3K..",
            "K3K122222222222222222221K3K..",
            ".K3122222222222222222222 3K..",
            ".K212222222233332222222212K..",
            ".K212222223344443222222212K..",
            ".K2122222233333322222222 2K..",
            "..K2122222223333222222221K...",
            "..K221222222222222222221 K...",
            "...K221222222222222221 2K....",
            "....K2221111111111111 22K....",
            ".....KK2211111111111122KK....",
            "......KKKK222222222 KKKK.....",
            ".....K11K..KK...KK..K11K.....",
            "....K11K...K1K...K1K..K1K....",
            "....KKK....KKK...KKK..KKK....",
        };

        // 16 x 16 witch-hat + moon crest for the boot splash
        private static readonly string[] LOGO =
        {
            "................",
            ".......pp.......",
            "......pMMp......",
            "......pMMp......",
            ".....pMMMMp.....",
            ".....pMMMMp.....",
            "....pMMMMMMp....",
            "...pMMMMMMMMp...",
            "..pMMMMMMMMMMp..",
            ".ppppppppppppp..",
            ".pcccccccccccp..",
            ".ppppppppppppp..",
            "......pKKp......",
            ".....pKKKKp.....",
            "......pKKp......",
            "................",
        };

        // 10 x 10 coin
        private static readonly string[] COIN =
        {
            "...KKKK...",
            ".KKyyyyKK.",
            ".KyYYYYyK.",
            "KyYYccYYyK",
            "KyYcccYYyK",
            "KyYccYYYyK",
            "KyYYYYYYyK",
            ".KyYYYYyK.",
            ".KKyyyyKK.",
            "...KKKK...",
        };

        // 16 x 18 — Rookie adventurer (red tunic, little sword, round head)
        private static readonly string[] ROOKIE =
        {
            "................",
            ".....KKKK.......",
            "....KSSSSK......",
            "...KSSSSSSK.....",
            "...KSssssSK.....",
            "...KSsKsKsK.....",
            "...KSssssSK.....",
            "....KSssSK......",
            "...KKttttKK...L.",
            "..KtTTTTTTtK..L.",
            "..KtTtTTtTtK.LL.",
            "..sKtTTTTtKs.L..",
            "..sKttttttKs.L..",
            "...KttttttK..K..",
            "...KwwKKwwK.....",
            "...Kww.Kww K....",
            "..Kww..Kww K....",
            "..KK....KK......",
        };

        // 16 x 16 — Bark Treant (nature)
        private static readonly string[] TREANT =
        {
            "................",
            "....KK...KK.....",
            "...KnnK.KnnK....",
            "...KnNnKnNnK....",
            "..KnNNnnnNNnK...",
            "..KnNnnnnnNnK...",
            "..KnNnKKKnNnK...",
            "..KnNnKGKnNnK...",
            "..KnNnnKnnNnK...",
            "...KnNnnnNnK....",
            "...KwwNNNwwK....",
            "...KwWwnwWwK....",
            "...KwWwwwWwK....",
            "...KwwK.KwwK....",
            "..KwwK...KwwK...",
            "..KKK.....KKK...",
        };

        // 13 x 14 — Emberling (fire imp)
        private static readonly string[] EMBERLING =
        {
            "......YY.....",
            ".....YRRY....",
            "....KRRRRK...",
            "...KRrRRrRK..",
            "..KRRRKRRRK..",
            "..KRRKYKRRK..",
            "..KRRRRRRRK..",
            "..KRrRRRRrK..",
            "...KRRRRRK...",
            "....KRKRK....",
            "....KRK.KRK..",
            "...KRK...KRK.",
            "...KK.....KK.",
            ".............",
        };

        // 13 x 14 — Frostkin (water/ice)
        private static readonly string[] FROSTKIN =
        {
            "......CC.....",
            ".....CBBC....",
            "....KBBBBK...",
            "...KBbBBbBK..",
            "..KBBBKBBBK..",
            "..KBBKCKBBK..",
            "..KBBBBBBBK..",
            "..KBbBBBBbK..",
            "..KBBBBBBBK..",
            "...KBCBCBK...",
            "...KBK.KBK...",
            "..KBK...KBK..",
            "..KK.....KK..",
            ".............",
        };

        // 13 x 14 — Coven Acolyte (arcane)
        private static readonly string[] ACOLYTE =
        {
            "......KK.....",
            ".....KppK....",
            "....KpMMpK...",
            "...KpMMMMpK..",
            "...KpMsMspK..",
            "...KpMMMMpK..",
            "..KppMMMMppK.",
            "..KpPPPPPPpK.",
            "..KpPpPPpPpK.",
            "..KpPPPPPPpK.",
            "...KpPPPPpK..",
            "...KppKKppK..",
            "..KKK...KKK..",
            ".............",
        };

        // 13 x 13 — Miremaw (poison)
        private static readonly string[] MIREMAW =
        {
            ".............",
            "...KK...KK...",
            "..KppK.KppK..",
            "..KpMpKpMpK..",
            ".KppMMpMMppK.",
            ".KpMMMMMMMpK.",
            ".KpMKMKMKMpK.",
            ".KpMMMMMMMpK.",
            ".KpMHKHKHMpK.",
            "..KpMMMMMpK..",
            "..KppKpKppK..",
            "..KK..K..KK..",
            ".............",
        };

        // 28 x 28 — The Coven Matriarch (boss): tall hooded arcane figure, four arms
        private static readonly string[] BOSS =
        {
            "............KKKK............",
            "..........KKppppKK..........",
            ".........KpMMMMMMpK.........",
            "........KpMMMMMMMMpK........",
            ".......KpMMMMMMMMMMpK.......",
            ".......KpMMsMMMMsMMpK.......",
            "......KpMMMMMMMMMMMMpK......",
            "......KpMMMMKMMKMMMMpK......",
            "......KpMMMMMMMMMMMMpK......",
            ".....KppMMMMMMMMMMMMppK.....",
            "..K..KpPPPPPPPPPPPPPPpK..K..",
            ".KpK.KpPPPPPPPPPPPPPPpK.KpK.",
            "KpMpKKpPPMMPPPPPPMMPPpKKpMpK",
            "KpMpppPPPMMPPPPPPMMPPPpppMpK",
            "KpMMMPPPPPPPPPPPPPPPPPPMMMpK",
            ".KpMMPPPPPPMMMMMMPPPPPPMMpK.",
            "..KppPPPPPPMMYYMMPPPPPPppK..",
            "...KpPPPPPPPMMMMPPPPPPPpK...",
            "...KpPPPPPPPPPPPPPPPPPPpK...",
            "...KpPPPPPPPPPPPPPPPPPPpK...",
            "...KppPPPPPPPPPPPPPPPPppK...",
            "....KpPPPPPPPPPPPPPPPPpK....",
            "....KppPPPPPPPPPPPPPPppK....",
            ".....KpppPPPPPPPPPPpppK.....",
            "......KKppppKKKKppppKK......",
            ".......KKKK......KKKK.......",
            "......K11K........K11K......",
            "......KKKK........KKKK......",
        };

        // 12 x 12 — sleeping cat for the menu counter
        private static readonly string[] CAT =
        {
            "............",
            "..K......K..",
            ".KKK....KKK.",
            ".K1KKKKKK1K.",
            "K11111111 1K",
            "K1111111111K",
            "K11K1111K11K",
            "K1111111111K",
            ".K11111111K.",
            "..KKKKKKKK.K",
            "........KKKK",
            "............",
        };

        // 12 x 12 — herb leaf (green by default; tinted per element by ElementSwap)
        private static readonly string[] HERB =
        {
            "......K.....",
            ".....KGK....",
            "....KGHGK...",
            "...KGHHGK...",
            "..KGHHHGK...",
            "..KGHnHGK...",
            "..KGnHnGK...",
            "...KGnGK....",
            "....KwK.....",
            "....KwK.....",
            "...KwK......",
            "...KK.......",
        };

        // 14 x 16 — potion flask (green fill; tinted per element)
        private static readonly string[] FLASK =
        {
            "....KKKK....",
            "....KccK....",
            "....KccK....",
            "...KKccKK...",
            "..KKc..cKK..",
            "..Kc....cK..",
            ".KKc.HH.cKK.",
            ".KcGHHHHGcK.",
            ".KcGHHHHGcK.",
            ".KcGGHHGGcK.",
            ".KcGGGGGGcK.",
            ".KcGGGGGGcK.",
            ".KKcGGGGcKK.",
            "..KKcccc KK.",
            "...KKKKKK...",
            "............",
        };

        // 16 x 16 — 9-slice UI panel (wood border, ink fill, thin inner bevel)
        private static readonly string[] PANEL9 =
        {
            "WWWWWWWWWWWWWWWW",
            "WwwwwwwwwwwwwwwW",
            "Ww############wW",
            "Ww#++++++++++#wW",
            "Ww#+########+#wW",
            "Ww#+########+#wW",
            "Ww#+########+#wW",
            "Ww#+########+#wW",
            "Ww#+########+#wW",
            "Ww#+########+#wW",
            "Ww#+########+#wW",
            "Ww#++++++++++#wW",
            "Ww############wW",
            "WwwwwwwwwwwwwwwW",
            "WWWWWWWWWWWWWWWW",
            "WWWWWWWWWWWWWWWW",
        };

        // --- element icons: colour + distinct silhouette (a11y) ------------
        private static readonly string[] EI_FIRE =
        {
            "....K....",
            "...KRK...",
            "..KRRRK..",
            "..KRYRK..",
            ".KRRYRRK.",
            ".KRYYYRK.",
            "KRRYYYRRK",
            "KRRRRRRRK",
            ".KKKKKKK.",
        };
        private static readonly string[] EI_WATER =
        {
            "....K....",
            "....K....",
            "...KBK...",
            "..KBBBK..",
            "..KBCBK..",
            ".KBBCBBK.",
            ".KBCCBBK.",
            ".KBBBBBK.",
            "..KKKKK..",
        };
        private static readonly string[] EI_NATURE =
        {
            "..KKKKK..",
            ".KGGGGGK.",
            "KGHGHGHGK",
            "KGGGHGGGK",
            "KGHGHGHGK",
            "KGGGHGGGK",
            "KGHGHGHGK",
            ".KGGGGGK.",
            "..KKKKK..",
        };
        private static readonly string[] EI_POISON =
        {
            "..KKKKK..",
            ".KMMMMMK.",
            "KMMHKHMMK",
            "KMHMMMHMK",
            "KMKMHMKMK",
            "KMHMMMHMK",
            "KMMHKHMMK",
            ".KMMMMMK.",
            "..KKKKK..",
        };
        private static readonly string[] EI_ARCANE =
        {
            "....K....",
            "...KMK...",
            "K..KMK..K",
            ".KKMMMKK.",
            "KMMMMMMMK",
            ".KKMMMKK.",
            "K..KMK..K",
            "...KMK...",
            "....K....",
        };
    }
}
