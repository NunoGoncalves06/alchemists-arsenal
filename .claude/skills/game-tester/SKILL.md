---
name: game-tester
description: Use this skill after any change to gameplay, UI, or scene-building code — before telling the user something is fixed. Compiles the project, drives a full headless day-loop playthrough inside the real Unity Editor (batchmode) while capturing real screenshots of the live UI, and gives a manual smoke-test checklist for whatever the screenshots can't settle.
---
You are the QA lead for "Alchemist's Arsenal". Your job is to catch regressions
**before** the user has to find them by playing. Compile-checking alone is not
enough — the bugs reported in the first two live playtests (tutorial bubbles
hiding buttons, a tofu box on SEND TO EXPEDITION, Prep/Bottling permanently
locked, an unreadable Evening report) were all compile-clean and only showed
up by actually running the game and looking at it.

Run tiers in order. Stop and fix before moving to the next tier — a tier-2
failure makes tier-3 pointless.

## Tier 1 — compile check (seconds, always run first)

```bash
bash "<scratchpad>/compilecheck.sh"
```

(If that script isn't in the current scratchpad, recreate it: for BOTH the
runtime assembly and the Editor assembly, copy the matching on-disk
`Assembly-CSharp*.csproj`, swap in a fresh `<Compile Include>` glob —
`Assets/Scripts/**/*.cs` excluding `Editor/` for the runtime one,
`Assets/Scripts/Editor/**/*.cs` for the Editor one — and point the Editor
project's `<ProjectReference>` at the freshly-generated runtime scratch
project instead of the possibly-stale on-disk `Assembly-CSharp.csproj` (this
bit the first run of this tool: a new runtime file didn't exist yet in the
on-disk project Unity had last generated, so the Editor-only check couldn't
see it). Then `dotnet build -t:Rebuild` both.)

This catches syntax/type errors only. It does **not** catch: null refs that
only happen at runtime, UI elements overlapping, timing/hang bugs, or
anything about how it *looks* or *feels* — that's Tier 2.

## Tier 2 — headless full-loop playtest WITH screenshots (2-4 minutes)

Run for any change touching `GameLoopManager`, the expedition, cauldron/quality,
save, or UI screen routing. Two files, split across assemblies on purpose —
`Assets/Scripts/Editor/HeadlessPlaytest.cs` (the CLI entry point — opens
`Boot.unity`, arms a `SessionState` flag, flips Play Mode) and
`Assets/Scripts/Core/HeadlessPlaytestRunner.cs` (a MonoBehaviour that notices
that flag after Play Mode's domain reload and drives everything else). Read
`HeadlessPlaytestRunner`'s class doc before touching either file — an
Editor-side coroutine driving across the Play Mode domain-reload boundary is
exactly the bug that made the first version of this tool hang silently until
killed (it logged one line, then nothing — the reload wiped its state and
dropped its `EditorApplication.update` subscription; the game kept running
fine, just with nothing left driving it). Don't "simplify" this back to a
single Editor-side script.

The runner drives the actual game — not a mock — through Boot → Main Menu →
Day 1 (tutorial, no boss) → Day 2 (boss enabled), calling the same manager
APIs the UI's buttons call (`ConfirmOrder`, `BeginHandoff`, `BeginAfternoon`,
`BeginEvening`, `Sleep`, …). It fails on any timeout, stuck phase, or logged
error/exception, **and** it captures real PNG screenshots of the live UI at
~15 checkpoints (main menu, both tutorial steps, all four Morning tabs,
mid-fight, the Evening report, the Upgrades tab, the biome map — see the file
for the exact list). Two checkpoints (switching the Morning tab, opening
Upgrades) call the screen's own private method via reflection since there's
no real mouse to click an unnamed button with — read the file's comments for
exactly which and why; everything else is the same call a button's onClick
makes.

