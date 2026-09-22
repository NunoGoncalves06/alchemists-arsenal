#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;
using TMPro;
using AlchemistsArsenal.Combat;
using AlchemistsArsenal.Data;
using AlchemistsArsenal.Systems;
using AlchemistsArsenal.UI;
using Debug = UnityEngine.Debug;

namespace AlchemistsArsenal.Core
{
    /// <summary>
    /// The runtime half of the headless playtest — see
    /// <c>Assets/Scripts/Editor/HeadlessPlaytest.cs</c> for the CLI entry point and
    /// the full write-up of why this is split across two assemblies.
    ///
    /// Short version: entering Play Mode triggers a domain reload, which wipes
    /// Editor-side static state and drops any <c>EditorApplication.update</c>
    /// subscription made before the reload. An Editor-side coroutine trying to drive
    /// through that transition gets silently abandoned mid-flight — that's exactly
    /// what happened the first time this tool ran: one log line, then nothing,
    /// looking like a hang (the game kept running fine; nothing was driving it
    /// anymore) until the process was killed. A MonoBehaviour coroutine started
    /// AFTER the reload has no such problem, so the split is: the Editor side only
    /// arms a flag via <see cref="SessionState"/> (which survives the reload) and
    /// flips <see cref="EditorApplication.isPlaying"/>; this class notices the flag
    /// once the reloaded domain is stable and runs the entire drive-and-screenshot
    /// sequence itself, ending the process with <see cref="EditorApplication.Exit"/>.
    ///
    /// Lives in <c>Core/</c>, not <c>Editor/</c>, on purpose: Unity always compiles
    /// anything under an <c>Editor/</c> folder into a separate editor-only assembly
    /// that the runtime assembly cannot reference, regardless of asmdefs — this
    /// class has to be reachable from a normal runtime load callback, so it has to
    /// live in the runtime assembly and gate itself with <c>#if UNITY_EDITOR</c>
    /// instead (the same pattern already used by <c>MainMenuScreen.Quit()</c>).
    /// </summary>
    public static class HeadlessPlaytestRunner
    {
        public const string ArmedKey = "AA_HeadlessPlaytestArmed";
        private const string ReportPath = "headless-playtest-report.txt";
        private const string ScreensDir = "headless-screens";
        // day 1: tutorial / no boss, solo. day 2: boss enabled, solo.
        // day 3: REPLAYS biome 0 with a full three-hero party, so it is directly
        // comparable with day 1 on the same road - the regression test for
        // "does a bigger party trivialise the early game".
        private const int DaysToRun = 3;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoStart()
        {
            if (!SessionState.GetBool(ArmedKey, false)) return;
            SessionState.SetBool(ArmedKey, false); // one-shot — consume immediately so a manual Play doesn't re-trigger it

            var go = new GameObject("~HeadlessPlaytestRunner");
            UnityEngine.Object.DontDestroyOnLoad(go);
            go.AddComponent<Driver>().Begin();
        }

        private class Driver : MonoBehaviour
        {
            private StringBuilder _log;
            private int _errorCount;
            private Stopwatch _wall;
            /// <summary>"quick" (the day loop + one fight per boss) or "full" (also a
            /// seed sweep across all five biomes). From <c>-playtestSuite</c>.</summary>
            private string _suite = "quick";
            private bool FullSuite => _suite == "full";

            /// <summary>One entry per resolved fight, joined into the report's
            /// FINGERPRINT line so two runs can be diffed.</summary>
            private readonly List<string> _fingerprint = new List<string>();

            /// <summary>The harness's own save file. See <see cref="SaveSystem.SlotOverride"/>.</summary>
            private const int HarnessSlot = 7;

            /// <summary>
            /// Every frame advances game time by exactly one physics step. Fights used
            /// to run on real, variable frame deltas, so drawing one sprite fewer on one
            /// frame could flip a seeded fight's outcome.
            ///
            /// It has to be the physics step itself, not a round 1/60: at 1/60 against
            /// 50 Hz physics the fixed-step accumulator carries a remainder from frame
            /// to frame, so a harness that spent a few extra frames anywhere earlier
            /// (one more screenshot) started the next fight at a different phase, and
            /// the fight diverged. One step per frame leaves no remainder to carry.
            /// Time scale stays 1: the speed comes from batchmode rendering frames as
            /// fast as it can, not from coarser steps.
            /// </summary>
            private float FrameStep => Time.fixedDeltaTime;

            public void Begin()
            {
                _log = new StringBuilder();
                _errorCount = 0;
                _wall = Stopwatch.StartNew();
                Application.logMessageReceived += OnUnityLog;
                _suite = ReadSuiteArg();
                SaveSystem.SlotOverride = HarnessSlot;
                SettingsService.ExpeditionSpeedOverride = 1;
                CutsceneScreen.AutoAdvance = true;
                StoryStage.SkipIntro = true;
                Time.captureDeltaTime = FrameStep;
                Log($"=== HEADLESS PLAYTEST START (runtime driver, post-reload, suite={_suite}) ===");
                Log($"Save slot pinned to slot_{HarnessSlot}.json, fight speed pinned to 1x, fixed step {FrameStep:F4}s.");
                StartCoroutine(DriveThenFinish());
            }

            private static string ReadSuiteArg()
            {
                string[] args = Environment.GetCommandLineArgs();
                for (int i = 0; i < args.Length - 1; i++)
                    if (args[i] == "-playtestSuite") return args[i + 1].ToLowerInvariant();
                return "quick";
            }

            // ------------------------------------------------------------ script

            /// <summary>
            /// Fixed seed so a run is comparable to the one before it.
            ///
            /// Without this the harness was a coin toss: monster spawn positions come
            /// from UnityEngine.Random, which Unity seeds from the clock, so the same
            /// build won both expeditions on one run and lost both on the next. Any
            /// combat tuning judged from a single run was reading noise — two changes
            /// were reverted this way before the variance was measured (5 runs of one
            /// unchanged build: 3 won both days, 2 lost both). Change this seed to
            /// sample a different fight; keep it fixed while comparing two builds.
            /// </summary>
            private const int RandomSeed = 20260912;

            /// <summary>
            /// Runs the script to completion or to its first failure, and reports
            /// either way. Drive() itself bails out with `yield break` all over the
            /// place; without this wrapper those exits never write the report and
            /// never quit the Editor.
            /// </summary>
            private IEnumerator DriveThenFinish()
            {
                IEnumerator drive = Drive();
                while (true)
                {
                    object current;
                    try
                    {
                        if (!drive.MoveNext()) break;
                        current = drive.Current;
                    }
                    catch (Exception e)
                    {
                        Fail($"Driver threw: {e}");
                        break;
                    }
                    yield return current;
                }
                Finish();
            }

