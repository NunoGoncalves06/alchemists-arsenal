# Art brief — Alchemist's Arsenal

Two things live here:
1. **What ships now** — every sprite is hand-authored pixel-by-pixel in
   `Assets/Scripts/Art/PixelSprites.cs` (a string-grid DSL, baked to
   point-filtered textures at load). No asset-store content, no image files.
2. **How the team upgrades it** — redraw any sprite at the same pixel size in an
   editor and assign it to the matching data field; the code already falls back
   to `PixelSprites` when the field is null. No code change.

---

## The style (from the references)

- **1px hard black outline** on everything (`#17111c`).
- **Small flat palette per sprite** — 2–3 shades of one hue + the outline.
- **Top-left light**: highlight the upper-left edge, shade the lower-right.
- **Chunky, readable silhouettes** — a monster must read at 16px from across a room.
- Cauldron: fat rounded body, 3 stubby legs, 2 side handles, lighter rim, green
  brew with 2–3 rising round bubbles.
- Characters: ~16px, big head, 2–3 body colours, one readable prop (sword, staff, bow).

Palette anchors (match these so new art sits with the UI — `UITheme`):
`ink #1b141f` · `parchment #efe2c4` · `wood #6b4a2f` · `candle #e8b64c` ·
`witch #7b4d9e` · Nature `#5ea637` · Fire `#e2683a` · Water `#3f8fd0` ·
Poison `#b6c33f` · Arcane `#c451a8`.

---

## Sprite manifest (redraw list)

| Sprite | Size (px) | Where it goes | Current source |
|---|---|---|---|
| Cauldron | 30×30 | `ShopWorld` (world) | `PixelSprites.Cauldron()` |
| Herb leaf (×5 elements) | 12×12 | `HerbData.icon` *(SO — Phase 2)*, `ShopWorld` bodies | `PixelSprites.Herb(e)` |
| Potion flask (×5) | 14×16 | `BombData.icon` | `PixelSprites.Flask(e)` |
| Rookie (adventurer) | 16×18 | `AdventurerData.characterSprite` | `PixelSprites.Rookie()` |
| Bark Treant | 16×16 | `MonsterData.sprite` (Whispering Woods) | `PixelSprites.Monster("treant")` |
| Emberling | 14×14 | `MonsterData.sprite` (Cinder Peaks) | `PixelSprites.Monster("ember")` |
| Frostkin | 14×14 | `MonsterData.sprite` (Frostbite Caverns) | `PixelSprites.Monster("frost")` |
| Coven Acolyte | 14×14 | `MonsterData.sprite` (Coven's Peak) | `PixelSprites.Monster("acolyte")` |
| Miremaw | 14×14 | `MonsterData.sprite` (Venom Swamp) | `PixelSprites.Monster("mire")` |
| Coven Matriarch (boss) | 28×28 | `BossDefinition` sprite *(Phase 2 field)* | `PixelSprites.Boss()` |
| Element icons (×5) | 9×9 | `UIFactory.ElementBadge` | `PixelSprites.ElementIcon(e)` |
| 9-slice UI panel | 16×16, 4px border | `UIFactory.FramedPanel` | `PixelSprites.Panel9()` |
| Coin | 10×10 | gold counters | `PixelSprites.Coin()` |
| Boot crest / title logo | 16×16 → scale | `BootScreen` | `PixelSprites.BootLogo()` |
| Sleeping cat | 12×12 | menu counter *(Phase 2)* | `PixelSprites.CatSleeping()` |
| **Opening cutscene frames** | ~64×48, 3–6 frames each | `DiaryEntryData.cutsceneFrames` — `diary_00`, `diary_ww`, `diary_perfect` | procedural blocks (`DiaryScreen`) |
| Biome backdrops (×5) | ~64×36, 3 parallax layers | `BiomeData` *(Phase 2 field)* | `PixelArt.Backdrop` tint |

**Wiring an authored sprite:** import the PNG (point filter, no compression,
PPU 16), select the `MonsterData` / `AdventurerData` / `BombData` asset in
`Assets/ScriptableObjects/`, drag the sprite into its `sprite` / `characterSprite`
/ `icon` field. Done — `MonsterSpawner` / `ExpeditionWorld` / the UI use it
instead of the code sprite automatically. Cutscene frames: create a
`DiaryEntryData` asset (`Create ▸ Alchemist's Arsenal ▸ Story ▸ Diary Entry`),
set `id` to match (`diary_00` etc.), fill `cutsceneFrames`.

---

## If the team can't draw it — where to get pixel art

For the rubric's **"self-made assets"** category the strongest answer is the team
drawing the final set (the manifest above is small — ~15 characters + UI, a few
days in Aseprite). If that's not possible:

- **Aseprite** (~$20, or free if built from source) — the standard pixel tool.
  Libresprite is a free fork.
- **Commission** a pixel artist for the ~15-sprite set: itch.io "hire an artist"
  board, r/gameDevClassifieds, Fiverr/Upgrade "pixel art character 16x16". Budget
  ~$150–400 for the full Phase-0 set with a consistent style. Give them this file.
- **CC0 / free packs** (allowed by most course rubrics for *placeholder* only —
  check yours; using them as final art usually loses the "self-made" points):
  Kenney.nl (CC0), OpenGameArt.org (filter to CC0), itch.io "free pixel art".
- **AI-assisted** (e.g. a pixel-art diffusion model) then hand-cleaned in Aseprite
  — check the rubric allows it; many treat AI art the same as asset-store art.

The code doesn't care where the sprite comes from — same field, same size.

---

## TMP setup (fixes "Display 1 No cameras rendering" / missing text)

That error had two causes, both handled in code now, but the clean fix is:

1. **Import TMP Essentials:** `Window ▸ TextMeshPro ▸ Import TMP Essential
   Resources`. If that importer *errors* (as reported):
   - close Unity, delete the `Library/` folder, reopen (forces a clean reimport), then retry.
   - or in Package Manager, reinstall **TextMeshPro** / **com.unity.ugui**.
   - "Import TMP Examples & Extras" is **optional** — skip it, it's only demo scenes.
2. Code fallback (already in `UITheme.Font`): if TMP Essentials is missing, the
   game builds a runtime font from a system font so text still renders — but
   importing the real essentials gives sharper text and the SDF shader.
3. The camera error is fixed by `Bootstrap` creating a persistent `BootCamera`.


## Procedural art (2026-09)
Everything that is not a hand-typed grid is drawn from geometry with
`PixelCanvas`, in the same style as the grids:
- 16 px per world unit, the same pixel size as the characters;
- a 1-px ink outline (`#17111c`) on figures;
- flat ramps lit from the upper left, with Bayer-dithered steps between them.

| Where | What |
|---|---|
| `ShopArt`, `ShopProps` | the pot (split front and back around the liquid), mortar, pestle, flask, ladle, cork, planks, wall, shelves of jars, candles, herb bundles |
| `BossArt` | the Woodwose and the Matriarch as rig parts, each baked with a white silhouette for the hit flash; decals, ward runes, projectiles, debris chips |
| `StoryArt` | eight lit 192×108 scenes and the cast (Nell, Tam, Veil, Ysolde unmasked, the kettle) |
| `BiomeArt` | the five 544×320 arena backdrops |
| `ParticleArt` | the round glow and puff textures (particles show their whole texture) |

Rules:
- A sprite swapped at runtime swaps its material with it (`SpriteMaterials.For`).
- The headless harness exports every procedural sprite, as baked, to
  `headless-screens/art/` (`PixelCanvas.ExportDir`). The design document's images
  come from there, and from the harness screenshots.
