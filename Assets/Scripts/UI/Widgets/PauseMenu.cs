using UnityEngine;
using UnityEngine.UI;
using AlchemistsArsenal.Audio;
using AlchemistsArsenal.Core;

namespace AlchemistsArsenal.UI
{
    /// <summary>
    /// The morning's pause menu, over the benches. Open, the game stands still: time
    /// is stopped (so physics and every bench), the morning clock does not run, and
    /// the music and sounds hold where they are. Nothing resumes until RESUME. Only
    /// what is needed: the sound levels, two comfort switches, RESUME, and back to the
    /// title (the run is saved; that morning starts again on Continue).
    /// </summary>
    public class PauseMenu : MonoBehaviour
    {
        private TimeControl.Handle _pause;
        private bool _open;

        public bool IsOpen => _open;

        public static PauseMenu Build(Transform parent)
        {
            var root = UIFactory.Root(parent, "PauseMenu");
            UIFactory.Stretch(root);
            var menu = root.gameObject.AddComponent<PauseMenu>();
            // Its own layer, over everything on screen (the tutorial's spotlight included).
            var layer = root.gameObject.AddComponent<Canvas>();
            layer.overrideSorting = true;
            layer.sortingOrder = 900;
            root.gameObject.AddComponent<GraphicRaycaster>();

            var dim = UIFactory.Panel(root, UITheme.Alpha(UITheme.Ground, 0.82f), "Dim");
            UIFactory.Stretch(dim.rectTransform);   // also swallows clicks meant for the shop behind

            var panel = UIFactory.Panel(root, UITheme.Surface, "Panel");
            UIFactory.Place(panel.rectTransform, 0.32f, 0.16f, 0.68f, 0.84f);
            var col = UIFactory.VStack(panel.transform, 12f, new RectOffset(28, 28, 26, 26));
            UIFactory.Stretch((RectTransform)col.transform);

            UIFactory.Title(col.transform, "Paused", UITheme.SizeTitle);
            UIFactory.Label(col.transform, "The morning clock is stopped.", UITheme.SizeSmall, UITheme.TextLow)
                .gameObject.AddComponent<LayoutElement>().minHeight = 26;

            SettingsScreen.Slider(col.transform, "Master volume", () => SettingsService.MasterVolume, v => SettingsService.MasterVolume = v);
            SettingsScreen.Slider(col.transform, "Music", () => SettingsService.MusicVolume, v => SettingsService.MusicVolume = v);
            SettingsScreen.Slider(col.transform, "Sound", () => SettingsService.SfxVolume, v => SettingsService.SfxVolume = v);
            SettingsScreen.Toggle(col.transform, "Reduce motion", () => SettingsService.ReduceMotion, v => SettingsService.ReduceMotion = v);
            SettingsScreen.Toggle(col.transform, "Screen shake", () => SettingsService.ScreenShake, v => SettingsService.ScreenShake = v);

            var resume = UIFactory.Button(col.transform, "RESUME", menu.Close);
            resume.gameObject.AddComponent<LayoutElement>().minHeight = 52;
            var title = UIFactory.Button(col.transform, "MAIN MENU", menu.ToMainMenu, primary: false);
            title.gameObject.AddComponent<LayoutElement>().minHeight = 46;
            UIFactory.Label(col.transform, "Your run is saved. This morning starts again when you continue.", UITheme.SizeTiny, UITheme.TextLow)
                .gameObject.AddComponent<LayoutElement>().minHeight = 22;

            root.gameObject.SetActive(false);
            return menu;
        }

        public void Open()
        {
            if (_open) return;
            _open = true;
            gameObject.SetActive(true);
            transform.SetAsLastSibling();
            if (TimeControl.Instance != null) _pause = TimeControl.Instance.PushPause();
            AudioManager.SetPaused(true);
        }

        public void Close()
        {
            if (!_open) return;
            _open = false;
            _pause.Dispose();
            _pause = default;
            AudioManager.SetPaused(false);
            gameObject.SetActive(false);
        }

        public void Toggle()
        {
            if (_open) Close(); else Open();
        }

        public void ToMainMenu()
        {
            Close();
            if (GameLoopManager.Instance != null) GameLoopManager.Instance.SaveAndGoToMainMenu();
        }

        // Leaving the morning by any other way (the clock, a send) must never leave the game stopped.
        private void OnDisable()
        {
            if (_open) Close();
        }
    }
}
