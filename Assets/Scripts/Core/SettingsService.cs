using System;
using UnityEngine;

namespace AlchemistsArsenal.Core
{
    /// <summary>
    /// Global player settings — audio volumes, accessibility toggles. Stored in
    /// <see cref="PlayerPrefs"/>, NOT in the save file (DESIGN.md §9 / reviewer W7).
    /// Applied live; there is no "apply" button.
    /// </summary>
    public static class SettingsService
    {
        public static event Action OnChanged;

        public static float MasterVolume { get => Get("vol.master", 0.9f); set => Set("vol.master", value); }
        public static float MusicVolume  { get => Get("vol.music", 0.7f);  set => Set("vol.music", value); }
        public static float SfxVolume    { get => Get("vol.sfx", 0.9f);    set => Set("vol.sfx", value); }
        public static float SpeechVolume { get => Get("vol.speech", 0.9f); set => Set("vol.speech", value); }

        public static bool ReduceMotion  { get => GetB("a11y.reduceMotion"); set => SetB("a11y.reduceMotion", value); }
        public static bool ScreenShake   { get => GetB("a11y.screenShake", true); set => SetB("a11y.screenShake", value); }
        public static float TextScale    { get => Get("a11y.textScale", 1f); set => Set("a11y.textScale", value); }
        public static bool ShowAiThinking { get => GetB("hud.aiThinking"); set => SetB("hud.aiThinking", value); }
        public static int DefaultExpeditionSpeed
        {
            get => ExpeditionSpeedOverride ?? Mathf.Clamp(PlayerPrefs.GetInt("hud.expSpeed", 1), 1, 2);
            set { PlayerPrefs.SetInt("hud.expSpeed", Mathf.Clamp(value, 1, 2)); Raise(); }
        }

        /// <summary>
        /// In-memory only, never written to PlayerPrefs. The headless playtest pins
        /// the fight speed with it: PlayerPrefs are shared with the developer's
        /// Editor, and whatever speed they last picked used to leak into test runs.
        /// </summary>
        public static int? ExpeditionSpeedOverride;

        private static float Get(string k, float d) => PlayerPrefs.GetFloat(k, d);
        private static void Set(string k, float v) { PlayerPrefs.SetFloat(k, Mathf.Clamp01(v)); Raise(); }
        private static bool GetB(string k, bool d = false) => PlayerPrefs.GetInt(k, d ? 1 : 0) == 1;
        private static void SetB(string k, bool v) { PlayerPrefs.SetInt(k, v ? 1 : 0); Raise(); }

        private static void Raise()
        {
            PlayerPrefs.Save();
            OnChanged?.Invoke();
        }
    }
}