            private IEnumerator Drive()
            {
                UnityEngine.Random.InitState(RandomSeed);
                Log($"Random seed {RandomSeed} — runs are comparable to each other.");

                foreach (var step in WaitUntil(() => GameLoopManager.Instance != null && SaveSystem.Instance != null, 10f,
                        "boot managers (GameLoopManager/SaveSystem)"))
                    yield return step;
                if (_errorCount > 0) yield break;

                foreach (var step in WaitForPhase(GamePhase.MainMenu, 10f)) { if (_errorCount > 0) yield break; yield return step; }
                if (_errorCount > 0) yield break;
                foreach (var step in Settle("boot_main_menu")) yield return step;
                foreach (var step in ProbeClocks()) yield return step;

                // Run the pure meta-layer checks (save round-trip, migration
                // clamping, perk symmetry, cost curves, the quality budget) inside
                // this same batchmode session. They are a MonoBehaviour because
                // they need JsonUtility and persistentDataPath, and without this
                // nothing ever executed them - a suite that only runs when someone
                // remembers to drop it in a scene is a suite that does not run.
                // Any Check() failure logs an error, which OnUnityLog turns into a
                // failure of this run.
                foreach (var step in RunSimulationChecks()) yield return step;
                if (_errorCount > 0) yield break;

                // No 4x time-scale push any more. It never actually applied to the
                // fights (the HUD pushes its own 1x/2x request on top of it), and a
                // coarser step would make the harness fight differently from a player.

                for (int day = 1; day <= DaysToRun && _errorCount == 0; day++)
                {
                    Log($"--- DAY {day} ---");

                    if (day == 1)
                    {
                        GameLoopManager.Instance.StartNewGame();
                        yield return null;
                        // The opening plays between the first Day Intro and the morning;
                        // under the harness each shot moves on by itself once it is in.
                        foreach (var step in PlayOutCutscenes("opening", expect: true)) yield return step;
                        if (_errorCount > 0) yield break;
                        if (!SaveSystem.Instance.State.openingCinematicSeen)
                            Fail("The opening played but was not recorded as seen.");
                    }

                    foreach (var step in WaitForPhase(GamePhase.Morning, 15f)) { if (_errorCount > 0) yield break; yield return step; }
                    if (_errorCount > 0) yield break;
                    foreach (var step in Settle($"day{day}_morning_counter")) yield return step;

                    // Morning: call MorningScreen's own AcceptOrder() — the exact
                    // method the ACCEPT ORDER button's onClick calls — rather than
                    // GameLoopManager.ConfirmOrder directly. Calling the manager API
                    // only LOOKED equivalent: it skipped AcceptOrder's own
                    // HookOrder()+RefreshOrder() (left the ticket panel stuck on
                    // "No order yet") and its Prep/Bottling daily-pick reset (left
                    // both permanently reading "No herbs/seals left today") — both
                    // confirmed via screenshot review. AcceptOrder() also switches to
                    // the Cauldron tab itself. See the class doc's "reflection note"
                    // for why this goes through reflection rather than a simulated
                    // mouse click.
                    object morningScreen = UIManager.Instance != null ? UIManager.Instance.ScreenOf(ScreenId.Morning) : null;
                    CallPrivate(morningScreen, "AcceptOrder");
                    yield return null;
                    ActiveOrder order = CraftingManager.Instance != null ? CraftingManager.Instance.CurrentOrder : null;
                    if (order == null) { Fail("AcceptOrder did not produce a CurrentOrder."); yield break; }
                    Log($"Order '{order.potionName}' accepted, quality {order.qualityScore} ({order.GetGrade()})");

                    // TutorialManager flips StationsUnlocked on its own coroutine once
                    // it notices the order — wait for the real flag instead of
                    // assuming frame timing against a second, independent coroutine.
                    // On day 1 the tutorial's Welcome step blocks ~8 REAL seconds
                    // (unscaled — TimeControl's speed-up doesn't touch it) before it
                    // even reaches the Counter step that unlocks these tabs, so the
                    // timeout here has to clear that, not just network jitter.
                    foreach (var step in WaitUntil(() => TutorialManager.StationsUnlocked, 15f, "StationsUnlocked after AcceptOrder"))
                        yield return step;
                    if (_errorCount > 0) yield break;
                    // Prep comes first now: the Cauldron will not brew until the
                    // recipe's leaves are crushed in and ground, so a driver that
                    // skipped straight to stirring would sit on a cold pot forever.
                    // Every bench is played for real through the scripted pointer.
                    SwitchMorningTab(morningScreen, "Prep");
                    foreach (var step in Settle($"day{day}_morning_prep")) yield return step;
                    foreach (var step in DrivePrep(order, day)) { if (_errorCount > 0) break; yield return step; }
                    if (_errorCount > 0) yield break;

                    SwitchMorningTab(morningScreen, "Cauldron");
                    foreach (var step in Settle($"day{day}_morning_cauldron")) yield return step;
                    foreach (var step in DriveCauldron(order, day)) { if (_errorCount > 0) break; yield return step; }
                    if (_errorCount > 0) yield break;
                    foreach (var step in Settle($"day{day}_morning_cauldron_done")) yield return step;

                    SwitchMorningTab(morningScreen, "Bottling");
                    foreach (var step in Settle($"day{day}_morning_bottling")) yield return step;
                    foreach (var step in DriveBottling(order, day)) { if (_errorCount > 0) break; yield return step; }
                    if (_errorCount > 0) yield break;
                    foreach (var step in Settle($"day{day}_morning_bottling_done")) yield return step;
                    Log($"Morning done: {order.qualityScore} ({order.GetGrade()}); flawless would be {QualityBudget.FlawlessMorning()}.");

                    GameLoopManager.Instance.BeginHandoff();
                    foreach (var step in WaitForPhase(GamePhase.Handoff, 5f)) { if (_errorCount > 0) yield break; yield return step; }
                    if (_errorCount > 0) yield break;
                    foreach (var step in Settle($"day{day}_handoff")) yield return step;

                    GameLoopManager.Instance.BeginAfternoon();
                    foreach (var step in WaitForPhase(GamePhase.Afternoon, 5f)) { if (_errorCount > 0) yield break; yield return step; }
                    if (_errorCount > 0) yield break;
                    // WaitForPhase has not yielded (the phase is set synchronously), so
                    // this is still the frame BeginAfternoon ran on — the one t00s shows.
                    CheckFirstAfternoonFrame();
                    if (_errorCount > 0) yield break;

                    // A screenshot roughly every second for the whole fight, not just
                    // one frame at the start — a player reported the adventurer's
                    // sprite visibly changing partway through combat, which a single
                    // "just after Afternoon begins" + a "DEFEAT screen" capture can
                    // never catch (the DEFEAT frame is taken after the adventurer's
                    // corpse is already gone — there's no "damaged but still alive"
                    // frame in that pair at all).
                    foreach (var step in WaitForExpeditionEndWithCaptures(90f, $"day{day}_afternoon", captureEvery: 2f)) { if (_errorCount > 0) yield break; yield return step; }
                    if (_errorCount > 0) yield break;
                    foreach (var step in Settle($"day{day}_afternoon_result")) yield return step;

                    var world = GameLoopManager.Instance.CurrentExpedition;
                    if (world != null)
                    {
                        int expected = SaveSystem.Instance != null
                            ? SaveSystem.Instance.State.DeployedParty().Count : 1;
                        if (world.Party.Count != expected)
                            Fail($"Party size {world.Party.Count}, expected {expected} from the deploy cap.");
                        if (world.PartyRecords.Count == 0 || world.PartyRecords[0] == null)
                            Fail("Arena built an anonymous adventurer - the roster was not used.");
                        else
                        {
                            var names = new System.Text.StringBuilder();
                            int totalHp = 0, totalAmmo = 0;
                            for (int i = 0; i < world.PartyRecords.Count; i++)
                            {
                                HeroRecord r = world.PartyRecords[i];
                                if (r == null) continue;
                                if (names.Length > 0) names.Append(", ");
                                names.Append($"{r.displayName} Lv{r.level} {r.affinity}/{r.archetypeId}");
                                if (i < world.Party.Count && world.Party[i] != null) totalHp += world.Party[i].MaxHP;
                            }
                            if (GameLoopManager.Instance.PendingLoadouts != null)
                                foreach (var lo in GameLoopManager.Instance.PendingLoadouts)
                                    foreach (var slot in lo.Slots) totalAmmo += slot.count;

                            Log($"PARTY x{world.Party.Count} on biome {SaveSystem.Instance.State.TargetBiomeIndex}: " +
                                $"{names} | {totalHp} HP total, {totalAmmo} flasks total.");
                        }
                    }

                    GameLoopManager.Instance.BeginEvening();
                    foreach (var step in WaitForPhase(GamePhase.Evening, 10f)) { if (_errorCount > 0) yield break; yield return step; }
                    if (_errorCount > 0) yield break;
                    // A fight that earned part of the story plays it over the Evening first.
                    foreach (var step in PlayOutCutscenes($"day{day} evening", expect: false)) yield return step;
                    ExpeditionReport today = GameLoopManager.Instance.LatestReport;
                    if (today != null && today.won && today.bossDefeated && SaveSystem.Instance.State.TargetBiomeIndex >= 0)
                    {
                        var st0 = SaveSystem.Instance.State;
                        if (Story.StoryDirector.Has(st0, Story.StoryDirector.WoodwoseSeen) && st0.HasDiary("diary_woodwose"))
                            Log("The Woodwose fell: its scene played, and its diary page is unlocked.");
                        else if (!Story.StoryDirector.Has(st0, Story.StoryDirector.WoodwoseSeen))
                            Fail("A guardian fell but its scene was not recorded as watched.");
                    }
                    foreach (var step in Settle($"day{day}_evening_report")) yield return step;

                    if (day == 1)
                    {
                        // A first clear must unlock its diary entry. BeginEvening used to
                        // evaluate the diary after advancing the road, i.e. against the
                        // NEXT biome, and the day-3 replay of biome 0 hid it.
                        ExpeditionReport first = GameLoopManager.Instance.LatestReport;
                        if (first != null && first.won && !SaveSystem.Instance.State.HasDiary("diary_ww"))
                            Fail("Day 1 cleared the Whispering Woods but 'diary_ww' did not unlock.");

                        // The UI is English and formats numbers invariantly ("x1.00", not "x1,00").
                        string formatted = string.Format("{0:0.00}", 1.5f);
                        if (formatted != "1.50")
                            Fail($"UI numbers format as \"{formatted}\" - the invariant culture is not in effect.");

                        // The diary, for its layout and its nav glyphs.
                        DiaryScreen.OpenEntryId = null;
                        DiaryScreen.FromOpeningCinematic = false;
                        UIManager.Instance.Show(ScreenId.Diary);
                        foreach (var step in Settle("day1_diary")) yield return step;
                        UIManager.Instance.Show(ScreenId.Evening);
                        yield return null;
                    }

                    object eveningScreen = UIManager.Instance != null ? UIManager.Instance.ScreenOf(ScreenId.Evening) : null;
                    CallPrivate(eveningScreen, "ShowUpgrades");
                    foreach (var step in Settle($"day{day}_evening_upgrades")) yield return step;

                    // Two real days of income is 150-250 g, which renders every
                    // roster button as "can't afford" and tells us nothing. Top up
                    // AFTER the report screenshot so the ledger above stays honest,
                    // then exercise the actual transactions.
                    RunState st = SaveSystem.Instance != null ? SaveSystem.Instance.State : null;
                    if (st != null) st.gold = 800;

                    CallPrivate(eveningScreen, "ShowRoster");
                    foreach (var step in Settle($"day{day}_evening_roster")) yield return step;

                    if (st != null && day == 2)
                    {
                        int before = st.roster.Count;
                        if (before < 1)
                            Fail("Roster is empty at Evening - a run must always own at least one hero.");
                        CallPrivate(eveningScreen, "HireCandidate",
                            Data.HeroCatalog.HireCost(st.roster.Count));
                        if (st.roster.Count != before + 1)
                            Fail($"Hire did not add a hero (roster {before} -> {st.roster.Count}).");

                        string leadId = st.roster[0].id;
                        int lvlBefore = st.roster[0].level;
                        CallPrivate(eveningScreen, "LevelHero", leadId,
                            Data.HeroCatalog.LevelUpCost(lvlBefore));
                        if (st.roster[0].level != lvlBefore + 1)
                            Fail($"Level up did not take (level {lvlBefore} -> {st.roster[0].level}).");

                        Log($"Roster: {st.roster.Count} heroes, lead is {st.roster[0].displayName} " +
                            $"Lv{st.roster[0].level} ({st.roster[0].affinity}), {st.gold} g left.");
                        foreach (var step in Settle($"day{day}_evening_roster_bought")) yield return step;

                        // Build a full party for day 3, which replays biome 0 so
                        // it is directly comparable with day 1's solo run on the
                        // same road. This is the regression test for "does a
                        // three-hero party trivialise the early game".
                        st.bestGrades[1] = Math.Max(st.bestGrades[1], 1);
                        st.bestGrades[3] = Math.Max(st.bestGrades[3], 1);
                        st.gold = 2000;
                        CallPrivate(eveningScreen, "BuyUpgrade", UpgradeCatalog.SecondPack,
                            CostOf(UpgradeCatalog.SecondPack));
                        CallPrivate(eveningScreen, "BuyUpgrade", UpgradeCatalog.ThirdPack,
                            CostOf(UpgradeCatalog.ThirdPack));
                        if (st.DeployCap != 3)
                            Fail($"Deploy cap is {st.DeployCap} after buying both packs, expected 3.");

                        while (st.roster.Count < 3)
                        {
                            int n = st.roster.Count;
                            CallPrivate(eveningScreen, "HireCandidate", Data.HeroCatalog.HireCost(n));
                            if (st.roster.Count == n) { Fail("Could not hire up to a full party."); break; }
                        }
                        foreach (HeroRecord h in st.roster) h.deployed = true;

                        CallPrivate(eveningScreen, "ShowRoster");
                        foreach (var step in Settle($"day{day}_evening_roster_full")) yield return step;
                    }

                    GameLoopManager.Instance.BeginBiomeMap();
                    foreach (var step in WaitForPhase(GamePhase.BiomeMap, 5f)) { if (_errorCount > 0) yield break; yield return step; }
                    if (_errorCount > 0) yield break;
                    foreach (var step in Settle($"day{day}_biome_map")) yield return step;

                    if (day < DaysToRun) GameLoopManager.Instance.Sleep(day == 2 ? 0 : -1);
                }


                // The expedition slice scenarios run a REAL arena and tear down by
                // destroying every CombatantBody in the scene, so they run only
                // after the day loop is done - never alongside a live party.
                foreach (var step in RunExpeditionScenarios()) yield return step;

                // The physics and AI suites that never used to run anywhere.
                foreach (var step in RunSuite<DebugTools.BallisticLauncherSimulationTest>(30f)) yield return step;
                foreach (var step in RunSuite<DebugTools.BossAndMovementSimulationTest>(40f)) yield return step;
                foreach (var step in RunSuite<DebugTools.CauldronSimulationTest>(10f)) yield return step;
                foreach (var step in RunSuite<DebugTools.StationTabPhysicsSimulationTest>(20f)) yield return step;

                foreach (var step in RunBossScenarios()) yield return step;
                if (FullSuite)
                    foreach (var step in RunBiomeSweep()) yield return step;

                // Last, because it changes what the whole run remembers.
                foreach (var step in RunEnding()) yield return step;

                if (_errorCount == 0) Log("=== FULL LOOP COMPLETED — all days resolved cleanly ===");
            }

