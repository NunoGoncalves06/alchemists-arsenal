# Phase 0 — "Level-2 Floor" — build status

Implemented against `ROADMAP.md` Phase 0. Verified: `dotnet build` of a
scratch csproj over all `Assets/Scripts/**/*.cs` — **0 errors** (Unity CLI is not
available on this machine; see "Verification" in the rebase memory note).

| # | Task | Status | Where |
|---|---|---|---|
| 0.1 | Scene skeleton | ✅ (adapted) | `Boot.unity` + `Core/Bootstrap.cs`; Shop/Expedition are loop-owned GameObject roots, not additive `.unity` scenes — see Deviation 1 |
| 0.2 | `GameLoopManager` + `GamePhase` + morning budget + `TimeControl` | ✅ | `Core/GameLoopManager.cs`, `Core/GamePhase.cs`, `Core/TimeControl.cs` |
| 0.3 | `SaveSystem` + `RunState` + atomic/versioned JSON + autosave | ✅ | `Core/SaveSystem.cs`, `Core/RunState.cs` |
| 0.4 | `StartNewOrder(name,element)`, born at 25, drop both auto-starts, `LoadoutBuilder` | ✅ | `Systems/CraftingManager.cs`, `Systems/ActiveOrder.cs`, `Core/LoadoutBuilder.cs` |
| 0.4a | `ActiveOrder.AdjustQuality` both directions; event both ways; green-zone raises quality | ✅ | `Systems/ActiveOrder.cs`, `Crafting/PhysicsCauldronManager.cs` |
| 0.5 | `BombProjectile2D.OnDetonatedGlobal`, `CombatantBody.OnAnyDied/Damaged`, `MonsterTag`, `ExpeditionTelemetry`, `ExpeditionReport`, loot fields on `MonsterData` | ✅ | `Combat/BombProjectile2D.cs`, `Combat/CombatantBody.cs`, `Combat/Expedition/*` |
| 0.6 | `UIManager` router + `UITheme` + `UIFactory` component kit | ✅ | `UI/UIManager.cs`, `UI/UITheme.cs`, `UI/UIFactory.cs` |
| 0.7 | Morning shell + Counter + Cauldron (no Prep/Bottling) | ✅ | `UI/Screens/MorningScreen.cs` |
| 0.7a | Scene-lifecycle hardening (`PhysicsCauldronManager.OnDestroy` nulls Instance; managers null Instance) | ✅ | `Crafting/PhysicsCauldronManager.cs` + every new manager |
| 0.8 | Expedition HUD; delete `OnGUI` | ✅ | `UI/Screens/ExpeditionHudScreen.cs`; `Combat/Expedition/ExpeditionBootstrap.cs` OnGUI removed |
| 0.9 | Evening Report + `Economy` payout by grade | ✅ | `UI/Screens/EveningScreen.cs`, `Combat/Expedition/Economy.cs` |
| 0.10 | `DiaryEntryData` + diary screen + cutscene sequencer + opening cinematic + 2 entries | ✅ | `Story/DiaryEntryData.cs`, `Story/DiaryManager.cs`, `UI/Screens/DiaryScreen.cs` (3 entries) |
| 0.11 | `TutorialManager` FSM for Counter + Cauldron | ✅ | `UI/TutorialManager.cs` |
| 0.12 | `AudioManager` + shop/forest beds + SFX + Animalese | ✅ (procedural) | `Audio/AudioManager.cs` — see Deviation 2 |
| 0.13 | Biome 1 art pass | 🟡 procedural | `Core/PixelArt.cs` extends `PlaceholderArt`; authored sprites are Deepening 6 — see Deviation 2 |
| 0.14 | Delete `Systems.QualityTier` → `CombatQuality` | ✅ | removed from `ActiveOrder.cs`; `CauldronUI`/`CauldronSimulationTest` updated |
| 0.15 | Installer smoke test | 🟡 script verified | `installer/AlchemistsArsenal.iss` sane; `productName` set to `AlchemistsArsenal` to match; the actual `Build → ISCC → install` run needs the Unity editor — see Deviation 3 |

## How to run the slice

1. Open the project in Unity 6000.6.0f1.
2. Open `Assets/Scenes/Boot.unity` (already scene 0 in Build Settings).
3. Press **Play**. Flow: splash → Main Menu → **NEW GAME** → opening diary → Day 1
   tutorial (Counter + Cauldron) → send Rookie → watch the expedition → Evening
   report → Diary → Biome Map → **SLEEP** → Day 2 (Prep/Bottling still locked).

## Deviations from the roadmap (no-editor constraints)

**1 — "3 additive scenes" → 3 GameObject roots.** `Boot.unity` is a real scene;
`Core` is `Bootstrap`'s persistent manager tree; `ShopWorld` / `ExpeditionWorld`
are roots the loop `new GameObject()`s and `Destroy()`s. Same isolation guarantees
(sim never under a UI root; every scene-scoped singleton nulls `Instance` on
destroy — reviewer X5). Promoting these to `Shop.unity` / `Expedition.unity` is a
mechanical change to `GameLoopManager.EnsureShopWorld` / `BeginAfternoon` and does
not touch the manager API. Reason: hand-authoring scene YAML without the editor is
error-prone.

**2 — Art & audio are procedural, not authored.** `PixelArt` bakes tinted
discs/backdrops at runtime; `AudioManager` synthesises the two music beds, the SFX
bank and the Animalese vowels with `AudioClip.Create`. Every data SO already has
`Sprite` / clip fields — assigning hand-drawn art / recorded audio takes over with
no code change (Deepening 6 / 4.5). This satisfies the *systems* for cats 3/7 at
Level 1–2; the hand-made-assets emphasis for a top score is the art pass.

**3 — Installer not executed.** Producing `build/` needs a Unity player build,
which needs the editor. The `.iss` is correct and `productName` now matches its
`AppExeName`. Run `Unity ▸ Build (Windows x64) → build/` then
`ISCC.exe installer\AlchemistsArsenal.iss`.

## Known rough edges (for the reviewer pass)

- uGUI is code-built with rough anchoring — spacing/overlap will need a polish
  pass (Phase 2). Focus of Phase 0 is *the loop runs and every rubric category is
  represented*, not visual finish.
- Element badges reuse `PlaceholderArt` disc/diamond/star shapes — the authored
  triangle/hexagon/droplet set is Phase 2 (`UIFactory.ShapeFor` has the TODO).
- `StationManager` (the older 4-tab FSM) is unused by the Phase-0 Morning screen,
  which has its own 2-tab rail. It's still in the repo for Phase 2's full shell.
