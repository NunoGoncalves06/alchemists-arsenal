# Playing the expedition slice

The afternoon auto-battler is fully code-assembled — no prefabs or wiring needed.

## Fastest path

1. `File ▸ New Scene` → **Basic 2D (URP)** (or any empty scene).
2. `GameObject ▸ Create Empty`, name it `Bootstrap`.
3. `Add Component ▸ Expedition Bootstrap`.
4. Press **Play**.

`ExpeditionBootstrap` builds, on `Start()`:

- a camera (if the scene has none) and a tinted ground,
- 2 adventurers — each with `AdventurerMovementController` (Approach → Reposition → Throw →
  Retreat, physics steering), `UtilityAI_CombatController` (IAUS bomb choice), and
  `BallisticBombLauncher` (ballistic arc + `OverlapCircleAll` detonation),
- a `MonsterSpawner` + `ExpeditionManager` that runs 3 monster waves then the boss
  (`BossPhaseManager` HFSM + `ElementalDamageAccumulator`),
- an on-screen HUD (phase, wave, monster count, adventurer HP, VICTORY / DEFEAT).

## Tuning

Every field on `ExpeditionBootstrap` is optional:

| Field | Leave empty → | Assign → |
|---|---|---|
| `Biome` | code-built "Venom Swamp" (3 waves + boss) | run `Alchemist ▸ Generate Combat/Boss Data`, build a `BiomeData` asset, drop it here |
| `Elemental Matrix` | code default (Fire→Nature→Water wheel) | a generated `ElementalMatrix.asset` |
| `Loadout` | 4 bombs × ~6 | a generated `AdventurerLoadout.asset` (the morning shop's output) |
| `Boss Override` | code-built Coven Matriarch | `BossDefinition_CovenMatriarch.asset` from `Alchemist ▸ Generate Boss Data` |
| `Adventurer Count` / `Health` | 2 / 120 | — |

## Verifying without pressing Play

Attach `BossAndMovementSimulationTest`, `ExpeditionSimulationTest`,
`BallisticLauncherSimulationTest`, or `UtilityAiSimulationTest` to a GameObject and press
Play — each logs PASS/FAIL lines to the Console.

## Placeholder art

`PlaceholderArt` bakes tinted discs / diamonds / stars at runtime so the slice is
visible. **Not final art** — assign hand-drawn `Sprite`s to `MonsterData.sprite` /
`AdventurerData.characterSprite` and they take over automatically.

## Windows installer

`installer/AlchemistsArsenal.iss` — build the game to `build/`, then
`ISCC.exe installer\AlchemistsArsenal.iss` produces `AlchemistsArsenal-Setup.exe`.
