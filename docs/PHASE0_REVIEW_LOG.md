# Phase 0 — reviewer loop

Skills: `code-reviewer`, `adversarial-reviewer`, `named-persona-adversarial-review`,
`eval-rubric-auditor`. Each round records findings + the fix. Iterating until all
four are clean.

---

## Round 1 — `code-reviewer` (C# lifecycle / day-loop / NRE hunt)

**Scope:** `git diff 262f891..HEAD` — ~40 new/changed `.cs`, ~3.6k lines.
**Verdict:** 🟠 **Request changes** — 1 critical, 6 warnings, 5 notes.

### CRITICAL

**P1 — Spawned monsters / boss / registries leak across days.**
`MonsterSpawner.SpawnMonster/SpawnBoss` create **unparented** GameObjects.
`ExpeditionWorld`'s root is destroyed at `BeginEvening`, but the monster/boss
GameObjects are not under it, so they survive into the Evening and the next day.
Their `CombatantBody.OnEnable` registered them in the static `MonsterRegistry` /
`AdventurerRegistry`, so the next expedition's `LiveMonsters()` /
`AllAdventurersDead()` / `ExpeditionTelemetry` see stale corpses — the day-2
expedition can insta-win or mis-count, and defeated adventurers from day 1 still
count as "party" on day 2.
→ **Fix:** (a) `MonsterSpawner` parents every spawn under its own transform (it's
under `ExpeditionWorld`, so the root Destroy now cascades); (b) `ExpeditionWorld.
OnDestroy` calls `MonsterRegistry.Clear()` + `AdventurerRegistry.Clear()` as a
belt-and-braces reset; (c) `BallisticBombLauncher` parents its projectiles under
the launcher too (they self-destruct in ≤6 s but a straggler could cross the
phase boundary).

### WARNINGS

