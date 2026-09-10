# Design Review Log

Recursive reviewer passes over `DESIGN.md` / `DESIGN_GAPS.md` / `ROADMAP.md`.
Each round: run the reviewer skill → record findings → apply fixes to the design
→ note the resolution. Skills used: `engineering-skills:adversarial-reviewer`,
`engineering-skills:named-persona-adversarial-review`, `engineering-skills:code-reviewer`.

---

## Round 1 — `adversarial-reviewer` (Saboteur / New Hire / Integrity Auditor)

**Reviewed:** `DESIGN.md` v0.1, `DESIGN_GAPS.md` v0.1, `ROADMAP.md` v0.1
**Verdict:** 🔴 **BLOCK** — 2 critical design holes, 7 warnings.

### CRITICAL

**C1 — No "minimum viable potion". A rushed/incomplete craft silently guarantees a lost expedition.**
_Saboteur + New Hire._ If the morning clock expires during Prep (before any
reagent reaches the cauldron), `LoadoutBuilder` derives the bomb element "from
reagents" — of which there are none. The adventurer enters the expedition with an
empty `AdventurerLoadout`; `UtilityAI_CombatController` finds `_readyBombs.Count
== 0`, never throws, and the run is an unwinnable stalemate until the wave
timeout, with **no feedback explaining why**. The core loop has no floor.
→ **Fix:** define a guaranteed baseline. Confirming the order at Counter locks in
`(potionName, element)` immediately and provisions a **1-charge "Sludge"
fallback bomb** of that element at a fixed Poor grade (score 25). Any real
brewing improves on it. `LoadoutBuilder` always emits ≥1 usable `BombData`.
Bottling seal replaces the fallback; if never sealed, the fallback ships. The
Report screen explicitly calls this out ("Unfinished potion — shipped as raw
Sludge, ×0.50 damage").

**C2 — The time-control model is unspecified and self-contradictory.**
_Saboteur + New Hire (both hit it)._ §7.6 and §7.11 say the tutorial "slows the
`GameLoopManager` clock"; §7.8 has expedition speed writing `Time.timeScale`;
§7.12 pause "stores/restores prior `Time.timeScale`"; ROADMAP 2.14 belatedly adds
a `TimeControl` owner. These fight: if tutorial slow-mo and pause both mutate
`Time.timeScale`, restore logic clobbers; and slowing `Time.timeScale` during the
morning changes cauldron stir feel.
→ **Fix:** new §2.5 "Time model", authoritative:
- **Morning budget is real wall-clock** (`unscaledDeltaTime`), never `Time.timeScale`.
- Tutorial "slow" = a `GameLoopManager.budgetRateMultiplier` (0.35×) on the
  budget drain only. Simulation runs at normal speed.
- `Time.timeScale` has exactly **one owner: `TimeControl`** (built in Phase 1,
  not Phase 2). Only expedition speed (1×/2×) and pause (0×) push requests;
  `TimeControl` stack-restores. Tutorial never touches it.
- §7.6, §7.8, §7.11, §7.12 rewritten to reference §2.5.

### WARNINGS

**W1 — `ActiveOrder` lifecycle is ambiguous; Counter can silently reset a
partly-brewed order.** `CraftingManager.Awake()` auto-calls `StartNewOrder()`.
If Counter *also* creates the order on confirm, a player who stirs before
confirming loses that progress (quality resets to 100). → Remove the `Awake`
auto-start (guard behind a `bootstrapOrderForTests` flag so headless tests keep
working — ROADMAP 1.3). **Counter confirmation is the sole creator of the day's
`ActiveOrder`.** Added to DESIGN §7.6.1 + GAPS B2.

**W2 — Design asserts events that don't exist.** `ExpeditionTelemetry` "listens
to throw/detonate/loot"; only `OnBombThrowRequested` exists. → New ROADMAP task
**1.5a**: add `BallisticBombLauncher.OnDetonated(Vector3 pos, ElementType,
PotionGrade, int hitCount)` and `CombatantBody.OnKilled(Team, MonsterData?)`.
Telemetry + damage numbers + diary ingredient-discovery all depend on these.

**W3 — Scene / world architecture is undefined.** The cauldron sim must be alive
during Morning; the expedition arena is a physically separate space. One scene
with spatial offset? Additive scenes? → New DESIGN §11 "Scene & world layout":
single `Game` scene; Morning shop world at origin, expedition arena at x+500;
one persistent `UIManager` canvas; camera cuts between them on phase change; sim
roots (`PhysicsCauldronManager`, arena) are never under UI/screen roots.
ROADMAP **1.0** added (scene skeleton) as the first Phase-1 task.

**W4 — `HeatGauge` vs the existing `CauldronUI` Slider contract.** §7.6.3 says
"keep `CauldronUI`, wire refs to new components", but `CauldronUI` is typed
against `Slider`/`Image`/`RectTransform`/`TextMeshProUGUI`. A bespoke radial
`HeatGauge` breaks that. → Decision: `HeatGauge` is a **horizontal `Slider`-based**
composite exposing the same four fields, OR `CauldronUI` gets a light refactor to
an `IHeatView` interface. DESIGN §7.6.3 + §8 updated to pick the interface route
(cleaner, keeps the physics/UI decoupling the codebase already values).

**W5 — Unarmed party members.** v1 brews for one adventurer, but
`ExpeditionBootstrap` spawns 2 with identical loadouts. → DESIGN §7.7 + §2.6:
**v1 spawns exactly 1 adventurer.** Party grows via the roster unlock; each
extra slot needs its own morning craft (or ships a Sludge fallback). One clear
rule, stated once (removes the per-screen "up to 4 later" hedging — W-NH4).

**W6 — Save robustness.** No `saveVersion` field, no atomic write, save only
"on sleep". A schema change bricks every existing save; a crash mid-write
corrupts the slot; an afternoon crash loses the whole day. → ROADMAP 1.7
acceptance updated: `int saveVersion` + migration stub, write-temp-then-`File.
Replace`, and **autosave at every phase boundary** (morning done / expedition
done / sleep), not just sleep.

**W7 — Two persistence systems for settings.** §7.3 puts settings in
`PlayerPrefs`; §9 puts a `SettingsBlock` in `SaveGame`. → Settings are **global**
(`PlayerPrefs` via `SettingsService`). `SettingsBlock` removed from `SaveGame`.
The per-save tutorial-seen flag stays in `SaveGame` (+ a "replay tutorial"
button in Settings).

### NOTES (applied silently to the docs)
- N1 reasoning inspector: stale/empty `LastBreakdown` between waves → show
  "waiting for a decision…" placeholder; ship **off by default**, toggle in
  pause menu, framed as "show adventurer thinking".
- N2 economy soft-lock: Poor pay is ×0.40 of base (non-zero) so no hard
  bankruptcy, but add an explicit **"replay a cleared biome for reduced gold"**
  path so a stuck player is never fully blocked. Added to DESIGN §7.10.
- N3 `DiaryEntryData.unlockCondition` needs a type → small `DiaryUnlock { enum
  Kind, string param }`. DESIGN §7.9.4.
- N4 biome for "today" = `ProgressionManager.CurrentBiomeIndex`, fixed at day
  start; replay is a distinct non-advancing mode selected on the Biome Map before
  sleep. DESIGN §7.10 clarified.

### Summary
The visual/screen design is solid; the **loop-integrity** layer was thin. The
single most important fix is **C1** — the loop must have a floor so a bad morning
is a *worse expedition*, never a silent unwinnable one. C2 + W3 remove genuine
"a second engineer would build the wrong thing" ambiguity. All 13 items applied
in `DESIGN.md` v0.2.

---

## Round 2 — `named-persona-adversarial-review`

**Personas:** Cagan + Jobs (product), Brooks + Torvalds (engineers), all grounded
in `references/persona_principles.md`.
**Reviewed:** `DESIGN.md` v0.2, `DESIGN_GAPS.md`, `ROADMAP.md` (post round 1).
**Verdict:** 🔴 **BLOCKER** — 1 blocker, 2 criticals, 4 warnings. The screen
design is sound; the **plan** optimises the wrong thing and the **manager
topology** is over-built.

### BLOCKER

**B-R2 — No "rubric floor" milestone; the plan is solution-first, not
problem-first.** _Cagan (fall in love with the problem — high, SVPG/*Inspired*)
concurring with Jobs (start from the experience — high, WWDC 1997)._ The problem
this project actually has: *a student team must show all 8 rubric categories at
Level 2 by a deadline.* The roadmap is 6 sequential phases / ~50 tasks / ~150
sprites / 16 managers — it builds the whole game. If time runs out mid-Phase-2
the result is an ugly connected loop with **zero** Story, Sound, Tutorial. Every
category at 1→2 beats four at Level 3 and four at Level 0.
→ **Fix:** new **Phase 0 — "Level-2 Floor"**: one thin vertical slice that
*touches every rubric category at minimum-viable* (1 biome, 1 boss, the physics
cauldron, IAUS visible, 3 diary entries + opening cinematic, 2 music tracks +
Animalese, Day-1 tutorial over the stations that exist, Inno installer, self-made
art for that one slice). Ship that, *then* the existing phases become
"deepening". ROADMAP restructured; old Phase 1–6 renumbered under a "Deepening"
banner.

### CRITICAL

**C-R2a — 16 global singletons is accidental complexity for this scope.**
_Brooks (essential vs accidental — high, *No Silver Bullet*)._ The essential
runtime state is small: one `RunState` (day, gold, biomeIndex, owned herbs /
upgrades / adventurers, best grades, unlocked diary, tutorial flag) plus its
`SaveGame` mirror. `InventoryManager` / `RosterManager` / `UpgradeManager` /
`ProgressionManager` are CRUD over that state — they become **fields on
`RunState` + small stateless helpers**. `ForecastService` / `LoadoutBuilder` /
`ExpeditionTelemetry` hold no lifetime — **plain classes / static functions**,
not MonoBehaviours.
→ **Fix:** DESIGN §3 + §9 rewritten. Target **6 MonoBehaviour managers**:
`GameLoopManager`, `SaveSystem` (owns `RunState`), `AudioManager`, `UIManager`,
`TimeControl`, `TutorialManager`. Everything else is data or pure logic.

**C-R2b — the Raw Sludge "fallback" (round-1 C1 fix) is a special case that
leaks into every downstream system.** _Torvalds (eliminate the special case —
high, TED 2016) concurring with Brooks._ "Every real crafting step *replaces* the
fallback" forces Report / telemetry / payment / diary to branch on "real potion
vs Sludge".
→ **Fix (good-taste restructure):** there is **no fallback and no replace**. The
day's `ActiveOrder` is *created at score 25* (Poor) of the Counter-chosen
element. Crafting only ever **modifies that one order** (deductions and
bonuses both). Bottling seal is "finalize", not "swap". A never-brewed potion
*is* a score-25 potion — the existing `CombatQuality` math already grades it
Poor. No `isSludge` flag anywhere. DESIGN §7.6.4 + §3 + ROADMAP 1.4 rewritten.

### WARNINGS

**W-R2a — the first 3 minutes are five interaction models.** _Jobs (design is
how it works — high, NYT 2003)._ Counter + rhythm-chop + dryer-timer + mash-meter
+ stir-physics + stop-the-meter fill, all under a clock, is too much front door;
the tutorial slow-clock is a patch on that. → DESIGN §7.6: **Prep unlocks Day 2,
Bottling Day 3** (via the station-lock mechanism that already exists — used for
*pacing*, not only tutorial). Day 1 = Counter + Cauldron only (the physics
centrepiece + the one real decision).

**W-R2b — quality is judged on consequences the player can't see in the
moment.** _Jobs._ Deductions accrue across 4 screens and surface only as a text
log ("−7 Cauldron: too hot"). → every deduction must have an **immediate physical
on-screen consequence** at its station (brew visibly scorches / darkens; a
mis-timed chop visibly shatters the herb; early dryer pull = herb stays green and
damp). The `DeductionLog` becomes a *recap*, not the player's mental model.
DESIGN §7.6.2–.4 + §7.9.1 updated.

**W-R2c — the AI being visible is a rubric differentiator, and it's buried.**
_Cagan._ Reasoning inspector "off by default, in a submenu" means graders won't
see cat-8 / the novelty. → add a **lightweight always-on ticker** ("Rookie chose
Tidevial — Water ×2 vs Bark Treant") above the party dock; the full breakdown
panel stays the toggle. DESIGN §7.8.

**W-R2d — single scene with spatial offset (`ShopWorld @0`, `ExpeditionArena
@+500`) is an accidental-complexity trap.** _Brooks._ Physics layers, camera
culling, light bleed, stray `FindObjectsOfType`. → DESIGN §11: **three additive
scenes** — `Core.unity` (persistent managers + UI), `Shop.unity`,
`Expedition.unity`; `GameLoopManager` loads/unloads Shop ⇄ Expedition on phase
change. Simpler to reason about than one big offset scene.

### NOTES (applied silently)
- N-R2a `IHeatView` (round-1 W4) is premature abstraction with one implementer
  (Beck: fewest elements). → reverted: `HeatGauge` is simply a `Slider`-based
  composite exposing the four fields `CauldronUI` already expects. No interface
  until a second gauge exists.
- N-R2b tag every design element `[floor]` (rubric-load-bearing) vs `[deepening]`
  (`RushModifierData`, replay mode, 3 save slots, 4 flask shapes, cork types,
  `BossSketchAssembler`, parallax menu are all `[deepening]`).
- N-R2c evening sub-tabs and the station rail are two nav systems — both are just
  `UIManager` screen groups; station-nav isn't special (it keeps `StationManager`
  as its *state*, but the rail is a plain screen-group view).

### Integrity check (Feynman)
Would these people's documented philosophy actually point here? Cagan on
problem-vs-solution → yes, the roadmap is a solution artifact. Brooks on
manager count → yes, 16 globals is textbook accidental complexity. Torvalds on
the Sludge branch → yes, "replace the fallback" is exactly the special-case
smell his linked-list example targets. Jobs on the busy front door → yes.
Findings stand on merit independent of the names.

### Summary
Round 1 fixed loop *integrity*; round 2 fixes plan *focus* and architecture
*weight*. Do B-R2 (rubric-floor reorder) before any code. C-R2a/b make the build
smaller and the loop model cleaner. 11 items applied in `DESIGN.md` v0.3 /
`ROADMAP.md` v0.2.

---

## Round 3 — `code-reviewer` (design ↔ actual `Assets/Scripts`)

**Loaded:** `rules/universal.md` + `languages/csharp.md`. Checked every API
`DESIGN.md` §3/§7 names against the real source.
**Verdict:** 🟠 **Request changes** — the visual/UX design is sound and the
"6 managers + pure helpers + `RunState`" topology fits the codebase's style, but
**6 design claims contradict the actual code.** All fixable in the docs.

### Confirmed correct
`StationManager` (events `OnStationChanged`/`OnStationEntered`/`OnStationExited`/
`OnLockStateChanged`, `TrySwitchStation`, `SetStationLocked`, `UnlockAllStations`,
`IsStationUnlocked`, `StationSet`) ✅ · `PhysicsCauldronManager` (`Heat01`,
`MinOptimalHeat`, `MaxOptimalHeat`, `OnHeatChanged`, `SetHeat`, `PhysicsStepCount`)
✅ · `ExpeditionManager` (`Phase`, `WaveNumber`, `TotalWaves`, `BossInstance`,
`OnPhaseChanged`, `OnWaveStarted`, `OnFinished`) ✅ · `UtilityAI_CombatController.
LastBreakdown` ✅ · `CombatQuality` static grade math ✅ · `CauldronUI` serialized
fields ✅ · `BossPhaseManager.CurrentPhase` ✅.

### CONTRADICTIONS (design fixed to match code)

**X1 — `CombatantBody` already has the death/damage events the design wanted to
add.** ROADMAP 1.5a proposed `CombatantBody.OnKilled(Team, MonsterData?)`. The
code already exposes `event Action<CombatantBody> OnDied` and `event
Action<DamageInfo> OnDamaged` (`DamageInfo` = amount, element, point, source).
`CombatantBody` does **not** retain its `MonsterData` (only `InitialiseFrom`
copies stats). → **Fix:** telemetry uses the existing `OnDied` / `OnDamaged`;
loot needs `MonsterSpawner` to keep an instance→`MonsterData` map (it spawns
them), not a new event field. ROADMAP 1.5a rewritten.

**X2 — detonation happens in `BombProjectile2D`, not `BallisticBombLauncher`.**
The launcher only spawns + `Configure()`s the projectile; `OverlapCircleAll` +
damage is inside `BombProjectile2D`. → **Fix:** the new detonation event goes on
**`BombProjectile2D`** (`OnDetonated(pos, element, grade, hitCount)`); the
launcher may forward it. DESIGN §7.8 + ROADMAP 1.5a corrected.

**X3 — `CraftingManager.CompleteActiveOrder()` auto-starts the next order** (and
`Awake` starts the first). The design's one-order-per-day + "Counter is the sole
creator" model needs *both* auto-starts gone. → **Fix:** ROADMAP 1.3 / DESIGN
§7.6.1 amended: `CompleteActiveOrder()` just fires `OnOrderCompleted` and clears
`CurrentOrder`; the next order is created only by the next day's Counter confirm.
Keep a `bootstrapOrderForTests` flag for the headless sims.

**X4 — `ActiveOrder` can only *lose* quality.** It starts at `qualityScore = 100`,
exposes `ApplyDeduction` (decrease only), and `OnQualityChanged` fires only on
deduction. DESIGN §7.6.1 now says the order is *born at 25* and clean steps
*raise* it. → **Fix:** new `ActiveOrder` API required — `ApplyBonus(int, station,
reason)` / or a single `AdjustQuality(delta, …)` — and `OnQualityChanged` must
fire on both directions. Added as ROADMAP **0.4a**. (Note: the "starts at 100 and
decays" model is what the rubric literally rewards — the *decay* mechanic — so
DESIGN §2.2 keeps decay as the framing; the born-at-25 change is specifically the
*floor* so an untouched order isn't a free Great. Reconciled in §7.6.1.)

**X5 — existing singletons are unplaced in the §11 scene model, and
`PhysicsCauldronManager` isn't safe for additive unload.** `StationManager` and
`CraftingManager` are `DontDestroyOnLoad` singletons that null `Instance` in
`OnDestroy`; `PhysicsCauldronManager` is a plain singleton that **does not null
`Instance` on destroy** and reads `Camera.main`/`Input` every `Update`. If
`Shop.unity` unloads/reloads (§11), `PhysicsCauldronManager.Instance` dangles and
`CauldronUI.OnEnable` can bind a destroyed manager. → **Fix:** DESIGN §11 +
GAPS updated: `StationManager` + `CraftingManager` move to `Core.unity` (they are
already `DontDestroyOnLoad`); `PhysicsCauldronManager` lives in `Shop.unity` and
gets an `OnDestroy` that nulls `Instance` (ROADMAP **0.7a**). The "6 managers" in
§3 are the *new* ones — the three existing crafting singletons are a documented
pre-existing set alongside them.

**X6 — TMP vs legacy `UnityEngine.UI.Text` mismatch.** `CauldronUI` uses
`TextMeshProUGUI`; `StationUIManager.ApplyDefinition` uses `UnityEngine.UI.Text`.
DESIGN §5.3 mandates TMP everywhere. → **Fix:** ROADMAP **2.1a** — migrate
`StationUIManager` label lookup to `TMP_Text`; standardise all components on TMP.

### NOTES (csharp.md idioms — for the build phase, not the design)
- `PhysicsCauldronManager.Update()` calls `OnHeatChanged` **every frame
  unconditionally** — the design's `HeatGauge` must not do per-frame layout work
  in the handler (cache, compare, early-out).
- `BombProjectile2D` default `detonationMask = 0` auto-resolves via
  `CombatLayers` — the new `OnDetonated` must fire *after* mask resolution.
- Prefer `record` for `RunState`'s nested value types; `CombatQuality` already
  models the house style (static + switch expressions).
- `AdventurerLoadout` is mutated at runtime only via a private dict in the AI
  controller (the asset is never touched) — `LoadoutBuilder` must follow the same
  rule: build a fresh runtime `AdventurerLoadout` instance, never write the SO.

### Summary
No blocker; the design is *implementable* but six statements would have sent a
builder down the wrong path (re-adding events that exist, hooking the wrong
class, leaving a dangling singleton). All six corrected in `DESIGN.md` v0.4 /
`ROADMAP.md` v0.3. The core architecture call — small manager set, pure helpers,
one `RunState`, event-driven views — is a good fit for how this codebase is
already written.

---

## Round 4 — convergence pass on v0.4 (all three lenses, abbreviated)

**Verdict:** 🟢 **CLEAN** — only NOTES; no new CRITICAL/WARNING. The recursive
loop has converged.

- **Saboteur:** the loop floor (born-at-25, single `ActiveOrder`, `LoadoutBuilder`
  at `BeginAfternoon`) closes the "silent unwinnable expedition" hole; the time
  model (§2.5) closes the `Time.timeScale` collisions; scene lifecycle (§11 +
  X5) closes the dangling-singleton hole. No remaining path to a stuck run.
- **New Hire:** §3 canonical-list + §11 scene table + the "screen specs name
  intent, not final types" note remove the "which class do I build" ambiguity.
  NOTE: a builder still needs the *layout numbers* — that is what the design
  canvas artifact is for; DESIGN.md §7 should link it once it exists. → done
  (this doc + ROADMAP D10 reference the canvas).
- **Integrity auditor:** save is versioned + atomic + autosaved; settings are
  single-sourced in `PlayerPrefs`; replay mode prevents economic soft-lock.
  NOTE: `saveVersion` migration is a *stub* in Phase 0 and only becomes real in
  Deepening 1.7 — acceptable for a slice with one save format, flagged so it
  isn't forgotten.
- **Cagan/Jobs:** Phase 0 (rubric-floor vertical slice) is now the plan's spine;
  progressive station unlock + physical deduction feedback + always-on AI ticker
  address the "too busy / illegible / buried novelty" findings.
- **Brooks/Torvalds:** 6 managers + `RunState` + pure helpers; no `isSludge`
  special case; `IHeatView` reverted. Manager count is proportionate.

**Exit:** 3 substantive rounds (each found real, applied fixes) + 1 clean
convergence pass. Per the skill's exit condition ("CLEAN on 2 consecutive
rounds" / "BLOCKER→fix→1 re-review"), the design review loop is **complete for
the documentation stage.** Re-open it after Phase 0 code lands (the reviewers
then have real code, not just docs, to bite).

---

## Round 3 — `code-reviewer`

_(pending)_
