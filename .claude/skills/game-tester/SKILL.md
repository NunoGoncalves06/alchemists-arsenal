---
name: game-tester
description: Use this skill after any change to gameplay, UI, or scene-building code — before telling the user something is fixed. Compiles the project, drives a full headless day-loop playthrough inside the real Unity Editor (batchmode), and gives a manual smoke-test checklist for what automation can't see (visual/feel/audio bugs).
---
You are the QA lead for "Alchemist's Arsenal". Your job is to catch regressions
**before** the user has to find them by playing — the compile-check alone is
not enough; two of the bugs reported in the first live playtest (tutorial
bubbles hiding the cauldron, "SEND TO EXPEDITION" showing a tofu box) were
both compile-clean and would only show up by actually running the game.

Run tiers in order. Stop and fix before moving to the next tier — a tier-2
failure makes tier-3 pointless.

## Tier 1 — compile check (seconds, always run first)

```bash
bash "<scratchpad>/compilecheck.sh"
```

(If that script isn't in the current scratchpad, recreate it: it copies
`Assembly-CSharp.csproj`, swaps in a fresh `<Compile Include>` glob of
`Assets/Scripts/**/*.cs` excluding `Editor/`, and runs `dotnet build -t:Rebuild`.)
For files under `Assets/Scripts/Editor/`, compile-check them the same way but
against `Assembly-CSharp-Editor.csproj` instead (it already references
`UnityEditor.dll` + `Assembly-CSharp.csproj`) — swap its `<Compile Include>`
group for a glob of `Assets/Scripts/Editor/**/*.cs`.

This catches syntax/type errors only. It does **not** catch: null refs that
only happen at runtime, UI elements overlapping, timing/hang bugs, or
anything about how it *looks* or *feels*.

## Tier 2 — headless full-loop playtest (1–3 minutes, run for any change touching GameLoopManager, expedition, cauldron/quality, save, or UI screen routing)

`Assets/Scripts/Editor/HeadlessPlaytest.cs` drives the actual game — not a
mock — through Boot → NewGame → Day 1 (tutorial, no boss) → Day 2 (boss
enabled), calling the same manager APIs the UI calls (`ConfirmOrder`,
`BeginHandoff`, `BeginAfternoon`, `BeginEvening`, `Sleep`, …), and fails on
any timeout, stuck phase, or logged error/exception.

**Before running: close the Unity Editor if it's open.** Unity is
single-instance per project — batchmode will refuse to start (or fight over
the same save file) if the Editor already has the project open. Tell the user
you need it closed rather than silently failing; if you can't confirm it's
closed, ask.

```bash
"/c/Program Files/Unity/Hub/Editor/6000.6.0f1/Editor/Unity.exe" \
  -batchmode -nographics -quit \
  -projectPath "C:/Game-Dev/Game-Repo" \
  -executeMethod AlchemistsArsenal.EditorTools.HeadlessPlaytest.RunFullLoop \
  -logFile "C:/Game-Dev/Game-Repo/headless-playtest.log"
echo "exit code: $?"
cat "C:/Game-Dev/Game-Repo/headless-playtest-report.txt"
```

Run it with `run_in_background: true` (Bash tool) — it can take a couple of
minutes — then poll `headless-playtest.log` / the report file rather than
blocking. Exit code `0` + `RESULT: PASS` in the report = the loop completed
both days cleanly. Anything else: read `headless-playtest-report.txt` for the
failing step (it logs every phase transition with elapsed time) and grep
`headless-playtest.log` for `error CS` (compile) or the first
`[HeadlessPlaytest] FAIL:` / Unity exception line (runtime).

What Tier 2 *does* cover: the state machine never getting stuck (a wave that
never ends, a phase transition that never fires, a save/load corrupt-loop),
NullReferenceExceptions and other logged errors anywhere in the run, and the
expedition actually resolving (win or lose) within its timeouts on both a
boss-less and a boss day.

What Tier 2 does **not** cover — this is exactly the gap that let the
reported bugs through, so don't skip Tier 3:
- Anything about layout/overlap (a panel or bubble sitting on top of the
  thing the player needs to click or see)
- Font/glyph rendering (a missing glyph renders as a box — the driver never
  looks at a screenshot)
- Input feel (mouse sensitivity, "is this too twitchy") — the driver sets
  quality directly via `ActiveOrder.AdjustQuality`, it doesn't simulate mouse
  movement
- Audio
- Camera framing / whether something is actually visible on screen vs. just
  "not null" in the object graph

## Tier 3 — manual smoke-test checklist (the user runs this; you can't press Play)

You cannot open the Unity Editor's Game view or move a mouse in it. After
tiers 1–2 pass, hand the user this checklist for anything that touches UI,
the cauldron, or combat feel — ask them to confirm each item, don't assume:

1. **Boot → Main Menu**: no "No cameras rendering", text renders (no tofu
   boxes on any button — check ones with a trailing glyph or icon-only
   labels specifically).
2. **Day 1 tutorial**: the coach bubble never sits on top of the Counter's
   ACCEPT button, the Cauldron gauge, or the SEND button; you can always see
   and click what you need to.
3. **Cauldron**: stirring only does anything while the cursor is over the
   pot; a small, deliberate mouse movement doesn't spike the gauge to max; a
   BREW progress bar fills over ~10–15s of good stirring and locks in the
   result (no infinite-stir feeling).
4. **Handoff → Afternoon**: the adventurer sprite stays on screen and
   visibly fights — it doesn't run off-camera or become indistinguishable
   from the monster pack.
5. **Waves**: a cleared wave advances automatically within a couple of
   seconds; the NEXT WAVE button (top-center) skips a dragging wave.
6. **Day 1 specifically**: no boss fight (day 1 is the teaching run).
7. **Day 2+**: the boss appears and the fight actually ends (win or lose)
   without hanging.
8. **Evening**: the report screen shows after every expedition, win or lose.

## If you add a new gameplay/UI system

Add a step to `HeadlessPlaytest.Drive()` that exercises it (or a new
`[MenuItem]` entry point for something outside the day loop, e.g. an
inventory or shop-upgrade screen) rather than leaving it uncovered — the
skill is only as good as what it drives.

## Optional upgrade path: a live Editor bridge

A live MCP bridge into a *running* Unity Editor (e.g. an installed
`unity-mcp` package exposing scene-tree inspection, console reading, and
Play-mode control as MCP tools) would let Claude inspect the actual Game view
and console interactively instead of only batchmode runs. That is **not**
installed in this project — don't imply it's available. If the user wants
it, it needs: adding the package via Package Manager (a Git URL), running its
bridge server, and connecting it here as an MCP server — a deliberate
multi-step setup to do with the user, not something to wire up silently.
