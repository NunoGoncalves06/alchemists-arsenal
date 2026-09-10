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

## Round 2 — `adversarial-reviewer`

_(pending)_

## Round 3 — `named-persona-adversarial-review`

_(pending)_

## Round 4 — `eval-rubric-auditor`

_(pending)_
