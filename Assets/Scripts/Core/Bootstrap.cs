using UnityEngine;
using AlchemistsArsenal.Systems;
using AlchemistsArsenal.Audio;
using AlchemistsArsenal.UI;

namespace AlchemistsArsenal.Core
{
    /// <summary>
    /// The one thing in <c>Boot.unity</c>. Stands up every persistent manager in
    /// dependency order, then hands off to <see cref="UIManager"/> (which shows the
    /// Boot splash → Main Menu). DESIGN.md §11 "Core" — code-built, not authored.
    /// </summary>
    [DefaultExecutionOrder(-1000)]
    public class Bootstrap : MonoBehaviour
    {
        private static bool _done;

        // Reset the guard when the domain reloads OR when "fast enter play mode"
        // skips the reload (reviewer P5).
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetGuard() => _done = false;

        private void Awake()
        {
            if (_done || GameLoopManager.Instance != null) { Destroy(gameObject); return; }
            _done = true;
            DontDestroyOnLoad(gameObject);

            // A persistent fallback camera so the game view always has something
            // clearing the screen — the shop / arena cameras (depth -1) draw over
            // it, and the Boot / Menu screens (Screen-Space-Overlay canvas) draw
            // over everything. Without this Unity shows "Display 1 No cameras
            // rendering" until a world spawns.
            var camGo = new GameObject("BootCamera");
            camGo.transform.SetParent(transform, false);
            var cam = camGo.AddComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = 5f;
            cam.depth = -100;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.106f, 0.078f, 0.122f); // ink-900
            cam.cullingMask = 0; // renders nothing itself — just clears

            // order: time + save first, then the loop, then presentation.
            Add<TimeControl>("TimeControl");
            Add<SaveSystem>("SaveSystem");
            Add<CraftingManager>("CraftingManager");
            Add<GameLoopManager>("GameLoopManager");
            Add<AudioManager>("AudioManager");
            Add<TutorialManager>("TutorialManager");
            Add<UIManager>("UIManager");
        }

        private T Add<T>(string name) where T : Component
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            return go.AddComponent<T>();
        }
    }
}