            // --------------------------------------------------------- bench bots
            //
            // The morning is played through the real benches with a scripted pointer
            // (PhysicsKit.Pointer.Scripted): the same code path a mouse drives, so the
            // leaves really fly into the mortar, the pestle really falls, the spoon
            // really stirs the floating herbs and the ladle really pours droplets into
            // the flask. The points that come out are what a careful player earns, not
            // a transcribed constant.

            private readonly PhysicsKit.ScriptedPointer _pointer = new PhysicsKit.ScriptedPointer();

            private IEnumerable Frames(int n)
            {
                for (int i = 0; i < n; i++) yield return null;
            }

            private IEnumerable DrivePrep(ActiveOrder order, int day)
            {
                var bench = Crafting.PrepBench.Instance;
                var mix = CraftingManager.Instance != null ? CraftingManager.Instance.Mixture : null;
                if (bench == null || mix == null) { Fail("Prep bench or mixture missing."); yield break; }
                PhysicsKit.Pointer.Scripted = _pointer;

                // Leaves: click each one the recipe asks for; a click tosses it into the bowl.
                foreach (ElementType want in mix.Recipe.Steps)
                {
                    var leaf = bench.BestLeafFor(want);
                    if (leaf == null) { Fail($"No {want} leaf on the Prep bench."); break; }
                    int before = mix.Added.Count;
                    Vector2 at = leaf.Body.position;
                    _pointer.World = at;
                    _pointer.Press();
                    foreach (var f in Frames(3)) yield return f;
                    _pointer.Release();
                    foreach (var f in Frames(2)) yield return f;
                    Log($"Prep: clicked {leaf.Data.DisplayName} at {at}; it left at {leaf.Body.linearVelocity} toward the bowl at {bench.MortarWorld}.");
                    float t = 0f;
                    while (mix.Added.Count == before && t < 6f) { t += Time.deltaTime; yield return null; }
                    if (mix.Added.Count == before)
                    {
                        Fail($"The {leaf.Data.DisplayName} never settled in the mortar (at {leaf.Body.position}, bowl {bench.MortarWorld}).");
                        break;
                    }
                }
                if (_errorCount > 0) { PhysicsKit.Pointer.Scripted = null; yield break; }
                foreach (var step in Settle($"day{day}_morning_prep_mortar")) yield return step;

                // Strikes: hold on the mortar until the pestle would land at the ideal
                // speed, then let go and let gravity do it.
                for (int s = 0; s < QualityBudget.GrindStrikes && !mix.Ground; s++)
                {
                    int left = bench.StrikesLeft;
                    _pointer.World = bench.MortarWorld;
                    _pointer.Press();
                    float t = 0f;
                    // Lift, let the pestle settle under the hold, then keep lifting to
                    // the height that lands it at the ideal speed.
                    while (!bench.LiftReady && t < 3f) { t += Time.deltaTime; yield return null; }
                    while (bench.PredictedStrikeSpeed < Crafting.PrepBench.IdealStrike - 0.1f && t < 6f)
                    { t += Time.deltaTime; yield return null; }
                    Log($"Prep lift {s + 1}: ready={bench.LiftReady} pestle at {bench.PestleWorld} (bowl {bench.MortarWorld}), predicted {bench.PredictedStrikeSpeed:0.00} m/s after {t:0.00}s.");
                    _pointer.Release();
                    t = 0f;
                    while (bench.StrikesLeft == left && t < 4f) { t += Time.deltaTime; yield return null; }
                    if (bench.StrikesLeft == left) { Fail($"Pestle strike {s + 1} never registered on the bowl."); break; }
                    Log($"Prep strike {s + 1}: {bench.LastStrikeSpeed:0.00} m/s (clean {Crafting.PrepBench.IdealStrike}±{bench.CurrentBand})");
                    foreach (var f in Frames(25)) yield return f;
                }
                PhysicsKit.Pointer.Scripted = null;
                if (!mix.Ready) Fail("Prep did not finish: the mixture is not ready after three strikes.");
                Log($"Prep: {mix.Recipe.Name} ({mix.Recipe.Shorthand}) -> {mix.Evaluate()} mix, quality {order.qualityScore}");
            }

