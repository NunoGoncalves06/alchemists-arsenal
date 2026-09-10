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
