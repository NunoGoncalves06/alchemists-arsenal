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

## Round 3 — `named-persona-adversarial-review`

_(pending)_

## Round 4 — `eval-rubric-auditor`

_(pending)_