            private IEnumerable DriveCauldron(ActiveOrder order, int day)
            {
                var pot = Crafting.PhysicsCauldronManager.Instance;
                var mix = CraftingManager.Instance != null ? CraftingManager.Instance.Mixture : null;
                if (pot == null || pot.Liquid == null) { Fail("No cauldron / surface to stir."); yield break; }

                // Wait for the mash to land on the surface.
                float w = 0f;
                int want = mix != null ? mix.Added.Count : 0;
                while (pot.Liquid.Floaters.Count < want && w < 5f) { w += Time.deltaTime; yield return null; }
                if (pot.Liquid.Floaters.Count < want)
                    Fail($"Only {pot.Liquid.Floaters.Count} of {want} leaves reached the cauldron's surface.");

                PhysicsKit.Pointer.Scripted = _pointer;
                float R = pot.Liquid.Radius;
                float dir = pot.RequiredClockwise ? -1f : 1f;
                float angle = 0f;

                // Day 2 also checks the fumble: a frantic stir must slop a herb out.
                if (day == 2)
                {
                    int splashes = pot.SplashCount;
                    for (float t = 0f; t < 1.8f && pot.SplashCount == splashes; t += Time.deltaTime)
                    {
                        angle += dir * 820f * Time.deltaTime * Mathf.Deg2Rad;
                        _pointer.World = pot.ToWorld(new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * R * 0.6f);
                        yield return null;
                    }
                    if (pot.SplashCount == splashes) Fail("A frantic stir (820 deg/s) never slopped a herb out of the pot.");
                    else Log("Cauldron fumble: a frantic stir slopped a herb out, as it should.");
                    foreach (var f in Frames(40)) yield return f;
                }

                float elapsed = 0f;
                bool captured = false;
                while (!pot.IsBrewComplete && elapsed < 60f)
                {
                    // Aim the stir speed at the heat the band wants; come up to it quickly.
                    float target = pot.BandCentre;
                    float power = pot.Heat01 < pot.MinOptimalHeat ? 0.85f
                        : pot.Heat01 > pot.MaxOptimalHeat ? 0.2f
                        : Mathf.Clamp((target - 0.15f) / 0.85f, 0.15f, 0.9f);
                    angle += dir * power * 420f * Time.deltaTime * Mathf.Deg2Rad;
                    _pointer.World = pot.ToWorld(new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * R * 0.6f);
                    elapsed += Time.deltaTime;
                    if (!captured && pot.BrewProgress01 > 0.4f)
                    {
                        captured = true;
                        Capture($"day{day}_morning_cauldron_brewing");
                    }
                    yield return null;
                }
                PhysicsKit.Pointer.Scripted = null;
                if (!pot.IsBrewComplete) Fail($"The brew never completed in 60 s of stirring (progress {pot.BrewProgress01:P0}).");
                Log($"Cauldron: brewed in {elapsed:0.0}s, {pot.DissolvedFraction:P0} dissolved, {pot.SplashCount} slopped, quality {order.qualityScore}");
            }

            private IEnumerable DriveBottling(ActiveOrder order, int day)
            {
                var bench = Crafting.BottlingBench.Instance;
                if (bench == null) { Fail("No Bottling bench."); yield break; }

                bench.PourHeld = true;
                float t = 0f;
                bool captured = false;
                // Let go a little early: what is already in the air still lands.
                while (bench.Fill01 < 0.72f && t < 12f)
                {
                    t += Time.deltaTime;
                    if (!captured && bench.Fill01 > 0.35f) { captured = true; Capture($"day{day}_morning_bottling_pour"); }
                    yield return null;
                }
                bench.PourHeld = false;
                if (bench.Fill01 < 0.72f) Fail($"Pouring for 12 s only filled the flask to {bench.Fill01:P0}.");
                t = 0f;
                while (bench.Current == Crafting.BottlingBench.Step.Pour && t < 8f) { t += Time.deltaTime; yield return null; }
                if (bench.Current == Crafting.BottlingBench.Step.Pour) { Fail("The pour was never scored (droplets never settled)."); yield break; }
                Log($"Bottling: poured to {bench.Fill01:P0} ({bench.Spilled} spilled)");

                // Seal on the beat (the needle runs on real time).
                for (int guard = 0; guard < 200000 && Mathf.Abs(Crafting.BottlingBench.SealNeedle01() - 0.5f) > 0.02f; guard++)
                    yield return null;
                bench.Seal();
                foreach (var f in Frames(30)) yield return f;
                bench.ApplyLabel(order.element);
                if (bench.Current != Crafting.BottlingBench.Step.Done) Fail($"Bottling ended at step {bench.Current}, not Done.");
                Log($"Bottling: sealed and labelled, quality {order.qualityScore}");
            }

            private void SwitchMorningTab(object morningScreen, string stationTabName)
            {
                if (morningScreen == null) { Fail("MorningScreen instance not found for tab switch."); return; }
                Type nested = morningScreen.GetType().GetNestedType("StationTab", BindingFlags.NonPublic);
                if (nested == null) { Fail("MorningScreen.StationTab enum not found (renamed?)."); return; }
                object value;
                try { value = Enum.Parse(nested, stationTabName); }
                catch (Exception e) { Fail($"StationTab.{stationTabName} not found: {e.Message}"); return; }
                CallPrivate(morningScreen, "SwitchTab", value);
            }

            // ------------------------------------------------------------ waiters

            private IEnumerable WaitForPhase(GamePhase phase, float timeoutSeconds)
            {
                float start = Time.unscaledTime;
                while (GameLoopManager.Instance == null || GameLoopManager.Instance.Phase != phase)
                {
                    if (Time.unscaledTime - start > timeoutSeconds)
                    {
                        Fail($"Timed out after {timeoutSeconds}s waiting for phase {phase} " +
                             $"(stuck at {GameLoopManager.Instance?.Phase}).");
                        yield break;
                    }
                    yield return null;
                }
                Log($"Phase -> {phase} ({Time.unscaledTime - start:F1}s)");
            }