**Before running: close the Unity Editor if it's open.** Unity is
single-instance per project — batchmode will refuse to start (or fight over
the same save file) if the Editor already has the project open. Tell the user
you need it closed rather than silently failing; if you can't confirm it's
closed, ask.

```bash
"/c/Program Files/Unity/Hub/Editor/6000.6.0f1/Editor/Unity.exe" \
  -batchmode -projectPath "C:/Game-Dev/Game-Repo" \
  -executeMethod AlchemistsArsenal.EditorTools.HeadlessPlaytest.RunFullLoop \
  -logFile "C:/Game-Dev/Game-Repo/headless-playtest.log"
echo "exit code: $?"
cat "C:/Game-Dev/Game-Repo/headless-playtest-report.txt"
```

**Do NOT add `-nographics`** (screenshots need real rendering) **and do NOT
add `-quit`.** `RunFullLoop()` returns immediately after arming the flag and
starting Play Mode — the actual work hasn't happened yet at that point — so
`-quit` would exit Unity before `HeadlessPlaytestRunner` ever got to run (the
very first version of this tool hit exactly that: immediate shutdown after
one log line, which could be mistaken for a fast pass but was actually
nothing running at all). `HeadlessPlaytestRunner.Finish()` calls
`EditorApplication.Exit` itself once the loop genuinely finishes.

Run it with `run_in_background: true` (Bash tool) and poll rather than
blocking. When it's done:

1. Check exit code and `headless-playtest-report.txt` for `RESULT: PASS` vs
   `FAIL` — if FAIL, the report names the failing step with elapsed time; grep
   `headless-playtest.log` for `error CS` (compile got through anyway — rerun
   Tier 1) or the first `[HeadlessPlaytest] FAIL:` / Unity exception line.
2. **Read every PNG in `headless-screens/` with the Read tool** (it renders
   images) — this is the actual point of Tier 2 now. Look at each one
   specifically for: text/buttons covered by another element, missing or
   boxed (tofu) glyphs, a panel that's the wrong size or empty when it
   shouldn't be, sprites that look wrong or unreadable multi-column text. Do
   this before telling the user anything is fixed — a green exit code only
   means the state machine didn't get stuck; it says nothing about whether
   the screen looked right.

What Tier 2 does **not** cover even with screenshots:
- Input feel over time (mouse sensitivity, "does this feel twitchy") — the
  driver doesn't simulate mouse movement, it sets state directly
- Audio
- Animation/motion (a bobbing arrow, a moving needle) — a screenshot is one
  frame

## Tier 3 — manual checklist (the user runs this for anything Tier 2's screenshots didn't settle)

1. **Cauldron feel**: stirring only does anything with the cursor over the
   pot; a small deliberate move doesn't spike the gauge to max; the BREW bar
   fills over ~10-15s and locks the result in.
2. **Combat feel**: the adventurer stays on screen and visibly fights across
   a whole run, not just the one captured frame.
3. **Sound**: SFX and Animalese speech play at the moments they should.
4. Anything a screenshot genuinely can't show — ask the user to confirm, and
   say specifically what to look for, not "does it work."

## If you add a new gameplay/UI system

Add a step to `HeadlessPlaytest.Drive()` that exercises it AND captures a
screenshot of it (or a new `[MenuItem]` entry point for something outside the
day loop) rather than leaving it uncovered — the skill is only as good as
what it drives and looks at.

## Optional upgrade path: a live Editor bridge

A live MCP bridge into a *running* Unity Editor (e.g. an installed
`unity-mcp` package exposing scene-tree inspection, console reading, and
Play-mode control as MCP tools) would let Claude inspect the actual Game view
and console interactively instead of only batchmode runs. That is **not**
installed in this project — don't imply it's available. If the user wants
it, it needs: adding the package via Package Manager (a Git URL), running its
bridge server, and connecting it here as an MCP server — a deliberate
multi-step setup to do with the user, not something to wire up silently.
