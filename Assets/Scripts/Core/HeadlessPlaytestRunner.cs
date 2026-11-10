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
        // day 1: tutorial, solo, the Woods (no guardian). day 2: solo, the Woodwose first met.
        // day 3: REPLAYS biome 0 with a full three-hero party, so it is directly
        // comparable with day 1 on the same road - the regression test for
        // "does a bigger party trivialise the early game".
        private const int DaysToRun = 4;

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
            /// <summary>Only the difficulty curve: every road against every party a player could field.</summary>
            private bool BalanceSuite => _suite == "balance";

            /// <summary>One entry per resolved fight, joined into the report's
            /// FINGERPRINT line so two runs can be diffed.</summary>
            private readonly List<string> _fingerprint = new List<string>();

            /// <summary>The harness's slot, in its own save folder. See <see cref="SaveSystem.DirectoryOverride"/>.</summary>
            private const int HarnessSlot = 0;

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
                // Its own save folder, emptied: the title starts with nothing to continue.
                string harnessSaves = Path.Combine(Application.persistentDataPath, "harness");
                try { if (Directory.Exists(harnessSaves)) Directory.Delete(harnessSaves, true); } catch (Exception) { }
                Directory.CreateDirectory(harnessSaves);
                SaveSystem.DirectoryOverride = harnessSaves;
                SettingsService.ExpeditionSpeedOverride = 1;
                CutsceneScreen.AutoAdvance = true;
                StoryStage.SkipIntro = true;
                // The procedural art, pixel for pixel, for the design document.
                Art.PixelCanvas.ExportDir = Path.Combine(Directory.GetCurrentDirectory(), "headless-screens", "art");
                Time.captureDeltaTime = FrameStep;
                Log($"=== HEADLESS PLAYTEST START (runtime driver, post-reload, suite={_suite}) ===");
                Log($"Saves pinned to {SaveSystem.DirectoryOverride}, fight speed pinned to 1x, fixed step {FrameStep:F4}s.");
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
                if (ContinueButtonLit()) Fail("The title's CONTINUE is lit with no save in any slot.");
                else Log("Title: CONTINUE is dark with no save.");
                foreach (var step in ProbeClocks()) yield return step;

                if (BalanceSuite)
                {
                    SaveSystem.Instance.NewGame(HarnessSlot);
                    foreach (var step in RunDifficultyCurve()) yield return step;
                    yield break;
                }

                // Run the pure meta-layer checks (save round-trip, migration
                // clamping, perk symmetry, cost curves, the quality budget) inside
                // this same batchmode session. They are a MonoBehaviour because
                // they need JsonUtility and persistentDataPath, and without this
                // nothing ever executed them - a suite that only runs when someone
                // remembers to drop it in a scene is a suite that does not run.
                // Any Check() failure logs an error, which OnUnityLog turns into a
                // failure of this run.
                foreach (var step in RunSimulationChecks()) yield return step;
                // The suites that need real physics time: the strike speed against a dropped body.
                foreach (var step in RunSuite<DebugTools.PrepStrikeSimulationTest>(10f)) yield return step;
                if (_errorCount > 0) yield break;

                // No 4x time-scale push any more. It never actually applied to the
                // fights (the HUD pushes its own 1x/2x request on top of it), and a
                // coarser step would make the harness fight differently from a player.

                for (int day = 1; day <= DaysToRun && _errorCount == 0; day++)
                {
                    Log($"--- DAY {day} ---");

                    if (day == 1)
                    {
                        GameLoopManager.Instance.StartNewGame(HarnessSlot);
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
                    if (day == 1) foreach (var step in CheckPauseAndTitleRoundTrip()) { if (_errorCount > 0) yield break; yield return step; }
                    if (day == 1) foreach (var step in CheckOutsideClicksDoNotAdvance()) { if (_errorCount > 0) yield break; yield return step; }
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
                    // Every fighter going out today comes to the Counter and orders their
                    // own flask, in roster order — through the same method the TAKE THIS
                    // JOB button reaches, once per fighter.
                    int partySize = SaveSystem.Instance.State.DeployedParty().Count;
                    // The first fighter's job goes the way a player takes it: the tag
                    // carried across the counter and dropped on the order book.
                    if (day == 1)
                    {
                        foreach (var step in WaitRealtime(1.3f)) yield return step;
                        Capture("day1_spotlight_job");
                        if (TutorialManager.Instance == null || !TutorialManager.Active)
                            Fail("Day 1 opened with no lesson on screen.");
                    }
                    foreach (var step in DragFirstJobTag(day)) yield return step;
                    for (int k = 0; k < partySize + 1 && UI.Stations.CounterStation.NextFighter != null; k++)
                    {
                        CallPrivate(morningScreen, "AcceptOrder");
                        yield return null;
                    }
                    CraftingManager cmgr = CraftingManager.Instance;
                    if (cmgr == null || cmgr.Orders.Count == 0) { Fail("AcceptOrder did not produce an order."); yield break; }
                    if (cmgr.Orders.Count != partySize)
                        Fail($"{partySize} fighter(s) going out but {cmgr.Orders.Count} order(s) taken at the Counter.");
                    if (UI.Stations.CounterStation.NextFighter != null) Fail("A fighter was left waiting at the Counter.");
                    var orders = new List<ActiveOrder>(cmgr.Orders);
                    var partyNow = SaveSystem.Instance.State.DeployedParty();
                    for (int k = 0; k < orders.Count; k++)
                    {
                        ActiveOrder o = orders[k];
                        // The one at the Counter is the fighter who carries the flask.
                        if (k < partyNow.Count && o.heroId != partyNow[k].id)
                            Fail($"Order {k + 1} is for '{o.heroName}', but {partyNow[k].displayName} was next in line.");
                        Log($"Order for {o.heroName}: '{o.potionName}', quality {o.qualityScore} ({o.GetGrade()})");
                    }

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
                    if (orders.Count > 1)
                    {
                        SwitchMorningTab(morningScreen, "Counter");
                        foreach (var step in Settle($"day{day}_morning_counter_served")) yield return step;
                    }

                    // Each road asks for more of the morning (Data.Complexity): check the
                    // level's promises before playing it.
                    RunState ms = SaveSystem.Instance != null ? SaveSystem.Instance.State : null;
                    int level = Data.Complexity.Level(ms);
                    int wantSteps = new[] { 2, 3, 3, 4, 5 }[level];
                    foreach (ActiveOrder o in orders)
                        if (o.Mixture != null && o.Mixture.Recipe.StepCount != wantSteps)
                            Fail($"Day {day} (level {level}) brews {o.Mixture.Recipe.Name} with {o.Mixture.Recipe.StepCount} leaves, not {wantSteps}.");
                    if (MorningScreen.ReadyToSend)
                        Fail("The party could be sent before a single flask was brewed.");
                    Log($"Day {day}: level {level}, {wantSteps}-leaf recipes ({orders[0].Mixture?.Recipe.Name}), malting {(Data.Complexity.MaltingOn(level) ? "on" : "off")}.");

                    if (Data.Complexity.MaltingOn(level))
                    {
                        // Malting comes first, on two benches: nothing reaches Prep until its
                        // grain has been steeped and sprouted at Steep, then dried at Kiln.
                        SwitchMorningTab(morningScreen, "Steep");
                        foreach (var step in Settle($"day{day}_morning_steep")) yield return step;
                        if (ms != null && ms.HasUpgrade(Data.UpgradeCatalog.SteepingVat) && !ms.seenUpgrades.Contains(Data.UpgradeCatalog.SteepingVat))
                            Fail("The Steep bench never announced its new Steeping Vat.");
                        foreach (var step in DriveSteep(orders, day)) { if (_errorCount > 0) break; yield return step; }
                        if (_errorCount > 0) yield break;
                        SwitchMorningTab(morningScreen, "Kiln");
                        foreach (var step in Settle($"day{day}_morning_kiln")) yield return step;
                        if (ms != null && ms.HasUpgrade(Data.UpgradeCatalog.DraughtKiln) && !ms.seenUpgrades.Contains(Data.UpgradeCatalog.DraughtKiln))
                            Fail("The Kiln bench never announced its new Draught Kiln.");
                        foreach (var step in DriveKiln(orders, day)) { if (_errorCount > 0) break; yield return step; }
                        if (_errorCount > 0) yield break;
                        CheckLedgerStations(orders);
                    }
                    else
                    {
                        // The first road has no Steep or Kiln bench: the grain comes malted.
                        foreach (ActiveOrder o in orders)
                            if (o.stage != BrewStage.Prep)
                                Fail($"On level {level} {o.heroName}'s order starts at {o.stage}, not Prep.");
                    }

                    // Then Prep: the Cauldron will not brew until the recipe's leaves are
                    // crushed in and ground. Every bench is played for real through the
                    // scripted pointer, one order after another; each bench takes up the
                    // next order waiting at it by itself.
                    SwitchMorningTab(morningScreen, "Prep");
                    foreach (var step in Settle($"day{day}_morning_prep")) yield return step;
                    for (int k = 0; k < orders.Count && _errorCount == 0; k++)
                    {
                        _orderTag = k == 0 ? "" : $"_o{k + 1}";
                        foreach (var step in DrivePrep(orders[k], day)) { if (_errorCount > 0) break; yield return step; }
                        if (k == 0 && orders.Count > 1 && Crafting.PhysicsCauldronManager.AutoStir && _errorCount == 0)
                            foreach (var step in PeekAtClockwork(morningScreen, day)) yield return step;
                    }
                    if (_errorCount > 0) yield break;

                    SwitchMorningTab(morningScreen, "Cauldron");
                    _orderTag = "";
                    _clockworkBrews = 0;
                    foreach (var step in Settle($"day{day}_morning_cauldron")) yield return step;
                    if (ms != null && ms.HasUpgrade(Data.UpgradeCatalog.ClockworkStirrer) && !ms.seenUpgrades.Contains(Data.UpgradeCatalog.ClockworkStirrer))
                        Fail("The Cauldron never announced its new Clockwork Stirrer.");
                    for (int k = 0; k < orders.Count && _errorCount == 0; k++)
                    {
                        _orderTag = k == 0 ? "" : $"_o{k + 1}";
                        foreach (var step in DriveCauldron(orders[k], day)) { if (_errorCount > 0) break; yield return step; }
                        if (_errorCount > 0) break;
                        foreach (var step in Settle($"day{day}{_orderTag}_morning_cauldron_done")) yield return step;
                    }
                    if (_errorCount > 0) yield break;
                    if (Crafting.PhysicsCauldronManager.AutoStir && orders.Count > 1 && _clockworkBrews == 0)
                        Fail("The clockwork stirrer is installed but brewed nothing by itself.");
                    foreach (var f in Frames(80)) yield return f;
                    var coldView = UnityEngine.Object.FindFirstObjectByType<Crafting.CauldronView>();
                    if (coldView != null && coldView.FireLit) Fail($"Day {day}: every recipe is stirred and nothing waits, but the fire is still lit.");
                    else Capture($"day{day}_morning_cauldron_cold");
                    foreach (var step in StirTheColdPot(coldView, day)) yield return step;

                    SwitchMorningTab(morningScreen, "Bottling");
                    _orderTag = "";
                    foreach (var step in Settle($"day{day}_morning_bottling")) yield return step;
                    for (int k = 0; k < orders.Count && _errorCount == 0; k++)
                    {
                        _orderTag = k == 0 ? "" : $"_o{k + 1}";
                        foreach (var step in DriveBottling(orders[k], day)) { if (_errorCount > 0) break; yield return step; }
                        if (_errorCount > 0) break;
                        foreach (var step in Settle($"day{day}{_orderTag}_morning_bottling_done")) yield return step;
                    }
                    // (the slide is photographed inside DriveBottling, while the flask is on its way)
                    _orderTag = "";
                    if (_errorCount > 0) yield break;

                    // Then Delivery: every sealed flask handed to the fighter who ordered it.
                    SwitchMorningTab(morningScreen, "Delivery");
                    foreach (var step in DriveDelivery(orders, day)) { if (_errorCount > 0) break; yield return step; }
                    if (_errorCount > 0) yield break;

                    foreach (ActiveOrder o in orders)
                        Log($"Morning done for {o.heroName}: {o.qualityScore} ({o.GetGrade()}), {o.stage}; flawless would be {QualityBudget.FlawlessMorning()}.");
                    if (!cmgr.AllDone) Fail("Not every order was sealed, labelled and handed over by the end of the morning.");

                    if (!MorningScreen.ReadyToSend)
                        Fail("Every flask is finished but the party still cannot be sent.");
                    // The send lesson: the spotlight on the SEND button, before it is pressed.
                    foreach (var step in WaitRealtime(1.3f)) yield return step;
                    Capture($"day{day}_morning_send_spotlight");
                    Log($"Lessons seen by day {day}: {string.Join(", ", SaveSystem.Instance.State.seenLessons)}");
                    CheckDistinctRecipes(day, cmgr);
                    GameLoopManager.Instance.BeginHandoff();
                    Log($"Day {day}: handed over with {SaveSystem.Instance.State.morningLeft01:P0} of the morning left " +
                        $"(quick hands +{Economy.SpeedBonus(SaveSystem.Instance.State.morningLeft01):P0}).");
                    foreach (var step in WaitForPhase(GamePhase.Handoff, 5f)) { if (_errorCount > 0) yield break; yield return step; }
                    if (_errorCount > 0) yield break;
                    foreach (var step in Settle($"day{day}_handoff")) yield return step;

                    GameLoopManager.Instance.BeginAfternoon();
                    foreach (var step in WaitForPhase(GamePhase.Afternoon, 5f)) { if (_errorCount > 0) yield break; yield return step; }
                    if (_errorCount > 0) yield break;
                    // WaitForPhase has not yielded (the phase is set synchronously), so
                    // this is still the frame BeginAfternoon ran on — the one t00s shows.
                    CheckFirstAfternoonFrame();
                    CheckCarriedFlasks();
                    CheckLevelStatsInFight(day);
                    if (_errorCount > 0) yield break;
                    if (day == 1) CheckPauseAndResume();

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
                    if (day == 1) Capture("day1_afternoon_noboss");
                    CheckFighterDamage(day);

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
                    // The first clear unlocks a diary page: its toast, while it is up.
                    if (day == 1) foreach (var step in Settle("day1_evening_toast")) yield return step;
                    // A fight that earned part of the story plays it over the Evening first.
                    bool wwSeenBefore = Story.StoryDirector.Has(SaveSystem.Instance.State, Story.StoryDirector.WoodwoseSeen);
                    foreach (var step in PlayOutCutscenes($"day{day} evening", expect: false)) yield return step;
                    ExpeditionReport today = GameLoopManager.Instance.LatestReport;
                    foreach (var step in CheckSpeedBonus(day, today)) yield return step;
                    var stw = SaveSystem.Instance.State;
                    int roadToday = today != null ? today.roadIndex : -1;
                    if (roadToday == 0)
                    {
                        // The Woods are the first job: waves only, no guardian, on any day.
                        if (today.bossDefeated || !string.IsNullOrEmpty(today.bossId))
                            Fail($"Day {day}: a guardian was fought in the Woods (id '{today.bossId}', defeated {today.bossDefeated}); the Woods have none.");
                        else if (!wwSeenBefore && (Story.StoryDirector.Has(stw, Story.StoryDirector.WoodwoseSeen) || stw.HasDiary("diary_woodwose")))
                            Fail($"Day {day}: the Woodwose's scene or diary page appeared after the Woods.");
                        else Log($"Day {day}: the Woods, no guardian, no Woodwose scene, no Woodwose page.");
                    }
                    else if (!wwSeenBefore && today != null && today.won && today.bossDefeated && today.bossId == "woodwose")
                    {
                        // The first night the Woodwose falls: on the second road, as first met; its scene plays and its page opens.
                        if (roadToday != 1 || today.bossTier != 0)
                            Fail($"Day {day}: the Woodwose first fell on road {roadToday} at tier {today.bossTier}; it is first met on road 1 at tier 0.");
                        else if (!Story.StoryDirector.Has(stw, Story.StoryDirector.WoodwoseSeen) || !stw.HasDiary("diary_woodwose"))
                            Fail($"Day {day}: the Woodwose fell for the first time but its scene or diary page did not appear.");
                        else Log($"Day {day}: the Woodwose fell for the first time ({BiomeLibrary.Name(roadToday)}, tier {today.bossTier}); its scene played and its page is unlocked.");
                    }
                    if (today != null && today.won && today.bossDefeated && SaveSystem.Instance.State.TargetBiomeIndex >= 0)
                    {
                        var st0 = SaveSystem.Instance.State;
                        if (Story.StoryDirector.Has(st0, Story.StoryDirector.WoodwoseSeen) && st0.HasDiary("diary_woodwose"))
                            Log("The Woodwose fell: its scene played, and its diary page is unlocked.");
                        else if (!Story.StoryDirector.Has(st0, Story.StoryDirector.WoodwoseSeen))
                            Fail("A guardian fell but its scene was not recorded as watched.");
                    }
                    foreach (var step in Settle($"day{day}_evening_report")) yield return step;
                    if (!_brokenPageShot && stw.HasDiary("diary_woodwose_broken"))
                    {
                        // The broken Woodwose's page, to set beside its fight and its scene.
                        _brokenPageShot = true;
                        DiaryScreen.OpenEntryId = "diary_woodwose_broken";
                        DiaryScreen.FromOpeningCinematic = false;
                        UIManager.Instance.Show(ScreenId.Diary);
                        foreach (var step in WaitRealtime(1.5f)) yield return step;
                        Capture($"day{day}_diary_woodwose_broken");
                        DiaryScreen.OpenEntryId = null;
                        UIManager.Instance.Show(ScreenId.Evening);
                        yield return null;
                    }
                    if (day == DaysToRun) foreach (var step in CheckEveningMenu()) yield return step;
                    if (day == 1) foreach (var step in WalkEveningBriefing()) yield return step;

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
                        CheckLevelUpNote(eveningScreen as EveningScreen, st, st.roster[0], lvlBefore);
                        CallPrivate(eveningScreen, "ShowRoster");
                        foreach (var step in Settle($"day{day}_evening_levelup")) yield return step;

                        Log($"Roster: {st.roster.Count} heroes, lead is {st.roster[0].displayName} " +
                            $"Lv{st.roster[0].level} ({st.roster[0].affinity}), {st.gold} g left.");
                        foreach (var step in Settle($"day{day}_evening_roster_bought")) yield return step;

                        // Build a full party for day 3, which replays biome 0 so
                        // it is directly comparable with day 1's solo run on the
                        // same road. This is the regression test for "does a
                        // three-hero party trivialise the early game".
                        // Hiring alone sends a fighter out: no party slot to buy first.
                        st.gold = 2000;

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

                    if (st != null && day == 3)
                    {
                        // Every bench machine, so day 4 plays the rebuilt benches (days 1-3
                        // played them all by hand), and the road kit the Swamp asks for.
                        // A second tier cannot be bought before its first.
                        st.gold = 4000;
                        CallPrivate(eveningScreen, "ShowUpgrades");
                        CallPrivate(eveningScreen, "BuyUpgrade", Data.UpgradeCatalog.TemperedGlass, 160);
                        if (st.HasUpgrade(Data.UpgradeCatalog.TemperedGlass))
                            Fail("Tempered Glass was sold before Heavier Flasks - the tier requirement is not enforced.");
                        // First tiers, then second: the Swamp asks for the whole road kit.
                        for (int pass = 0; pass < 2; pass++)
                            foreach (var up in Data.UpgradeCatalog.All)
                            {
                                if ((up.Requires.Length > 0) != (pass == 1) || st.HasUpgrade(up.Id)) continue;
                                CallPrivate(eveningScreen, "BuyUpgrade", up.Id, up.Cost);
                                if (!st.HasUpgrade(up.Id)) Fail($"Buying {up.DisplayName} did not take.");
                            }
                        Log($"Upgrades bought: {string.Join(", ", st.ownedUpgrades)}; {st.gold} g left.");
                        foreach (var f in Frames(12)) yield return f;
                        Capture($"day{day}_evening_upgrades_installing");
                        // The reveal runs on real time (the Evening is paused): let it land.
                        for (float t0 = Time.unscaledTime; Time.unscaledTime - t0 < 1.8f;) yield return null;
                        foreach (var step in Settle($"day{day}_evening_upgrades_bought")) yield return step;
                    }

                    if (day == 1) foreach (var step in CheckMapBack(eveningScreen)) { if (_errorCount > 0) yield break; yield return step; }
                    GameLoopManager.Instance.BeginBiomeMap();
                    foreach (var step in WaitForPhase(GamePhase.BiomeMap, 5f)) { if (_errorCount > 0) yield break; yield return step; }
                    if (_errorCount > 0) yield break;
                    foreach (var step in Settle($"day{day}_biome_map")) yield return step;

                    // Always on to the next road: each day brings the next level of the morning.
                    if (day < DaysToRun) GameLoopManager.Instance.Sleep(-1);
                    if (day == 1 && _dayBeforeSleep >= 0)
                    {
                        // SLEEP after a BACK passes one night, like any other.
                        if (SaveSystem.Instance.State.day != _dayBeforeSleep)
                            Fail($"SLEEP after a map BACK moved the day {_dayBeforeSleep} -> {SaveSystem.Instance.State.day}; Sleep does not advance it (BeginEvening did).");
                        _dayBeforeSleep = -1;
                    }
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
                foreach (var step in CaptureGuardianPortraits()) yield return step;
                if (FullSuite)
                {
                    foreach (var step in RunBiomeSweep()) yield return step;
                    foreach (var step in RunGradeSweep()) yield return step;
                    foreach (var step in RunDifficultyCurve()) yield return step;
                }

                foreach (var step in CheckSlotDelete()) yield return step;

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

            /// <summary>Suffix for screenshots of the second and later orders of a morning ("_o2").</summary>
            private string _orderTag = "";
            private bool _lastScenarioWon;
            /// <summary>The guardian the last scenario met (its story id), or "" when none came out.</summary>
            private string _lastScenarioBossId = "";
            private int _clockworkBrews;

            /// <summary>
            /// The Steep bench, every order in turn: pour to the line, steep, skim, turn the
            /// bed (with the turn lesson's focus checked), then scoop the green malt for the
            /// kiln. Every frame it checks no order is at two benches at once.
            /// </summary>
            private IEnumerable DriveSteep(List<ActiveOrder> orders, int day)
            {
                var bench = Crafting.SteepBench.Instance;
                var kiln = Crafting.KilnBench.Instance;
                if (bench == null || kiln == null) { Fail("No Steep or Kiln bench."); yield break; }

                float t = 0f, settle = 0f, nextSkim = 0f;
                bool lidDragged = false, maltDragged = false;
                bool pourShot = false, soakShot = false, turnShot = false, buoyancyChecked = false, vatBarShot = false;
                bool turnLessonChecked = false, turnLessonShot = false;
                float turnLessonSeenAt = 0f;
                TimeControl.Handle turnLessonPause = default;
                RunState runAtStart = SaveSystem.Instance != null ? SaveSystem.Instance.State : null;
                bool turnLessonDue = Data.Complexity.TurningOn(Data.Complexity.Level()) && !bench.Vat
                                     && runAtStart != null && !runAtStart.seenLessons.Contains("turn");
                float vatWait = 0f;
                float limit = 50f * orders.Count;
                while (t < limit && _errorCount == 0)
                {
                    bool done = true;
                    foreach (ActiveOrder o in orders) if (o.stage == BrewStage.Steep) done = false;
                    if (done) break;
                    CheckOneBenchEach(bench, kiln);

                    ActiveOrder tub = bench.TubOrder;
                    if (tub != null)
                    {
                        switch (tub.steepStep)
                        {
                            case SteepStep.Filling:
                                foreach (var f in ShowLesson("sack", wait: true)) yield return f;
                                foreach (var f in ShowLesson("lid", wait: false)) yield return f;
                                bool enough = bench.GoodInJar >= QualityBudget.GrainTarget;
                                if (!enough && TutorialManager.CurrentLessonId == "lid")
                                    Fail($"Tutorial: the lid lesson came up with {bench.GoodInJar} of {QualityBudget.GrainTarget} grains in the jar.");
                                if (bench.Vat)
                                {
                                    // The player only pours: the vat seals itself at the line.
                                    bench.PourHeld = true;
                                    // Its bar runs to the grains the machine needs, with a small green box at the end.
                                    if (!vatBarShot && bench.GoodInJar >= QualityBudget.GrainTarget / 2)
                                    {
                                        vatBarShot = true;
                                        var want = Crafting.SteepBench.FillGauge(true);
                                        Vector2 band = bench.JarGaugeBand;
                                        if (Mathf.Abs(band.x - want.bandMin01) > 0.001f || Mathf.Abs(band.y - want.bandMax01) > 0.001f)
                                            Fail($"Steep: the vat's gauge box is {band.x:0.00} to {band.y:0.00}, not {want.bandMin01:0.00} to {want.bandMax01:0.00}.");
                                        else Log($"Steep: the vat's bar ends at the {QualityBudget.GrainTarget} grains it needs, its green box at the end ({band.x:0.00} to {band.y:0.00}).");
                                        Capture($"day{day}_morning_steep_fill");
                                    }
                                    if (enough && !pourShot) { pourShot = true; Capture($"day{day}_morning_steep_pour"); }
                                    vatWait = enough ? vatWait + Time.deltaTime : 0f;
                                    if (vatWait > 3f) Fail("Steep: the grain is at the line but the vat never sealed - no action possible, the player is stuck.");
                                    break;
                                }
                                bench.PourHeld = !enough;
                                if (!enough) { settle = 0f; break; }
                                settle += Time.deltaTime;
                                if (settle < 1.0f) break;   // what is still in the air lands, and the husks rise
                                if (!buoyancyChecked) { buoyancyChecked = true; CheckBuoyancy(bench); }
                                if (!pourShot) { pourShot = true; Capture($"day{day}_morning_steep_pour"); }
                                foreach (var f in ShowLesson("lid", wait: true)) yield return f;
                                if (day == 2 && !lidDragged)
                                {
                                    // By hand: carry the lid from beside the jar onto its mouth.
                                    lidDragged = true;
                                    foreach (var f in Drag(bench.LidWorld + new Vector2(0f, 0.15f), bench.JarMouthWorld, $"day{day}_morning_steep_lid_carried"))
                                        yield return f;
                                    if (tub.steepStep != SteepStep.Soaking)
                                        Fail($"The lid was carried onto the jar but it did not steep (step {tub.steepStep}).");
                                    else Log("Steep: the lid carried onto the jar steeped it.");
                                }
                                else bench.Steep();
                                settle = 0f;
                                break;
                            case SteepStep.Soaking:
                                if (!soakShot && bench.SoakProgress01 > 0.3f) { soakShot = true; Capture($"day{day}_morning_steep_soak"); }
                                if (bench.Vat)
                                {
                                    if (bench.SoakProgress01 > 0.97f && bench.HusksInJar > 0)
                                        Fail($"Steep: the soak is ending with {bench.HusksInJar} husks the vat never skimmed.");
                                    break;
                                }
                                if (bench.HusksInJar > 0 && Time.time >= nextSkim) { bench.SkimNext(); nextSkim = Time.time + 0.25f; }
                                break;
                            case SteepStep.Germinating:
                                if (bench.Hint.Contains("another bench"))
                                    Fail($"Steep: the sprouting hint sends the player away: \"{bench.Hint}\"");
                                if (bench.GerminateProgress01 > 0.1f && bench.GerminateProgress01 < Crafting.SteepBench.TurnFrom)
                                {
                                    // The green box is the window to turn the bed in, while turning is the player's job.
                                    bool matters = Data.Complexity.TurningOn(Data.Complexity.Level()) && !bench.Vat && bench.TurnPending;
                                    Vector2 box = bench.JarGaugeBand;
                                    bool shown = box.y > 0.001f;
                                    if (matters && (Mathf.Abs(box.x - Crafting.SteepBench.TurnFrom) > 0.001f || Mathf.Abs(box.y - Crafting.SteepBench.TurnBy) > 0.001f))
                                        Fail($"Steep: sprouting shows its turn window as {box.x:0.00} to {box.y:0.00}, not {Crafting.SteepBench.TurnFrom:0.00} to {Crafting.SteepBench.TurnBy:0.00}.");
                                    if (!matters && shown)
                                        Fail($"Steep: a turn window is shown ({box.x:0.00} to {box.y:0.00}) where the bed turns itself.");
                                }
                                // The turn lesson: the jar alone lit, and the bench held until it is learnt.
                                // The spotlight fades and closes in on unscaled time (wall clock under
                                // batchmode), the bed sprouts on game time: game time is paused until
                                // it has been photographed, so the bed cannot mat while we wait.
                                if (turnLessonChecked && !turnLessonShot && Time.unscaledTime - turnLessonSeenAt > 1.4f)   // 0.25 s fade, then the 0.9 s close-in
                                {
                                    turnLessonShot = true;
                                    if (TutorialManager.CurrentLessonId == "turn") Capture($"day{day}_morning_steep_turn_lesson");
                                    else Fail("The turn lesson closed before it could be photographed.");
                                    turnLessonPause.Dispose();
                                }
                                if (!turnLessonChecked && TutorialManager.CurrentLessonId == "turn")
                                {
                                    turnLessonChecked = true;
                                    turnLessonSeenAt = Time.unscaledTime;
                                    if (TimeControl.Instance != null) turnLessonPause = TimeControl.Instance.PushPause();
                                    var screen = UIManager.Instance != null ? UIManager.Instance.ScreenOf(ScreenId.Morning) as MorningScreen : null;
                                    int before = screen != null ? screen.ActiveTabIndex : -1;
                                    SwitchMorningTab(screen, "Prep");
                                    if (screen == null || screen.ActiveTabIndex != before)
                                        Fail($"The turn lesson was up, but the bench switched away (tab {before} -> {(screen != null ? screen.ActiveTabIndex : -1)}).");
                                    else Log("Steep: the turn lesson held the bench (a switch to Prep was refused).");
                                }
                                if (bench.Vat)
                                {
                                    if (!turnShot && bench.GerminateProgress01 > 0.3f) { turnShot = true; Capture($"day{day}_morning_steep_sprouting"); }
                                    if (bench.TurnDue && bench.GerminateProgress01 > 0.6f)
                                        Fail("Steep: the bed is due a turn the vat never gave it - the player is stuck with no spoon to turn it.");
                                    break;
                                }
                                if (bench.TurnDue && (!turnLessonChecked || turnLessonShot))
                                {
                                    if (!turnShot) { turnShot = true; Capture($"day{day}_morning_steep_sprouting"); }
                                    bench.Turn();
                                }
                                break;
                            case SteepStep.Green:
                                foreach (var f in ShowLesson("scoop", wait: true)) yield return f;
                                if ((day == 2 || bench.Vat) && !maltDragged)
                                {
                                    // By hand: scoop the green malt out of the jar into the sack (the vat's basket) for the kiln.
                                    maltDragged = true;
                                    ActiveOrder moving = tub;
                                    foreach (var f in Drag(bench.JarWorld, bench.KilnSackWorld, $"day{day}_morning_steep_scoop_carried"))
                                        yield return f;
                                    if (moving.stage != BrewStage.Kiln)
                                        Fail($"The green malt was scooped into the kiln's sack, but the order is at {moving.stage}, not Kiln.");
                                    else Log("Steep: the malt scooped into the sack went on to the Kiln.");
                                }
                                else bench.Scoop();
                                break;
                        }
                    }
                    t += Time.deltaTime;
                    yield return null;
                }
                bench.PourHeld = false;
                turnLessonPause.Dispose();   // never leave the clock stopped if the loop ended mid-wait
                if (turnLessonDue && !turnLessonChecked && _errorCount == 0)
                    Fail("The bed was turned on a turning road, but the turn lesson never came up at the jar.");
                foreach (ActiveOrder o in orders)
                    if (o.stage == BrewStage.Steep) Fail($"{o.heroName}'s steeping never finished (step {o.steepStep}) in {limit:0} s.");
            }

            /// <summary>
            /// The Kiln bench, every order in turn: take the waiting sack onto the tray, keep
            /// the fire in the band, let it dry. Checks no order is at two benches at once.
            /// </summary>
            private IEnumerable DriveKiln(List<ActiveOrder> orders, int day)
            {
                var bench = Crafting.KilnBench.Instance;
                var steep = Crafting.SteepBench.Instance;
                if (bench == null || steep == null) { Fail("No Kiln or Steep bench."); yield break; }
                float t = 0f, nextStoke = 0f;
                bool loadDragged = false, kilnShot = false;
                float limit = 30f * orders.Count;
                while (t < limit && _errorCount == 0)
                {
                    bool done = true;
                    foreach (ActiveOrder o in orders) if (o.stage == BrewStage.Kiln) done = false;
                    if (done) break;
                    CheckOneBenchEach(steep, bench);

                    if (bench.CanLoadKiln)
                    {
                        CheckRackOnScreen(bench);
                        foreach (var f in ShowLesson("kiln", wait: true)) yield return f;
                        if (day == 2 && !loadDragged)
                        {
                            // By hand: carry the waiting sack from the rack onto the tray.
                            loadDragged = true;
                            ActiveOrder moving = bench.NextWaiting;
                            foreach (var f in Drag(bench.NextBasketBounds.center, bench.KilnTrayWorld, $"day{day}_morning_kiln_sack_carried"))
                                yield return f;
                            if (bench.KilnOrder != moving)
                                Fail("The sack of green malt was carried onto the tray but the kiln did not take it.");
                            else Log("Kiln: the sack carried onto the tray loaded it.");
                        }
                        else bench.LoadKiln();
                    }
                    foreach (var f in ShowLesson("stoke", wait: false)) yield return f;
                    // Keep the kiln in its band: a log whenever it drops toward the bottom of it.
                    if (bench.KilnOrder != null && !bench.Draught && bench.Heat01 < 0.52f && bench.LogsBurning < 3
                        && bench.CanStoke && Time.time >= nextStoke)
                    {
                        bench.Stoke();
                        nextStoke = Time.time + 1.1f;
                    }
                    if (!kilnShot && bench.KilnOrder != null && bench.KilnProgress01 > 0.4f)
                    {
                        kilnShot = true;
                        Capture($"day{day}_morning_kiln_drying");
                    }
                    t += Time.deltaTime;
                    yield return null;
                }
                foreach (ActiveOrder o in orders)
                {
                    if (o.stage <= BrewStage.Kiln)
                        Fail($"{o.heroName}'s malt never dried (at {o.stage}, kiln step {o.kilnStep}) in {limit:0} s.");
                    else
                    {
                        var lines = new List<string>();
                        foreach (var d in o.deductions)
                            if (d.station == Crafting.SteepBench.Station || d.station == Crafting.KilnBench.Station)
                                lines.Add($"{d.station} {(d.pointsDelta >= 0 ? "+" : "")}{d.pointsDelta} {d.reason}");
                        Log($"Malting: {o.heroName}'s malt done — quality {o.MaltQuality01:P0} ({o.maltPoints} of {QualityBudget.MaltMax}), flask at {o.qualityScore}. Lines: {string.Join("; ", lines)}");
                    }
                }
            }

            /// <summary>An order is at one bench at a time, and each bench only holds an order at its own stage.</summary>
            private void CheckOneBenchEach(Crafting.SteepBench steep, Crafting.KilnBench kiln)
            {
                if (steep.TubOrder != null && steep.TubOrder.stage != BrewStage.Steep)
                    Fail($"The Steep bench holds {steep.TubOrder.heroName}'s order at {steep.TubOrder.stage}.");
                if (kiln.KilnOrder != null && kiln.KilnOrder.stage != BrewStage.Kiln)
                    Fail($"The Kiln bench holds {kiln.KilnOrder.heroName}'s order at {kiln.KilnOrder.stage}.");
                if (steep.TubOrder != null && steep.TubOrder == kiln.KilnOrder)
                    Fail($"{steep.TubOrder.heroName}'s order is at the Steep and the Kiln bench at once.");
            }

            /// <summary>Every malting line says which of the two benches it came from (the old shared label is gone).</summary>
            private void CheckLedgerStations(List<ActiveOrder> orders)
            {
                foreach (ActiveOrder o in orders)
                    foreach (var d in o.deductions)
                        if (d.station == "Malting")
                            Fail($"{o.heroName}'s flask has a line from the old shared Malting bench: \"{d.reason}\".");
            }

            /// <summary>
            /// The Delivery bench: every sealed flask handed to the fighter who ordered it,
            /// by hand on the first order of the day (and, with more than one fighter, a
            /// drop on the wrong one first, which must be refused).
            /// </summary>
            private IEnumerable DriveDelivery(List<ActiveOrder> orders, int day)
            {
                var bench = Crafting.DeliveryBench.Instance;
                if (bench == null) { Fail("No Delivery bench."); yield break; }
                foreach (var step in WaitUntil(() =>
                {
                    foreach (ActiveOrder o in orders) if (o.stage != BrewStage.Delivery) return false;
                    return true;
                }, 8f, "every sealed flask reaching the Delivery bench")) yield return step;
                if (_errorCount > 0) yield break;
                foreach (var step in Settle($"day{day}_morning_delivery")) yield return step;

                for (int k = 0; k < orders.Count && _errorCount == 0; k++)
                {
                    ActiveOrder order = orders[k];
                    if (k == 0)
                    {
                        if (orders.Count > 1)
                        {
                            // The wrong fighter first: the flask must go back to the tray.
                            foreach (var f in Drag(bench.FlaskWorld(order), bench.FighterWorld(orders[1].heroId))) yield return f;
                            foreach (var f in Frames(5)) yield return f;
                            if (!string.IsNullOrEmpty(order.deliveredTo) || bench.HandingOver)
                                Fail($"{order.heroName}'s flask was handed to {orders[1].heroName}.");
                            else Log($"Delivery: {order.heroName}'s flask dropped on {orders[1].heroName} went back to the tray.");
                        }
                        // By hand: carry the flask to its fighter, and photograph it on its way into the hand.
                        foreach (var f in Drag(bench.FlaskWorld(order), bench.FighterWorld(order.heroId))) yield return f;
                        foreach (var step in WaitRealtime(0.25f)) yield return step;
                        Capture($"day{day}_morning_delivery_handover");
                    }
                    else if (!bench.HandOver(order)) Fail($"{order.heroName}'s flask could not be handed over.");
                    foreach (var step in WaitUntil(() => order.Finished, 5f, $"{order.heroName}'s flask being handed over")) yield return step;
                    if (order.deliveredTo != order.heroId)
                        Fail($"{order.heroName}'s flask went to '{order.deliveredTo}', not to them.");
                }
                if (_errorCount > 0) yield break;
                Log($"Delivery: {orders.Count} flask(s) handed over, each to the fighter who ordered it.");
                foreach (var step in Settle($"day{day}_morning_delivery_done")) yield return step;
            }

            /// <summary>Each fighter goes into the arena with the flask that was handed to them.</summary>
            /// <summary>The level-up line names the same before and after numbers the fight is built from.</summary>
            private void CheckLevelUpNote(EveningScreen evening, RunState st, HeroRecord hero, int from)
            {
                HeroStats a = HeroStats.For(from, st), b = HeroStats.For(from + 1, st);
                string note = evening != null ? evening.LevelUpNote : null;
                string want = $"HP {a.Hp} → {b.Hp}, base damage {a.Damage} → {b.Damage}, flasks +{a.FlaskBonus} → +{b.FlaskBonus}";
                if (note == null || !note.Contains(want)) Fail($"Level-up note '{note}' does not show '{want}'.");
                else Log($"Level-up shown: {note}");
            }

            /// <summary>Every fighter goes out with exactly the HP, damage scale and flasks the roster shows for their level.</summary>
            private void CheckLevelStatsInFight(int day)
            {
                var world = GameLoopManager.Instance.CurrentExpedition;
                var loadouts = GameLoopManager.Instance.PendingLoadouts;
                var carried = GameLoopManager.Instance.PendingCarried;
                RunState st = SaveSystem.Instance.State;
                if (world == null || loadouts == null) return;
                for (int i = 0; i < world.Party.Count && i < world.PartyRecords.Count; i++)
                {
                    HeroRecord h = world.PartyRecords[i];
                    CombatantBody body = world.Party[i];
                    if (h == null || body == null) continue;
                    HeroStats shown = HeroStats.For(h.level, st);
                    var ai = body.GetComponent<UtilityAI_CombatController>();
                    ActiveOrder order = carried != null && i < carried.Count ? carried[i] : null;
                    PotionGrade grade = order != null ? order.GetGrade() : PotionGrade.Poor;
                    int flasks = 0;
                    foreach (var slot in loadouts[i].Slots) flasks += slot.count;
                    int wantFlasks = LoadoutBuilder.AmmoFor(grade) + shown.FlaskBonus;
                    if (body.MaxHP != shown.Hp || ai == null || !Mathf.Approximately(ai.LevelDamageScale, shown.DamageScale) || flasks != wantFlasks)
                        Fail($"Day {day}: {h.displayName} Lv{h.level} fights with {body.MaxHP} HP, x{(ai != null ? ai.LevelDamageScale : 0f):0.00} damage, " +
                             $"{flasks} flasks; the roster shows {shown.Hp} HP, x{shown.DamageScale:0.00}, {wantFlasks} flasks ({grade}).");
                    else Log($"Day {day}: {h.displayName} Lv{h.level} fights with what the roster shows: {shown.Hp} HP, x{shown.DamageScale:0.00} damage, {flasks} flasks.");
                }
            }

            /// <summary>No two of the day's orders share a recipe or a potion name.</summary>
            private void CheckDistinctRecipes(int day, CraftingManager cm)
            {
                if (cm == null || cm.Orders.Count < 2) return;
                var names = new HashSet<string>();
                var steps = new HashSet<string>();
                var list = new List<string>();
                foreach (ActiveOrder o in cm.Orders)
                {
                    PotionRecipe r = o.Mixture?.Recipe;
                    if (r == null) continue;
                    names.Add(r.Name);
                    steps.Add(r.Result + ":" + string.Join(",", r.Steps));
                    list.Add($"{o.heroName}: {r.Name} ({r.Shorthand})");
                }
                if (names.Count != list.Count || steps.Count != list.Count)
                    Fail($"Day {day}: two fighters share a recipe - {string.Join(" | ", list)}");
                else Log($"Day {day}: {list.Count} fighters, {list.Count} different recipes - {string.Join(" | ", list)}");
            }

            /// <summary>The quick-hands bonus is what the formula gives for the morning left, it is in the gold, and the report shows it.</summary>
            private IEnumerable CheckSpeedBonus(int day, ExpeditionReport r)
            {
                if (r == null) yield break;
                int want = r.won ? Economy.SpeedBonusGold(r.goldPaidByGrade, r.speedLeft01) : 0;
                if (r.speedBonus != want)
                    Fail($"Day {day}: quick hands paid {r.speedBonus} g, the formula gives {want} g ({r.speedLeft01:P0} left of the morning, purse {r.goldPaidByGrade} g).");
                else if (r.TotalGold != r.goldFromLoot + r.goldPaidByGrade + r.speedBonus)
                    Fail($"Day {day}: the day's gold {r.TotalGold} g leaves out the quick-hands bonus.");
                else Log($"Day {day}: quick hands +{r.speedBonus} g ({r.speedLeft01:P0} of the morning left, purse {r.goldPaidByGrade} g).");
                if (r.speedBonus > 0 && !_speedShot)
                {
                    _speedShot = true;
                    foreach (var step in Settle($"day{day}_evening_speed_bonus")) yield return step;
                }
            }

            /// <summary>
            /// After the last brew the pot is cold. Stirring it on (the spoon, both ways, and a hard
            /// spin of what floats in it) must not light it: only the next mash does.
            /// </summary>
            private IEnumerable StirTheColdPot(Crafting.CauldronView view, int day)
            {
                var pot = UnityEngine.Object.FindFirstObjectByType<Crafting.PhysicsCauldronManager>();
                if (view == null || pot == null || pot.Liquid == null) yield break;
                PhysicsKit.Pointer.Scripted = _pointer;
                float R = pot.Liquid.Radius, angle = 0f, relitAt = -1f, gripped = 0f;
                _pointer.Press();
                for (float t = 0f; t < 2f; t += Time.deltaTime)
                {
                    float dir = t < 1f ? 1f : -1f;
                    angle += dir * pot.FullStirDegPerSec * 0.6f * Time.deltaTime * Mathf.Deg2Rad;
                    _pointer.World = pot.ToWorld(new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * R * 0.6f);
                    foreach (var f in pot.Liquid.Floaters)
                        if (f.Body != null) f.Body.AddTorque(dir * 4f * f.Body.inertia * 10f, ForceMode2D.Force);
                    if ((view.FireLit || view.LooksBrewing) && relitAt < 0f) relitAt = t;
                    if (pot.MouseOverCauldron) gripped += Time.deltaTime;
                    yield return null;
                }
                _pointer.Release();
                if (relitAt >= 0f) Fail($"Day {day}: the brew is done and nothing waits, but stirring the pot lit the fire again (after {relitAt:0.0} s).");
                else if (gripped > 0f) Fail($"Day {day}: the spoon gripped an empty pot for {gripped:0.0} s; with nothing to brew it must not stir anything.");
                else Log($"Day {day}: the pot stayed cold through 2 s of stirring after the last brew{(Crafting.PhysicsCauldronManager.AutoStir ? " (clockwork stirrer owned)" : "")}; " +
                         $"the spoon gripped for {gripped:0.0} s (an empty pot has nothing to stir), the floaters were spun by hand.");
                Capture($"day{day}_morning_cauldron_cold_stirred");
            }

            private bool _speedShot;
            private bool _brokenPageShot;

            private void CheckCarriedFlasks()
            {
                var loop = GameLoopManager.Instance;
                var party = loop.PendingParty;
                var carried = loop.PendingCarried;
                if (party == null || carried == null || carried.Count != party.Count)
                {
                    Fail($"The afternoon's flasks ({carried?.Count}) do not line up with the party ({party?.Count}).");
                    return;
                }
                for (int i = 0; i < party.Count; i++)
                    if (carried[i] == null || carried[i].deliveredTo != party[i].id)
                        Fail($"{party[i].displayName} went into the arena without the flask handed to them ({(carried[i] == null ? "none" : "handed to '" + carried[i].deliveredTo + "'")}).");
                if (_errorCount == 0) Log($"Afternoon: every fighter carries the flask handed to them ({party.Count}).");
            }


            /// <summary>
            /// A hand on the bench: press at <paramref name="from"/>, carry to
            /// <paramref name="to"/> over half a second, capture mid-way, let go.
            /// </summary>
            private IEnumerable Drag(Vector2 from, Vector2 to, string capture = null)
            {
                PhysicsKit.Pointer.Scripted = _pointer;
                _pointer.World = from;
                foreach (var f in Frames(2)) yield return f;
                _pointer.Press();
                foreach (var f in Frames(2)) yield return f;
                for (int i = 0; i <= 30; i++) { _pointer.World = Vector2.Lerp(from, to, i / 30f); yield return null; }
                if (!string.IsNullOrEmpty(capture)) Capture(capture);
                _pointer.Release();
                foreach (var f in Frames(3)) yield return f;
                PhysicsKit.Pointer.Scripted = null;
            }

            /// <summary>The steep's water must sort the grain: husks float, sound barley sinks.</summary>
            private void CheckBuoyancy(Crafting.SteepBench bench)
            {
                float huskY = 0f, goodY = 0f; int husks = 0, good = 0;
                foreach (var g in bench.TubGrains)
                {
                    if (g.Body == null || g.Skimmed) continue;
                    if (g.Husk) { huskY += g.Body.position.y; husks++; }
                    else { goodY += g.Body.position.y; good++; }
                }
                if (husks == 0 || good == 0) { Log("Steep: no husks in this pour to check the water with."); return; }
                huskY /= husks; goodY /= good;
                if (huskY < goodY + 0.3f)
                    Fail($"The steep did not sort the grain: husks sit at {huskY:0.00}, sound grain at {goodY:0.00} (husks must float above it).");
                else
                    Log($"Steep: the water sorted the grain — {husks} husks floating at {huskY:0.00}, {good} grains sunk to {goodY:0.00}.");
            }

            /// <summary>
            /// The recipe note's leaf marks: with the pointer on one, a tooltip names its
            /// kind ("Poison") at once; off it, the tooltip is gone.
            /// </summary>
            private IEnumerable CheckLeafTooltip(BrewMixture mix)
            {
                var morning = UIManager.Instance != null ? UIManager.Instance.ScreenOf(ScreenId.Morning) as MorningScreen : null;
                var prep = morning != null ? morning.Prep : null;
                Camera cam = Camera.main;
                foreach (var f in Frames(3)) yield return f;   // the note builds its marks on Refresh
                RectTransform icon = prep != null ? prep.StepIcon(0) : null;
                if (icon == null || cam == null) { Fail("Leaf tooltip: no leaf mark on the recipe note (or no camera)."); yield break; }
                Vector2 screen = RectTransformUtility.WorldToScreenPoint(null, icon.TransformPoint(icon.rect.center));
                Vector3 sp = new Vector3(screen.x, screen.y, Mathf.Abs(cam.transform.position.z));
                Vector2 saved = _pointer.World;
                _pointer.World = cam.ScreenToWorldPoint(sp);
                foreach (var f in Frames(2)) yield return f;
                string want = mix.Recipe.Steps[0].ToString();
                if (prep.TooltipText != want) Fail($"Leaf tooltip: on the first leaf mark it says '{prep.TooltipText ?? "(hidden)"}', not '{want}'.");
                Capture("day1_prep_tooltip");
                _pointer.World = saved;
                foreach (var f in Frames(2)) yield return f;
                if (prep.TooltipText != null) Fail($"Leaf tooltip: still up ('{prep.TooltipText}') after the pointer left the mark.");
                else Log($"Leaf tooltip: '{want}' over the first leaf mark, gone off it.");
            }

            private IEnumerable DrivePrep(ActiveOrder order, int day)
            {
                var bench = Crafting.PrepBench.Instance;
                if (bench == null || order == null) { Fail("Prep bench or order missing."); yield break; }
                // The bench takes up the next order waiting at Prep by itself.
                foreach (var step in WaitUntil(() => bench.Working == order, 6f, $"the Prep bench taking up {order.heroName}'s order"))
                    yield return step;
                if (_errorCount > 0) yield break;
                var mix = order.Mixture;
                if (mix == null) { Fail("Order has no mixture."); yield break; }
                PhysicsKit.Pointer.Scripted = _pointer;
                if (day == 1 && _orderTag == "") foreach (var step in CheckLeafTooltip(mix)) yield return step;

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
                foreach (var step in Settle($"day{day}{_orderTag}_morning_prep_mortar")) yield return step;
                if (Crafting.PrepBench.Crusher)
                {
                    // The auto crusher strikes by itself: the player has nothing to press.
                    PhysicsKit.Pointer.Scripted = null;
                    bool midShot = false;
                    float waited = 0f;
                    while (!mix.Ground && waited < 20f && _errorCount == 0)
                    {
                        if (!midShot && bench.StrikesLeft < QualityBudget.GrindStrikes) { midShot = true; Capture($"day{day}{_orderTag}_morning_prep_crusher"); }
                        waited += Time.deltaTime;
                        yield return null;
                    }
                    if (!mix.Ground)
                        Fail($"Prep: the crusher struck {QualityBudget.GrindStrikes - bench.StrikesLeft} of {QualityBudget.GrindStrikes} in 20 s - no action possible, the player is stuck.");
                    int clean = 0;
                    foreach (var d in order.deductions) if (d.station == "Prep" && d.reason.StartsWith("Clean strike")) clean++;
                    Log($"Prep: the crusher struck {clean} clean of {QualityBudget.GrindStrikes} by itself, last at {bench.LastStrikeSpeed:0.00} m/s.");
                    if (clean < QualityBudget.GrindStrikes)
                    {
                        var lines = new List<string>();
                        foreach (var d in order.deductions) if (d.station == "Prep") lines.Add(d.reason);
                        Fail($"Prep: the crusher landed only {clean} clean strikes of {QualityBudget.GrindStrikes}: {string.Join("; ", lines)}");
                    }
                    if (!mix.Ready) Fail("Prep did not finish: the crusher's mixture is not ready.");
                    yield break;
                }
                if (!bench.PressHintShowing)
                    Fail("The leaves are in, but the bench is not pointing at the bowl (press-here hint missing).");

                // The bowl is drawn in three-quarter view: a leaf that has settled must
                // be drawn inside its mouth, not hanging over the front of the mortar.
                foreach (var l in bench.Leaves)
                {
                    if (l.Body == null || !l.InBowl) continue;
                    if (!bench.ShowsInBowl(l))
                        Fail($"A settled {l.Data.DisplayName} is drawn at {l.Art.transform.position} — outside the mortar's mouth (bowl {bench.MortarWorld}).");
                }

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
                    // Every stroke reads up from rest: the needle starts at the bottom.
                    if (bench.LiftReady && bench.PredictedStrikeSpeed > 1f)
                        Fail($"Pestle strike {s + 1}: the dial started at {bench.PredictedStrikeSpeed:0.00} m/s, not from rest.");
                    if (s == 0) Capture($"day{day}{_orderTag}_morning_prep_dial_start");
                    bool greenShot = s != 0;
                    // Let go inside the green, at a different place in it each time: low, middle, high.
                    float[] places = { 0f, -0.7f, 0.7f };
                    float place = places[(day + s) % places.Length];
                    float want = Crafting.PrepBench.IdealStrike + place * bench.CurrentBand;
                    while (bench.PredictedStrikeSpeed < want && t < 6f)
                    {
                        if (!greenShot && bench.PredictedStrikeSpeed >= Crafting.PrepBench.IdealStrike - bench.CurrentBand * 0.5f)
                        {
                            greenShot = true;
                            Capture($"day{day}{_orderTag}_morning_prep_dial_green");
                        }
                        t += Time.deltaTime; yield return null;
                    }
                    Log($"Prep lift {s + 1}: ready={bench.LiftReady} pestle at {bench.PestleWorld} (bowl {bench.MortarWorld}), predicted {bench.PredictedStrikeSpeed:0.00} m/s after {t:0.00}s.");
                    _pointer.Release();
                    t = 0f;
                    while (bench.StrikesLeft == left && t < 4f) { t += Time.deltaTime; yield return null; }
                    if (bench.StrikesLeft == left) { Fail($"Pestle strike {s + 1} never registered on the bowl."); break; }
                    Log($"Prep strike {s + 1}: {bench.LastStrikeSpeed:0.00} m/s (clean {Crafting.PrepBench.IdealStrike}±{bench.CurrentBand}) on {bench.LastStrikeWith}");

                    // One number: what the dial said at the release, what the pestle hit at, and what
                    // the ledger says all agree, and a release inside the green is never penalised.
                    float predicted = bench.ReleasePrediction, measured = bench.LastStrikeSpeed;
                    string ledger = "";
                    for (int i = order.deductions.Count - 1; i >= 0 && ledger == ""; i--)
                        if (order.deductions[i].station == "Prep" && order.deductions[i].reason.Contains("m/s")) ledger = order.deductions[i].reason;
                    var m = System.Text.RegularExpressions.Regex.Match(ledger, @"(\d+\.\d) m/s");
                    float logged = m.Success ? float.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture) : -1f;
                    if (Mathf.Abs(predicted - measured) > 0.1f)
                        Fail($"Prep strike {s + 1}: the dial read {predicted:0.00} m/s at the release but the pestle hit at {measured:0.00}.");
                    if (Mathf.Abs(logged - measured) > 0.051f)
                        Fail($"Prep strike {s + 1}: the pestle hit at {measured:0.00} m/s but the ledger says {logged:0.0} ({ledger}).");
                    if (!ledger.StartsWith("Clean strike"))
                        Fail($"Prep strike {s + 1}: let go at {predicted:0.00} m/s, inside the green ({want:0.00}), and was penalised: {ledger}");
                    foreach (var f in Frames(25)) yield return f;
                }
                PhysicsKit.Pointer.Scripted = null;
                if (!mix.Ready) Fail("Prep did not finish: the mixture is not ready after three strikes.");
                Log($"Prep: {mix.Recipe.Name} ({mix.Recipe.Shorthand}) -> {mix.Evaluate()} mix, quality {order.qualityScore}");
            }

            /// <summary>
            /// With the clockwork stirrer, the first fighter's flask brews by itself
            /// while the next is at Prep: look in on the pot once, mid-brew, and go back.
            /// </summary>
            private IEnumerable PeekAtClockwork(object morningScreen, int day)
            {
                var pot = Crafting.PhysicsCauldronManager.Instance;
                if (pot == null) yield break;
                foreach (var step in WaitUntil(() => pot.ClockworkTurning && pot.BrewProgress01 > 0.2f, 10f,
                             "the clockwork paddle brewing on its own"))
                    yield return step;
                if (_errorCount > 0) yield break;
                SwitchMorningTab(morningScreen, "Cauldron");
                foreach (var f in Frames(20)) yield return f;
                Capture($"day{day}_morning_cauldron_clockwork");
                SwitchMorningTab(morningScreen, "Prep");
                foreach (var f in Frames(2)) yield return f;
            }

            private IEnumerable DriveCauldron(ActiveOrder order, int day)
            {
                var pot = Crafting.PhysicsCauldronManager.Instance;
                if (pot == null || pot.Liquid == null || order == null) { Fail("No cauldron / surface to stir."); yield break; }
                // The pot takes up the next brewed mash by itself (after the last brew has been seen done).
                // With the clockwork stirrer it may already have brewed it alone while we were at Prep.
                foreach (var step in WaitUntil(() => pot.Working == order || order.stage > BrewStage.Cauldron, 8f,
                             $"the cauldron taking up {order.heroName}'s mash"))
                    yield return step;
                if (_errorCount > 0) yield break;
                if (order.stage > BrewStage.Cauldron)
                {
                    int paddle = 0;
                    foreach (var d in order.deductions) if (d.reason.Contains("clockwork paddle")) paddle += d.pointsDelta;
                    if (!Crafting.PhysicsCauldronManager.AutoStir || paddle <= 0)
                        Fail($"{order.heroName}'s flask left the cauldron without anyone stirring it.");
                    else
                    {
                        _clockworkBrews++;
                        Log($"Cauldron: the clockwork paddle brewed {order.heroName}'s flask alone while we worked Prep (+{paddle} of {QualityBudget.BrewTotal}), quality {order.qualityScore}");
                    }
                    yield break;
                }
                var mix = order.Mixture;
                var cauldronView = UnityEngine.Object.FindFirstObjectByType<Crafting.CauldronView>();
                if (cauldronView != null && !cauldronView.FireLit)
                    Fail($"Cauldron: {order.heroName}'s mash is in the pot but the fire is out.");

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

                // Day 2 also checks both fumbles: stirred too fast the pot slops over
                // the rim, and left too slow the brew catches and then burns on it.
                if (day == 2 && order.queueIndex == 0)
                {
                    int splashes = pot.SplashCount;
                    float frantic = pot.FullStirDegPerSec * 1.6f;
                    for (float t = 0f; t < 2.5f && pot.SplashCount == splashes; t += Time.deltaTime)
                    {
                        angle += dir * frantic * Time.deltaTime * Mathf.Deg2Rad;
                        _pointer.World = pot.ToWorld(new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * R * 0.6f);
                        yield return null;
                    }
                    if (pot.SplashCount == splashes) Fail($"A frantic stir ({frantic:0} deg/s) never slopped the pot over its rim.");
                    else Log($"Cauldron fumble: a frantic stir slopped the pot over its rim (slosh {pot.Slosh01:0.00}), as it should.");
                    Capture($"day{day}{_orderTag}_morning_cauldron_spill");
                    foreach (var f in Frames(40)) yield return f;

                    // Now stop, with the spoon still over the brew: the bottom catches.
                    int before = order.deductions.Count;
                    for (float t = 0f; t < 6f && !pot.Burning; t += Time.deltaTime) yield return null;
                    if (!pot.Burning)
                        Fail($"Six seconds of not stirring never got the bottom burning (scorch {pot.Scorch01:0.00}).");
                    else
                    {
                        bool charged = false;
                        for (int i = before; i < order.deductions.Count; i++)
                            if (order.deductions[i].reason.Contains("bottom")) charged = true;
                        if (!charged) Fail("The bottom burned but nothing was charged for it.");
                        else Log($"Cauldron fumble: the bottom caught and burned (scorch {pot.Scorch01:0.00}), as it should.");
                    }
                    Capture($"day{day}{_orderTag}_morning_cauldron_burning");

                    // Stirred the wrong way for a while, the pot says so; right again, it stops.
                    if (cauldronView != null && !Crafting.PhysicsCauldronManager.AutoStir)
                    {
                        float wrongFor = 0f;
                        while (wrongFor < 1.7f)
                        {
                            angle -= dir * pot.StirBandCentre * pot.FullStirDegPerSec * Time.deltaTime * Mathf.Deg2Rad;
                            _pointer.World = pot.ToWorld(new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * R * 0.6f);
                            wrongFor += Time.deltaTime;
                            if (wrongFor < 0.6f && cauldronView.WrongWayShown) Fail("Cauldron: the wrong-way notice came up before the stir had been wrong for a second.");
                            yield return null;
                        }
                        if (!cauldronView.WrongWayShown) Fail("Cauldron: stirred the wrong way for 1.7 s and the pot said nothing.");
                        else
                        {
                            // Photographed on a lit phase of the blink, with the spoon still going the wrong way.
                            for (float lit = 0f; lit < 0.6f && cauldronView.WrongWayAlpha < 0.9f; lit += Time.deltaTime)
                            {
                                angle -= dir * pot.StirBandCentre * pot.FullStirDegPerSec * Time.deltaTime * Mathf.Deg2Rad;
                                _pointer.World = pot.ToWorld(new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * R * 0.6f);
                                yield return null;
                            }
                            Capture($"day{day}{_orderTag}_morning_cauldron_wrongway");
                            Log("Cauldron: the wrong-way notice came up after a second of it.");
                        }
                        for (float back = 0f; back < 0.6f; back += Time.deltaTime)
                        {
                            angle += dir * pot.StirBandCentre * pot.FullStirDegPerSec * Time.deltaTime * Mathf.Deg2Rad;
                            _pointer.World = pot.ToWorld(new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * R * 0.6f);
                            yield return null;
                        }
                        if (cauldronView.WrongWayShown) Fail("Cauldron: stirred the right way again and the wrong-way notice stayed up.");
                    }
                }

                // With the clockwork stirrer, the second flask is left to the paddle:
                // the spoon stays off the pot and the brew must still finish.
                if (Crafting.PhysicsCauldronManager.AutoStir)
                {
                    // Bought, the stirrer has the pot: no spoon to pick up at all.
                    if (GameObject.Find("SpoonArt") != null) Fail("The clockwork stirrer is bought, but the hand spoon is still at the pot.");
                    _pointer.World = pot.ToWorld(new Vector2(R * 4f, R * 3f));
                    float waited = 0f;
                    bool shot = false;
                    while (!pot.IsBrewComplete && waited < 90f)
                    {
                        if (!pot.ClockworkTurning && waited > 0.5f && !pot.IsBrewComplete)
                        { Fail("The clockwork paddle is not turning with the spoon off the pot."); break; }
                        if (!shot && pot.BrewProgress01 > 0.3f) { shot = true; Capture($"day{day}{_orderTag}_morning_cauldron_clockwork"); }
                        waited += Time.deltaTime;
                        yield return null;
                    }
                    PhysicsKit.Pointer.Scripted = null;
                    if (!pot.IsBrewComplete) Fail($"The clockwork stirrer never finished the brew in 90 s (progress {pot.BrewProgress01:P0}).");
                    else _clockworkBrews++;
                    if (pot.IsBrewComplete) Log($"Cauldron: the clockwork paddle brewed {order.heroName}'s flask alone in {waited:0.0}s, {pot.DissolvedFraction:P0} dissolved, quality {order.qualityScore}");
                    yield break;
                }

                float elapsed = 0f;
                bool captured = false;
                while (!pot.IsBrewComplete && elapsed < 60f)
                {
                    // Aim the stir at the middle of the band and correct toward it: too
                    // slow and it sticks, too fast and it slops.
                    float power = pot.TooSlow ? pot.MaxOptimalStir
                        : pot.TooFast ? pot.MinOptimalStir
                        : pot.StirBandCentre;
                    angle += dir * power * pot.FullStirDegPerSec * Time.deltaTime * Mathf.Deg2Rad;
                    _pointer.World = pot.ToWorld(new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * R * 0.6f);
                    elapsed += Time.deltaTime;
                    if (cauldronView != null && cauldronView.WrongWayShown && pot.StirringCorrectly)
                        Fail("Cauldron: the wrong-way notice is up while the stir is right.");
                    if (!captured && pot.BrewProgress01 > 0.4f)
                    {
                        captured = true;
                        Capture($"day{day}{_orderTag}_morning_cauldron_brewing");
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
                if (bench == null || order == null) { Fail("No Bottling bench."); yield break; }
                // The doser fills the flask on its own, so it may be poured before we get here.
                foreach (var step in WaitUntil(() => bench.Working == order
                                                     && (bench.Current == Crafting.BottlingBench.Step.Pour || Crafting.BottlingBench.Funnel),
                             8f, $"the bottling bench taking up {order.heroName}'s brew"))
                    yield return step;
                if (_errorCount > 0) yield break;
                if (day == 1 && _orderTag == "")
                {
                    // The pour lesson as the player first sees it: the ladle lit, the
                    // flask and its gauge kept lit, the caption clear of all three.
                    foreach (var step in WaitRealtime(1.4f)) yield return step;
                    Capture("day1_spotlight_pour");
                }

                float t = 0f;
                bool captured = false;
                if (Crafting.BottlingBench.Funnel)
                {
                    // The auto doser measures the brew in: no ladle, nothing to hold.
                    if (GameObject.Find("Ladle") != null) Fail("The auto doser is bought, but the hand ladle is still on the bench.");
                    while (bench.Current == Crafting.BottlingBench.Step.Pour && t < 12f)
                    {
                        t += Time.deltaTime;
                        if (!captured && bench.Fill01 > 0.35f)
                        {
                            captured = true;
                            Capture($"day{day}{_orderTag}_morning_bottling_pour");
                            // The window and the drops are the potion's own colour, not a fixed blue.
                            Color want = PixelArt.Element(order.element);
                            if (!SameColour(bench.DoserLevelColor, want))
                                Fail($"Bottling: the doser's window shows {bench.DoserLevelColor}, not the {order.element} potion's {want}.");
                            if (!SameColour(bench.StreamColor, want))
                                Fail($"Bottling: the doser's drops are {bench.StreamColor}, not the {order.element} potion's {want}.");
                        }
                        yield return null;
                    }
                    if (bench.Current == Crafting.BottlingBench.Step.Pour)
                    { Fail($"Bottling: the doser never finished the pour in 12 s ({bench.Fill01:P0}) - no action possible, the player is stuck."); yield break; }
                    if (bench.Fill01 < Crafting.BottlingBench.TargetLow || bench.Fill01 > Crafting.BottlingBench.TargetHigh)
                        Fail($"Bottling: the doser filled the flask to {bench.Fill01:P0}, outside the gold lines.");
                    Log($"Bottling: the doser measured it to {bench.Fill01:P0} by itself.");
                }
                bench.PourHeld = !Crafting.BottlingBench.Funnel;
                // Let go a little early: what is already in the air still lands. One
                // flask on day 3 is poured until it brims over instead: a brimful flask
                // used to keep its pour open for ever and lock the send.
                bool brim = day == 3 && _orderTag == "_o3";
                float stopAt = brim ? 1.05f : 0.72f;
                while (bench.PourHeld && bench.Fill01 < stopAt && t < (brim ? 9f : 12f))
                {
                    t += Time.deltaTime;
                    if (!captured && bench.Fill01 > 0.35f) { captured = true; Capture($"day{day}{_orderTag}_morning_bottling_pour"); }
                    yield return null;
                }
                bench.PourHeld = false;
                if (brim) Capture($"day{day}{_orderTag}_morning_bottling_brimful");
                if (!brim && bench.Fill01 < 0.72f && !Crafting.BottlingBench.Funnel) Fail($"Pouring for 12 s only filled the flask to {bench.Fill01:P0}.");
                t = 0f;
                while (bench.Current == Crafting.BottlingBench.Step.Pour && t < 8f) { t += Time.deltaTime; yield return null; }
                if (brim && bench.Current != Crafting.BottlingBench.Step.Pour)
                    Log($"Bottling: a brimful flask ({bench.Fill01:P0}) was still scored and moved on.");
                if (bench.Current == Crafting.BottlingBench.Step.Pour) { Fail("The pour was never scored (droplets never settled)."); yield break; }
                Log($"Bottling: poured to {bench.Fill01:P0} ({bench.Spilled} spilled), brew drawn up to row {bench.LiquidLevelRow}");
                int pouredRow = bench.LiquidLevelRow;
                float pouredFill = bench.Fill01;

                // What is in the flask has to be visible in it: the glass draws the brew
                // as a level, and a flask poured to the line is most of the way up the
                // bulb. It used to be droplets alone, and the flask looked empty.
                int expectedRow = Art.ShopArt.FlaskLevelRow(bench.Fill01 * Art.ShopArt.FlaskBulbVolumePx);
                if (bench.LiquidLevelRow != expectedRow)
                    Fail($"The flask is {bench.Fill01:P0} full but the brew is drawn to row {bench.LiquidLevelRow}, not {expectedRow}.");
                if (bench.LiquidLevelRow > Art.ShopArt.FlaskBodyCY)
                    Fail($"A flask at {bench.Fill01:P0} draws no brew above the middle of its bulb (row {bench.LiquidLevelRow}).");

                if (day == 1)
                {
                    // The old call still works (the needle runs on real time).
                    for (int guard = 0; guard < 200000 && Mathf.Abs(Crafting.BottlingBench.SealNeedle01() - 0.5f) > 0.02f; guard++)
                        yield return null;
                    bench.Seal();
                }
                else
                {
                    // By hand: pick the cork out of its dish, carry it over the neck, let go.
                    Vector2 from = bench.CorkWorld, to = bench.FlaskMouthWorld + new Vector2(0.02f, 1.1f);
                    if (Vector2.Distance(from, bench.CorkDishWorld) > 0.05f)
                        Fail($"The cork is not waiting in its dish ({from} vs {bench.CorkDishWorld}).");
                    PhysicsKit.Pointer.Scripted = _pointer;
                    _pointer.World = from;
                    foreach (var f in Frames(2)) yield return f;
                    _pointer.Press();
                    for (int i = 0; i <= 30; i++) { _pointer.World = Vector2.Lerp(from, to, i / 30f); yield return null; }
                    Capture($"day{day}{_orderTag}_morning_bottling_cork_carried");
                    _pointer.Release();
                    foreach (var f in Frames(2)) yield return f;
                    PhysicsKit.Pointer.Scripted = null;
                }
                t = 0f;
                while (!bench.CorkSeated && t < 3f) { t += Time.deltaTime; yield return null; }
                float off = Vector2.Distance(bench.CorkWorld, bench.CorkSeatWorld);
                if (!bench.CorkSeated || off > 0.03f)
                    Fail($"A clean seal left the cork at {bench.CorkWorld}, {off:0.000} from its seat at {bench.CorkSeatWorld} (seated={bench.CorkSeated}).");
                else Log($"Bottling: the cork went home into the neck, {off:0.000} off its seat.");
                foreach (var f in Frames(10)) yield return f;
                if (bench.Current == Crafting.BottlingBench.Step.Label && day >= 2)
                {
                    // By hand: take the right tag off the rail and press it on the glass.
                    Vector2 from = bench.TagWorld(order.element) + new Vector2(0f, -0.3f), to = bench.LabelSpotWorld;
                    PhysicsKit.Pointer.Scripted = _pointer;
                    _pointer.World = from;
                    foreach (var f in Frames(2)) yield return f;
                    _pointer.Press();
                    for (int i = 0; i <= 30; i++) { _pointer.World = Vector2.Lerp(from, to, i / 30f); yield return null; }
                    Capture($"day{day}{_orderTag}_morning_bottling_label_carried");
                    _pointer.Release();
                    foreach (var f in Frames(3)) yield return f;
                    PhysicsKit.Pointer.Scripted = null;
                    if (!bench.Labelled) Fail("The label was dragged onto the flask but did not stick.");
                }
                else bench.ApplyLabel(order.element);
                if (bench.Current != Crafting.BottlingBench.Step.Done) Fail($"Bottling ended at step {bench.Current}, not Done.");
                Log($"Bottling: sealed and labelled, quality {order.qualityScore}");

                // The sealed flask slides off the bench: photograph it on the way.
                float slideWait = 0f;
                while (bench.SlideProgress01 < 0.7f && slideWait < 6f) { slideWait += Time.unscaledDeltaTime; yield return null; }
                if (bench.SlideProgress01 < 0.7f) Fail("Bottling: the sealed flask never slid off the bench.");
                else
                {
                    Capture($"day{day}{_orderTag}_morning_bottling_slide");
                    // The brew goes with the glass: still the level it was poured to, never empty.
                    if (bench.LiquidLevelRow != pouredRow || bench.LiquidLevelRow >= Art.ShopArt.FlaskH)
                        Fail($"Bottling: the flask sliding away is drawn to row {bench.LiquidLevelRow} (poured to {pouredRow}, empty is {Art.ShopArt.FlaskH}).");
                    if (!Mathf.Approximately(bench.Fill01, pouredFill))
                        Fail($"Bottling: the sealed flask's fill changed from {pouredFill:P0} to {bench.Fill01:P0} on its way out.");
                }
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

                float bossSeenAt = -1f;
                bool bossShot = false;
                while (world.Expedition.Phase != ExpeditionPhase.Won && world.Expedition.Phase != ExpeditionPhase.Lost)
                {
                    // The guardian at the end of the waves, once it has come out of the ground.
                    if (world.Expedition.Phase == ExpeditionPhase.BossFight && world.Expedition.BossInstance != null)
                    {
                        if (bossSeenAt < 0f && GameLoopManager.Instance.TargetBiomeIndex == 0)
                            Fail($"Day {SaveSystem.Instance.State.day}: a guardian came out in the Woods ({world.Expedition.BossInstance.name}); the Woods have none.");
                        if (bossSeenAt < 0f) bossSeenAt = Time.time;
                        if (!bossShot && Time.time - bossSeenAt > 2.2f) { bossShot = true; Capture($"{namePrefix}_boss"); Capture($"{namePrefix}_hud_damage"); }
                    }
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

            /// <summary>Each party card's damage total is that fighter's share: together they are every bomb's damage.</summary>
            private void CheckFighterDamage(int day)
            {
                var hud = HudScreen() as ExpeditionHudScreen;
                var world = GameLoopManager.Instance.CurrentExpedition;
                if (hud == null || world == null || world.Telemetry == null) return;
                int cards = 0, best = -1, bestDmg = -1;
                for (int i = 0; i < hud.CardCount; i++)
                {
                    cards += hud.CardDamage(i);
                    if (hud.CardDamage(i) > bestDmg) { bestDmg = hud.CardDamage(i); best = i; }
                }
                int bombs = 0;
                foreach (var b in world.Telemetry.Report.bombs) bombs += b.totalDamage;
                if (cards != bombs) Fail($"Day {day}: the party cards credit {cards} damage, the bombs did {bombs}.");
                else if (bombs > 0 && bestDmg <= 0) Fail($"Day {day}: no party card shows any damage.");
                else Log($"Day {day}: per-fighter damage on the cards adds up to {cards} (most: card {best + 1}, {bestDmg}).");
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

            /// <summary>
            /// Drag the first job tag onto the Counter's order book with real uGUI
            /// drag events, and check the job was taken (the drop path, not the click
            /// shortcut).
            /// </summary>
            private IEnumerable DragFirstJobTag(int day)
            {
                var screen = UIManager.Instance != null ? UIManager.Instance.ScreenOf(ScreenId.Morning) as MorningScreen : null;
                var counter = screen != null ? screen.Counter : null;
                RectTransform first = counter != null ? counter.FirstOfferTag : null;
                DragTag tag = first != null ? first.GetComponentInParent<DragTag>() : null;
                if (tag == null || counter.OrderBook == null || UnityEngine.EventSystems.EventSystem.current == null)
                {
                    Fail("The Counter has no job tag or order book to drag onto.");
                    yield break;
                }
                int before = CraftingManager.Instance != null ? CraftingManager.Instance.Orders.Count : 0;
                Vector2 from = RectTransformUtility.WorldToScreenPoint(null, tag.Paper.position);
                Vector2 to = RectTransformUtility.WorldToScreenPoint(null, counter.OrderBook.position);
                var pe = new UnityEngine.EventSystems.PointerEventData(UnityEngine.EventSystems.EventSystem.current)
                {
                    position = from, pressPosition = from,
                    button = UnityEngine.EventSystems.PointerEventData.InputButton.Left,
                };
                var ev = UnityEngine.EventSystems.ExecuteEvents.beginDragHandler;
                UnityEngine.EventSystems.ExecuteEvents.Execute(tag.gameObject, pe, ev);
                pe.dragging = true;
                for (int i = 1; i <= 8; i++)
                {
                    pe.position = Vector2.Lerp(from, to, i / 10f);
                    UnityEngine.EventSystems.ExecuteEvents.Execute(tag.gameObject, pe, UnityEngine.EventSystems.ExecuteEvents.dragHandler);
                    yield return null;
                }
                Capture($"day{day}_morning_counter_tag_carried");
                pe.position = to;
                UnityEngine.EventSystems.ExecuteEvents.Execute(tag.gameObject, pe, UnityEngine.EventSystems.ExecuteEvents.dragHandler);
                UnityEngine.EventSystems.ExecuteEvents.Execute(tag.gameObject, pe, UnityEngine.EventSystems.ExecuteEvents.endDragHandler);
                yield return null;
                int after = CraftingManager.Instance != null ? CraftingManager.Instance.Orders.Count : 0;
                if (after != before + 1) Fail($"Dropping a job tag on the order book took {after - before} job(s), not 1.");
                else Log("Counter: the job tag dropped on the order book was taken.");
            }

            /// <summary>Let wall-clock time pass (animations run on unscaled time).</summary>
            private bool ContinueButtonLit()
            {
                object title = UIManager.Instance != null ? UIManager.Instance.ScreenOf(ScreenId.MainMenu) : null;
                var field = title != null ? title.GetType().GetField("_continue", BindingFlags.NonPublic | BindingFlags.Instance) : null;
                var button = field != null ? field.GetValue(title) as UnityEngine.UI.Button : null;
                return button != null && button.interactable;
            }

            /// <summary>
            /// The pause menu stops the morning dead: no clock, no physics, no music, no
            /// bench moving, nothing charged; and closing it starts it all again. Then
            /// MAIN MENU saves the run, the title lights CONTINUE, the slot screen shows
            /// the slot, and Continue comes back to the same day.
            /// </summary>
            private IEnumerable CheckPauseAndTitleRoundTrip()
            {
                var morning = UIManager.Instance.ScreenOf(ScreenId.Morning) as MorningScreen;
                if (morning == null || morning.Pause == null) { Fail("Pause: the morning has no pause menu."); yield break; }
                var loop = GameLoopManager.Instance;
                var prep = Crafting.PrepBench.Instance;

                foreach (var f in Frames(10)) yield return f;
                float clock = loop.MorningRemaining01;
                Vector2 leaves = Vector2.zero;
                if (prep != null) foreach (var l in prep.Leaves) if (l.Body != null) leaves += l.Body.position;
                int charges = 0;
                if (CraftingManager.Instance != null) foreach (var o in CraftingManager.Instance.Orders) charges += o.deductions.Count;

                morning.Pause.Open();
                foreach (var step in WaitRealtime(1.5f)) yield return step;
                Capture("day1_pause_menu");
                if (!Mathf.Approximately(Time.timeScale, 0f)) Fail($"Pause: time still runs at {Time.timeScale}x.");
                if (!AudioListener.pause) Fail("Pause: the music and sounds keep playing.");
                if (!Mathf.Approximately(loop.MorningRemaining01, clock))
                    Fail($"Pause: the morning clock ran while paused ({clock:0.0000} -> {loop.MorningRemaining01:0.0000}).");
                Vector2 leavesNow = Vector2.zero;
                if (prep != null) foreach (var l in prep.Leaves) if (l.Body != null) leavesNow += l.Body.position;
                if ((leavesNow - leaves).sqrMagnitude > 1e-8f) Fail("Pause: the leaves on the Prep bench moved while paused.");
                int chargesNow = 0;
                if (CraftingManager.Instance != null) foreach (var o in CraftingManager.Instance.Orders) chargesNow += o.deductions.Count;
                if (chargesNow != charges) Fail("Pause: something was charged while paused.");

                morning.Pause.Close();
                if (Mathf.Approximately(Time.timeScale, 0f) || AudioListener.pause) Fail("Pause: closing it did not start the game again.");
                foreach (var step in WaitRealtime(0.6f)) yield return step;
                if (loop.MorningRemaining01 >= clock) Fail("Pause: after RESUME the morning clock does not run.");
                else Log($"Pause: the clock held at {clock:0.000} for 1.5 s paused, then ran on ({loop.MorningRemaining01:0.000}).");

                // Out to the title: the run is saved, and the same day comes back.
                int day = SaveSystem.Instance.State.day;
                int gold = SaveSystem.Instance.State.gold;
                morning.Pause.Open();
                morning.Pause.ToMainMenu();
                foreach (var step in WaitForPhase(GamePhase.MainMenu, 5f)) yield return step;
                if (Mathf.Approximately(Time.timeScale, 0f) || AudioListener.pause) Fail("Pause: going to the title left the game stopped.");
                foreach (var step in Settle("main_menu_with_save")) yield return step;
                RunState onDisk = SaveSystem.Instance.Peek(HarnessSlot);
                if (onDisk == null || onDisk.day != day || onDisk.gold != gold || onDisk.contracts.Count != 0)
                    Fail($"Pause: MAIN MENU did not save the run as it was (day {onDisk?.day}, gold {onDisk?.gold}, jobs {onDisk?.contracts.Count}).");
                if (!ContinueButtonLit()) Fail("Title: CONTINUE stays dark with a save in a slot.");

                SlotScreen.Open(newGame: false);
                foreach (var step in Settle("slots_continue")) yield return step;
                SlotScreen.Open(newGame: true);
                foreach (var step in Settle("slots_new")) yield return step;

                if (!loop.Continue(HarnessSlot)) { Fail("Continue could not load the harness slot."); yield break; }
                foreach (var step in WaitForPhase(GamePhase.Morning, 15f)) yield return step;
                if (SaveSystem.Instance.State.day != day) Fail($"Continue came back on day {SaveSystem.Instance.State.day}, not {day}.");
                else Log($"Title round trip: saved from the pause menu, CONTINUE lit, back on day {day}.");
            }

            /// <summary>After the last road, the Evening has MENU beside SLEEP (and not before).</summary>
            private IEnumerable CheckEveningMenu()
            {
                var evening = UIManager.Instance.ScreenOf(ScreenId.Evening) as EveningScreen;
                RunState st = SaveSystem.Instance.State;
                if (evening == null || st == null) yield break;
                if (evening.MenuShown != EveningScreen.ShowsMenu(st)) Fail("Evening: MENU is up when it should not be (or missing when it should).");
                int saved = st.bestGrades[BiomeLibrary.Count - 1];
                st.bestGrades[BiomeLibrary.Count - 1] = Mathf.Max(1, saved);
                CallPrivate(evening, "OnShow");   // Show() of the screen already up does not refresh it
                foreach (var step in Settle("evening_after_last_road_menu")) yield return step;
                if (!evening.MenuShown) Fail("Evening: the last road is cleared but there is no MENU beside SLEEP.");
                st.bestGrades[BiomeLibrary.Count - 1] = saved;
                CallPrivate(evening, "OnShow");
                yield return null;
            }

            private readonly HashSet<string> _lessonShots = new HashSet<string>();

            /// <summary>
            /// Photograph a lesson's spotlight once, with the game held while it fades in
            /// and closes on its target. With <paramref name="wait"/> a lesson that has not
            /// been seen yet is waited for (a bench can be driven before the lesson starts).
            /// </summary>
            private IEnumerable ShowLesson(string id, bool wait)
            {
                if (_lessonShots.Contains(id)) yield break;
                RunState st = SaveSystem.Instance != null ? SaveSystem.Instance.State : null;
                if (wait && st != null && !st.seenLessons.Contains(id))
                {
                    float until = Time.realtimeSinceStartup + 1.5f;
                    while (TutorialManager.CurrentLessonId != id && Time.realtimeSinceStartup < until) yield return null;
                }
                if (TutorialManager.CurrentLessonId != id) yield break;
                _lessonShots.Add(id);
                CheckLessonCentred(id);
                TimeControl.Handle hold = default;
                if (TimeControl.Instance != null) hold = TimeControl.Instance.PushPause();
                foreach (var step in WaitRealtime(1.4f)) yield return step;
                if (TutorialManager.CurrentLessonId == id) Capture($"lesson_{id}");
                hold.Dispose();
            }

            /// <summary>The first lesson is up; ten clicks on the dark outside its light leave it exactly where it was.</summary>
            private IEnumerable CheckOutsideClicksDoNotAdvance()
            {
                RunState st = SaveSystem.Instance.State;
                float until = Time.realtimeSinceStartup + 3f;
                while (TutorialManager.CurrentLessonId != "job" && Time.realtimeSinceStartup < until) yield return null;
                if (TutorialManager.CurrentLessonId != "job") { Fail("Tutorial: the day-1 'job' lesson never came up."); yield break; }
                foreach (var f in Frames(30)) yield return f;   // let the dark close in
                for (int i = 0; i < 10; i++) TutorialManager.Instance.Spot.ClickOutside();
                yield return null;
                if (TutorialManager.CurrentLessonId != "job" || st.seenLessons.Contains("job"))
                    Fail($"Tutorial: ten clicks outside the light moved the lesson on (now {TutorialManager.CurrentLessonId ?? "none"}).");
                else Log("Tutorial: ten clicks outside the light left the lesson as it was.");
            }

            /// <summary>
            /// A lesson's spotlight is centred on what it points at: the target's centre is the
            /// centre of the art you can see (found in the scene by name, not asked of the bench).
            /// </summary>
            private void CheckLessonCentred(string id)
            {
                Lesson l = TutorialScript.Lessons.Find(x => x.Id == id);
                LessonTarget? t = l != null ? l.Where() : null;
                if (!t.HasValue || !t.Value.IsWorld) return;
                string root = id == "sack" ? "Sack" : id == "lid" ? "JarLid" : id == "scoop" || id == "turn" ? "SteepingJar" : id == "kiln" ? "RackSack0" : null;
                if (root == null) return;
                Bounds seen = default; bool any = false;
                foreach (var go in UnityEngine.Object.FindObjectsByType<GameObject>(FindObjectsSortMode.None))
                {
                    if (go.name != root || !go.activeInHierarchy) continue;
                    foreach (var r in go.GetComponentsInChildren<SpriteRenderer>())
                        if (r.enabled && r.sprite != null) { seen = r.bounds; any = true; break; }   // the first sprite drawn there
                    if (any) break;
                }
                if (!any) { Fail($"Lesson '{id}': nothing named {root} to centre on."); return; }
                float off = Vector2.Distance(t.Value.World, seen.center);
                if (off > 0.02f) Fail($"Lesson '{id}': the spotlight is {off:0.00} off the centre of the {root} it points at (at {t.Value.World}, art centre {(Vector2)seen.center}).");
                else Log($"Lesson '{id}': the spotlight is on the centre of the {root} ({off:0.000} off).");
            }

            /// <summary>Every basket on the rack is fully on the bench's view (not under the rail or the dock) and none overlaps another.</summary>
            private void CheckRackOnScreen(Crafting.KilnBench bench)
            {
                Camera cam = ShopWorld.Instance != null ? ShopWorld.Instance.WorldCamera : null;
                if (cam == null) return;
                var shown = new List<Bounds>();
                for (int i = 0; i < RunState.MaxParty; i++)
                {
                    GameObject go = GameObject.Find("RackSack" + i);
                    SpriteRenderer r = go != null ? go.GetComponentInChildren<SpriteRenderer>() : null;
                    if (r != null && r.enabled) shown.Add(r.bounds);
                }
                for (int i = 0; i < shown.Count; i++)
                {
                    Vector3 lo = cam.WorldToViewportPoint(shown[i].min), hi = cam.WorldToViewportPoint(shown[i].max);
                    // The world camera draws the bench's own area: its viewport is the bench, 0 to 1.
                    if (lo.x < 0.02f || hi.x > 0.98f)
                        Fail($"Kiln: basket {i + 1} of {shown.Count} on the rack is off the bench's view (viewport x {lo.x:0.00} to {hi.x:0.00}).");
                    if (i > 0 && shown[i].min.x < shown[i - 1].max.x - 0.3f)
                        Fail($"Kiln: baskets {i} and {i + 1} on the rack overlap.");
                }
                Log($"Kiln: {shown.Count} basket(s) on the rack, all on screen.");
            }

            private static bool SameColour(Color a, Color b) =>
                Mathf.Abs(a.r - b.r) < 0.02f && Mathf.Abs(a.g - b.g) < 0.02f && Mathf.Abs(a.b - b.b) < 0.02f;

            private IEnumerable WaitRealtime(float seconds)
            {
                float until = Time.realtimeSinceStartup + seconds;
                for (int guard = 0; guard < 2000 && Time.realtimeSinceStartup < until; guard++) yield return null;
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

            /// <summary>
            /// The first Evening's briefing, line by line, as a player meets it: the
            /// report (click on), the Upgrades tab and what is in it, the Party tab and
            /// what is in it, the diary, then sleep. Each line is photographed once the
            /// spotlight has closed in, and must be the lesson expected.
            /// </summary>
            private IEnumerable WalkEveningBriefing()
            {
                object evening = UIManager.Instance != null ? UIManager.Instance.ScreenOf(ScreenId.Evening) : null;
                var steps = new (string id, System.Action act)[]
                {
                    ("ev_report", () => TutorialManager.Instance.Continue()),
                    ("ev_upgrades", () => CallPrivate(evening, "ShowUpgrades")),
                    ("ev_upgrades_body", () => TutorialManager.Instance.Continue()),
                    ("ev_party", () => CallPrivate(evening, "ShowRoster")),
                    ("ev_party_body", () => TutorialManager.Instance.Continue()),
                    ("ev_diary", () => UIManager.Instance.Show(ScreenId.Diary)),
                };
                foreach (var (id, act) in steps)
                {
                    foreach (var step in WaitRealtime(1.3f)) yield return step;
                    if (TutorialManager.CurrentLessonId != id)
                    {
                        Fail($"Evening briefing: expected '{id}', but the lesson on screen is '{TutorialManager.CurrentLessonId ?? "none"}'.");
                        yield break;
                    }
                    Capture($"day1_briefing_{id}");
                    act();
                    yield return null;
                }
                UIManager.Instance.Show(ScreenId.Evening);
                foreach (var step in WaitRealtime(1.3f)) yield return step;
                if (TutorialManager.CurrentLessonId != "ev_sleep")
                    Fail($"Evening briefing: expected 'ev_sleep' after the diary, got '{TutorialManager.CurrentLessonId ?? "none"}'.");
                else Capture("day1_briefing_ev_sleep");
                Log($"Evening briefing: {string.Join(", ", SaveSystem.Instance.State.seenLessons.FindAll(x => x.StartsWith("ev_")))} seen.");
            }

            /// <summary>
            /// The HUD's pause stops time, and 1x (or 2x) plays again: a player once
            /// paused and found no way back, since the speeds did nothing while paused.
            /// </summary>
            private void CheckPauseAndResume()
            {
                object hud = UIManager.Instance != null ? UIManager.Instance.ScreenOf(ScreenId.ExpeditionHud) : null;
                float before = Time.timeScale;
                CallPrivate(hud, "TogglePause");
                if (!Mathf.Approximately(Time.timeScale, 0f)) { Fail($"Pause left time running at {Time.timeScale}x."); return; }
                Capture("day1_afternoon_paused");
                CallPrivate(hud, "SetFast", before > 1.5f);
                if (!Mathf.Approximately(Time.timeScale, before))
                    Fail($"Pressing a speed while paused left time at {Time.timeScale}x, not {before}x.");
                else Log("HUD: pause stopped time, and the speed button played it again.");
            }

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
                go.AddComponent<DebugTools.BrewFlowSimulationTest>();
                go.AddComponent<DebugTools.BenchUpgradeSimulationTest>();
                go.AddComponent<DebugTools.SaveSlotSimulationTest>();
                go.AddComponent<DebugTools.TutorialFocusSimulationTest>();
                go.AddComponent<DebugTools.KilnRackSimulationTest>();
                go.AddComponent<DebugTools.BenchFeelSimulationTest>();
                go.AddComponent<DebugTools.CombatBossSimulationTest>();
                go.AddComponent<DebugTools.ProgressionSimulationTest>();
                yield return null;   // Start() runs on the next frame
                yield return null;
                Destroy(go);

                // A full pestle lift must land faster than the top of the widest clean
                // band, so the dial rises through the green rather than stopping in it.
                float fullLift = Mathf.Sqrt(2f * Mathf.Abs(Physics2D.gravity.y) * Crafting.PrepBench.MaxLift);
                float greenTop = Crafting.PrepBench.IdealStrike + Crafting.PrepBench.WidestBand;
                if (fullLift <= greenTop) Fail($"A full pestle lift lands at {fullLift:0.00} m/s, inside the clean band (top {greenTop:0.00}).");

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
            /// Coven Matriarch, and meets the Woodwose only on the roads it guards, so without
            /// this the bosses were barely exercised at all.
            /// </summary>
            private IEnumerable RunBossScenarios()
            {
                foreach (var step in RunScenario(1, 5, "boss_woodwose", capture: true)) yield return step;
                // A replay of the Woods late in the run is still the first road: no guardian.
                foreach (var step in RunScenario(0, 5, "replay_woods_day5", capture: false)) yield return step;
                if (!string.IsNullOrEmpty(_lastScenarioBossId))
                    Fail($"replay_woods_day5: a guardian ('{_lastScenarioBossId}') came out in the Woods on day 5; the Woods have none.");
                else Log("replay_woods_day5: no guardian.");
                // The Peak is tuned to need the road kit (BiomeLibrary.RoadTierNeeded): she is
                // met by the party a player has to bring there.
                var saved = WithRoadKit(BiomeLibrary.RoadTierNeeded[BiomeLibrary.Count - 1]);
                foreach (var step in RunScenario(BiomeLibrary.Count - 1, 5, "boss_matriarch", capture: true)) yield return step;
                RestoreUpgrades(saved);
                foreach (var step in RunLostRoad()) yield return step;
            }

            /// <summary>
            /// A road lost on purpose, under the real HUD: one fighter, no road kit, on the
            /// Swamp (it needs three and the full kit). The slab must say DEFEAT and give two
            /// or three tips that fit what happened.
            /// </summary>
            private IEnumerable RunLostRoad()
            {
                var ui = UIManager.Instance;
                var hud = ui != null ? ui.ScreenOf(ScreenId.ExpeditionHud) as ExpeditionHudScreen : null;
                if (hud == null) { Fail("Lost road: no HUD screen."); yield break; }
                RunState st = SaveSystem.Instance.State;
                var saved = new List<string>(st.ownedUpgrades);
                foreach (var up in UpgradeCatalog.All) if (!up.IsBench) st.ownedUpgrades.Remove(up.Id);
                ScreenId back = ui.Current;
                ui.Show(ScreenId.ExpeditionHud);
                const int swamp = 3;
                foreach (var step in RunScenario(swamp, 5, "lost_road", capture: false, partySize: 1, hudWatch: hud)) yield return step;
                if (_lastScenarioWon) Fail("Lost road: one bare fighter won the Swamp - the curve has moved.");
                string tips = hud.TipsText;
                int lines = string.IsNullOrEmpty(tips) ? 0 : tips.Split('\n').Length;
                if (lines < 2 || lines > 3) Fail($"Lost road: the DEFEAT slab shows {lines} tips, expected 2 or 3 ('{tips}').");
                else Log($"Lost road: DEFEAT with {lines} tips: {tips.Replace("\n", " | ")}");
                ui.Show(back);
                st.ownedUpgrades.Clear();
                st.ownedUpgrades.AddRange(saved);
            }

            /// <summary>
            /// Each guardian drawn alone and idle on the game's dark ground, for documents:
            /// every fight capture has heroes, rings or damage numbers over the figure.
            /// Its own camera, far from any world, so nothing else is in the frame.
            /// </summary>
            private IEnumerable CaptureGuardianPortraits()
            {
                // The Woodwose as it is met on each of its roads: the Woods, frozen in the Caverns, broken in the Swamp.
                var guardians = new (string id, BossDefinition def)[]
                {
                    ("woodwose", DefaultExpeditionData.Woodwose(0)),
                    ("woodwose_t1", DefaultExpeditionData.Woodwose(1)),
                    ("woodwose_t2", DefaultExpeditionData.Woodwose(2)),
                    ("matriarch", DefaultExpeditionData.Matriarch()),
                };
                foreach (var (id, def) in guardians)
                {
                    var root = new GameObject($"~Portrait_{id}");
                    root.transform.position = new Vector3(0f, 1000f, 0f);
                    var visual = Vfx.BossVisual.Attach(root, def);
                    Camera cam = PortraitCamera();
                    foreach (var f in Frames(90)) yield return f;   // settle into its idle pose
                    RenderPortrait(cam, $"portrait_{id}");

                    // Every attack the guardian has, once: the windup and the blow, each its own motion.
                    var seen = new HashSet<Vfx.BossVisual.Motion>();
                    foreach (BossPhaseData phase in def.Phases)
                        foreach (BossAttackPattern pattern in phase.AttackPatterns)
                        {
                            var motion = Vfx.BossVisual.MotionOf(pattern.Shape, pattern.AreaRadius);
                            if (id.Contains("_t") || !seen.Add(motion)) continue;   // the tiers move like the first
                            visual.PreviewAttack(pattern, 0.8f);
                            visual.PreviewHold = true;
                            visual.PreviewAt(false, 0.95f);
                            foreach (var f in Frames(2)) yield return f;
                            RenderPortrait(cam, $"attack_{id}_{motion.ToString().ToLowerInvariant()}_windup");
                            visual.PreviewAt(true, motion == Vfx.BossVisual.Motion.Spin ? 0.22f : 0.28f);   // the spin edge-on, mid-turn
                            foreach (var f in Frames(2)) yield return f;
                            RenderPortrait(cam, $"attack_{id}_{motion.ToString().ToLowerInvariant()}_release");
                            visual.PreviewHold = false;
                            foreach (var f in Frames(40)) yield return f;   // back to idle before the next
                        }
                    Log($"Portraits: {id} with {seen.Count} attack motion(s): {string.Join(", ", seen)}.");
                    Destroy(cam.gameObject);
                    Destroy(root);
                    yield return null;
                }
            }

            private static Camera PortraitCamera()
            {
                var cam = new GameObject("~PortraitCamera").AddComponent<Camera>();
                cam.orthographic = true;
                cam.orthographicSize = 3.8f;
                cam.transform.position = new Vector3(0f, 1001.6f, -10f);
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0.106f, 0.078f, 0.122f);
                cam.enabled = false;
                return cam;
            }

            private void RenderPortrait(Camera cam, string name)
            {
                const int w = 600, h = 800;
                var rt = new RenderTexture(w, h, 24);
                var prev = RenderTexture.active;
                cam.targetTexture = rt;
                cam.Render();
                RenderTexture.active = rt;
                var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                tex.Apply();
                RenderTexture.active = prev;
                cam.targetTexture = null;

                string dir = Path.Combine(Directory.GetCurrentDirectory(), ScreensDir);
                Directory.CreateDirectory(dir);
                File.WriteAllBytes(Path.Combine(dir, $"{name}.png"), tex.EncodeToPNG());
                Log($"Screenshot: {ScreensDir}/{name}.png");
                Destroy(tex);
                rt.Release();
                Destroy(rt);
            }

            /// <summary>Lend the run the road upgrades up to <paramref name="tier"/>; returns what it owned before.</summary>
            private static List<string> WithRoadKit(int tier)
            {
                RunState st = SaveSystem.Instance != null ? SaveSystem.Instance.State : null;
                if (st == null) return null;
                var saved = new List<string>(st.ownedUpgrades);
                foreach (var up in UpgradeCatalog.All)
                {
                    if (up.IsBench || st.HasUpgrade(up.Id)) continue;
                    bool second = up.Requires.Length > 0;
                    if (tier >= 2 || (tier >= 1 && !second)) st.ownedUpgrades.Add(up.Id);
                }
                return saved;
            }

            private static void RestoreUpgrades(List<string> saved)
            {
                RunState st = SaveSystem.Instance != null ? SaveSystem.Instance.State : null;
                if (st != null && saved != null) st.ownedUpgrades = saved;
            }

            /// <summary>
            /// Full suite only: both guardians against every flask grade. The Woodwose
            /// with a full party and alone; the Matriarch with the party and road kit the
            /// Peak asks for. This is the loop's promise measured, not assumed: what the
            /// morning brewed decides whether the afternoon's guardian falls.
            /// </summary>
            private IEnumerable RunGradeSweep()
            {
                int[] grades = { 97, 85, 70, 50 };
                foreach (int q in grades)
                    foreach (var step in RunScenario(1, 11, $"grade_b1_q{q}_x3", capture: false, quality: q, partySize: 3))
                        yield return step;
                foreach (int q in new[] { 97, 85, 70 })
                    foreach (var step in RunScenario(1, 11, $"grade_b1_q{q}_x1", capture: false, quality: q, partySize: 1))
                        yield return step;

                int peak = BiomeLibrary.Count - 1;
                var saved = WithRoadKit(BiomeLibrary.RoadTierNeeded[peak]);
                foreach (int q in grades)
                    foreach (var step in RunScenario(peak, 11, $"grade_b{peak}_q{q}_x{BiomeLibrary.FightersNeeded[peak]}", capture: false,
                                 quality: q, partySize: BiomeLibrary.FightersNeeded[peak]))
                        yield return step;
                RestoreUpgrades(saved);
            }

            /// <summary>
            /// The difficulty curve, measured: every road against the parties a player
            /// could field by then — alone or hired up, with or without the road
            /// upgrades — all carrying Great flasks. The first two roads must be won
            /// alone and bare; from the third on, a player who neither hires nor buys
            /// must lose, and one who does both must be able to win. Uses a fixed
            /// roster of four (the Rookie and the notice board's first three) and puts
            /// the run's own roster and upgrades back afterwards.
            /// </summary>
            private IEnumerable RunDifficultyCurve()
            {
                RunState st = SaveSystem.Instance != null ? SaveSystem.Instance.State : null;
                if (st == null) { Fail("Difficulty curve: no run state."); yield break; }
                var savedRoster = st.roster;
                var savedUps = st.ownedUpgrades;

                var four = new List<HeroRecord> { HeroCatalog.NewHire("Rookie", 0) };
                while (four.Count < RunState.MaxParty) four.Add(HeroCatalog.CandidateFor(2, four));
                foreach (HeroRecord h in four) { h.deployed = true; h.level = 1; h.restUntilDay = 0; }
                st.roster = four;

                string[] none = { };
                string[] road1 = { UpgradeCatalog.HeavierFlasks, UpgradeCatalog.SpareVials, UpgradeCatalog.ThickBoots, UpgradeCatalog.QuickHands };
                var road2 = new List<string>(road1) { UpgradeCatalog.TemperedGlass, UpgradeCatalog.Bandolier, UpgradeCatalog.HardLeathers }.ToArray();
                var cells = new (string tag, int party, string[] ups)[]
                {
                    ("x1_bare", 1, none), ("x1_road1", 1, road1), ("x1_road2", 1, road2),
                    ("x2_bare", 2, none), ("x2_road1", 2, road1),
                    ("x3_bare", 3, none), ("x3_road1", 3, road1), ("x3_road2", 3, road2),
                };
                var won = new Dictionary<string, int>();
                var table = new StringBuilder();
                int[] seeds = { 11, 12 };
                for (int biome = 0; biome < BiomeLibrary.Count && _errorCount == 0; biome++)
                {
                    table.Append($"\n  b{biome} {BiomeLibrary.Name(biome),-18}");
                    foreach (var cell in cells)
                    {
                        int wins = 0;
                        foreach (int seed in seeds)
                        {
                            st.ownedUpgrades = new List<string>(cell.ups);
                            foreach (var step in RunScenario(biome, seed, $"curve_b{biome}_{cell.tag}_s{seed}", capture: false,
                                         quality: 85, partySize: cell.party))
                                yield return step;
                            if (_lastScenarioWon) wins++;
                        }
                        won[$"b{biome}_{cell.tag}"] = wins;
                        table.Append($" {cell.tag}={wins}/{seeds.Length}");
                    }
                }
                // The Woods have no guardian: a first-time player, solo, nothing bought,
                // must win them even with a weak flask.
                var firstDay = new StringBuilder();
                foreach (int q in new[] { 70, 55, 40 })
                {
                    int w = 0;
                    foreach (int seed in seeds)
                    {
                        st.ownedUpgrades = new List<string>(none);
                        foreach (var step in RunScenario(0, seed, $"day1_q{q}_s{seed}", capture: false, quality: q, partySize: 1))
                            yield return step;
                        if (_lastScenarioWon) w++;
                    }
                    firstDay.Append($" quality {q}: {w}/{seeds.Length}");
                    // Reported, not failed: the fix would be a stat change, which is the user's call.
                    if (w < seeds.Length) Log($"DAY-1 CHECK: the Woods (no guardian, solo, nothing bought) lost with a quality-{q} flask: {w}/{seeds.Length} wins.");
                }
                Log($"Day 1 (the Woods, no guardian, solo, nothing bought), wins by flask quality:{firstDay}");

                // The same cells with mixed brews, as a real party carries them (book, Broad, Far):
                // the tight Caverns cell and every Swamp cell. A mixed party must win wherever the plain one does.
                var mixed = new StringBuilder();
                foreach (var (biome, cell) in MixedCells(cells))
                {
                    int wins = 0;
                    foreach (int seed in seeds)
                    {
                        st.ownedUpgrades = new List<string>(cell.ups);
                        foreach (var step in RunScenario(biome, seed, $"mix_b{biome}_{cell.tag}_s{seed}", capture: false,
                                     quality: 85, partySize: cell.party, mixedVariants: true))
                            yield return step;
                        if (_lastScenarioWon) wins++;
                    }
                    int plain = won.TryGetValue($"b{biome}_{cell.tag}", out int pw) ? pw : -1;
                    mixed.Append($" b{biome}_{cell.tag}={wins}/{seeds.Length} (plain {plain}/{seeds.Length})");
                    if (plain == seeds.Length && wins < seeds.Length)
                        Fail($"Mixed brews: b{biome}_{cell.tag} won {wins}/{seeds.Length} with Broad/Far flasks, the plain party {plain}/{seeds.Length}.");
                }
                Log($"Mixed brews (book/Broad/Far), wins of {seeds.Length} seeds:{mixed}");
                st.roster = savedRoster;
                st.ownedUpgrades = savedUps;
                Log($"Difficulty curve (Great flasks, wins of {seeds.Length} seeds):{table}");

                void Expect(string key, bool mustWin, string why)
                {
                    if (!won.TryGetValue(key, out int w)) return;
                    if (mustWin && w < seeds.Length) Fail($"Difficulty curve: {key} won {w}/{seeds.Length} — {why}");
                    if (!mustWin && w > 0) Fail($"Difficulty curve: {key} won {w}/{seeds.Length} — {why}");
                }
                string Cell(int party, int tier) => $"x{party}_{(tier == 0 ? "bare" : "road" + tier)}";
                for (int b = 0; b < BiomeLibrary.Count; b++)
                {
                    int p = BiomeLibrary.FightersNeeded[b], t = BiomeLibrary.RoadTierNeeded[b];
                    Expect($"b{b}_{Cell(p, t)}", true, $"what the road asks for must be enough ({BiomeLibrary.Needs(b)})");
                    Expect($"b{b}_x3_road2", true, "a hired, upgraded party must be able to win every road.");
                    if (p > 1 || t > 0)
                        Expect($"b{b}_x1_bare", false, "a player who neither hires nor upgrades must lose here.");
                    if (t >= 2)
                        Expect($"b{b}_{Cell(p, 0)}", false, "bodies alone must not carry it: it needs the upgrades too.");
                    if (p >= 3)
                        Expect($"b{b}_x1_road2", false, "upgrades alone must not carry it: it needs hired fighters too.");
                }
            }

            /// <summary>The cells measured again with mixed brews: Caverns x2_road1 and every Swamp cell.</summary>
            private static IEnumerable<(int biome, (string tag, int party, string[] ups) cell)> MixedCells((string tag, int party, string[] ups)[] cells)
            {
                foreach (var c in cells)
                    if (c.tag == "x2_road1") yield return (2, c);
                foreach (var c in cells)
                    if (c.party > 1) yield return (3, c);   // a solo fighter has only the book's recipe
            }

            /// <summary>Full suite only: every biome on five seeds, for win rates and HP margins.</summary>
            private IEnumerable RunBiomeSweep()
            {
                for (int biome = 0; biome < BiomeLibrary.Count; biome++)
                    for (int seed = 11; seed <= 15; seed++)
                        foreach (var step in RunScenario(biome, seed, $"sweep_b{biome}_s{seed}", capture: false, snapAt: seed == 11 ? 6f : -1f))
                            yield return step;
            }

            /// <summary>
            /// A real arena for <paramref name="biome"/> with its boss enabled, fought by
            /// whatever party the run has deployed (three heroes after day 2) carrying a
            /// Great flask of the element the Counter recommends for that road. The day
            /// seeds the monster spawner, so the fight is reproducible.
            /// </summary>
            private IEnumerable RunScenario(int biome, int seedDay, string label, bool capture, int quality = 85, int partySize = 0,
                float snapAt = -1f, ExpeditionHudScreen hudWatch = null, bool mixedVariants = false)
            {
                RunState st = SaveSystem.Instance != null ? SaveSystem.Instance.State : null;
                if (st == null) { Fail($"Scenario {label}: no run state."); yield break; }

                int savedDay = st.day, savedReplay = st.replayBiomeIndex;
                st.day = seedDay;
                st.replayBiomeIndex = biome;

                var canvases = new List<Canvas>();
                if (hudWatch == null)
                    foreach (var c in FindObjectsByType<Canvas>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
                        if (c.enabled) { c.enabled = false; canvases.Add(c); }

                BiomeData data = BiomeLibrary.Get(biome);
                ElementType element = ContractBoard.Counter(ContractBoard.Dominant(ContractBoard.ThreatCounts(data)));
                var order = new ActiveOrder("scenario", $"{element} Flask", element);
                order.AdjustQuality(quality - order.qualityScore, "HeadlessPlaytest", $"scenario flask ({CombatQuality.GradeFor(quality)})");
                var party = st.DeployedParty();
                if (partySize > 0 && party.Count > partySize) party = party.GetRange(0, partySize);
                var orders = new List<ActiveOrder> { order };
                // As the Counter hands them out: fighter i brews take i (the book's, Broad, Far) of the same recipe.
                for (int i = 1; mixedVariants && i < party.Count; i++)
                {
                    var o = new ActiveOrder($"scenario{i}", $"{element} Flask", element)
                    {
                        Mixture = new BrewMixture(element, Complexity.RecipeTier(biome), RecipeBook.VariantFor(element, orders.ConvertAll(x => x.element))),
                    };
                    o.AdjustQuality(quality - o.qualityScore, "HeadlessPlaytest", $"scenario flask ({CombatQuality.GradeFor(quality)})");
                    orders.Add(o);
                }
                var loadouts = LoadoutBuilder.BuildAll(orders, party);

                var root = new GameObject($"~Scenario_{label}");
                var world = root.AddComponent<ExpeditionWorld>();
                world.Build(data, loadouts, party);
                _lastScenarioBossId = "";
                if (hudWatch != null) hudWatch.Watch(world, biome);

                float start = Time.time, nextCapture = 0f;
                const float Timeout = 240f;
                var watch = new BossWatch();
                while (world.Expedition.Phase != ExpeditionPhase.Won && world.Expedition.Phase != ExpeditionPhase.Lost
                       && Time.time - start < Timeout)
                {
                    if (world.Expedition.Phase == ExpeditionPhase.BossFight && world.Expedition.BossInstance != null)
                    {
                        watch.Attach(world.Expedition.BossInstance);
                        if (string.IsNullOrEmpty(_lastScenarioBossId)) _lastScenarioBossId = data.Boss != null && data.Boss.StoryId.Length > 0 ? data.Boss.StoryId : world.Expedition.BossInstance.name;
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
                    if (snapAt >= 0f && Time.time - start >= snapAt)
                    {
                        Capture($"{label}_view");
                        snapAt = -1f;
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
                _lastScenarioWon = world.Expedition.Phase == ExpeditionPhase.Won;
                if (hudWatch != null)
                {
                    // DEFEAT waits for the party to leave the field (at most ResultWaitCap); then it must be empty.
                    float waited = 0f;
                    while (!world.Expedition.ResultShown && waited < ExpeditionManager.ResultWaitCap + 1f) { waited += Time.deltaTime; yield return null; }
                    if (!world.Expedition.ResultShown) Fail("Lost road: the DEFEAT slab never came up.");
                    foreach (CombatantBody b in world.Party)
                        if (b != null && b.gameObject.activeInHierarchy && waited < ExpeditionManager.ResultWaitCap)
                            Fail($"Lost road: DEFEAT came up while {b.name} was still on the field.");
                    Log($"Lost road: DEFEAT shown {waited:0.0} s after the loss, with the field empty.");
                    foreach (var f in Frames(20)) yield return f;   // the slab and its tips settle in
                    Capture("defeat_tips");
                }
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
            /// <summary>
            /// The map's BACK: from the Upgrades tab to the map and back again, the day,
            /// the gold and tomorrow's road are as they were, and the Evening is on the
            /// tab the player left. Then the loop goes on to the map as usual.
            /// </summary>
            private IEnumerable CheckMapBack(object eveningScreen)
            {
                RunState st = SaveSystem.Instance.State;
                var eve = eveningScreen as EveningScreen;
                if (eve == null) { Fail("Map BACK: no Evening screen."); yield break; }
                CallPrivate(eveningScreen, "ShowUpgrades");
                int day0 = st.day, gold0 = st.gold, replay0 = st.replayBiomeIndex, tab0 = eve.ActiveTabIndex;
                GameLoopManager.Instance.BeginBiomeMap();
                foreach (var step in WaitForPhase(GamePhase.BiomeMap, 5f)) { if (_errorCount > 0) yield break; yield return step; }
                foreach (var step in Settle("day1_biome_map_back")) yield return step;
                CallPrivate(UIManager.Instance.ScreenOf(ScreenId.BiomeMap), "GoBack");
                foreach (var step in WaitForPhase(GamePhase.Evening, 5f)) { if (_errorCount > 0) yield break; yield return step; }
                if (_errorCount > 0) yield break;
                if (UIManager.Instance.Current != ScreenId.Evening)
                    Fail($"Map BACK: landed on {UIManager.Instance.Current}, not the Evening.");
                else if (st.day != day0 || st.gold != gold0 || st.replayBiomeIndex != replay0)
                    Fail($"Map BACK changed the run: day {day0}->{st.day}, gold {gold0}->{st.gold}, replay {replay0}->{st.replayBiomeIndex}.");
                else if (eve.ActiveTabIndex != tab0)
                    Fail($"Map BACK: the Evening came back on tab {eve.ActiveTabIndex}, not the one left ({tab0}).");
                else Log("Map BACK: back at the Evening on the same tab; day, gold and tomorrow's road unchanged.");
                foreach (var step in Settle("day1_evening_after_back")) yield return step;
                _dayBeforeSleep = st.day;
            }

            /// <summary>The day before the first SLEEP after a map BACK: SLEEP must move it by exactly one.</summary>
            private int _dayBeforeSleep = -1;

            /// <summary>
            /// The slot screen's DELETE, in a scratch save folder: three saves, the middle
            /// one deleted through the confirmation, the other two untouched. The run in
            /// memory and the harness's own folder are put back afterwards.
            /// </summary>
            private IEnumerable CheckSlotDelete()
            {
                string before = SaveSystem.DirectoryOverride;
                string dir = System.IO.Path.Combine(before ?? Application.persistentDataPath, "delete-test");
                ScreenId was = UIManager.Instance.Current;
                try
                {
                    if (System.IO.Directory.Exists(dir)) System.IO.Directory.Delete(dir, true);
                    System.IO.Directory.CreateDirectory(dir);
                    for (int i = 0; i < SaveSystem.Slots; i++)
                    {
                        RunState r = RunState.NewGame(i);
                        r.day = 3 + 2 * i;
                        r.lastSavedUnixSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                        System.IO.File.WriteAllText(System.IO.Path.Combine(dir, $"slot_{i}.json"), JsonUtility.ToJson(r, true));
                    }
                    SaveSystem.DirectoryOverride = dir;
                }
                catch (Exception e) { Fail($"Slot delete: could not set up the scratch saves: {e.Message}"); SaveSystem.DirectoryOverride = before; yield break; }

                SlotScreen.Open(newGame: false);
                yield return null;
                var screen = UIManager.Instance.ScreenOf(ScreenId.Slots) as SlotScreen;
                if (screen == null) { Fail("Slot delete: no slot screen."); SaveSystem.DirectoryOverride = before; yield break; }
                CallPrivate(screen, "AskDelete", 1);
                if (screen.DeletePending != 1) Fail("Slot delete: DELETE on slot 2 did not ask first.");
                foreach (var step in Settle("slots_delete_confirm")) yield return step;
                CallPrivate(screen, "ConfirmDelete");
                SaveSystem save = SaveSystem.Instance;
                RunState s0 = save.Peek(0), s2 = save.Peek(2);
                if (save.SlotExists(1)) Fail("Slot delete: the deleted slot is still on disk.");
                else if (s0 == null || s0.day != 3 || s2 == null || s2.day != 7)
                    Fail("Slot delete: deleting slot 2 changed another slot.");
                else if (screen.DeletePending != -1) Fail("Slot delete: the confirmation stayed open after DELETE.");
                else Log("Slot delete: slot 2 deleted after the confirmation; slots 1 and 3 untouched.");
                foreach (var step in Settle("slots_after_delete")) yield return step;

                SaveSystem.DirectoryOverride = before;
                try { System.IO.Directory.Delete(dir, true); } catch { /* best effort */ }
                UIManager.Instance.Show(was);
                yield return null;
            }

            private IEnumerable RunEnding()
            {
                RunState s = SaveSystem.Instance != null ? SaveSystem.Instance.State : null;
                if (s == null) { Fail("Ending: no run state."); yield break; }

                int peak = BiomeLibrary.Count - 1;
                var report = new ExpeditionReport { won = true, bossDefeated = true, bossId = "matriarch" };
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
                SaveSystem.DirectoryOverride = null;
                SettingsService.ExpeditionSpeedOverride = null;
                CutsceneScreen.AutoAdvance = false;
                StoryStage.SkipIntro = false;
                Art.PixelCanvas.ExportDir = null;

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
