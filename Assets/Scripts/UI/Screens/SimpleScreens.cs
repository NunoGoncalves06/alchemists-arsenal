using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using AlchemistsArsenal.Core;
using AlchemistsArsenal.Combat;
using AlchemistsArsenal.Story;

namespace AlchemistsArsenal.UI
{
    // ------------------------------------------------------------------- Boot

    public class BootScreen : GameScreen
    {
        protected override void Build()
        {
            UIFactory.Box(transform, UITheme.Ink900, Rt);
            var logo = UIFactory.Label(transform, "ALCHEMIST'S\nARSENAL", 64, UITheme.Candle,
                TextAlignmentOptions.Center, bold: true);
            UIFactory.Stretch(logo.rectTransform);
            var tag = UIFactory.Label(transform, "all art · music · sound made in-house", 16, UITheme.ParchmentDim,
                TextAlignmentOptions.Bottom);
            UIFactory.Stretch(tag.rectTransform, 24f);
        }

        protected override void OnShow() => StartCoroutine(Advance());

        private IEnumerator Advance()
        {
            yield return new WaitForSecondsRealtime(1.2f);
            if (GameLoopManager.Instance != null) GameLoopManager.Instance.GoToMainMenu();
        }
    }

    // --------------------------------------------------------------- Main menu

    public class MainMenuScreen : GameScreen
    {
        private Button _continue;
        private TextMeshProUGUI _warn;

        protected override void Build()
        {
            UIFactory.Box(transform, UITheme.Ink800, Rt);

            var title = UIFactory.Label(transform, "ALCHEMIST'S ARSENAL", 46, UITheme.Candle, TextAlignmentOptions.TopLeft, true);
            title.rectTransform.anchorMin = new Vector2(0.06f, 0.72f);
            title.rectTransform.anchorMax = new Vector2(0.6f, 0.86f);
            title.rectTransform.offsetMin = title.rectTransform.offsetMax = Vector2.zero;

            var col = UIFactory.VStack(transform, 12f);
            var rt = (RectTransform)col.transform;
            rt.anchorMin = new Vector2(0.06f, 0.28f);
            rt.anchorMax = new Vector2(0.34f, 0.68f);
            rt.offsetMin = rt.offsetMax = Vector2.zero;

            _continue = UIFactory.Button(col.transform, "CONTINUE",
                () => { if (GameLoopManager.Instance != null) GameLoopManager.Instance.Continue(); });
            Fix(_continue, 56);
            Fix(UIFactory.Button(col.transform, "NEW GAME", () => GameLoopManager.Instance.StartNewGame(), primary: false), 56);
            Fix(UIFactory.Button(col.transform, "SETTINGS", () => UIManager.Instance.Show(ScreenId.Settings), primary: false), 56);
            Fix(UIFactory.Button(col.transform, "QUIT", Quit, primary: false), 56);

            _warn = UIFactory.Label(col.transform, "", 14, UITheme.Danger);
            _warn.gameObject.AddComponent<LayoutElement>().minHeight = 40;
        }

        protected override void OnShow()
        {
            if (SaveSystem.Instance == null) return;
            bool exists = SaveSystem.Instance.SlotExists(0);
            bool corrupt = exists && SaveSystem.Instance.SlotCorrupt(0);
            if (_continue != null) _continue.interactable = exists && !corrupt;
            if (_warn != null)
                _warn.text = corrupt
                    ? "Save file unreadable. NEW GAME will overwrite it."
                    : "";
        }

        private static void Fix(Button b, float h) => b.gameObject.AddComponent<LayoutElement>().minHeight = h;

        private static void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }

    // ---------------------------------------------------------------- Settings

    public class SettingsScreen : GameScreen
    {
        protected override void Build()
        {
            UIFactory.Box(transform, UITheme.Ink900, Rt);
            var panel = UIFactory.Panel(transform, UITheme.Ink800, "Panel");
            var prt = panel.rectTransform;
            prt.anchorMin = new Vector2(0.28f, 0.16f); prt.anchorMax = new Vector2(0.72f, 0.84f);
            prt.offsetMin = prt.offsetMax = Vector2.zero;

            var col = UIFactory.VStack(panel.transform, 14f, new RectOffset(24, 24, 24, 24));
            UIFactory.Stretch((RectTransform)col.transform);

            UIFactory.Label(col.transform, "SETTINGS", 28, UITheme.Candle, TextAlignmentOptions.TopLeft, true);
            Slider(col.transform, "Master volume", () => SettingsService.MasterVolume, v => SettingsService.MasterVolume = v);
            Slider(col.transform, "Music", () => SettingsService.MusicVolume, v => SettingsService.MusicVolume = v);
            Slider(col.transform, "Sound", () => SettingsService.SfxVolume, v => SettingsService.SfxVolume = v);
            Toggle(col.transform, "Reduce motion", () => SettingsService.ReduceMotion, v => SettingsService.ReduceMotion = v);
            Toggle(col.transform, "Screen shake", () => SettingsService.ScreenShake, v => SettingsService.ScreenShake = v);
            Toggle(col.transform, "Show adventurer thinking", () => SettingsService.ShowAiThinking, v => SettingsService.ShowAiThinking = v);

            var back = UIFactory.Button(col.transform, "BACK", () =>
                UIManager.Instance.Show(GameLoopManager.Instance != null && GameLoopManager.Instance.Phase != GamePhase.Boot
                    ? MapPhase() : ScreenId.MainMenu));
            back.gameObject.AddComponent<LayoutElement>().minHeight = 48;
        }

