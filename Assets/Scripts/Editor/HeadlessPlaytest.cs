#if UNITY_EDITOR
using System;
using System.Collections;
using System.Diagnostics;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using AlchemistsArsenal.Combat;
using AlchemistsArsenal.Core;
using AlchemistsArsenal.Systems;
using Debug = UnityEngine.Debug;

namespace AlchemistsArsenal.EditorTools
{
    /// <summary>
    /// Batchmode play-mode driver for the <c>game-tester</c> skill. Loads
    /// <c>Boot.unity</c>, enters Play Mode, and drives the real day loop
    /// end-to-end through the manager APIs (<see cref="GameLoopManager"/> etc.) —
    /// not mouse/UI simulation. It exercises the state machine, save system, and
    /// expedition resolution across two in-game days (day 1 = tutorial / no boss,
    /// day 2 = boss enabled) and fails on any timeout, stuck phase, or logged
    /// error/exception. It does NOT catch visual/feel bugs (occluded UI, missing
    /// glyphs, mis-tuned physics) — see the skill's manual checklist for those.
    ///
    /// Run from the command line (close the Editor first — Unity is single-instance
    /// per project, batchmode will refuse to start otherwise):
    /// <code>
    /// "C:\Program Files\Unity\Hub\Editor\6000.6.0f1\Editor\Unity.exe" ^
    ///   -batchmode -nographics -quit -projectPath "C:\Game-Dev\Game-Repo" ^
    ///   -executeMethod AlchemistsArsenal.EditorTools.HeadlessPlaytest.RunFullLoop ^
    ///   -logFile "C:\Game-Dev\Game-Repo\headless-playtest.log"
    /// </code>
    /// Exit code 0 = every step passed; non-zero = see
    /// <c>headless-playtest-report.txt</c> (written next to the project) and the
    /// log file for the failing step.
    /// </summary>
    public static class HeadlessPlaytest
    {
        private const string ReportPath = "headless-playtest-report.txt";
        private const int DaysToRun = 2; // day 1: tutorial / no boss. day 2: boss enabled.

        private static StringBuilder _log;
        private static int _errorCount;
        private static Stopwatch _wall;
        private static IEnumerator _routine;
        private static TimeControl.Handle _fastForward;

        [MenuItem("Alchemist/Headless Playtest (full day loop)")]
        public static void RunFullLoop()
        {
            _log = new StringBuilder();
            _errorCount = 0;
            _wall = Stopwatch.StartNew();
            Application.logMessageReceived += OnUnityLog;

            Log("=== HEADLESS PLAYTEST START ===");
            _routine = Drive();
            EditorApplication.update += Tick;
        }

        private static void Tick()
        {
            try
            {
                bool more;
                try { more = _routine.MoveNext(); }
                catch (Exception e) { Fail("Driver threw: " + e); more = false; }

                if (!more)
                {
                    EditorApplication.update -= Tick;
                    Finish();
                }
            }
            catch (Exception e)
            {
                // Finish() itself must never throw past batchmode, or the process hangs.
                Debug.LogError("[HeadlessPlaytest] Fatal in Tick/Finish: " + e);
                EditorApplication.update -= Tick;
                SafeExit(1);
            }
        }

        // ---------------------------------------------------------------- script

        private static IEnumerator Drive()
        {
            Log("Opening Boot.unity...");
            EditorSceneManager.OpenScene("Assets/Scenes/Boot.unity");
            EditorApplication.isPlaying = true;

            double t0 = EditorApplication.timeSinceStartup;
            while (!EditorApplication.isPlaying)
            {
                if (EditorApplication.timeSinceStartup - t0 > 15)
                {
                    Fail("Never entered Play Mode within 15s.");
                    yield break;
                }
                yield return null;
            }

            // Let Bootstrap/Awake/Start run.
            yield return null; yield return null; yield return null;

            foreach (var step in WaitUntil(() => GameLoopManager.Instance != null && SaveSystem.Instance != null, 10f,
                    "boot managers (GameLoopManager/SaveSystem)"))
                yield return step;
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
                    {
                        SaveSystem.Instance.State.openingCinematicSeen = true;
                        SaveSystem.Instance.State.tutorialCompleted = true;
                    }
                }

                foreach (var step in WaitForPhase(GamePhase.Morning, 15f)) { if (_errorCount > 0) yield break; yield return step; }
                if (_errorCount > 0) yield break;

                // Morning: accept an order and brew it well, then hand off — via the
                // real APIs, same path the Counter/Cauldron UI calls.
                GameLoopManager.Instance.ConfirmOrder("Headless Test Flask", ElementType.Fire);
                yield return null;
                ActiveOrder order = Systems.CraftingManager.Instance != null ? Systems.CraftingManager.Instance.CurrentOrder : null;
                if (order == null) { Fail("ConfirmOrder did not produce a CurrentOrder."); yield break; }
                order.AdjustQuality(65, "HeadlessPlaytest", "simulated good brew");
                Log($"Order '{order.potionName}' quality -> {order.qualityScore} ({order.GetGrade()})");

