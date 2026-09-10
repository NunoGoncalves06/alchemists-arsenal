# Alchemist's Arsenal — Build Roadmap

> Step-by-step: what is **done**, what is **in progress**, what is **to do**, in
> build order. Companion to `DESIGN.md` (what) and `DESIGN_GAPS.md` (why).
> Each task has an **acceptance check**. Rubric tags: `[1]`=Mechanics `[2]`=Story
> `[3]`=Assets `[4]`=Levels `[5]`=Physics `[6]`=Usability `[7]`=Sound `[8]`=AI.

---

## DONE ✅ (in `git`, verified compiling)

| # | What | Commit | Acceptance |
|---|---|---|---|
| D1 | Bootstrap Unity 6000.6 2D URP project, cauldron system seed | `5fa36bf` | project opens |
| D2 | **Station tab-nav FSM** — `StationManager`, `StationSet`/`StationDefinition` SOs, `StationUIManager` view, generators | `a731064` | `[6]` FSM switches, locks, events; `StationTabPhysicsSimulationTest` passes |
| D3 | **IAUS combat AI** — `UtilityAI_CombatController`, `CombatDecisionEngine`, considerations, registries | `e1a1406` | `[8]` `UtilityAiSimulationTest` passes |
| D4 | **Ballistic launcher** — `BallisticSolver`, `BallisticBombLauncher`, `BombProjectile2D`, `CombatQuality` 4-band | `95c46bf` | `[5]` `BallisticLauncherSimulationTest` passes |
| D5 | **Boss HFSM** — `BossPhaseManager`, `BossBehaviourRunner`, `ElementalDamageAccumulator`, phase scorer, adventurer movement FSM, ward loop | `617879e` | `[8]` `BossAndMovementSimulationTest` passes |
| D6 | **Playable expedition slice** — `ExpeditionBootstrap` (code-assembles arena), `ExpeditionManager`, `MonsterSpawner`, `DefaultExpeditionData`, `BiomeData`/`AdventurerData` SOs, `PlaceholderArt`, `OnGUI` HUD | `8de2778` | drop `ExpeditionBootstrap` on empty scene, press Play → real fight, `ExpeditionSimulationTest` win/lose |
| D7 | Reviewer round (adversarial + named-persona) findings addressed — decisive transitions, wave timeout, bootstrap split | `da61104` | re-review verdict CONCERNS (no criticals) |
| D8 | Combat/station/boss `ScriptableObject` assets generated + tracked | `d6534a7`, `a4f63c6` | assets in `Assets/ScriptableObjects/` |
| D9 | Inno Setup installer `installer/AlchemistsArsenal.iss` + `EXPEDITION_SETUP.md` | `8de2778` | `[6]` (packaging half) |
| D10 | **This design pass** — `DESIGN.md`, `DESIGN_GAPS.md`, `ROADMAP.md`, `DESIGN_REVIEW_LOG.md`, design canvas artifact | _this branch_ | docs reviewed through 2 reviewer rounds |

---

## PHASE 0 — The "Level-2 Floor" vertical slice  🔴 DO THIS FIRST (reviewer B-R2)

> **Why this exists:** the rubric is scored on what a grader sees *running*. A
> plan that builds the whole game phase-by-phase risks ending with four
> categories polished and four at zero. Phase 0 is **one thin slice that touches
> all 8 categories at minimum-viable**, shippable on its own. Only `[floor]`
> items. Everything in Phases 1–6 below is *deepening* that comes after.
>
> **Slice content:** Boot → Main Menu → New Game → Day 1 (tutorial: Counter +
> Cauldron only) → Afternoon (Biome 1, 3 waves, no boss) → Evening (Report +
> Diary) → Day 2 (Prep unlocks) → … → Biome 1 boss → opening + 2 diary entries.
> One biome fully art-passed, one music track pair, Animalese, installer.