        private static ScreenId MapPhase() => GameLoopManager.Instance.Phase switch
        {
            GamePhase.Morning => ScreenId.Morning,
            GamePhase.Evening => ScreenId.Evening,
            GamePhase.Afternoon => ScreenId.ExpeditionHud,
            _ => ScreenId.MainMenu,
        };

        private static void Slider(Transform parent, string label, System.Func<float> get, System.Action<float> set)
        {
            var row = UIFactory.HStack(parent, 12f);
            row.gameObject.AddComponent<LayoutElement>().minHeight = 34;
            UIFactory.Label(row.transform, label, 18, UITheme.Parchment).rectTransform.sizeDelta = new Vector2(240, 30);
            var s = new GameObject("Slider", typeof(RectTransform)).AddComponent<Slider>();
            s.transform.SetParent(row.transform, false);
            var bg = UIFactory.Panel(s.transform, UITheme.Ink700); UIFactory.Stretch(bg.rectTransform);
            var fill = UIFactory.Panel(s.transform, UITheme.Candle);
            s.fillRect = fill.rectTransform; UIFactory.Stretch(fill.rectTransform);
            s.minValue = 0; s.maxValue = 1; s.value = get();
            s.onValueChanged.AddListener(v => set(v));
            var le = s.gameObject.AddComponent<LayoutElement>(); le.minWidth = 220; le.minHeight = 22;
        }

        private static void Toggle(Transform parent, string label, System.Func<bool> get, System.Action<bool> set)
        {
            var row = UIFactory.HStack(parent, 12f);
            row.gameObject.AddComponent<LayoutElement>().minHeight = 34;
            UIFactory.Label(row.transform, label, 18, UITheme.Parchment).rectTransform.sizeDelta = new Vector2(300, 30);
            Button btn = null;
            btn = UIFactory.Button(row.transform, get() ? "ON" : "OFF", () => { set(!get()); }, primary: get());
            var le = btn.gameObject.AddComponent<LayoutElement>(); le.minWidth = 90; le.minHeight = 30;
        }
    }

    // --------------------------------------------------------------- Day intro

    public class DayIntroScreen : GameScreen
    {
        private TextMeshProUGUI _title;
        private TextMeshProUGUI _flavour;

        protected override void Build()
        {
            UIFactory.Box(transform, UITheme.Ink900, Rt);
            _title = UIFactory.Label(transform, "DAY 1", 56, UITheme.Candle, TextAlignmentOptions.Center, true);
            var trt = _title.rectTransform;
            trt.anchorMin = new Vector2(0.1f, 0.5f); trt.anchorMax = new Vector2(0.9f, 0.66f);
            trt.offsetMin = trt.offsetMax = Vector2.zero;
            _flavour = UIFactory.Label(transform, "", 20, UITheme.ParchmentDim, TextAlignmentOptions.Center);
            var frt = _flavour.rectTransform;
            frt.anchorMin = new Vector2(0.15f, 0.4f); frt.anchorMax = new Vector2(0.85f, 0.5f);
            frt.offsetMin = frt.offsetMax = Vector2.zero;
        }

        protected override void OnShow()
        {
            var s = SaveSystem.Instance != null ? SaveSystem.Instance.State : null;
            int biome = s != null ? s.TargetBiomeIndex : 0;
            _title.text = $"DAY {(s != null ? s.day : 1)} — {BiomeLibrary.Name(biome).ToUpper()}";
            _flavour.text = s != null && s.IsReplayDay ? "A road you have walked before. Half the pay, but pay all the same."
                : BiomeFlavour(biome);
            StartCoroutine(Advance());
        }

        private IEnumerator Advance()
        {
            yield return new WaitForSecondsRealtime(1.8f);

            // Opening cinematic plays once, right after the first Day Intro.
            var s = SaveSystem.Instance.State;
            if (s != null && s.HasDiary("diary_00") && !s.openingCinematicSeen)
            {
                s.openingCinematicSeen = true;
                SaveSystem.Instance.MarkDirty();
                DiaryScreen.FromOpeningCinematic = true;
                DiaryScreen.OpenEntryId = "diary_00";
                UIManager.Instance.Show(ScreenId.Diary);
                yield break;
            }
            GameLoopManager.Instance.BeginMorning();
        }

        private static string BiomeFlavour(int i) => i switch
        {
            0 => "The trees lean in when you pass. Bring something green.",
            1 => "Ash on the wind, and everything is trying to keep warm the hard way.",
            2 => "The cold in here is patient. Your potions will not be.",
            3 => "Every breath tastes faintly of copper. Do not linger.",
            _ => "The spire has been watching you climb toward it for five days.",
        };
    }