            /// <summary>Like WaitForExpeditionEnd, but takes a screenshot roughly
            /// every <paramref name="captureEvery"/> real seconds while it waits, so
            /// the fight's whole timeline is visible afterward, not just one frame.</summary>
            private IEnumerable WaitForExpeditionEndWithCaptures(float timeoutSeconds, string namePrefix, float captureEvery = 1f)
            {
                float start = Time.unscaledTime;
                float gameStart = Time.time;
                float nextCapture = gameStart;
                ExpeditionWorld world = GameLoopManager.Instance.CurrentExpedition;
                if (world == null || world.Expedition == null)
                {
                    Fail("No ExpeditionWorld/ExpeditionManager present after BeginAfternoon.");
                    yield break;
                }

                // Every "X throws ..." ticker line must name someone who is actually
                // in the arena. It used to name the contract's buyer for every throw.
                var partyNames = new HashSet<string>();
                foreach (HeroRecord h in world.PartyRecords)
                    if (h != null) partyNames.Add(h.displayName);
                TMP_Text ticker = PrivateText(HudScreen(), "_ticker");
                string lastTicker = null;
                int throwLines = 0;

                while (world.Expedition.Phase != ExpeditionPhase.Won && world.Expedition.Phase != ExpeditionPhase.Lost)
                {
                    string line = ticker != null ? ticker.text : null;
                    if (!string.IsNullOrEmpty(line) && line != lastTicker)
                    {
                        lastTicker = line;
                        int at = line.IndexOf(" throws ", StringComparison.Ordinal);
                        if (at > 0)
                        {
                            throwLines++;
                            string who = line.Substring(0, at);
                            if (partyNames.Count > 0 && !partyNames.Contains(who))
                            {
                                Fail($"Ticker credits '{who}' with a throw, but the party is " +
                                     $"{string.Join(", ", partyNames)}: \"{line}\"");
                                yield break;
                            }
                        }
                    }

                    float now = Time.unscaledTime;
                    float gameNow = Time.time;
                    if (now - start > timeoutSeconds)
                    {
                        Fail($"Expedition never resolved within {timeoutSeconds}s " +
                             $"(stuck at {world.Expedition.Phase}, wave {world.Expedition.WaveNumber}/{world.Expedition.TotalWaves}).");
                        yield break;
                    }
                    // Spaced in GAME seconds: under the fixed step a whole fight takes
                    // about a wall-clock second, so a real-time cadence caught one frame.
                    if (gameNow >= nextCapture)
                    {
                        Capture($"{namePrefix}_t{Mathf.RoundToInt(gameNow - gameStart):00}s");
                        nextCapture = gameNow + captureEvery;
                    }
                    yield return null;
                }
                // The bug this guards: a wave that hit its safety cap used to be
                // despawned and fall through to Win(), reporting a full clear.
                if (world.Expedition.Phase == ExpeditionPhase.Won
                    && world.Expedition.WavesCleared < world.Expedition.TotalWaves)
                    Fail($"Reported a WIN with only {world.Expedition.WavesCleared}/" +
                         $"{world.Expedition.TotalWaves} waves cleared.");

                var rep = world.Telemetry != null ? world.Telemetry.Report : null;
                int flasksLeft = 0;
                foreach (var b in world.Party)
                    if (b != null)
                    {
                        var c = b.GetComponent<UtilityAI_CombatController>();
                        if (c != null) flasksLeft += c.FlasksLeft;
                    }
                string detail = rep != null
                    ? $", waves {rep.wavesCleared}/{rep.totalWaves}, " +
                      $"down {rep.partyDown}/{rep.partyTotal}, {flasksLeft} flasks left"
                    : "";
                Log($"Expedition resolved: {world.Expedition.Phase} " +
                    $"({Time.unscaledTime - start:F1}s{detail})");
                _fingerprint.Add($"{namePrefix}={FightSummary(world, Time.time - gameStart)}");
                Log($"Ticker: {throwLines} throw line(s) seen, all credited to the party " +
                    $"({string.Join(", ", partyNames)}).");
            }