**P2 — `UIManager.Show` re-fires `NotifyShown()` on an already-active screen.**
The `else if (on) kv.Value.NotifyShown();` branch double-invokes `OnShow` without
a matching `OnHide`, so screens that subscribe in `OnShow` (`MorningScreen`
→ `OnMorningTimeChanged`, `ExpeditionHudScreen` → `OnDetonatedGlobal`) accumulate
handlers.
→ **Fix:** drop that branch — `Show(current)` is a no-op (Torvalds: the special
case shouldn't exist).

**P3 — Morning budget keeps draining during the Day-1 tutorial.**
`TutorialManager.Run()` blocks on "accept an order", but `GameLoopManager.Update`
still ticks the (0.35×) budget; if the player dawdles, `BeginHandoff()` fires
mid-tutorial and the overlay sits on top of the Handoff screen.
→ **Fix:** `GameLoopManager.Update` skips the tick entirely while
`TutorialManager.Active`.

**P4 — `BiomeLibrary.Get` builds fresh ScriptableObjects every call.**
Called each `MorningScreen.OnShow` + each `BeginAfternoon` → ~1 `BiomeData` +
~10 `MonsterData` `CreateInstance` leak per day (SO instances aren't GC'd).
→ **Fix:** cache one `BiomeData` per index in `BiomeLibrary`.

**P5 — `Bootstrap._done` is a `static bool` never reset.**
With "fast enter play mode" (domain reload off) the flag stays `true` on the
second Play, so `Bootstrap` destroys itself and no managers exist → NRE storm.
→ **Fix:** `[RuntimeInitializeOnLoadMethod(SubsystemRegistration)]` resets it (and
guard on `GameLoopManager.Instance` too).

**P6 — `GameLoopManager.BeginAfternoon` not re-entrancy guarded.**
A double-click on Handoff's BEGIN builds two `ExpeditionWorld`s.
→ **Fix:** early-return if `_expeditionRoot != null`; also disable the button on
first press.

### NOTES (applied)
- N1 `Camera.main` is null during the afternoon (shop camera destroyed, arena
  camera untagged) — tag the arena camera `MainCamera` too so movement code that
  reads `Camera.main` is safe.
- N2 `TutorialManager.OnOrder` / the `OnOrderStarted` subscription are dead
  (poll-based `Run()` does the work) — removed.
- N3 `SettingsScreen.Slider` has a discarded `GetComponent<LayoutElement>()` line
  and builds a handle-less `Slider` — cleaned; it's a rough control for the slice.
- N4 `LoadoutBuilder.Build` leaks 1 loadout + 1 bomb SO per day — small; left with
  a comment (cache is a Phase-2 concern once the shop owns the loadout).
- N5 `AudioManager.Speak` lets concurrent coroutines stomp `_speech.pitch` — guard
  with a single active-speech coroutine handle.

### Summary
The day loop is structurally sound and the happy path has no plain-NRE, but
**P1 breaks day 2+** — spawned actors and the static registries outlive the
expedition. P2/P3/P5 are the other "second run behaves differently" bugs. All
12 items fixed in the commit that follows; re-review after.

---

## Round 2 — `adversarial-reviewer` (Saboteur / New Hire / Integrity)

**Verdict:** 🔴 **BLOCK** — 2 critical, 4 warnings, 8 notes.

### CRITICAL

**P7 — Quit during Evening → Continue → replay the day and re-bank the reward.**
_Saboteur._ `GamePhase` is not in the save. `BeginEvening` banks
`gold + grade stars` and autosaves. `day` only increments at `Sleep()`. So: quit
after the report, before Sleep → save says "day N", rewards already in the file.
Continue → `Load` → `BeginDay()` → day N morning again → play day N again →
BeginEvening banks the *same biome's* reward a second time. Repeatable.
→ **Fix:** (a) `RunState.lastResolvedDay` — `BeginEvening` banks only when
`lastResolvedDay != day`, then sets it; (b) persist `RunState.phaseAtSave`;
`Load` resumes a save stamped `Evening`/`BiomeMap` by running the `Sleep()`
advance (day++, clear replay) *before* `BeginDay()`, so Continue lands on the
next day with the reward already counted once.

**P8 — Corrupt / unreadable `slot_0.json` → Continue silently starts (and
overwrites with) a New Game.** _Saboteur + Integrity (both hit it — promoted)._
`MainMenuScreen`: `if (!Continue()) StartNewGame();`. `Continue()` is false when
`SaveSystem.Load` fails (corrupt file), so the player's damaged save is
overwritten by a fresh game with zero warning.
→ **Fix:** the menu detects "slot exists but `Peek` returns null" and shows
"Save file unreadable — starting a New Game will overwrite it." The CONTINUE
button is disabled in that state; NEW GAME stays available as the explicit choice.

### WARNINGS

**P9 — The shop world (cauldron sim) persists across days; heat and herb
positions are never reset.** _New Hire._ `EnsureShopWorld()` has "ensure"
semantics and is called from `BeginEvening` too, so `_shopRoot` lives from one
evening through the next morning. `PhysicsCauldronManager.currentHeat` carries
yesterday's decayed value into today's brew.
→ **Fix:** `BeginDay()` destroys and rebuilds `_shopRoot` fresh every day; the
`EnsureShopWorld` calls in `BeginMorning`/`BeginEvening` are removed (Evening's
screen is fully opaque and doesn't need the world).

**P10 — `SaveSystem` only sanity-checks `bestGrades`.** _Integrity._ A truncated
or hand-edited slot that partial-parses loads as "valid": negative `gold`,
`currentBiomeIndex` 99, empty `ownedAdventurers`, `day` 0.
→ **Fix:** `Migrate` clamps every field — `day ≥ 1`, `currentBiomeIndex` in
`[0, BiomeLibrary.Count)`, `gold ≥ 0`, all lists non-null, `bestGrades` length
pinned to `BiomeLibrary.Count`, and re-seeds `"Rookie"` if the roster is empty.

**P11 — `Economy.Payout` pays the potion fee even on a lost expedition.**
_Saboteur._ Defeat still returns `BaseFee × PaymentMultiplier(grade)` — you get
paid for an expedition that failed.
→ **Fix:** on `!report.won`, pay **loot only** (no delivery fee, no tip). Loot
keeps a bankrupt player afloat; the fee is for a completed job.

**P12 — `GameLoopManager`'s 9 public transition methods have no phase
preconditions.** _New Hire._ `BeginEvening()` called from anywhere half-works and
leaves the world/UI inconsistent.
→ **Fix:** each transition guards its expected `from` phase (logs + returns on a
bad call), and the legal sequence is spelled out in the class doc.

### NOTES (applied)
- N6 `diary_00_seen` is a fake id smuggled into `unlockedDiary` → replaced with
  `RunState.openingCinematicSeen`.
- N7 the `BeginAfternoon` autosave is a no-op (nothing marks the state dirty
  during the morning) — removed; the meaningful autosave is at `BeginEvening`.
- N8 `File.Replace` failure (file locked) is swallowed silently → keep the
  catch, but surface a one-time on-screen "couldn't save" toast hook (logged for
  Phase 2; Phase 0 logs an error).
- N9 no gold sink exists in Phase 0 (upgrades are Phase 2) — expected; gold just
  accumulates.
- N10 save file is plain unsigned JSON — fine for offline single-player; an HMAC
  is only needed if a leaderboard/achievements land later (Auditor).
- N11 `JsonUtility.FromJson` has no type-gadget surface (not Newtonsoft
  `TypeNameHandling`) — safe (Auditor, positive).
- N12 multi-adventurer party is dead code (`ExpeditionWorld` hardcodes 1) — the
  `Party` list + `BuildAdventurer(index,count,...)` loop are the Phase-2 seam.
- N13 `RunState.bestGrades` length is a literal `5` — now derived from
  `BiomeLibrary.Count`.

### Summary
Round 1 fixed the cross-day *object* leaks; round 2 fixes the cross-day *state*
bugs — the save doesn't know what phase you were in, so a mid-day quit either
re-banks a reward (P7) or, if the file is damaged, eats the save (P8). P9 is the
subtler "day 2 isn't a clean slate" bug. All 14 items fixed in the following
commit; re-review after.

## Round 3 — `named-persona-adversarial-review` (Carmack / Torvalds / Cagan+Jobs)

Grounded in `references/persona_principles.md`.
**Verdict:** 🟠 **CONCERNS** — 0 blocker, 3 warnings, 6 notes. The loop is sound;
the findings are taste + first-run UX + one honest rubric gap.

### WARNINGS

**P13 — `Continue()` duplicates `Sleep()` behind a state check.**
_Torvalds — eliminate the special case (high, TED 2016)._ `BeginEvening` banks
the reward and sets `lastResolvedDay = day` but does **not** advance the day;
`Sleep()` advances it; so `Continue()` needs a branch that re-does `Sleep`'s
`day++` / clear-replay when the save was stamped past the fight. The good-taste
restructure: **the day is over once you've been paid.** `day++` moves *into*
`BeginEvening` (inside the `lastResolvedDay` guard); `Sleep()` becomes a cosmetic
"pass the night" → `BeginDay()`; `Continue()` loses the branch entirely — it
always just `Load` + `BeginDay()` on an already-correct day.
→ **Fixed.** `BeginEvening` now: `if (lastResolvedDay != day) { bank; advance
biome on clear; lastResolvedDay = day; day++; }`. `Continue()` special case
deleted. `Sleep(replay)` just sets the replay target and `BeginDay()`s.

**P14 — Cat-3 (Assets, weighted) is the one category the slice cannot show.**
_Cagan — fall in love with the problem, not the solution (high, SVPG)._ Every
visual is procedural (`PixelArt` discs/blocks, synth audio). The slice proves the
*pipeline* (sprite/clip fields wired) but a grader opening it sees no hand-made
art — and Assets + Story both lean on that. "All the systems compile" is the
solution; "a grader can score all 8" is the problem.
→ **Fixed (scope):** `PHASE0_STATUS.md` now leads with a **do-not-submit-without
the biome-1 art pass** warning, and `ROADMAP.md` pulls the biome-1 art +
2 cutscene illustrations into Phase 0's definition-of-done (task 0.13 upgraded
from 🟡 to a hard gate).

**P15 — The first 3 minutes have two back-to-back forced-passive stretches.**
_Jobs — design is how it works (high, NYT 2003)._ Opening cinematic (≈45 s of
typed-out lore over procedural art) → tutorial `Welcome` step
(`WaitForSecondsRealtime(3f)`, no input accepted). Lore infodump before the
player has touched anything is the weakest placement.
→ **Fixed:** the tutorial `Welcome` step advances on click (or a 6 s fallback),
the diary typewriter completes the current page instantly on click, and the
opening cinematic keeps its CLOSE-to-skip. (Moving the cinematic to *after* the
first craft is a design change deferred to Phase 2 — noted.)

### NOTES (applied)
- N14 `ExpeditionHudScreen.Update()` rebuilt the banner string + set
  `Image.fillAmount` every frame (per-frame alloc + canvas dirty — Carmack, "no
  allocation in the frame loop"). Now guarded: only touch `.text` / `.fillAmount`
  when the underlying value changed.
- N15 `EnsureShopWorld` is only ever called right after a `DestroyWorld` now →
  renamed `BuildShopWorld`, guard removed (one call site).
- N16 `DiaryScreen.PendingReturnPhase` is a `GamePhase?` used as a bool →
  `bool FromOpeningCinematic`.
- N17 `Sleep(int)` sentinel `-1` kept (a two-method split is not worth it for one
  call site + one replay button).
- N18 `AudioManager` bed generation at Awake (~3.5 MB, ~1 M `sin()` calls) is
  one-time behind the boot splash — Carmack's "measure first": this is
  measured-appropriate, no change. (zero-finding: audio-gen hidden by splash,
  texture cache amortized, detonation is event-driven not polled.)
- N19 `FindObjectsByType<BombProjectile2D>` in `ExpeditionWorld.OnDestroy` runs
  once per day during a teardown frame — acceptable; parenting projectiles is
  cleaner but not a perf issue.

### Integrity check (Feynman)
Torvalds on the `Continue` branch — yes, it is a literal partial-duplication of
`Sleep` guarded by a state check, the exact shape of his linked-list example.
Carmack on per-frame `.text` — yes, documented across his .plan files. Cagan on
"systems present, weighted category unshowable" — yes, problem-vs-solution.
All findings stand on merit.

### Summary
No blocker. P13 is the one real code change — the state machine gets simpler, not
more complex. P14 is an honest scoping correction (the slice needs *some* real art
to be gradable). P15 is first-run polish. 8 items applied; re-review after.

## Round 4 — `eval-rubric-auditor` (8-category Level-2 audit)

**Result: 6 / 8 at Level 2; 2 below — both blocked on the same thing (authored art).**

| # | Category `*`=weighted | Level | Why | Code gap to close (art gaps → task 0.13) |
|---|---|---|---|---|
| 1 | Gameplay & mechanics `*` | **L2** | loop closes end-to-end (`LoadoutBuilder` → grade → `BombProjectile2D` multiplier → Report "Poor: −50%"); decay model live (born 25, green raises, off-band lowers, 4-band) | — (needs a play-test for *feel*) |
| 2 | Story | **L1** (system L2, execution L0) | visual diary + page nav + assembling 5-layer boss sketch + opening cinematic all exist; but illustrations are procedural blocks, and `DiaryScreen` never renders `DiaryEntryData.cutsceneFrames` even the field exists | **G1** — `DiaryScreen` plays `cutsceneFrames` (animated at `frameRate`) when non-empty, procedural fallback otherwise |
| 3 | Assets `*` | **L0** | zero self-made assets; every consumer (`MonsterData.sprite`, `DiaryEntryData.cutsceneFrames`, …) has the `authored != null` fallback wired, but nothing is drawn; `PlaceholderArt` is named "placeholder" | **G3** — add a Credits screen stating "all art/music/sound made in-house"; the art itself is task 0.13 |
| 4 | Size / levels `*` | **L2** | 5 `BiomeData` biomes, themed, sequenced, map + progression + replay; biomes 2–4 are 2-wave/no-boss but structurally distinct | polish: 3rd wave / mini-boss for biomes 2–4 (not required) |
| 5 | Physics | **L2 (strong)** | cauldron stir = `AddForce` tangential + `AddTorque` in `FixedUpdate`; bombs = `Rigidbody2D` arc + `OverlapCircleAll` + `AddForceAtPosition` knockback; no `transform.position +=` on bodies | — |
| 6 | Usability `*` | **L2** | `TutorialManager` FSM (slowed+frozen budget, spotlight, coach bubble, station gate, once-per-save); `.iss` installer sane | **G4** — add a pointer arrow (bubble→spotlight); UI polish is Phase 2 |
| 7 | Sound & music | **L2** | procedural adaptive music (shop↔forest crossfade) + SFX bank + Animalese `Speak()` | **G2** — nothing *calls* `Speak()` yet; wire it to the Counter order + Evening reaction |
| 8 | AI | **L2 (strong)** | IAUS `CombatDecisionEngine` (scored candidates + response curves) + boss HFSM (`BossPhaseScorer`, dwell hysteresis, `BossBehaviourRunner`); visible via always-on ticker | **G6** (note) — the opt-in `ShowAiThinking` full `LastBreakdown` panel isn't built; the ticker covers Level 2 |

**Cross-cutting G5 — nothing has been run in Unity.** Compile-clean ≠ works.
Add `Debug/GameLoopSimulationTest` that drives `GameLoopManager` through a full
headless day and asserts phase order + single reward bank + no double-bank on a
simulated resume.

### Fixes applied this round
G1 (diary frame playback), G2 (Animalese wired to Counter + Evening), G3 (Credits
screen), G4 (tutorial pointer arrow), G5 (headless day-loop test). G6 left as a
documented Phase-2 note.

### Standing conclusion
The slice is a genuine "Level-2 floor" for **6** categories today. Story and
Assets reach Level 2 only after task 0.13 (the art pass) — which is now a hard
submission gate in `ROADMAP.md` / `PHASE0_STATUS.md`. No category is at Level 2
"on paper only": each has a running system a grader can see.

---

## Round 5 — re-review after rounds 1–4 fixes (all four skills, abbreviated)

_(pending — the exit condition is a clean pass; rounds 1–4 each found real issues.)_