                GameLoopManager.Instance.BeginHandoff();
                foreach (var step in WaitForPhase(GamePhase.Handoff, 5f)) { if (_errorCount > 0) yield break; yield return step; }
                if (_errorCount > 0) yield break;

                GameLoopManager.Instance.BeginAfternoon();
                foreach (var step in WaitForPhase(GamePhase.Afternoon, 5f)) { if (_errorCount > 0) yield break; yield return step; }
                if (_errorCount > 0) yield break;

                foreach (var step in WaitForExpeditionEnd(90f)) { if (_errorCount > 0) yield break; yield return step; }
                if (_errorCount > 0) yield break;

                GameLoopManager.Instance.BeginEvening();
                foreach (var step in WaitForPhase(GamePhase.Evening, 10f)) { if (_errorCount > 0) yield break; yield return step; }
                if (_errorCount > 0) yield break;

                GameLoopManager.Instance.BeginBiomeMap();
                foreach (var step in WaitForPhase(GamePhase.BiomeMap, 5f)) { if (_errorCount > 0) yield break; yield return step; }
                if (_errorCount > 0) yield break;

                if (day < DaysToRun) GameLoopManager.Instance.Sleep(-1);
            }

            if (_errorCount == 0) Log("=== FULL LOOP COMPLETED — all days resolved cleanly ===");
        }

        // ---------------------------------------------------------------- waiters

        private static IEnumerable WaitForPhase(GamePhase phase, float timeoutSeconds)
        {
            double start = EditorApplication.timeSinceStartup;
            while (GameLoopManager.Instance == null || GameLoopManager.Instance.Phase != phase)
            {
                if (EditorApplication.timeSinceStartup - start > timeoutSeconds)
                {
                    Fail($"Timed out after {timeoutSeconds}s waiting for phase {phase} " +
                         $"(stuck at {GameLoopManager.Instance?.Phase}).");
                    yield break;
                }
                yield return null;
            }
            Log($"Phase -> {phase} ({EditorApplication.timeSinceStartup - start:F1}s)");
        }

        private static IEnumerable WaitForExpeditionEnd(float timeoutSeconds)
        {
            double start = EditorApplication.timeSinceStartup;
            ExpeditionWorld world = GameLoopManager.Instance.CurrentExpedition;
            if (world == null || world.Expedition == null)
            {
                Fail("No ExpeditionWorld/ExpeditionManager present after BeginAfternoon.");
                yield break;
            }

            while (world.Expedition.Phase != ExpeditionPhase.Won && world.Expedition.Phase != ExpeditionPhase.Lost)
            {
                if (EditorApplication.timeSinceStartup - start > timeoutSeconds)
                {
                    Fail($"Expedition never resolved within {timeoutSeconds}s " +
                         $"(stuck at {world.Expedition.Phase}, wave {world.Expedition.WaveNumber}/{world.Expedition.TotalWaves}).");
                    yield break;
                }
                yield return null;
            }
            Log($"Expedition resolved: {world.Expedition.Phase} ({EditorApplication.timeSinceStartup - start:F1}s)");
        }

        /// <summary>Generic "wait until predicate or fail" used once at boot.</summary>
        private static IEnumerable WaitUntil(Func<bool> predicate, float timeoutSeconds, string what)
        {
            double start = EditorApplication.timeSinceStartup;
            while (!predicate())
            {
                if (EditorApplication.timeSinceStartup - start > timeoutSeconds)
                {
                    Fail($"Timed out after {timeoutSeconds}s waiting for {what}.");
                    yield break;
                }
                yield return null;
            }
        }

        // ---------------------------------------------------------------- plumbing

        private static void OnUnityLog(string condition, string stackTrace, LogType type)
        {
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
                Fail($"[{type}] {condition}");
        }

        private static void Log(string msg)
        {
            string line = $"[{_wall.Elapsed:mm\\:ss}] {msg}";
            Debug.Log("[HeadlessPlaytest] " + line);
            _log.AppendLine(line);
        }

        private static void Fail(string msg)
        {
            _errorCount++;
            string line = $"[{_wall.Elapsed:mm\\:ss}] FAIL: {msg}";
            Debug.LogError("[HeadlessPlaytest] " + line);
            _log.AppendLine(line);
        }

        private static void Finish()
        {
            _fastForward.Dispose();
            Application.logMessageReceived -= OnUnityLog;

            string verdict = _errorCount == 0 ? "PASS" : $"FAIL ({_errorCount} error(s))";
            _log.AppendLine($"=== RESULT: {verdict} ===");

            string path = Path.Combine(Directory.GetCurrentDirectory(), ReportPath);
            try { File.WriteAllText(path, _log.ToString()); }
            catch (Exception e) { Debug.LogError("[HeadlessPlaytest] Could not write report: " + e); }

            Debug.Log($"[HeadlessPlaytest] {verdict} — report written to {path}");

            if (EditorApplication.isPlaying) EditorApplication.isPlaying = false;
            SafeExit(_errorCount == 0 ? 0 : 1);
        }

        private static void SafeExit(int code)
        {
            if (Application.isBatchMode) EditorApplication.Exit(code);
        }
    }
}
#endif