| # | Task | Rubric | Acceptance |
|---|---|---|---|
| 0.1 | Scene skeleton (§11): `Boot` + `Core` + `Shop` + `Expedition` additive | — | Boot → Core; GameLoop loads/unloads Shop⇄Expedition |
| 0.2 | `GameLoopManager` + `GamePhase` + morning budget (`unscaledDeltaTime`) + `TimeControl` | `[1]` | headless walk Boot→Morning→Afternoon→Evening→Day2 |
| 0.3 | `SaveSystem` + `RunState` + JSON (atomic, versioned) + autosave on phase boundary; 1 slot is fine for the slice | `[6]` | quit mid-day, continue → same state |
| 0.4 | `StartNewOrder(name, element)`; order born at score 25; **drop BOTH auto-starts** (`Awake` + `CompleteActiveOrder`'s re-start — reviewer X3), keep `bootstrapOrderForTests`; `LoadoutBuilder` builds a **fresh runtime** `AdventurerLoadout` (never the SO) → 1 `BombData`, grade via `CombatQuality`, at `BeginAfternoon` | `[1]` | never-brew → Poor bomb thrown; good brew → visibly bigger numbers; sims still pass via flag |
| 0.4a | `ActiveOrder` gains `AdjustQuality(delta, station, reason)` (increase + decrease); `OnQualityChanged` fires **both directions** (reviewer X4) | `[1]` | clean chop raises score & pulses meter up; bad heat lowers it |
| 0.5 | `BombProjectile2D.OnDetonated(pos, element, grade, hitCount)` (detonation is in the projectile, not the launcher — X2); telemetry subscribes to it + `CombatantBody.OnDied`/`OnDamaged` (**existing** events — X1) + `MonsterSpawner` instance→`MonsterData` map for loot; `ExpeditionReport` accumulates | `[1]` | report has per-bomb damage + grade delta + loot |
| 0.6 | `UIManager` router + minimal `UITheme` + the ~8 core components (§8) as rough prefabs | `[6]``[3]` | screens switch; components themed |
| 0.7 | Morning shell + Counter panel + Cauldron panel (wire existing `CauldronUI`) — **no Prep/Bottling yet** | `[6]``[5]` | craft a potion end-to-end with 2 stations; physics cauldron on screen |
| 0.7a | Harden scene lifecycle (§11): `StationManager`+`CraftingManager` → `Core`; `PhysicsCauldronManager.OnDestroy` nulls `Instance`; `CauldronUI` re-binds on `Shop` reload (reviewer X5) | `[6]` | unload+reload `Shop` twice → cauldron + heat UI still work, no `NullReferenceException` |
| 0.8 | Expedition HUD (boss bar, party dock, always-on AI ticker, result slab) — delete `OnGUI` | `[6]``[8]` | all values bound; AI ticker shows element matchups |
| 0.9 | Evening: Report screen + `Economy` payout by grade | `[1]` | pay = base × `PaymentMultiplier(grade)` shown explicitly |
| 0.10 | `DiaryEntryData` + diary screen (2-page, page-turn) + `CutsceneSequencer` + opening cinematic + 2 entries | `[2]` | opening plays once; biome-1 clear unlocks an entry |
| 0.11 | `TutorialManager` FSM for Counter + Cauldron (spotlight + pointer + slowed budget) | `[6]` | Day 1 guided; Day 2 normal speed |
| 0.12 | `AudioManager` + shop/forest music pair + ~8 event SFX + Animalese synth on `SpeechBubble` | `[7]` | music crossfades on phase; adventurer "speaks" |
| 0.13 | Biome 1 art pass: bg parallax, ground, 3 monsters, herbs, UI chrome, 1 boss, 5-layer diary sketch | `[3]``[4]` | biome 1 looks final; sketch assembles |
| 0.14 | `Systems.QualityTier` deleted → `CombatQuality` everywhere | `[1]` | grep clean |
| 0.15 | Build → `ISCC.exe` → install on clean VM → smoke test the whole slice | `[6]` | `Setup.exe` installs & the slice is playable start→boss |

**Phase 0 exit = a submittable game.** Every rubric category is at ≥ Level 1,
most at Level 2. Re-run `/eval-rubric-auditor` + both adversarial reviewers here.
Then, and only then:

---

# DEEPENING — Phases 1–6

> Everything below raises quality/scope past the floor. Safe to cut, reorder, or
> partially land. Numbers kept from the pre-review draft for traceability;
> several tasks are now **already done in Phase 0** (marked ⟵0.x).

## PHASE 1 — Harden & complete the loop  `[1]`

> `GameLoopManager`/`SaveSystem`/`LoadoutBuilder`/telemetry/scene skeleton land in
> Phase 0. Phase 1 = the parts the slice deferred: 2nd–5th biomes, multi-slot
> saves, `RunState` polish, replay mode.

| # | Task | Depends | Acceptance check |
|---|---|---|---|
| # | Task | Depends | Acceptance |
|---|---|---|---|
| 1.0–1.5 | ⟵ **done in Phase 0** (scene skeleton 0.1, `TimeControl` 0.2, `GameLoopManager` 0.2, `StartNewOrder`+`LoadoutBuilder` 0.4, `ActiveOrder.AdjustQuality` 0.4a, `OnDetonated`+telemetry 0.5, scene-lifecycle hardening 0.7a, `QualityTier` delete 0.14, `UIManager` 0.6) | | |
| 1.6 | Meta state lives on **`RunState`** (gold, owned herbs/upgrades/adventurers, `currentBiomeIndex`, `bestGrades[]`, `unlockedDiary[]`, party) + stateless helpers — **no `InventoryManager`/`RosterManager`/`UpgradeManager`/`ProgressionManager` classes** (`DESIGN.md` §3, reviewer C-R2a) | 0.3 | one serializable struct round-trips through save; helpers are pure |
| 1.7 | Multi-slot saves (3) + Save Slots screen; `saveVersion` `Migrate()` real (not stub) | 0.3, 2.4 | 3 independent runs; old-version save migrates |
| 1.9 | Author biomes **2–5** `BiomeData` (waves + themes; final boss on #5) `[4]` | 0.13 | each of the 5 biomes runs to win/lose |
| 1.10 | Biome Map screen + replay mode (×0.5 gold, non-advancing — `DESIGN.md` §7.10) | 1.6, 1.9 | 5 nodes, grades, replay pays reduced, never soft-locks |

**Deepening Phase 1 exit:** all 5 biomes sequenced, multi-slot saves, no
god-object managers.

---

## PHASE 2 — Full breadth of screens + UI polish  `[6]``[3]`

> Phase 0 built the *slice* screens rough (Boot, Menu, Morning shell, Counter,
> Cauldron, Expedition HUD, Report, Diary) on a minimal theme. Phase 2 = the
> **remaining** screens (Settings, Credits, Save Slots, Day Intro, Prep, Bottling,
> Loadout handoff, Shop/Upgrades, Roster, Pause) + a real component library, real
> `UITheme`, and a polish pass on the slice screens. Every screen per `DESIGN.md`
> §7. The `OnGUI` HUD is already gone (0.8).

| # | Task | Depends | Acceptance |
|---|---|---|---|
| 2.1 | `UITheme` SO (palette/type tokens) + `Assets/UI/Components/` prefab library (§8): `PixelPanel/Button/TabButton/ElementBadge/GradeBadge/QualityMeter/HeatGauge/Toast/ConfirmModal/ResourceCounter/PortraitCard` | 0.6 | component gallery scene renders all states |
| 2.1a | Migrate `StationUIManager` label lookup from `UnityEngine.UI.Text` → `TMP_Text`; standardise every component on TextMeshPro (reviewer X6) | 2.1 | grep: no `UnityEngine.UI.Text` in `Assets/Scripts/UI`; station tab labels render in TMP |
| 2.2 | `ToastService` + `ConfirmModal` + custom cursor | 2.1 | toast on order seal; hold-to-confirm works |
| 2.3 | Boot → Main Menu → Settings → Credits screens; `SettingsService` (PlayerPrefs, live apply) | 2.1 | new game reaches Day Intro; settings persist |
| 2.4 | Save Slots screen | 1.7, 2.1 | 3 slots: new/continue/delete(hold) |
| 2.5 | Day Intro card | 1.1, 2.1 | shows day + biome, auto-advances |
| 2.6 | **Morning shell** — top bar (day/gold/`PhaseClock`/biome chip), station rail (reuse `StationUIManager`), right order dock (`OrderTicket`+`DeductionLog`+`QualityMeter`), `SEND TO EXPEDITION` CTA | 1.1–1.4, 2.1 | rail switches real panels; ticket tracks `ActiveOrder` live |
| 2.7 | **Counter** panel — `WaveForecastStrip`, `ElementDemandBar`, `AdventurerOrderCard`; `ForecastService`; confirm → `StartNewOrder(...)` | 1.3, 1.9, 2.6 | forecast shows real waves; choice sets the order |
| 2.8 | **Cauldron** panel — `CauldronViewport` (world sim via RenderTexture/camera, *outside* panel), wire existing `CauldronUI` refs, `HeatGauge`, `BrewProgressRing`, `ReagentDock` | 2.6 | stir + heat + quality all read real managers; sim keeps running on tab switch |
| 2.9 | **Prep** panel — `HerbData` SO, `PrepManager`, `RhythmBar`/`ProgressTimer`/`MashMeter`, reagent tray; sloppy work → `ActiveOrder.ApplyDeduction` | 2.6 | chop/dry/crush produce reagents; misses cost quality |
| 2.10 | **Bottling** panel — `BottlingManager`, `FlaskCarousel`, `FillMeter`, `CorkPicker`, `BombLabelPreview`+`GradeBadge`; seal → `LoadoutBuilder` + `CompleteActiveOrder` | 1.4, 2.6 | sealed bomb shows correct 4-band grade; arms adventurer |
| 2.11 | Loadout handoff screen | 1.4, 2.1 | party cards show brewed bombs + grades; begin expedition |
| 2.12 | **Expedition HUD** — `BossHealthBar`+`PhasePips`, party dock (`PortraitCard`/`AmmoPips`/`CooldownRadial`), `SpeedControl`, damage numbers, `AdvantagePopper`, `ResultSlab`; delete `OnGUI` | 1.5, 2.1 | all values read `ExpeditionManager`/`BossPhaseManager`/`CombatantBody`; result → Evening |
| 2.13 | Reasoning inspector — bind `UtilityAI_CombatController.LastBreakdown` `[8]` | 2.12 | toggles; shows live scored candidates for selected adventurer |
| 2.14 | `TimeControl` single owner (pause + speed + tutorial slow, stacked) | 2.12 | 2×, pause, and tutorial slow don't fight |
| 2.15 | Pause menu | 2.3, 2.14 | resume restores prior timescale |
| 2.16 | **Evening shell** + Report screen (`PayBreakdown` shows `PaymentMultiplier`, `PotionPerfCard`) | 1.5, 2.1 | pay = base × grade mult (+tip); potion perf card explains the delta |
| 2.17 | Shop/Upgrades screen + `UpgradeManager` + `UpgradeData` SOs; station managers read `UpgradeManager.Has()` | 1.2, 2.16 | buy *Instant Dryer* → Prep dryer becomes instant |
| 2.18 | Roster screen + `RosterManager` + hire flow | 1.2, 2.16 | hire a class, set tomorrow's party |
| 2.19 | Biome Map screen | 1.6, 2.16 | 5 nodes, grades, current pulses, sleep→save→next day |

**Phase 2 exit:** no `OnGUI`, no unbound scripts, every screen in `DESIGN.md`
exists and is theme-consistent.

---

## PHASE 3 — Story  `[2]`

| # | Task | Depends | Acceptance |
|---|---|---|---|
| 3.1 | `DiaryEntryData` SO (`entryText`, `Sprite[] frames`, `frameRate`, `unlockCondition`) | — | authorable entries |
| 3.2 | `DiaryManager` — `Unlocked`, `Unlock()`, milestone hooks (biome clear, first Perfect, ingredient discovery) | 1.5, 1.6 | clearing biome 1 unlocks its entry |
| 3.3 | Diary book screen — two-page, `PageTurn`, `CutsceneSequencer` (frame swapper), locked-entry silhouettes | 2.16, 3.1 | page turn anim; multi-frame entries animate + replay |
| 3.4 | `BossSketchAssembler` — 5-layer Coven Matriarch sketch, +1 layer per biome | 1.6, 3.3 | sketch grows each clear |
| 3.5 | Opening cinematic = `DiaryEntry_00`, auto-play after first Day Intro | 2.5, 3.3 | plays once per save, skippable |
| 3.6 | ~9 diary entries authored (text + illustrations) `[3]` | 3.1, art | full arc: curse origin → coven reveal → sequel hook |

---

## PHASE 4 — Sound  `[7]`

| # | Task | Depends | Acceptance |
|---|---|---|---|
| 4.1 | `AudioManager` — mixer groups (Master/Music/SFX/Speech), `Play(cue)`, adaptive music crossfade | 1.1 | shop↔forest track crossfades on phase change |
| 4.2 | Wire all `DESIGN.md` §5.6 cues (ui/craft/cauldron/expedition/boss/diary/coin) | 4.1, 2.x | each UI event triggers its cue |
| 4.3 | Cauldron bubble loop pitch tracks `Heat01` | 4.1, 2.8 | audible heat feedback |
| 4.4 | **Animalese speech synth** — pitch-shifted vowel sequencer; `SpeechBubble` triggers it | 4.1 | adventurer "speaks" gibberish on order/complaint/cheer/death |
| 4.5 | Shop (cozy) + 5 biome (tense, per-element) music tracks, event SFX bank `[3]` | 4.1 | all self-made, credited |

---

## PHASE 5 — Tutorial & polish  `[6]`

| # | Task | Depends | Acceptance |
|---|---|---|---|
| 5.1 | `TutorialManager` FSM — steps Welcome→Counter→Prep→Cauldron→Bottling→Handoff→Done; slows clock; locks non-target stations via `StationManager.SetStationLocked` | 1.1, 2.6 | Day-1 only; each step gated on a real completion signal |
| 5.2 | `SpotlightMask` (stencil) + `PointerArrow` + `CoachBubble` + progress dots; copy from `StationDefinition.TutorialHint` | 5.1, 2.1 | dim + spotlight + arrow points at the live target |
| 5.3 | "Time slowed — Day 1" badge; restore on `Done`, never re-show for that save | 5.1, 1.7 | second day runs at normal speed |
| 5.4 | Reduce-Motion / screen-shake / text-size settings honoured everywhere | 2.3 | toggling kills shake + parallax, scales text |
| 5.5 | Full build → `ISCC.exe` installer smoke test on a clean VM | 2.x | `AlchemistsArsenal-Setup.exe` installs & launches |

---

## PHASE 6 — Art production  `[3]` (parallel from Phase 2)

| # | Task | Acceptance |
|---|---|---|
| 6.1 | UI chrome sprite set (§D) + `UITheme` wired to 9-slices | components use real sprites |
| 6.2 | Element badges, grade badges, damage-number font | in HUD + tickets |
| 6.3 | Biome 1 (Whispering Woods) full art — bg parallax, ground, 3 monsters, herbs | biome 1 looks final |
| 6.4 | Coven Matriarch boss art + 5-layer diary sketch | boss + sketch assembler |
| 6.5 | Adventurer classes (portrait + anim sets) | expedition + roster |
| 6.6 | Biomes 2–5 art (palette-swap where sensible to fit scope) | all 5 biomes themed |
| 6.7 | Cutscene illustrations (opening + 9 entries) | diary complete |
| 6.8 | Menu / title / credits art + "all assets in-house" banner | main menu final |

---

## Recommended sequencing

```
   ┌─────────────────────────────────────────────────────┐
   │  PHASE 0  —  Level-2 Floor vertical slice            │  ← ship this first;
   │  (all 8 categories, minimum-viable, one biome)       │    it is a submittable game
   └─────────────────────────────────────────────────────┘
                          │  re-run all 3 review skills + eval-rubric-auditor here
                          ▼
   Deepening 1 (5 biomes, saves)  ──►  Deepening 2 (all screens + polish)
                                              │
                Deepening 6 (art) ────────────┤  (parallel, once components exist)
                Deepening 4 (sound) ──────────┤
                Deepening 3 (story) ──►  Deepening 5 (tutorial + a11y polish)
```

**Phase 0 is the gate.** It is deliberately a complete-but-shallow game, not a
deep-but-partial one — a grader can score every rubric box from the slice alone.
Re-run `/adversarial-reviewer` + `/named-persona-adversarial-review` +
`/eval-rubric-auditor` at the end of Phase 0 and after each Deepening phase.