            /// <summary>
            /// The frame BeginAfternoon ran on, before anything has had an Update.
            /// Guards two stale-first-frame bugs: the shop still drawing (its Destroy
            /// is deferred to end of frame, so its cauldron rendered inside the
            /// arena) and the HUD banner still carrying yesterday's biome.
            /// </summary>
            private void CheckFirstAfternoonFrame()
            {
                ExpeditionWorld world = GameLoopManager.Instance.CurrentExpedition;
                foreach (ShopWorld shop in FindObjectsByType<ShopWorld>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
                    foreach (Renderer r in shop.GetComponentsInChildren<Renderer>())
                        if (r.enabled)
                        {
                            Fail($"Shop renderer '{r.name}' is still drawing on the first afternoon frame - it shows in the arena.");
                            break;
                        }
                Camera main = Camera.main;
                if (world != null && main != world.ArenaCamera)
                    Fail($"Camera.main on the first afternoon frame is '{(main != null ? main.name : "none")}', " +
                         "not the arena camera.");

                TMP_Text banner = PrivateText(HudScreen(), "_banner");
                if (banner == null) return;
                string biome = BiomeLibrary.Name(GameLoopManager.Instance.TargetBiomeIndex);
                if (!banner.text.StartsWith(biome, StringComparison.Ordinal))
                    Fail($"First afternoon frame's banner reads \"{banner.text}\", expected today's biome '{biome}'.");
                else
                    Log($"First afternoon frame: banner \"{banner.text}\", no shop drawing, arena camera is main.");
            }

            private static object HudScreen() =>
                UIManager.Instance != null ? UIManager.Instance.ScreenOf(ScreenId.ExpeditionHud) : null;

            private IEnumerable WaitUntil(Func<bool> predicate, float timeoutSeconds, string what)
            {
                float start = Time.unscaledTime;
                while (!predicate())
                {
                    if (Time.unscaledTime - start > timeoutSeconds)
                    {
                        Fail($"Timed out after {timeoutSeconds}s waiting for {what}.");
                        yield break;
                    }
                    yield return null;
                }
            }

            private IEnumerable Settle(string screenshotName)
            {
                for (int i = 0; i < 6; i++) yield return null;
                Capture(screenshotName);
            }

            private IEnumerable SettleFrames(int frames)
            {
                for (int i = 0; i < frames; i++) yield return null;
            }

            // -------------------------------------------------------- screenshots
            //
            // ScreenCapture.CaptureScreenshot does NOT work here: it captures at
            // WaitForEndOfFrame, which never fires in windowless batchmode (no real
            // frame is ever presented) — the very first real run of this tool
            // "passed" with every checkpoint logged, and headless-screens/ came back
            // completely empty. Rendering a dedicated camera to a RenderTexture and
            // reading it back with ReadPixels works instead: it's pure GPU work with
            // no dependency on a window or frame presentation. The one thing that
            // breaks with that approach is the UI — every canvas in this project is
            // ScreenSpaceOverlay, which draws straight to the (nonexistent) window
            // and is invisible to ANY camera — so canvases are retargeted to
            // ScreenSpaceCamera pointed at the capture camera for the duration of
            // the shot, then restored. This picks up every canvas generically
            // (FindObjectsByType), not just UIManager's — TutorialManager builds its
            // own overlay canvas and it needs capturing too.

            private Camera _captureCam;
            private RenderTexture _captureRT;
            private const int CapW = 1600, CapH = 900;

            private void EnsureCaptureCamera()
            {
                if (_captureCam != null) return;
                var go = new GameObject("~HeadlessCaptureCamera");
                go.transform.SetParent(transform, false);
                _captureCam = go.AddComponent<Camera>();
                _captureCam.enabled = false; // rendered manually via Render(), never by the normal camera loop
                _captureRT = new RenderTexture(CapW, CapH, 24, RenderTextureFormat.ARGB32);

            }

            private void Capture(string name)
            {
                Texture2D tex = null;
                var restore = new List<(Canvas canvas, RenderMode mode, Camera cam, float plane)>();
                try
                {
                    EnsureCaptureCamera();

                    Camera world = Camera.main;
                    if (world != null)
                    {
                        _captureCam.CopyFrom(world); // matches framing/zoom/clear colour — copies targetTexture too, reset below
                    }
                    else
                    {
                        _captureCam.orthographic = true;
                        _captureCam.orthographicSize = 5f;
                        _captureCam.transform.position = new Vector3(0f, 0f, -10f);
                        _captureCam.clearFlags = CameraClearFlags.SolidColor;
                        _captureCam.backgroundColor = new Color(0.106f, 0.078f, 0.122f); // ink-900, matches Bootstrap's own fallback
                    }
                    _captureCam.targetTexture = _captureRT;
                    _captureCam.enabled = false;

                    // The shop camera draws into a viewport rect (the station column).
                    // Canvases retargeted onto a rect camera get squeezed into its rect,
                    // so the capture camera covers the whole frame instead and is
                    // zoomed out and shifted so the world lands in exactly the pixels
                    // the viewport would have put it in. Outside that rect the opaque
                    // UI panels cover what the extra frame shows.
                    if (world != null && world.orthographic && world.rect.width < 0.999f)
                    {
                        Rect r = world.rect;
                        float size = world.orthographicSize / Mathf.Max(0.01f, r.height);
                        float aspect = CapW / (float)CapH;
                        Vector2 centre = r.center;
                        Vector3 offset = new Vector3((0.5f - centre.x) * 2f * size * aspect, (0.5f - centre.y) * 2f * size, 0f);
                        _captureCam.rect = new Rect(0f, 0f, 1f, 1f);
                        _captureCam.orthographicSize = size;
                        _captureCam.transform.position = world.transform.position + offset;
                        _captureCam.backgroundColor = new Color(0.106f, 0.078f, 0.122f);
                    }

                    foreach (var c in FindObjectsByType<Canvas>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
                    {
                        if (c.renderMode != RenderMode.ScreenSpaceOverlay) continue;
                        restore.Add((c, c.renderMode, c.worldCamera, c.planeDistance));
                        c.renderMode = RenderMode.ScreenSpaceCamera;
                        c.worldCamera = _captureCam;
                        c.planeDistance = 1f;
                    }

                    _captureCam.Render();

                    RenderTexture prevActive = RenderTexture.active;
                    RenderTexture.active = _captureRT;
                    tex = new Texture2D(CapW, CapH, TextureFormat.RGB24, false);
                    tex.ReadPixels(new Rect(0, 0, CapW, CapH), 0, 0);
                    tex.Apply();
                    RenderTexture.active = prevActive;

                    string dir = Path.Combine(Directory.GetCurrentDirectory(), ScreensDir);
                    Directory.CreateDirectory(dir);
                    string path = Path.Combine(dir, name + ".png");
                    File.WriteAllBytes(path, tex.EncodeToPNG());
                    Log($"Screenshot: {ScreensDir}/{name}.png");
                }
                catch (Exception e)
                {
                    Log($"Screenshot '{name}' failed (non-fatal): {e.Message}");
                }
                finally
                {
                    foreach (var (canvas, mode, cam, plane) in restore)
                    {
                        if (canvas == null) continue;
                        canvas.renderMode = mode;
                        canvas.worldCamera = cam;
                        canvas.planeDistance = plane;
                    }
                    if (_captureCam != null) _captureCam.targetTexture = null;
                    if (tex != null) Destroy(tex);
                }
            }

            // -------------------------------------------------------- reflection

            private void CallPrivate(object target, string method, params object[] args)
            {
                if (target == null) { Fail($"CallPrivate: target is null for '{method}'."); return; }
                MethodInfo mi = target.GetType().GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance);
                if (mi == null) { Fail($"CallPrivate: '{method}' not found on {target.GetType().Name} (renamed?)."); return; }
                try { mi.Invoke(target, args); }
                catch (Exception e) { Fail($"CallPrivate '{method}' threw: {e.InnerException ?? e}"); }
            }

            private TMP_Text PrivateText(object target, string field)
            {
                if (target == null) { Fail($"PrivateText: target is null for '{field}'."); return null; }
                FieldInfo fi = target.GetType().GetField(field, BindingFlags.NonPublic | BindingFlags.Instance);
                var text = fi != null ? fi.GetValue(target) as TMP_Text : null;
                if (text == null) Fail($"PrivateText: '{field}' not found on {target.GetType().Name} (renamed?).");
                return text;
            }

            /// <summary>
            /// Spawn GameLoopSimulationTest, let its Start() run, then tear it down.
            /// </summary>
            /// <summary>
            /// Win / lose / out-of-flasks scenarios against a real ExpeditionManager.
            /// Budgeted past the sum of their own internal timeouts so a slow
            /// scenario is never cut off half-finished.
            /// </summary>
            private IEnumerable RunExpeditionScenarios()
            {
                var arena = new GameObject("~ExpeditionChecks");
                arena.AddComponent<DebugTools.ExpeditionSimulationTest>();

                float budget = 0f;
                while (budget < 150f && !DebugTools.ExpeditionSimulationTest.Finished)
                {
                    budget += Time.deltaTime;
                    yield return null;
                }
                if (!DebugTools.ExpeditionSimulationTest.Finished)
                    Fail($"Expedition slice scenarios did not finish within {budget:F0}s.");

                Destroy(arena);
                MonsterRegistry.Clear();
                AdventurerRegistry.Clear();
                Log("Expedition slice scenarios complete.");
            }

            private IEnumerable RunSimulationChecks()
            {
                var go = new GameObject("~SimulationChecks");
                go.AddComponent<DebugTools.GameLoopSimulationTest>();
                // Pure scoring checks - fake combatants, no arena, so they are safe
                // to run here rather than with the expedition scenarios.
                go.AddComponent<DebugTools.UtilityAiSimulationTest>();
                yield return null;   // Start() runs on the next frame
                yield return null;
                Destroy(go);

                Log(_errorCount == 0
                    ? "Meta-layer simulation checks passed."
                    : $"Meta-layer simulation checks reported {_errorCount} failure(s).");
            }

            /// <summary>
            /// Log how the three clocks move for a few frames, so a reader of the
            /// report can see whether unscaled time follows the fixed step (the
            /// harness's own timeouts are measured in it).
            /// </summary>
            private IEnumerable ProbeClocks()
            {
                float rt0 = Time.realtimeSinceStartup, ut0 = Time.unscaledTime, t0 = Time.time;
                for (int i = 0; i < 10; i++) yield return null;
                Log($"Clock probe over 10 frames: time +{Time.time - t0:F3}s, unscaled +{Time.unscaledTime - ut0:F3}s, " +
                    $"wall +{Time.realtimeSinceStartup - rt0:F3}s.");
            }

            /// <summary>"Won/23.3s/w3of3/hp412/fl19" — outcome, game seconds, waves,
            /// party HP left, flasks left. Compact so the fingerprint line diffs well.</summary>
            private static string FightSummary(ExpeditionWorld world, float gameSeconds)
            {
                int hp = 0, flasks = 0;
                foreach (var b in world.Party)
                {
                    if (b == null) continue;
                    if (b.IsAlive) hp += b.CurrentHP;
                    var c = b.GetComponent<UtilityAI_CombatController>();
                    if (c != null) flasks += c.FlasksLeft;
                }
                var exp = world.Expedition;
                string boss = "";
                if (exp.BossInstance != null)
                {
                    var bb = exp.BossInstance.GetComponent<CombatantBody>();
                    if (bb != null) boss = $"/boss{bb.CurrentHP}";
                }
                return $"{exp.Phase}/{gameSeconds:F1}s/w{exp.WavesCleared}of{exp.TotalWaves}/hp{hp}/fl{flasks}{boss}";
            }

            /// <summary>Add one self-checking suite, wait for it to report, tear it down.</summary>
            private IEnumerable RunSuite<T>(float timeoutSeconds) where T : MonoBehaviour, DebugTools.ISimulationSuite
            {
                var go = new GameObject($"~Suite_{typeof(T).Name}");
                T suite = go.AddComponent<T>();
                float t = 0f;
                while (!suite.Done && t < timeoutSeconds)
                {
                    t += Time.unscaledDeltaTime;
                    yield return null;
                }
                if (!suite.Done) Fail($"{typeof(T).Name} did not finish within {timeoutSeconds}s.");
                else Log($"{typeof(T).Name} finished ({t:F1}s).");
                Destroy(go);
                yield return null;
                MonsterRegistry.Clear();
                AdventurerRegistry.Clear();
            }

            /// <summary>
            /// Each guardian, fought once on a fixed seed. The day loop never reaches the
            /// Coven Matriarch and only meets the Woodwose on the day-3 replay, so without
            /// this the bosses were barely exercised at all.
            /// </summary>
            private IEnumerable RunBossScenarios()
            {
                foreach (var step in RunScenario(0, 5, "boss_woodwose", capture: true)) yield return step;
                foreach (var step in RunScenario(BiomeLibrary.Count - 1, 5, "boss_matriarch", capture: true)) yield return step;
            }

            /// <summary>Full suite only: every biome on five seeds, for win rates and HP margins.</summary>
            private IEnumerable RunBiomeSweep()
            {
                for (int biome = 0; biome < BiomeLibrary.Count; biome++)
                    for (int seed = 11; seed <= 15; seed++)
                        foreach (var step in RunScenario(biome, seed, $"sweep_b{biome}_s{seed}", capture: false))
                            yield return step;
            }

            /// <summary>
            /// A real arena for <paramref name="biome"/> with its boss enabled, fought by
            /// whatever party the run has deployed (three heroes after day 2) carrying a
            /// Great flask of the element the Counter recommends for that road. The day
            /// seeds the monster spawner, so the fight is reproducible.
            /// </summary>
            private IEnumerable RunScenario(int biome, int seedDay, string label, bool capture)
            {
                RunState st = SaveSystem.Instance != null ? SaveSystem.Instance.State : null;
                if (st == null) { Fail($"Scenario {label}: no run state."); yield break; }

                int savedDay = st.day, savedReplay = st.replayBiomeIndex;
                st.day = seedDay;
                st.replayBiomeIndex = biome;

                var canvases = new List<Canvas>();
                foreach (var c in FindObjectsByType<Canvas>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
                    if (c.enabled) { c.enabled = false; canvases.Add(c); }

                BiomeData data = BiomeLibrary.Get(biome);
                ElementType element = ContractBoard.Counter(ContractBoard.Dominant(ContractBoard.ThreatCounts(data)));
                var order = new ActiveOrder("scenario", $"{element} Flask", element);
                order.AdjustQuality(60, "HeadlessPlaytest", "scenario flask (Great)");
                var party = st.DeployedParty();
                var loadouts = LoadoutBuilder.BuildAll(new[] { order }, party);

                var root = new GameObject($"~Scenario_{label}");
                var world = root.AddComponent<ExpeditionWorld>();
                world.Build(data, loadouts, party, enableBoss: true);

                float start = Time.time, nextCapture = 0f;
                const float Timeout = 240f;
                var watch = new BossWatch();
                while (world.Expedition.Phase != ExpeditionPhase.Won && world.Expedition.Phase != ExpeditionPhase.Lost
                       && Time.time - start < Timeout)
                {
                    if (world.Expedition.Phase == ExpeditionPhase.BossFight && world.Expedition.BossInstance != null)
                    {
                        watch.Attach(world.Expedition.BossInstance);
                        watch.Tick();
                        if (capture)
                        {
                            foreach (string shot in watch.TakeShots()) Capture($"{label}_{shot}");
                            if (Time.time >= nextCapture)
                            {
                                Capture($"{label}_t{Mathf.RoundToInt(Time.time - start):000}s");
                                nextCapture = Time.time + 5f;
                            }
                        }
                        if (!watch.BoundsChecked && watch.Settled) CheckBossFigure(watch, label);
                    }
                    yield return null;
                }
                if (world.Expedition.Phase != ExpeditionPhase.Won && world.Expedition.Phase != ExpeditionPhase.Lost)
                    Fail($"Scenario {label} did not resolve within {Timeout}s of game time.");
                else if (world.Expedition.Phase == ExpeditionPhase.Won
                         && world.Expedition.WavesCleared < world.Expedition.TotalWaves)
                    Fail($"Scenario {label} reported a WIN with only {world.Expedition.WavesCleared}/{world.Expedition.TotalWaves} waves.");

                string summary = FightSummary(world, Time.time - start);
                if (watch.Boss != null || watch.FightSeconds > 0f) summary += $"/bt{watch.FightSeconds:F1}";
                Log($"Scenario {label} ({BiomeLibrary.Name(biome)}, party x{party.Count}): {summary} — {world.Expedition.OutcomeReason}");
                if (watch.Telegraphs > 0)
                    Log($"  {label} boss: {watch.Telegraphs} windups ({watch.ShapeCounts}), {watch.Landings} landings, " +
                        $"{watch.HeroHits} hero hits, phases {watch.PhasePath}.");
                _fingerprint.Add($"{label}={summary}");
                if (capture && world.Expedition.Phase == ExpeditionPhase.Won && watch.Telegraphs > 0)
                {
                    // Let the guardian come apart on camera before the arena is torn down.
                    float died = Time.time;
                    bool mid = false;
                    while (Time.time - died < 1.8f)
                    {
                        if (!mid && Time.time - died >= 0.55f) { Capture($"{label}_death"); mid = true; }
                        yield return null;
                    }
                    Capture($"{label}_remains");
                }
                else if (capture) Capture($"{label}_end");
                if (capture) CheckMaterials(root, label);

                Destroy(root);
                yield return null;
                MonsterRegistry.Clear();
                AdventurerRegistry.Clear();
                foreach (var c in canvases) if (c != null) c.enabled = true;
                st.day = savedDay;
                st.replayBiomeIndex = savedReplay;
            }

            /// <summary>
            /// Follows one boss through its fight: what it wound up, what landed, the
            /// phases it went through, and the moments worth a screenshot (arrival, the
            /// first windup of each shape, the first frame of each phase).
            /// </summary>
            private sealed class BossWatch
            {
                public GameObject Boss;
                public int Telegraphs, Landings, HeroHits;
                public float FightSeconds;
                public bool BoundsChecked;
                public string PhasePath = "Neutral";
                private BossPhaseManager _phase;
                private readonly Dictionary<BossAttackShape, int> _shapes = new Dictionary<BossAttackShape, int>();
                private readonly List<(float at, string name)> _due = new List<(float, string)>();
                private readonly HashSet<string> _seen = new HashSet<string>();
                private float _start;

                public bool Settled => _phase != null && !_phase.IsEntering && Time.time - _start > 0.3f;
                public string ShapeCounts
                {
                    get
                    {
                        var parts = new List<string>();
                        foreach (var kv in _shapes) parts.Add($"{kv.Key} x{kv.Value}");
                        return string.Join(", ", parts);
                    }
                }

                public void Attach(GameObject boss)
                {
                    if (boss == Boss) return;
                    Boss = boss;
                    _start = Time.time;
                    _phase = boss.GetComponent<BossPhaseManager>();
                    var exec = boss.GetComponent<BossAttackExecutor>();
                    if (exec != null)
                    {
                        exec.OnTelegraph += (p, o, spots, windup) =>
                        {
                            Telegraphs++;
                            _shapes.TryGetValue(p.Shape, out int n);
                            _shapes[p.Shape] = n + 1;
                            Once("telegraph_" + p.Shape.ToString().ToLowerInvariant(), windup * 0.7f);
                        };
                        exec.OnStrike += (p, at, caught) => { Landings++; HeroHits += caught; };
                    }
                    if (_phase != null)
                        _phase.OnPhaseChanged += (from, to) =>
                        {
                            PhasePath += ">" + to;
                            Once("phase_" + to.ToString().ToLowerInvariant(), 0.45f);
                        };
                    Once("entrance", 0.7f);
                    Once("arrived", (_phase != null && _phase.Definition != null ? _phase.Definition.EntranceSeconds : 1.5f) + 0.4f);
                }

                public void Tick()
                {
                    var body = Boss != null ? Boss.GetComponent<CombatantBody>() : null;
                    if (body != null && body.IsAlive) FightSeconds = Time.time - _start;
                }

                private void Once(string name, float delay)
                {
                    if (_seen.Add(name)) _due.Add((Time.time + delay, name));
                }

                public IEnumerable<string> TakeShots()
                {
                    for (int i = _due.Count - 1; i >= 0; i--)
                    {
                        if (Time.time < _due[i].at) continue;
                        string n = _due[i].name;
                        _due.RemoveAt(i);
                        yield return n;
                    }
                }
            }

            /// <summary>
            /// The boss's physics footprint must sit inside its drawn figure. The old
            /// boss scaled its collider with its sprite to a 2.42-unit radius, wider
            /// than the art: flasks burst on thin air beside it.
            /// </summary>
            private void CheckBossFigure(BossWatch watch, string label)
            {
                watch.BoundsChecked = true;
                var col = watch.Boss.GetComponent<Collider2D>();
                bool any = false;
                Bounds art = default;
                foreach (var sr in watch.Boss.GetComponentsInChildren<SpriteRenderer>())
                {
                    if (!sr.enabled || sr.sprite == null) continue;
                    string n = sr.gameObject.name;
                    if (n == "Shadow" || n == "Glow" || n == "Ward" || n == "Flash") continue;
                    if (!any) { art = sr.bounds; any = true; } else art.Encapsulate(sr.bounds);
                }
                if (col == null || !any) { Fail($"{label}: boss has no collider or no drawn figure."); return; }
                Bounds c = col.bounds;
                bool inside = c.min.x >= art.min.x && c.max.x <= art.max.x && c.min.y >= art.min.y && c.max.y <= art.max.y;
                string msg = $"{label}: collider {c.size.x:0.00}x{c.size.y:0.00} at {(Vector2)c.center}, figure {art.size.x:0.00}x{art.size.y:0.00} at {(Vector2)art.center}";
                if (inside) Log(msg + " - footprint inside the figure.");
                else Fail(msg + " - the collider pokes out of the drawn boss.");
            }

            /// <summary>Every sprite draws with a material built for its own texture (never one shared across textures).</summary>
            private void CheckMaterials(GameObject root, string label)
            {
                int bad = 0, seen = 0;
                foreach (var sr in root.GetComponentsInChildren<SpriteRenderer>(true))
                {
                    if (sr.sprite == null || sr.sharedMaterial == null) continue;
                    seen++;
                    Texture mt = sr.sharedMaterial.mainTexture;
                    if (mt != null && mt != sr.sprite.texture)
                    {
                        if (bad++ < 3) Fail($"{label}: '{sr.name}' draws {sr.sprite.texture.name} with a material for {mt.name}.");
                    }
                }
                if (bad == 0) Log($"{label}: {seen} sprite renderers, each on its own texture's material.");
            }

            /// <summary>
            /// Wait for a cutscene (if one starts within a few seconds), photograph every
            /// shot once it is fully on screen, and wait for it to hand back.
            /// </summary>
            private IEnumerable PlayOutCutscenes(string label, bool expect, float startWithin = 4f, float timeout = 180f)
            {
                float t0 = Time.unscaledTime;
                while (!CutsceneScreen.Playing && Time.unscaledTime - t0 < startWithin) yield return null;
                if (!CutsceneScreen.Playing)
                {
                    if (expect) Fail($"{label}: a cutscene was due and none played.");
                    yield break;
                }
                string last = null;
                int shots = 0;
                float start = Time.unscaledTime;
                while (CutsceneScreen.Playing && Time.unscaledTime - start < timeout)
                {
                    string key = CutsceneScreen.SettledShot;
                    if (key != null && key != last)
                    {
                        Capture($"cutscene_{key}");
                        last = key;
                        shots++;
                    }
                    yield return null;
                }
                if (CutsceneScreen.Playing) Fail($"{label}: the cutscene was still playing after {timeout}s.");
                else Log($"{label}: cutscene played through ({shots} shots photographed).");
            }

            /// <summary>
            /// The end of the story, as the Evening after a first Peak win meets it: the
            /// day is resolved (the scene is flagged and saved before anything plays),
            /// the Evening plays reveal, ending and credits, and the run afterwards
            /// remembers it, keeps its last diary pages, and still reaches the map.
            /// </summary>
            private IEnumerable RunEnding()
            {
                RunState s = SaveSystem.Instance != null ? SaveSystem.Instance.State : null;
                if (s == null) { Fail("Ending: no run state."); yield break; }

                int peak = BiomeLibrary.Count - 1;
                var report = new ExpeditionReport { won = true, bossDefeated = true };
                Story.DiaryManager.EvaluateAfterExpedition(s, peak, report);
                Story.StoryDirector.OnDayResolved(s, peak, report);
                SaveSystem.Instance.MarkDirty();
                SaveSystem.Instance.AutoSave();

                RunState onDisk = SaveSystem.Instance.Peek(0);
                if (onDisk == null || !Story.StoryDirector.Has(onDisk, Story.StoryDirector.EndingPending))
                    Fail("Ending: the pending ending was not saved before it played (a quit now would lose it).");

                UIManager.Instance.Show(ScreenId.Evening);
                foreach (var step in PlayOutCutscenes("ending", expect: true, timeout: 240f)) yield return step;

                onDisk = SaveSystem.Instance.Peek(0);
                if (!s.endingSeen || !Story.StoryDirector.Has(s, Story.StoryDirector.Epilogue))
                    Fail("Ending: the credits ended but the run does not remember the ending.");
                else if (onDisk == null || !onDisk.endingSeen)
                    Fail("Ending: the ending is remembered in memory but not on disk.");
                foreach (string id in new[] { "diary_mask", "diary_end", "diary_after" })
                    if (!s.HasDiary(id)) Fail($"Ending: diary page '{id}' did not unlock.");
                if (Story.StoryDirector.Due(s).Count != 0) Fail("Ending: a scene is still due after the credits.");
                if (UIManager.Instance.Current != ScreenId.Evening)
                    Fail($"Ending: the credits handed back to {UIManager.Instance.Current}, not the Evening.");
                else Log($"Ending: reveal, ending and credits played; {s.unlockedDiary.Count} diary pages; back at the Evening.");
                foreach (var step in Settle("after_ending_evening")) yield return step;

                // The shop stays open: the diary has the last page, the map still works,
                // and the menu shows the morning after.
                DiaryScreen.OpenEntryId = "diary_after";
                DiaryScreen.FromOpeningCinematic = false;
                UIManager.Instance.Show(ScreenId.Diary);
                foreach (var step in Settle("after_ending_diary")) yield return step;
                UIManager.Instance.Show(ScreenId.BiomeMap);
                foreach (var step in Settle("after_ending_map")) yield return step;
                UIManager.Instance.Show(ScreenId.MainMenu);
                // The key art fades up on unscaled time, which is wall-clock time under the harness.
                for (float t0 = Time.unscaledTime; Time.unscaledTime - t0 < 1.2f;) yield return null;
                foreach (var step in Settle("after_ending_menu")) yield return step;
            }

            // ---------------------------------------------------------- plumbing

            private void OnUnityLog(string condition, string stackTrace, LogType type)
            {
                if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
                    Fail($"[{type}] {condition}");
            }

            private void Log(string msg)
            {
                string line = $"[{_wall.Elapsed:mm\\:ss}] {msg}";
                Debug.Log("[HeadlessPlaytest] " + line);
                _log.AppendLine(line);
            }

            private static int CostOf(string upgradeId)
            {
                foreach (var u in UpgradeCatalog.All) if (u.Id == upgradeId) return u.Cost;
                return 0;
            }

            private void Fail(string msg)
            {
                _errorCount++;
                string line = $"[{_wall.Elapsed:mm\\:ss}] FAIL: {msg}";
                Debug.LogError("[HeadlessPlaytest] " + line);
                _log.AppendLine(line);
            }

            private bool _finished;

            private void Finish()
            {
                if (_finished) return;   // the wrapper always calls this; be idempotent
                _finished = true;

                Application.logMessageReceived -= OnUnityLog;
                PhysicsKit.Pointer.Scripted = null;
                Time.captureDeltaTime = 0f;
                SaveSystem.SlotOverride = -1;
                SettingsService.ExpeditionSpeedOverride = null;
                CutsceneScreen.AutoAdvance = false;
                StoryStage.SkipIntro = false;

                string verdict = _errorCount == 0 ? "PASS" : $"FAIL ({_errorCount} error(s))";
                _log.AppendLine($"FINGERPRINT: {string.Join(" ", _fingerprint)}");
                _log.AppendLine($"=== RESULT: {verdict} ===");

                string path = Path.Combine(Directory.GetCurrentDirectory(), ReportPath);
                try { File.WriteAllText(path, _log.ToString()); }
                catch (Exception e) { Debug.LogError("[HeadlessPlaytest] Could not write report: " + e); }

                Debug.Log($"[HeadlessPlaytest] {verdict} — report written to {path}");
                EditorApplication.Exit(_errorCount == 0 ? 0 : 1);
            }
        }
    }
}
#endif
