#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using AlchemistsArsenal.Core;

namespace AlchemistsArsenal.EditorTools
{
    /// <summary>
    /// CLI entry point for the batchmode headless playtest — the actual driver lives
    /// in <see cref="AlchemistsArsenal.Core.HeadlessPlaytestRunner"/> (runtime
    /// assembly), not here. That split exists because entering Play Mode triggers a
    /// domain reload: an Editor-side script that tries to drive the game across that
    /// boundary (an <c>EditorApplication.update</c>-pumped coroutine, say) gets its
    /// state wiped and its subscription dropped mid-flight — this is exactly what
    /// happened the first time this tool was built that way: it logged one line,
    /// then silently did nothing until the process was killed, looking like a hang.
    ///
    /// So this method does the one thing that's safe to do on the Editor side of
    /// that boundary: open the right scene, arm a flag that survives the reload
    /// (<see cref="SessionState"/>), and flip Play Mode. Everything else —
    /// driving the day loop, capturing screenshots, writing the report, exiting the
    /// process — happens in <see cref="HeadlessPlaytestRunner"/> once the reloaded
    /// domain is stable. Read that class's doc comment for the rest of the picture
    /// (checkpoints captured, the reflection-based tab switching, etc.).
    ///
    /// Run from the command line (close the Editor first — Unity is single-instance
    /// per project, batchmode will refuse to start otherwise). Screenshots need real
    /// rendering, so do NOT pass <c>-nographics</c> — and do NOT pass <c>-quit</c>
    /// either: this method returns immediately after arming the flag and flipping
    /// Play Mode, so <c>-quit</c> would exit Unity before the runtime driver ever
    /// got a chance to run. <see cref="HeadlessPlaytestRunner"/> calls
    /// <see cref="EditorApplication.Exit"/> itself once the loop genuinely finishes:
    /// <code>
    /// "C:\Program Files\Unity\Hub\Editor\6000.6.0f1\Editor\Unity.exe" ^
    ///   -batchmode -projectPath "C:\Game-Dev\Game-Repo" ^
    ///   -executeMethod AlchemistsArsenal.EditorTools.HeadlessPlaytest.RunFullLoop ^
    ///   -logFile "C:\Game-Dev\Game-Repo\headless-playtest.log"
    /// </code>
    /// Exit code 0 = every step passed; non-zero = see
    /// <c>headless-playtest-report.txt</c> and the log file for the failing step.
    /// Screenshots land in <c>headless-screens/*.png</c> next to the project.
    /// </summary>
    public static class HeadlessPlaytest
    {
        [MenuItem("Alchemist/Headless Playtest (full day loop)")]
        public static void RunFullLoop()
        {
            EditorSceneManager.OpenScene("Assets/Scenes/Boot.unity");
            SessionState.SetBool(HeadlessPlaytestRunner.ArmedKey, true);
            EditorApplication.isPlaying = true;
        }
    }
}
#endif
