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
    public static partial class PixelSprites
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
            ['v'] = new Color32(0x6b, 0x76, 0x1f, 0xff), // poison dark
            ['V'] = new Color32(0xb6, 0xc3, 0x3f, 0xff), // poison (UITheme.Poison)
            ['u'] = new Color32(0xe0, 0xea, 0x7c, 0xff), // poison bright
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
            ['@'] = new Color32(0xff, 0xff, 0xff, 0xff), // pure white (hit-flash silhouettes, highlights)
        };

        private static readonly Dictionary<string, Sprite> _cache = new Dictionary<string, Sprite>();

        /// <summary>The grid each baked sprite came from, so a white silhouette can be
        /// baked for it later (the baked textures are not CPU-readable).</summary>
        private static readonly Dictionary<Sprite, string[]> _rowsOf = new Dictionary<Sprite, string[]>();
        private static readonly Dictionary<Sprite, Sprite> _silhouettes = new Dictionary<Sprite, Sprite>();

        /// <summary>
        /// A pure-white silhouette of <paramref name="sprite"/>, same size and pivot, for
        /// hit flashes. Tinting can only darken a sprite, so a flash needs its own
        /// texture. Null for sprites that were not baked from a grid here.
        /// </summary>
        public static Sprite Silhouette(Sprite sprite)
        {
            if (sprite == null) return null;
            if (!_rowsOf.TryGetValue(sprite, out string[] rows)) return PixelCanvas.SilhouetteOf(sprite);
            if (_silhouettes.TryGetValue(sprite, out var s) && s != null) return s;
            var white = new Dictionary<char, char>();
            foreach (string r in rows)
                foreach (char c in r)
                    if (c != '.' && !white.ContainsKey(c)) white[c] = '@';
            var tex = BakeTexture(rows, white);
            s = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), sprite.pivot / sprite.rect.size, sprite.pixelsPerUnit);
            _silhouettes[sprite] = s;
            return s;
        }

        // ONE material PER TEXTURE — never one shared material for every sprite.
        //
        // This was a real, shipped bug: every world sprite was assigned the same
        // single Material instance. A SpriteRenderer normally supplies its own
        // sprite's texture per draw, but once several renderers share one material
        // the batcher groups them and the whole batch draws with a single bound
        // texture — so the adventurer rendered with whatever the monsters' texture
        // was. It looked correct whenever no monster was on screen (fight start,
        // between waves, after the last kill) and "changed skin" the instant a wave
        // walked in, copying exactly that wave's monster — including the boss fight,
        // where the adventurer and the boss drew as the same sprite at two sizes.
        //
        // Keyed by texture so sprites sharing a texture still batch together.
        /// <summary>The per-texture unlit material (see <see cref="SpriteMaterials"/>).</summary>
        public static Material MaterialFor(Sprite sprite) => SpriteMaterials.For(sprite);

        // ---- public accessors ----------------------------------------------

        public static Sprite Cauldron() => Bake("cauldron", CAULDRON);
        public static Sprite BootLogo() => Bake("logo", LOGO);
        public static Sprite Coin() => Bake("coin", COIN);
        public static Sprite Rookie() => Bake("rookie", ROOKIE);
        public static Sprite Boss() => Bake("boss", BOSS);
        public static Sprite CatSleeping() => Bake("cat", CAT);

        /// <summary>A small downward-pointing chevron for tutorial callouts — a real
        /// triangle, not a symmetric diamond (which reads as a ball, not an arrow).</summary>
        public static Sprite PointerArrow() => Bake("pointer_arrow", POINTER_ARROW);

        // --- station iconography + props -------------------------------------
        public static Sprite Bell() => Bake("bell", BELL);              // Counter
        public static Sprite Mortar() => Bake("mortar", MORTAR);        // Prep
        public static Sprite Spoon() => Bake("spoon", SPOON);           // the stirring spoon (world)
        public static Sprite Star() => Bake("star", STAR);
        public static Sprite Lock() => Bake("lock", LOCK);

        /// <summary>
        /// A customer bust for the Counter. Ids come from
        /// <c>Data.CustomerCatalog</c>; an unknown id falls back to the knight so a
        /// new catalog entry can never render as nothing.
        /// </summary>
        public static Sprite Buyer(string id)
        {
            switch ((id ?? "").ToLowerInvariant())
            {
                case "herbalist": return Bake("buyer_herbalist", BUYER_HERBALIST);
                case "merchant": return Bake("buyer_merchant", BUYER_MERCHANT);
                case "envoy": return Bake("buyer_envoy", BUYER_ENVOY);
                case "rookie": return Rookie();
                default: return Bake("buyer_knight", BUYER_KNIGHT);
            }
        }

        /// <summary>
        /// The same character head-to-toe, for the afternoon fight. Whoever ordered
        /// the potion is who carries it into the biome, so every customer needs two
        /// views: the shop-window bust above and this full body.
        /// </summary>
        public static Sprite Fighter(string id)
        {
            switch ((id ?? "").ToLowerInvariant())
            {
                case "herbalist": return Bake("fighter_herbalist", FIGHTER_HERBALIST);
                case "merchant": return Bake("fighter_merchant", FIGHTER_MERCHANT);
                case "envoy": return Bake("fighter_envoy", FIGHTER_ENVOY);
                case "knight": return Bake("fighter_knight", FIGHTER_KNIGHT);
                default: return Rookie();
            }
        }

        /// <summary>
        /// A monster's sprite, recoloured to its actual element. Families share a
        /// silhouette, so a Water "Mossback" drawn with the green Treant grid, or a
        /// Poison "Warded Effigy" drawn with the purple Acolyte grid, used to read as
        /// the wrong element entirely. Each grid's body ramp is swapped onto the
        /// element's ramp instead.
        /// </summary>
        public static Sprite Monster(string name, ElementType element)
        {
            var (key, rows, ramp, authored) = MonsterFamily(name);
            if (element == authored) return Bake(key, rows);
            char[] to = ElementRamp(element);
            var swap = new Dictionary<char, char> { [ramp[0]] = to[0], [ramp[1]] = to[1], [ramp[2]] = to[2] };
            return BakeTinted($"{key}_{element}", rows, swap);
        }

        /// <summary>A monster in its family's authored colours.</summary>
        public static Sprite Monster(string name)
        {
            var (key, rows, _, _) = MonsterFamily(name);
            return Bake(key, rows);
        }

        /// <summary>Grid, its (dark, mid, bright) body glyphs, and the element those glyphs are drawn in.</summary>
        private static (string key, string[] rows, char[] ramp, ElementType authored) MonsterFamily(string name)
        {
            string k = (name ?? "").ToLowerInvariant();
            if (k.Contains("ember") || k.Contains("cinder") || k.Contains("hound"))
                return ("m_ember", EMBERLING, new[] { 'r', 'R', 'Y' }, ElementType.Fire);
            if (k.Contains("frost") || k.Contains("rime"))
                return ("m_frost", FROSTKIN, new[] { 'b', 'B', 'C' }, ElementType.Water);
            if (k.Contains("acolyte") || k.Contains("effigy") || k.Contains("coven"))
                return ("m_acolyte", ACOLYTE, new[] { 'p', 'P', 'M' }, ElementType.Arcane);
            if (k.Contains("mire") || k.Contains("maw"))
                return ("m_mire", MIREMAW, new[] { 'p', 'M', 'H' }, ElementType.Arcane);
            return ("m_treant", TREANT, new[] { 'n', 'N', 'G' }, ElementType.Nature);
        }

        /// <summary>An element's (dark, mid, bright) palette glyphs.</summary>
        private static char[] ElementRamp(ElementType e) => e switch
        {
            ElementType.Fire => new[] { 'r', 'R', 'Y' },
            ElementType.Water => new[] { 'b', 'B', 'C' },
            ElementType.Poison => new[] { 'v', 'V', 'u' },
            ElementType.Arcane => new[] { 'p', 'P', 'M' },
            _ => new[] { 'g', 'G', 'H' },
        };

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
            _rowsOf[s] = rows;
            return s;
        }

        /// <summary>Bake with a custom pivot (0..1), e.g. feet-anchored characters and props.</summary>
        private static Sprite BakePivot(string key, string[] rows, Vector2 pivot01, Dictionary<char, char> swap = null)
        {
            if (_cache.TryGetValue(key, out var s) && s != null) return s;
            var tex = BakeTexture(rows, swap);
            s = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), pivot01, PPU);
            _cache[key] = s;
            _rowsOf[s] = rows;
            return s;
        }

        private static Sprite BakeTinted(string key, string[] rows, Dictionary<char, char> swap)
        {
            if (_cache.TryGetValue(key, out var s) && s != null) return s;
            var tex = BakeTexture(rows, swap);
            s = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), PPU);
            _cache[key] = s;
            _rowsOf[s] = rows;
            return s;
        }

        private static Texture2D BakeTexture(string[] rows, Dictionary<char, char> swap = null)
        {
            int h = rows.Length;
            int w = 0;
            foreach (var r in rows) if (r.Length > w) w = r.Length;

            // Every row must be the same width, and every glyph must be in the palette.
            // Both are authoring typos (a ragged row skews the sprite, an unknown glyph
            // silently draws a hole), so both are errors: the headless playtest fails
            // on any logged error, which is how they get caught.
            foreach (var r in rows)
                if (r.Length != w)
                {
                    Debug.LogError($"[PixelSprites] ragged sprite: a row is {r.Length} wide, expected {w}. Fix the string grid.");
                    break;
                }
            foreach (var r in rows)
                foreach (char c in r)
                    if (!Pal.ContainsKey(c) && (swap == null || !swap.ContainsKey(c)))
                    {
                        Debug.LogError($"[PixelSprites] unknown glyph '{c}' in a sprite grid.");
                        goto glyphsChecked;
                    }
            glyphsChecked:

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
                // Poison is yellow-green (#b6c33f) everywhere else in the game. It used
                // to swap to purple here, which made it read as Arcane in the world.
                ElementType.Poison => new Dictionary<char, char> { ['g'] = 'v', ['G'] = 'V', ['H'] = 'u', ['n'] = 'v', ['N'] = 'V' },
                ElementType.Arcane => new Dictionary<char, char> { ['g'] = 'p', ['G'] = 'P', ['H'] = 'M', ['n'] = 'p', ['N'] = 'M' },
                _ => new Dictionary<char, char>(), // Nature = the default greens
            };
        }
    }
}
