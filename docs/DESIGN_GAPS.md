# Design Gap Analysis — Alchemist's Arsenal

> **Companion to `DESIGN.md`.** What the game needs, what exists, what's missing,
> seen through a **design** lens (systems + screens + player-facing feel), then
> cross-checked against the 8 Level-2 rubric categories.
> Status legend: ✅ done · 🟡 partial · ❌ missing · ⚠ inconsistent

---

## A. Executive summary

The **simulation layer is strong** (physics cauldron, IAUS combat AI, boss HFSM,
wave director, elemental matrix, decoupled ScriptableObjects). The **entire
presentation layer, the day-orchestration layer, and the meta/progression layer
are missing or stubbed.** Concretely:

- The three day-phases (`Morning / Afternoon / Evening`) **do not connect** —
  there is no `GameLoopManager`, and the morning craft never arms an afternoon
  adventurer. This is the #1 gap: without it the "novel hybrid loop" the whole
  pitch rests on is not demonstrable.
- There is **no playable morning UI** — `StationUIManager` toggles panels that
  were never built; `CauldronUI` binds to sliders that don't exist.
- The **afternoon HUD is `OnGUI()` debug text.**
- **Evening does not exist** — no report, shop, roster, diary, biome map.
- **0 sprites, 0 prefabs, 1 default scene.** All art is runtime placeholder shapes.
- **No audio, no tutorial, no save system, no gold/economy.**

Nothing here requires throwing away code — every gap is additive and slots onto
the existing event surfaces.

---

## B. Gaps by system

### B1. Day orchestration — ❌ CRITICAL
- No `GameLoopManager` / `GamePhase` state. `ExpeditionBootstrap` jumps straight
  into a fight; the shop code runs in isolation.
- No morning time budget, no phase transitions, no "day N" concept, no
  advance-to-next-day.
- **Design need:** `GameLoopManager` owning `Boot/MainMenu/Morning/Afternoon/
  Evening/DayTransition`, a morning countdown, and the calls
  `BeginAfternoon()` / `BeginEvening()` / `AdvanceDay()`.

### B2. Morning → Afternoon wiring — ❌ CRITICAL
- `UtilityAI_CombatController.GetPotionQuality01()` reads
  `CraftingManager.Instance.CurrentOrder.qualityScore` — a single global number.
  Nothing converts a *specific brewed potion* (name + element + quality) into a
  *specific adventurer's `AdventurerLoadout`*.
- `CraftingManager.StartNewOrder()` hard-codes `defaultPotionName`; the Counter
  screen can't choose what to brew.
- **Design need:** `LoadoutBuilder` — Counter confirm provisions a **Raw Sludge
  fallback bomb** (score 25, chosen element) so the adventurer is armed no matter
  what; Bottling seal replaces it with the real `BombData` (element from reagents,
  stats scaled by `CombatQuality.GradeFor(score)`). `StartNewOrder(potionName,
  element)` overload; drop `CraftingManager`'s `Awake()` auto-start.
  **No code path may leave an adventurer with an empty `AdventurerLoadout`**
  (reviewer C1).

### B3. Screens / UI — ❌ (greenfield, see `DESIGN.md` §7)
| Screen | State |
|---|---|
| Boot / Main Menu / Settings / Credits | ❌ |
| Save Slots | ❌ (no `SaveSystem`) |
| Day Intro card | ❌ |
| Morning shell (top bar, rail, order dock) | 🟡 rail logic in `StationUIManager`, no panels/chrome |
| Counter station | ❌ |
| Prep station | ❌ (no `HerbData`, no `PrepManager`, no minigame) |
| Cauldron station | 🟡 `CauldronUI` script exists, unwired, no viewport |
| Bottling station | ❌ (no `BottlingManager`, no minigame) |
| Loadout handoff | ❌ |
| Expedition HUD | 🟡 `OnGUI` debug only |
| Reasoning inspector | ❌ (data ready: `LastBreakdown`) |
| Evening: Report / Shop / Roster / Diary | ❌ all |
| Biome Map | ❌ (no `ProgressionManager`) |
| Tutorial layer | ❌ (no `TutorialManager`; `SetStationLocked` hooks exist) |
| Pause menu | ❌ |
| Toasts / confirm modal / custom cursor | ❌ |