    // ----------------------------------------------------------------- Handoff

    public class HandoffScreen : GameScreen
    {
        private TextMeshProUGUI _summary;

        protected override void Build()
        {
            UIFactory.Box(transform, UITheme.Ink800, Rt);
            UIFactory.Label(transform, "TODAY'S PARTY", 30, UITheme.Candle, TextAlignmentOptions.Top, true)
                .rectTransform.anchorMin = new Vector2(0f, 0.78f);

            var card = UIFactory.Panel(transform, UITheme.Ink700, "Card");
            var crt = card.rectTransform;
            crt.anchorMin = new Vector2(0.32f, 0.4f); crt.anchorMax = new Vector2(0.68f, 0.72f);
            crt.offsetMin = crt.offsetMax = Vector2.zero;
            _summary = UIFactory.Label(card.transform, "", 20, UITheme.Parchment, TextAlignmentOptions.Center);
            UIFactory.Stretch(_summary.rectTransform, 16f);

            var begin = UIFactory.Button(transform, "BEGIN EXPEDITION  ▶", () => GameLoopManager.Instance.BeginAfternoon());
            var brt = begin.image.rectTransform;
            brt.anchorMin = new Vector2(0.38f, 0.16f); brt.anchorMax = new Vector2(0.62f, 0.26f);
            brt.offsetMin = brt.offsetMax = Vector2.zero;
        }

        protected override void OnShow()
        {
            var order = CraftingManager != null ? CraftingManager.CurrentOrder : null;
            if (order == null)
            {
                _summary.text = "Rookie carries a Raw Sludge flask.\nYou didn't finish a potion today.";
                return;
            }
            _summary.text = $"Rookie carries <b>{order.potionName}</b>\n" +
                            $"{order.element} · <color=#{ColorUtility.ToHtmlStringRGB(UITheme.GradeColor(order.GetGrade()))}>{order.GetGrade()}</color>  ({order.qualityScore}/100)";
        }

        private static Systems.CraftingManager CraftingManager => Systems.CraftingManager.Instance;
    }

    // ---------------------------------------------------------------- Biome map

    public class BiomeMapScreen : GameScreen
    {
        private RectTransform _dynamic;

        protected override void Build()
        {
            UIFactory.Box(transform, UITheme.Ink900, Rt);
            UIFactory.Label(transform, "THE FOREST ROAD", 30, UITheme.Candle, TextAlignmentOptions.TopLeft, true)
                .rectTransform.offsetMin = new Vector2(32, -60);
            _dynamic = UIFactory.Root(transform, "Dynamic");
        }

        protected override void OnShow()
        {
            foreach (Transform c in _dynamic) Destroy(c.gameObject);
            var s = SaveSystem.Instance.State;

            var v = UIFactory.VStack(_dynamic, 8f, new RectOffset(32, 32, 90, 0));
            var list = (RectTransform)v.transform;
            list.anchorMin = new Vector2(0f, 0.28f); list.anchorMax = new Vector2(0.62f, 1f);
            list.offsetMin = list.offsetMax = Vector2.zero;

            for (int i = 0; i < BiomeLibrary.Count; i++)
            {
                bool unlocked = s.IsBiomeUnlocked(i);
                bool current = i == s.currentBiomeIndex;
                int stars = s.bestGrades[i];
                string starStr = stars > 0 ? new string('*', stars) : "";
                var row = UIFactory.HStack(list.transform, 10f);
                row.gameObject.AddComponent<LayoutElement>().minHeight = 40;
                UIFactory.ElementBadge(row.transform, BiomeLibrary.Theme(i), 26);
                var lbl = UIFactory.Label(row.transform, $"{BiomeLibrary.Name(i)}  {starStr}", 18,
                    current ? UITheme.Candle : (unlocked ? UITheme.Parchment : UITheme.WoodDark));
                lbl.rectTransform.sizeDelta = new Vector2(340, 30);

                if (unlocked && stars > 0 && i != s.currentBiomeIndex)
                {
                    int idx = i;
                    var rp = UIFactory.Button(row.transform, "REPLAY", () => GameLoopManager.Instance.Sleep(idx), primary: false);
                    var le = rp.gameObject.AddComponent<LayoutElement>(); le.minWidth = 100; le.minHeight = 30;
                }
            }

            var hint = UIFactory.Label(_dynamic,
                "The road already moved forward when you cleared the biome. Sleeping just passes the night.\n" +
                "Replay a cleared biome for half the fee (loot still counts) — you never get stuck.",
                16, UITheme.ParchmentDim, TextAlignmentOptions.BottomLeft);
            hint.rectTransform.offsetMin = new Vector2(32, 24);
            hint.rectTransform.offsetMax = new Vector2(-500, 80);

            var sleep = UIFactory.Button(_dynamic, "SLEEP  ▶", () => GameLoopManager.Instance.Sleep(-1));
            var srt = sleep.image.rectTransform;
            srt.anchorMin = new Vector2(0.68f, 0.14f); srt.anchorMax = new Vector2(0.95f, 0.24f);
            srt.offsetMin = srt.offsetMax = Vector2.zero;
        }
    }
}
