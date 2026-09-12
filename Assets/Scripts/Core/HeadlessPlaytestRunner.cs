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