### B4. Economy & meta — ❌
- No gold/herb/upgrade state at all (design puts it on `RunState`, not a
  manager — see `DESIGN.md` §3). `CombatQuality.PaymentMultiplier` / `GoldTip`
  exist but nothing pays out.
- **Loot fields were dropped from `MonsterData`** — the GDD spec had
  `goldDropMin/Max` + `possibleHerbDrops`; the actual `MonsterData.cs` has only
  identity + stats. Report screen + diary ingredient-discovery need these
  re-added (on `MonsterData` and/or a `BiomeData` loot table) + a drop resolver
  (reviewer round 3, X1).
- No `UpgradeManager` + `UpgradeData`; none of the GDD upgrades are real.
- No `RosterManager` — adventurers are code-spawned clones, not hired classes.
- No `ProgressionManager` — the 5 biomes exist as `BiomeData` but nothing
  sequences them or records a per-biome grade.

### B5. Save/Load — ❌
- No serialization at all. GDD explicitly wants JSON save of unlocked
  adventurers, gold, upgrades. Need `SaveSystem` + `SaveGame` model + 3 slots.

### B6. Story / narrative — ❌ (Story rubric = 0 today)
- No `DiaryEntryData`, no `DiaryManager`, no cutscene sequencer, no opening
  cinematic, no boss-sketch assembler. Nothing narrative exists in code.

### B7. Audio — ❌ (Sound rubric = 0 today)
- No `AudioManager`, no music beds, no SFX, no Animalese speech synthesis.
- `StationManager` has a `// TODO: Hook AudioManager cross-fade` marker only.

### B8. Tutorial & packaging — 🟡
- No `TutorialManager`; no spotlight/pointer system. Hooks exist
  (`StationManager.SetStationLocked`, `StationDefinition.TutorialHint`).
- ✅ Inno Setup script (`installer/AlchemistsArsenal.iss`) + `EXPEDITION_SETUP.md`
  exist per memory — packaging half of Usability is in hand.

### B9. Art pipeline — ❌
- 0 authored sprites. `PlaceholderArt` bakes shapes at runtime. All `*Data` SOs
  have `Sprite` fields ready and unused. Need the full manifest (§D).

### B10. Scenes / prefabs — ❌
- Only `SampleScene`. No `MainMenu`/`Game` scene split, no UI prefabs, no
  component prefab library, no bootstrap scene.

---

## C. Gaps by rubric category (Level 2 target)

| # | Category (`*` = weighted) | Level-2 bar | Today | Gap to close |
|---|---|---|---|---|
| 1 | **Gameplay & mechanics** `*` | "something I haven't seen before" — craft quality decays and modifies an auto-battler | 🟡 systems exist, **loop not connected**, not playable end-to-end | B1, B2 + a playable morning→afternoon→evening slice. This is the make-or-break. |
| 2 | **Story** | visualised (image/video/animation), no text walls | ❌ nothing | B6 — diary book, opening cinematic, assembling boss sketch, `DiaryEntryData` |
| 3 | **Assets** `*` | self-made | ❌ placeholder shapes | B9 — full pixel-art manifest (§D), wire `Sprite` fields, credits banner |
| 4 | **Size / levels** `*` | 5+ levels | 🟡 5 `BiomeData` designed, only 1 default built, no sequencing | B4 (`ProgressionManager`) + author all 5 biomes + biome map screen |
| 5 | **Physics** | physics as gameplay, new ways | ✅ cauldron stir (torque/tangential force) + ballistic bombs + `OverlapCircleAll` | keep; surface it well in the Cauldron viewport. Add detonation knockback impulse if not already (verify `BombProjectile2D`). |
| 6 | **Usability** `*` | installer and/or tutorial | 🟡 installer ✅, tutorial ❌, no clean UI | B8 (`TutorialManager` + spotlight/pointers) + the whole clean UI remake |
| 7 | **Sound & music** | music and/or speech | ❌ nothing | B7 — shop + forest music, event SFX, Animalese speech synth |
| 8 | **AI** | ML/advanced, not if/else | ✅ IAUS combat AI + boss HFSM + accumulator | keep; make it **visible** via the reasoning inspector (also helps cat 1 & 6) |

