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
            private TimeControl.Handle _fastForward;

            public void Begin()
            {
                _log = new StringBuilder();
                _errorCount = 0;
                _wall = Stopwatch.StartNew();
                Application.logMessageReceived += OnUnityLog;
                Log("=== HEADLESS PLAYTEST START (runtime driver, post-reload) ===");
                StartCoroutine(DriveThenFinish());
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

                // Speed the whole run up — TimeControl is the sole owner of Time.timeScale
                // (DESIGN.md), so we ask through it rather than writing Time.timeScale.
                if (TimeControl.Instance != null)
                    _fastForward = TimeControl.Instance.Push(4f, "headless-playtest");

                for (int day = 1; day <= DaysToRun && _errorCount == 0; day++)
                {
                    Log($"--- DAY {day} ---");

                    if (day == 1)
                    {
                        GameLoopManager.Instance.StartNewGame();
                        yield return null;
                        // Skip the opening cinematic — it needs a player click to close,
                        // and this driver is testing the loop, not the diary UI.
                        if (SaveSystem.Instance.State != null)
                            SaveSystem.Instance.State.openingCinematicSeen = true;
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
                    SwitchMorningTab(morningScreen, "Prep");
                    foreach (var step in Settle($"day{day}_morning_prep")) yield return step;
                    CompletePrep(order);

                    SwitchMorningTab(morningScreen, "Cauldron");
                    foreach (var step in Settle($"day{day}_morning_cauldron")) yield return step;

                    // A moment of real simulated time so the world pot + heat gauge +
                    // brew bar are all visibly live in the screenshot, then finish the
                    // brew directly (this driver isn't a mouse).
                    foreach (var step in SettleFrames(20)) yield return step;
                    order.AdjustQuality(SimulatedBrewPoints, "HeadlessPlaytest", "simulated good brew");
                    order.AdjustQuality(SimulatedBenchPoints, "HeadlessPlaytest", "simulated bench work");

                    if (day == DaysToRun)
                    {
                        SwitchMorningTab(morningScreen, "Bottling");
                        foreach (var step in Settle($"day{day}_morning_bottling")) yield return step;
                    }

                    GameLoopManager.Instance.BeginHandoff();
                    foreach (var step in WaitForPhase(GamePhase.Handoff, 5f)) { if (_errorCount > 0) yield break; yield return step; }
                    if (_errorCount > 0) yield break;
                    foreach (var step in Settle($"day{day}_handoff")) yield return step;

                    GameLoopManager.Instance.BeginAfternoon();
                    foreach (var step in WaitForPhase(GamePhase.Afternoon, 5f)) { if (_errorCount > 0) yield break; yield return step; }
                    if (_errorCount > 0) yield break;

                    // A screenshot roughly every second for the whole fight, not just
                    // one frame at the start — a player reported the adventurer's
                    // sprite visibly changing partway through combat, which a single
                    // "just after Afternoon begins" + a "DEFEAT screen" capture can
                    // never catch (the DEFEAT frame is taken after the adventurer's
                    // corpse is already gone — there's no "damaged but still alive"
                    // frame in that pair at all).
                    foreach (var step in WaitForExpeditionEndWithCaptures(90f, $"day{day}_afternoon")) { if (_errorCount > 0) yield break; yield return step; }
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
                    foreach (var step in Settle($"day{day}_evening_report")) yield return step;

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

                if (_errorCount == 0) Log("=== FULL LOOP COMPLETED — all days resolved cleanly ===");
            }

            /// <summary>
            /// Follow the day's recipe exactly and grind it. This works the mixture
            /// directly rather than clicking bench cards: which tray slot holds which
            /// leaf is a per-day shuffle, and a driver that guessed slots would be
            /// testing the shuffle rather than the loop. The effects it triggers
            /// (quality swing, cauldron band width) are the same ones the bench
            /// applies.
            /// </summary>
            /// <summary>
            /// What a flawless cauldron is worth: brewBonusPoints (2) per
            /// deductionInterval (1s) across brewSeconds (11). Mirror this if
            /// PhysicsCauldronManager's quality constants change.
            /// </summary>
            private const int SimulatedBrewPoints = 22;

            /// <summary>
            /// The per-leaf and per-grind bonuses CompletePrep deliberately does
            /// not simulate (3 leaves on cue at a mid potency, 3 clean strikes),
            /// so the driver's flask is comparable to a played one.
            /// </summary>
            private const int SimulatedBenchPoints = 24;

            private void CompletePrep(ActiveOrder order)
            {
                var mix = CraftingManager.Instance != null ? CraftingManager.Instance.Mixture : null;
                if (mix == null) { Log("No mixture on the order — Prep skipped."); return; }

                var pot = Crafting.PhysicsCauldronManager.Instance;
                foreach (ElementType leaf in mix.Recipe.Steps)
                {
                    if (mix.AllLeavesIn) break;
                    mix.Added.Add(leaf);
                    if (pot != null) pot.DropIngredient(leaf);
                }

                mix.Ground = true;
                Data.MixOutcome outcome = mix.Evaluate();
                int delta = Data.RecipeBook.QualityDelta(outcome);
                if (delta != 0)
                    order.AdjustQuality(delta, "Prep", $"{outcome} mix — {mix.Recipe.Name}");
                if (pot != null) pot.ApplyMix(outcome);

                Log($"Prep: {mix.Recipe.Name} ({mix.Recipe.Shorthand}) -> {outcome} mix, quality {order.qualityScore}");
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
                float nextCapture = start;
                ExpeditionWorld world = GameLoopManager.Instance.CurrentExpedition;
                if (world == null || world.Expedition == null)
                {
                    Fail("No ExpeditionWorld/ExpeditionManager present after BeginAfternoon.");
                    yield break;
                }

                while (world.Expedition.Phase != ExpeditionPhase.Won && world.Expedition.Phase != ExpeditionPhase.Lost)
                {
                    float now = Time.unscaledTime;
                    if (now - start > timeoutSeconds)
                    {
                        Fail($"Expedition never resolved within {timeoutSeconds}s " +
                             $"(stuck at {world.Expedition.Phase}, wave {world.Expedition.WaveNumber}/{world.Expedition.TotalWaves}).");
                        yield break;
                    }
                    if (now >= nextCapture)
                    {
                        Capture($"{namePrefix}_t{Mathf.RoundToInt(now - start):00}s");
                        nextCapture = now + captureEvery;
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
            }

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

                _fastForward.Dispose();
                Application.logMessageReceived -= OnUnityLog;

                string verdict = _errorCount == 0 ? "PASS" : $"FAIL ({_errorCount} error(s))";
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
