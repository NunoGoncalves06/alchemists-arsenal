#if UNITY_EDITOR
using System;
using System.Collections;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;
using AlchemistsArsenal.Combat;
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
        private const int DaysToRun = 2; // day 1: tutorial / no boss. day 2: boss enabled.

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
                StartCoroutine(Drive());
            }

            // ------------------------------------------------------------ script

            private IEnumerator Drive()
            {
                foreach (var step in WaitUntil(() => GameLoopManager.Instance != null && SaveSystem.Instance != null, 10f,
                        "boot managers (GameLoopManager/SaveSystem)"))
                    yield return step;
                if (_errorCount > 0) yield break;

                foreach (var step in WaitForPhase(GamePhase.MainMenu, 10f)) { if (_errorCount > 0) yield break; yield return step; }
                if (_errorCount > 0) yield break;
                foreach (var step in Settle("boot_main_menu")) yield return step;

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

                    // Morning: accept an order (same call ACCEPT ORDER makes), then
                    // check in on Cauldron/Prep/Bottling via the screen's own
                    // SwitchTab — see the class doc's "reflection note" in the Editor
                    // half for why there's no simulated mouse click here.
                    GameLoopManager.Instance.ConfirmOrder("Headless Test Flask", ElementType.Fire);
                    yield return null;
                    ActiveOrder order = CraftingManager.Instance != null ? CraftingManager.Instance.CurrentOrder : null;
                    if (order == null) { Fail("ConfirmOrder did not produce a CurrentOrder."); yield break; }
                    Log($"Order '{order.potionName}' accepted, quality {order.qualityScore} ({order.GetGrade()})");

                    // TutorialManager flips StationsUnlocked on its own coroutine once
                    // it notices the order — wait for the real flag instead of
                    // assuming frame timing against a second, independent coroutine.
                    // On day 1 the tutorial's Welcome step blocks ~8 REAL seconds
                    // (unscaled — TimeControl's speed-up doesn't touch it) before it
                    // even reaches the Counter step that unlocks these tabs, so the
                    // timeout here has to clear that, not just network jitter.
                    foreach (var step in WaitUntil(() => TutorialManager.StationsUnlocked, 15f, "StationsUnlocked after ConfirmOrder"))
                        yield return step;
                    if (_errorCount > 0) yield break;

                    object morningScreen = UIManager.Instance != null ? UIManager.Instance.ScreenOf(ScreenId.Morning) : null;
                    SwitchMorningTab(morningScreen, "Cauldron");
                    foreach (var step in Settle($"day{day}_morning_cauldron")) yield return step;

                    // A moment of real simulated time so the world pot + heat gauge +
                    // brew bar are all visibly live in the screenshot, then finish the
                    // brew directly (this driver isn't a mouse).
                    foreach (var step in SettleFrames(20)) yield return step;
                    order.AdjustQuality(65, "HeadlessPlaytest", "simulated good brew");

                    if (day == DaysToRun)
                    {
                        SwitchMorningTab(morningScreen, "Prep");
                        foreach (var step in Settle($"day{day}_morning_prep")) yield return step;
                        SwitchMorningTab(morningScreen, "Bottling");
                        foreach (var step in Settle($"day{day}_morning_bottling")) yield return step;
                    }

                    GameLoopManager.Instance.BeginHandoff();
                    foreach (var step in WaitForPhase(GamePhase.Handoff, 5f)) { if (_errorCount > 0) yield break; yield return step; }
                    if (_errorCount > 0) yield break;

                    GameLoopManager.Instance.BeginAfternoon();
                    foreach (var step in WaitForPhase(GamePhase.Afternoon, 5f)) { if (_errorCount > 0) yield break; yield return step; }
                    if (_errorCount > 0) yield break;

                    // A couple of seconds into the fight — enough for the adventurer to
                    // have closed in and thrown at least once — for a mid-combat capture.
                    foreach (var step in SettleFrames(90)) yield return step;
                    Capture($"day{day}_afternoon_fight");

                    foreach (var step in WaitForExpeditionEnd(90f)) { if (_errorCount > 0) yield break; yield return step; }
                    if (_errorCount > 0) yield break;
                    foreach (var step in Settle($"day{day}_afternoon_result")) yield return step;

                    GameLoopManager.Instance.BeginEvening();
                    foreach (var step in WaitForPhase(GamePhase.Evening, 10f)) { if (_errorCount > 0) yield break; yield return step; }
                    if (_errorCount > 0) yield break;
                    foreach (var step in Settle($"day{day}_evening_report")) yield return step;

                    object eveningScreen = UIManager.Instance != null ? UIManager.Instance.ScreenOf(ScreenId.Evening) : null;
                    CallPrivate(eveningScreen, "ShowUpgrades");
                    foreach (var step in Settle($"day{day}_evening_upgrades")) yield return step;

                    GameLoopManager.Instance.BeginBiomeMap();
                    foreach (var step in WaitForPhase(GamePhase.BiomeMap, 5f)) { if (_errorCount > 0) yield break; yield return step; }
                    if (_errorCount > 0) yield break;
                    foreach (var step in Settle($"day{day}_biome_map")) yield return step;

                    if (day < DaysToRun) GameLoopManager.Instance.Sleep(-1);
                }

                if (_errorCount == 0) Log("=== FULL LOOP COMPLETED — all days resolved cleanly ===");
                Finish();
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

            private IEnumerable WaitForExpeditionEnd(float timeoutSeconds)
            {
                float start = Time.unscaledTime;
                ExpeditionWorld world = GameLoopManager.Instance.CurrentExpedition;
                if (world == null || world.Expedition == null)
                {
                    Fail("No ExpeditionWorld/ExpeditionManager present after BeginAfternoon.");
                    yield break;
                }

                while (world.Expedition.Phase != ExpeditionPhase.Won && world.Expedition.Phase != ExpeditionPhase.Lost)
                {
                    if (Time.unscaledTime - start > timeoutSeconds)
                    {
                        Fail($"Expedition never resolved within {timeoutSeconds}s " +
                             $"(stuck at {world.Expedition.Phase}, wave {world.Expedition.WaveNumber}/{world.Expedition.TotalWaves}).");
                        yield break;
                    }
                    yield return null;
                }
                Log($"Expedition resolved: {world.Expedition.Phase} ({Time.unscaledTime - start:F1}s)");
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

            private void Capture(string name)
            {
                try
                {
                    string dir = Path.Combine(Directory.GetCurrentDirectory(), ScreensDir);
                    Directory.CreateDirectory(dir);
                    string path = Path.Combine(dir, name + ".png");
                    ScreenCapture.CaptureScreenshot(path);
                    Log($"Screenshot: {ScreensDir}/{name}.png");
                }
                catch (Exception e)
                {
                    Log($"Screenshot '{name}' failed (non-fatal): {e.Message}");
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

            private void Fail(string msg)
            {
                _errorCount++;
                string line = $"[{_wall.Elapsed:mm\\:ss}] FAIL: {msg}";
                Debug.LogError("[HeadlessPlaytest] " + line);
                _log.AppendLine(line);
            }

            private void Finish()
            {
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