**Weighted priority order:** 1 → 4 → 3 → 6 (all `*`) then 2, 7 (currently 0) then
5, 8 (already strong, just needs to be *shown*).

---

## D. Pixel-art asset manifest (Assets rubric)

Group and owner TBD; every item self-drawn.

**UI chrome:** 9-slice panel (flat/raised/inset), button set, tab, slider track/
handle, checkbox, dropdown, scrollbar, tooltip, toast frame, modal frame,
progress ring, book/diary frame, parchment ticket, coin icon, padlock, check,
pointer arrow, spotlight vignette, custom cursor (arrow / hand / paddle).

**Element iconography:** 5 element badges × 3 sizes (colour + shape), ×2 grade
overlay; damage-number font sprites.

**Herbs:** ~8 herbs × {raw, chopped, dried, crushed} = ~32 jar/pile sprites +
8 shelf-jar icons.

**Bombs / flasks:** 4 flask shapes × 5 element fills = 20; cork/wax/rune sprites;
thrown-bomb sprite + trail + 5 element detonation bursts.

**Adventurers:** 3–5 classes × {portrait, idle, walk, throw, hurt, down} sprite
sets; speech-bubble frames.

**Monsters:** per biome ~3 archetypes × {idle, walk, hit, die} — Bark Treant,
Emberling, Frostkin + Cinder/Frost/Venom variants ≈ 12–15 monster sets.

**Boss:** Coven Matriarch — idle, 4 phase poses, attack frames (Arcane Bolt,
Coven Slam, Ward Pulse), ward shield bubble; **plus the assembling diary sketch
in 5 layers.**

**Biomes:** 5 × parallax background (3 layers each) + ground tile + ambient
particle sheet + biome map key-art + 5 map nodes + path trail.

**Story cutscenes:** opening curse cinematic (~6–8 frames), ~9 diary entry
illustrations (1–6 frames each), witch + loved-one character art.

**Menu / misc:** studio splash logo, title logo, cottage interior parallax
(3 layers), sleeping cat idle, credits art.

---

## E. Cross-cutting design risks

1. **Loop legibility.** If the player can't *see* that a Poor potion did half
   damage, cat-1 novelty is invisible. → Report screen's "potion performance"
   card + in-combat damage numbers are load-bearing, not polish.
2. **Morning pace vs. tutorial.** Four stations + minigames in a time budget is a
   lot to teach. The Day-1 slow clock + station lockout must be generous.
3. **Scope of art.** ~150+ sprites for a student team. The manifest should be
   triaged into "MVP look" (chrome + 1 biome + 1 boss + core cutscenes) vs.
   "full" so cat-3 hits Level 2 even if later biomes reuse palettes.
4. **`QualityTier` vs `CombatQuality`** ⚠ → **resolved:** `QualityTier` is
   deleted (ROADMAP 1.8b); all player-facing grades use `CombatQuality` 4-band.
5. **Expedition = spectator.** Risk it feels passive. Mitigations: speed control,
   reasoning inspector, and the payoff being *entirely* set by your morning.
6. **`Time.timeScale` collisions** — expedition speed control, pause menu, and
   tutorial slow-mo all want it. One `TimeControl` owner, stacked requests.
