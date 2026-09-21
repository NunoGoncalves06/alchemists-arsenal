using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using AlchemistsArsenal.Core;
using AlchemistsArsenal.Combat;
using AlchemistsArsenal.Data;
using AlchemistsArsenal.Story;

namespace AlchemistsArsenal.UI
{
    // ------------------------------------------------------------------- Boot

    public class BootScreen : GameScreen
    {
        protected override void Build()
        {
            UIFactory.Box(transform, UITheme.Ground, Rt);

            var crest = new GameObject("Crest", typeof(RectTransform)).AddComponent<UnityEngine.UI.Image>();
            crest.transform.SetParent(transform, false);
            crest.sprite = Art.PixelSprites.BootLogo();
            crest.rectTransform.anchorMin = crest.rectTransform.anchorMax = new Vector2(0.5f, 0.62f);
            crest.rectTransform.sizeDelta = new Vector2(160, 160);
            crest.raycastTarget = false;

            var logo = UIFactory.Title(transform, "Alchemist's Arsenal", UITheme.SizeDisplay + 6,
                UITheme.Candle, TextAlignmentOptions.Center);
            logo.characterSpacing = 3f;
            logo.rectTransform.anchorMin = new Vector2(0.1f, 0.36f);
            logo.rectTransform.anchorMax = new Vector2(0.9f, 0.48f);
            logo.rectTransform.offsetMin = logo.rectTransform.offsetMax = Vector2.zero;

            var tag = UIFactory.Label(transform, "all art · music · sound made in-house", UITheme.SizeSmall,
                UITheme.TextLow, TextAlignmentOptions.Bottom);
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
            UIFactory.Box(transform, UITheme.Ground, Rt);

            var title = UIFactory.Title(transform, "Alchemist's Arsenal", UITheme.SizeDisplay, UITheme.Candle);
            UIFactory.Place(title.rectTransform, 0.06f, 0.74f, 0.66f, 0.88f);
            var sub = UIFactory.Heading(transform, "brew in the morning · fight in the afternoon · pay the rent at night",
                UITheme.TextLow);
            UIFactory.Place(sub.rectTransform, 0.062f, 0.69f, 0.70f, 0.74f);

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
            Fix(UIFactory.Button(col.transform, "CREDITS", () => UIManager.Instance.Show(ScreenId.Credits), primary: false), 56);
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
            UIFactory.Box(transform, UITheme.Alpha(UITheme.Ground, 0.94f), Rt);
            var panel = UIFactory.Panel(transform, UITheme.Surface, "Panel");
            var prt = panel.rectTransform;
            prt.anchorMin = new Vector2(0.28f, 0.16f); prt.anchorMax = new Vector2(0.72f, 0.84f);
            prt.offsetMin = prt.offsetMax = Vector2.zero;

            var col = UIFactory.VStack(panel.transform, 14f, new RectOffset(24, 24, 24, 24));
            UIFactory.Stretch((RectTransform)col.transform);

            UIFactory.Title(col.transform, "Settings", UITheme.SizeTitle);
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

    // ----------------------------------------------------------------- Credits

    public class CreditsScreen : GameScreen
    {
        protected override void Build()
        {
            UIFactory.Box(transform, UITheme.Ground, Rt);
            var v = UIFactory.VStack(transform, 10f, new RectOffset(80, 80, 60, 60));
            UIFactory.Stretch((RectTransform)v.transform);
            UIFactory.Title(v.transform, "Alchemist's Arsenal", UITheme.SizeTitle + 4, UITheme.Candle,
                TextAlignmentOptions.Top);
            UIFactory.Label(v.transform,
                "A shop-management / auto-battler hybrid.\n\n" +
                "ALL ART, MUSIC AND SOUND CREATED IN-HOUSE.\n" +
                "Pixel art, procedural audio, and the Animalese speech synth were made\n" +
                "for this project — nothing from an asset store.\n\n" +
                "Built with Unity 6 · Inno Setup installer · 2D physics, IAUS combat AI,\n" +
                "a hierarchical boss FSM, and a decaying-quality crafting loop.",
                UITheme.SizeBody, UITheme.TextMid, TextAlignmentOptions.Top);
            var back = UIFactory.Button(v.transform, "BACK", () => UIManager.Instance.Show(ScreenId.MainMenu), primary: false);
            back.gameObject.AddComponent<LayoutElement>().minHeight = 48;
        }
    }

    // --------------------------------------------------------------- Day intro

    public class DayIntroScreen : GameScreen
    {
        private TextMeshProUGUI _title;
        private TextMeshProUGUI _flavour;

        protected override void Build()
        {
            UIFactory.Box(transform, UITheme.Ground, Rt);
            _title = UIFactory.Title(transform, "Day 1", UITheme.SizeDisplay + 10, UITheme.Candle,
                TextAlignmentOptions.Center);
            var trt = _title.rectTransform;
            trt.anchorMin = new Vector2(0.1f, 0.5f); trt.anchorMax = new Vector2(0.9f, 0.66f);
            trt.offsetMin = trt.offsetMax = Vector2.zero;
            _flavour = UIFactory.Label(transform, "", UITheme.SizeHeading, UITheme.TextMid, TextAlignmentOptions.Center);
            var frt = _flavour.rectTransform;
            frt.anchorMin = new Vector2(0.15f, 0.4f); frt.anchorMax = new Vector2(0.85f, 0.5f);
            frt.offsetMin = frt.offsetMax = Vector2.zero;
        }

        protected override void OnShow()
        {
            var s = SaveSystem.Instance != null ? SaveSystem.Instance.State : null;
            int biome = s != null ? s.TargetBiomeIndex : 0;
            _title.text = $"Day {(s != null ? s.day : 1)} — {BiomeLibrary.Name(biome)}";
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
        private TextMeshProUGUI _flaskName, _grade, _contract, _quality, _who, _sub;
        private UnityEngine.UI.Image _flaskArt, _fighterArt;

        protected override void Build()
        {
            UIFactory.Box(transform, UITheme.Ground, Rt);

            var title = UIFactory.Title(transform, "Today's party", UITheme.SizeTitle, UITheme.Candle,
                TextAlignmentOptions.Center);
            UIFactory.Place(title.rectTransform, 0f, 0.78f, 1f, 0.88f);
            _sub = UIFactory.Heading(transform, "", UITheme.TextLow, UITheme.SizeSmall,
                TextAlignmentOptions.Center);
            UIFactory.Place(_sub.rectTransform, 0f, 0.74f, 1f, 0.78f);

            // A plain bordered surface, not a Card: everything in here is anchored by
            // hand, so a layout stack would only fight it.
            var card = UIKit.Surface(transform, out Transform box, UITheme.Surface, UITheme.Line, "PartyCard");
            UIFactory.Place(card.rectTransform, 0.30f, 0.34f, 0.70f, 0.72f);

            _fighterArt = UIKit.Portrait(box, Art.PixelSprites.Rookie(), 120f);
            var frame = (RectTransform)_fighterArt.transform.parent.parent;   // art > mat > frame
            UIFactory.Place(frame, 0.06f, 0.30f, 0.32f, 0.92f);

            _who = UIFactory.Title(box, "", UITheme.SizeHeading + 2, UITheme.TextHi);
            UIFactory.Place(_who.rectTransform, 0.36f, 0.74f, 0.96f, 0.92f);

            _flaskArt = UIFactory.Icon(box, Art.PixelSprites.Flask(ElementType.Nature), 56f);
            UIFactory.Place(_flaskArt.rectTransform, 0.36f, 0.40f, 0.48f, 0.70f);

            _flaskName = UIFactory.Label(box, "", UITheme.SizeBody, UITheme.TextHi, TextAlignmentOptions.Left, true);
            UIFactory.Place(_flaskName.rectTransform, 0.50f, 0.56f, 0.96f, 0.72f);

            _grade = UIFactory.Label(box, "", UITheme.SizeBody, UITheme.Candle, TextAlignmentOptions.Left);
            UIFactory.Place(_grade.rectTransform, 0.50f, 0.40f, 0.96f, 0.56f);

            _quality = UIFactory.MonoLabel(box, "", UITheme.SizeSmall, UITheme.TextMid, TextAlignmentOptions.Left);
            UIFactory.Place(_quality.rectTransform, 0.36f, 0.24f, 0.96f, 0.38f);

            _contract = UIFactory.Label(box, "", UITheme.SizeSmall, UITheme.TextLow, TextAlignmentOptions.Left);
            UIFactory.Place(_contract.rectTransform, 0.06f, 0.06f, 0.96f, 0.24f);

            var begin = UIFactory.Button(transform, "BEGIN EXPEDITION", () => GameLoopManager.Instance.BeginAfternoon());
            UIFactory.Place(begin.image.rectTransform, 0.38f, 0.18f, 0.62f, 0.27f);
        }

        protected override void OnShow()
        {
            var order = Systems.CraftingManager.Instance != null
                ? Systems.CraftingManager.Instance.CurrentOrder : null;
            var job = SaveSystem.Instance != null && SaveSystem.Instance.State != null
                ? SaveSystem.Instance.State.contract : null;

            // Whoever ordered the potion is the one who walks the road with it.
            CustomerDefinition fighter = job != null && job.accepted
                ? CustomerCatalog.ById(job.buyerId) : CustomerCatalog.Rookie;
            _who.text = fighter.DisplayName;
            _fighterArt.sprite = Art.PixelSprites.Fighter(fighter.PortraitId);
            _sub.text = $"what {fighter.DisplayName} carries out of the shop";

            if (order == null)
            {
                _flaskArt.sprite = Art.PixelSprites.Flask(ElementType.Poison);
                _flaskName.text = "Raw Sludge";
                _grade.text = "<color=#d64550>NOTHING FINISHED</color>";
                _quality.text = "";
                _contract.text = $"You never took a job today. {fighter.DisplayName} goes out with the dregs.";
                return;
            }

            var grade = order.GetGrade();
            _flaskArt.sprite = Art.PixelSprites.Flask(order.element);
            _flaskName.text = order.potionName;
            _grade.text = $"<color=#{ColorUtility.ToHtmlStringRGB(UITheme.GradeColor(grade))}>" +
                          $"{grade.ToString().ToUpperInvariant()}</color>";
            _quality.text = $"quality {order.qualityScore} / 100";
            _contract.text = job != null && job.accepted
                ? job.Meets(grade)
                    ? $"{job.buyerName} asked for {job.RequiredGrade.ToString().ToUpperInvariant()} or better — this clears it."
                    : $"{job.buyerName} asked for {job.RequiredGrade.ToString().ToUpperInvariant()} or better. This is short, and they will pay half."
                : "";
        }
    }

    // ---------------------------------------------------------------- Biome map

    public class BiomeMapScreen : GameScreen
    {
        private RectTransform _dynamic;

        protected override void Build()
        {
            UIFactory.Box(transform, UITheme.Ground, Rt);

            var road = UIFactory.Title(transform, "The forest road", UITheme.SizeTitle);
            UIFactory.Place(road.rectTransform, 0f, 0.88f, 1f, 0.97f, 34f);
            var sub = UIFactory.Heading(transform, "where Rookie walks tomorrow", UITheme.TextLow);
            UIFactory.Place(sub.rectTransform, 0f, 0.84f, 1f, 0.88f, 36f);

            _dynamic = UIFactory.Root(transform, "Dynamic");
        }

        protected override void OnShow()
        {
            for (int i = _dynamic.childCount - 1; i >= 0; i--) Destroy(_dynamic.GetChild(i).gameObject);
            var s = SaveSystem.Instance.State;

            var card = UIKit.Card(_dynamic, "The five roads", out Transform list, spacing: 8f);
            UIFactory.Place(card.rectTransform, 0.05f, 0.2f, 0.62f, 0.82f);

            for (int i = 0; i < BiomeLibrary.Count; i++) BuildRoadRow(list, s, i);

            // Sized for three lines of SizeSmall under the card heading.
            var hintCard = UIKit.Card(_dynamic, "How the road works", out Transform hint, spacing: 6f);
            UIFactory.Place(hintCard.rectTransform, 0.05f, 0.03f, 0.62f, 0.18f);
            UIFactory.Label(hint,
                "Clearing a biome already moved you forward — sleeping just passes the night.\n" +
                "Replaying a cleared road pays half the fee (loot still counts), so you can never get stuck.\n" +
                $"Stars: {string.Join("  ·  ", ExpeditionReport.StarRules)}. One star opens the next road.",
                UITheme.SizeSmall, UITheme.TextMid);

            var next = UIKit.Card(_dynamic, "Tomorrow", out Transform nextBox, spacing: 6f);
            UIFactory.Place(next.rectTransform, 0.66f, 0.2f, 0.95f, 0.5f);
            int target = s.TargetBiomeIndex;
            var themeBadge = UIFactory.ElementBadge(nextBox, BiomeLibrary.Theme(target), 40f);
            UIFactory.FixedHeight(themeBadge.gameObject, 40f);
            var where = UIFactory.Title(nextBox, BiomeLibrary.Name(target), UITheme.SizeHeading + 2);
            UIFactory.FixedHeight(where.gameObject, 34f);
            UIFactory.Label(nextBox,
                s.IsReplayDay
                    ? "A road you have walked before. Half the fee, but pay all the same."
                    : "The next road you have not cleared.",
                UITheme.SizeSmall, UITheme.TextMid);

            var sleep = UIFactory.Button(_dynamic, "SLEEP", () => GameLoopManager.Instance.Sleep(-1));
            UIFactory.Place(sleep.image.rectTransform, 0.66f, 0.06f, 0.95f, 0.15f);
        }

        private void BuildRoadRow(Transform parent, RunState s, int index)
        {
            bool unlocked = s.IsBiomeUnlocked(index);
            bool current = index == s.TargetBiomeIndex;
            int stars = s.bestGrades[index];

            Image row = UIKit.Surface(parent, out Transform inner,
                current ? UITheme.SurfaceTop : UITheme.SurfaceHi,
                current ? UITheme.Candle : UITheme.LineSoft, "Road");
            UIFactory.FixedHeight(row.gameObject, 62f);

            var badge = UIFactory.ElementBadge(inner, BiomeLibrary.Theme(index), 30f);
            UIFactory.Place(badge.rectTransform, 0.02f, 0.24f, 0.08f, 0.76f);

            var name = UIFactory.Label(inner, BiomeLibrary.Name(index), UITheme.SizeBody,
                current ? UITheme.Candle : unlocked ? UITheme.TextHi : UITheme.TextLow,
                TextAlignmentOptions.Left, current);
            UIFactory.Place(name.rectTransform, 0.10f, 0f, 0.52f, 1f);

            var state = UIFactory.Label(inner,
                current ? "tomorrow's road" : !unlocked ? "locked" : stars > 0 ? "cleared" : "open",
                UITheme.SizeTiny, UITheme.TextLow, TextAlignmentOptions.Left);
            UIFactory.Place(state.rectTransform, 0.53f, 0f, 0.70f, 1f);

            for (int i = 0; i < 3; i++)
            {
                var star = UIFactory.Icon(inner, Art.PixelSprites.Star(), 20f,
                    i < stars ? Color.white : UITheme.Alpha(Color.white, 0.14f));
                UIFactory.Place(star.rectTransform, 0.70f + i * 0.055f, 0.3f, 0.745f + i * 0.055f, 0.7f);
            }

            if (unlocked && stars > 0 && !current)
            {
                int idx = index;
                var replay = UIFactory.Button(inner, "REPLAY", () => GameLoopManager.Instance.Sleep(idx),
                    primary: false);
                UIFactory.Place(replay.image.rectTransform, 0.87f, 0.16f, 0.98f, 0.84f);
            }
        }
    }
}
