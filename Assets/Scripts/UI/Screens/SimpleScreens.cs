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
            yield return UnscaledWait.Seconds(1.2f);
            if (GameLoopManager.Instance != null) GameLoopManager.Instance.GoToMainMenu();
        }
    }

    // --------------------------------------------------------------- Main menu

    public class MainMenuScreen : GameScreen
    {
        private Button _continue, _otherSaves;
        private TextMeshProUGUI _warn, _sub, _summary, _slotLine;
        private GameObject _card;
        private StoryStage _keyArt;
        private int _recent = -1;

        /// <summary>The Continue card's summary line ("Day 7 · Cinder Peaks · 245 g"); empty with nothing to continue.</summary>
        public string ContinueSummary => _card != null && _card.activeSelf && _summary != null ? _summary.text : "";

        protected override void Build()
        {
            UIFactory.Box(transform, UITheme.Ground, Rt);

            // The shop itself fills the screen, with the people in it: Nell, Tam, the fighters.
            var art = UIFactory.Root(transform, "KeyArt");
            UIFactory.Stretch(art);
            _keyArt = StoryStage.Create(art);

            // A dusk over the left, so the words stand clear of the picture.
            var shade = UIFactory.Panel(transform, UITheme.Alpha(UITheme.Ground, 0.95f), "Shade");
            shade.raycastTarget = false;
            UIFactory.Place(shade.rectTransform, 0f, 0f, 0.40f, 1f);
            var fade = UIFactory.Panel(transform, UITheme.Alpha(UITheme.Ground, 0.6f), "ShadeEdge");
            fade.raycastTarget = false;
            UIFactory.Place(fade.rectTransform, 0.40f, 0f, 0.46f, 1f);

            var title = UIFactory.Title(transform, "Alchemist's Arsenal", UITheme.SizeDisplay, UITheme.Candle);
            UIFactory.Place(title.rectTransform, 0.05f, 0.80f, 0.62f, 0.93f);
            _sub = UIFactory.Heading(transform, "brew in the morning · fight in the afternoon · pay the rent at night",
                UITheme.TextLow);
            UIFactory.Place(_sub.rectTransform, 0.052f, 0.75f, 0.62f, 0.80f);

            // The Continue card: where the last run stands, and one click back into it.
            var card = UIFactory.Panel(transform, UITheme.Alpha(UITheme.Ink900, 0.9f), "ContinueCard");
            _card = card.gameObject;
            var rim = card.gameObject.AddComponent<Outline>();
            rim.effectColor = UITheme.Alpha(UITheme.Candle, 0.35f);
            rim.effectDistance = new Vector2(1f, -1f);
            var edge = UIFactory.Panel(card.transform, UITheme.Candle, "Edge");
            edge.raycastTarget = false;
            edge.rectTransform.anchorMin = new Vector2(0f, 0f); edge.rectTransform.anchorMax = new Vector2(0f, 1f);
            edge.rectTransform.pivot = new Vector2(0f, 0.5f);
            edge.rectTransform.sizeDelta = new Vector2(4f, 0f); edge.rectTransform.anchoredPosition = Vector2.zero;
            UIFactory.Place(card.rectTransform, 0.05f, 0.56f, 0.36f, 0.72f);
            var inCard = UIFactory.VStack(card.transform, 8f, new RectOffset(22, 22, 18, 18));
            UIFactory.Stretch((RectTransform)inCard.transform);
            UIFactory.Flex(UIFactory.Heading(inCard.transform, "Your shop", UITheme.TextLow).gameObject, 1f, 0f);
            _summary = UIFactory.Label(inCard.transform, "", UITheme.SizeHeading, UITheme.Parchment, font: UITheme.Display);
            UIFactory.Flex(_summary.gameObject, 1f, 0f);
            _slotLine = UIFactory.Label(inCard.transform, "", UITheme.SizeSmall, UITheme.TextLow);
            UIFactory.Flex(_slotLine.gameObject, 1f, 0f);
            _continue = UIFactory.Button(inCard.transform, "CONTINUE", ContinueLatest);
            UIFactory.FixedHeight(_continue.gameObject, 52f);
            _otherSaves = LightButton(inCard.transform, null, "Other saves", () => SlotScreen.Open(newGame: false), 30f);

            // The rest: light rows, an icon and a word each.
            var col = UIFactory.VStack(transform, 6f);
            UIFactory.Place((RectTransform)col.transform, 0.05f, 0.16f, 0.30f, 0.45f);
            LightButton(col.transform, Art.PixelSprites.Cauldron(), "New game", () => SlotScreen.Open(newGame: true));
            LightButton(col.transform, Art.ShopArt.Gear(), "Settings", () => UIManager.Instance.Show(ScreenId.Settings));
            LightButton(col.transform, Art.PixelSprites.Star(), "Credits", () => UIManager.Instance.Show(ScreenId.Credits));
            LightButton(col.transform, Art.ShopArt.Door(), "Quit", Quit);

            _warn = UIFactory.Label(transform, "", 14, UITheme.Danger);
            UIFactory.Place(_warn.rectTransform, 0.05f, 0.08f, 0.38f, 0.14f);
        }

        /// <summary>
        /// A menu row with no slab behind it: an icon and a word, lit by a candle-coloured
        /// wash only under the pointer.
        /// </summary>
        private static Button LightButton(Transform parent, Sprite icon, string text, System.Action onClick, float height = 44f)
        {
            var img = UIFactory.Panel(parent, UITheme.Alpha(UITheme.Candle, 0.22f), "LightButton");
            var btn = img.gameObject.AddComponent<Button>();
            btn.targetGraphic = img;
            var c = btn.colors;
            c.normalColor = new Color(1f, 1f, 1f, 0f);
            c.highlightedColor = Color.white;
            c.selectedColor = new Color(1f, 1f, 1f, 0f);
            c.pressedColor = new Color(1f, 1f, 1f, 1.6f);
            c.fadeDuration = 0.08f;
            btn.colors = c;
            btn.onClick.AddListener(() => onClick());
            UIFactory.FixedHeight(img.gameObject, height);

            var row = UIFactory.HStack(img.transform, 14f, new RectOffset(10, 10, 0, 0));
            row.childAlignment = TextAnchor.MiddleLeft;
            UIFactory.Stretch((RectTransform)row.transform);
            if (icon != null)
            {
                var ic = UIFactory.Icon(row.transform, icon, 28f);
                var le = ic.gameObject.AddComponent<LayoutElement>();
                le.minWidth = le.preferredWidth = 28f; le.minHeight = le.preferredHeight = 28f;
            }
            var label = UIFactory.Label(row.transform, text, icon != null ? UITheme.SizeHeading : UITheme.SizeSmall + 1,
                icon != null ? UITheme.Parchment : UITheme.TextLow, TextAlignmentOptions.MidlineLeft, font: UITheme.Display);
            UIFactory.Flex(label.gameObject, 1f, 0f);
            return btn;
        }

        private void ContinueLatest()
        {
            if (_recent < 0 || GameLoopManager.Instance == null) { SlotScreen.Open(newGame: false); return; }
            GameLoopManager.Instance.Continue(_recent);
        }

        protected override void OnShow()
        {
            if (SaveSystem.Instance == null) return;
            bool any = SaveSystem.Instance.AnyContinuable();
            _recent = any ? SaveSlots.MostRecent(SaveSystem.Instance) : -1;
            RunState latest = _recent >= 0 ? SaveSystem.Instance.Peek(_recent) : null;
            // Nothing to continue: no card at all, and New game is the first thing on the page.
            if (_card != null) _card.SetActive(latest != null);
            if (_continue != null) _continue.interactable = latest != null;
            if (latest != null)
            {
                _summary.text = $"Day {latest.day}  ·  {BiomeLibrary.Name(latest.TargetBiomeIndex)}  ·  {latest.gold} g";
                _slotLine.text = $"Saved in slot {_recent + 1}";
                int saves = 0;
                for (int i = 0; i < SaveSystem.Slots; i++) if (SaveSystem.Instance.Peek(i) != null) saves++;
                _otherSaves.gameObject.SetActive(saves > 1);
                // The card fits what it holds: a row taller when it offers the other saves.
                UIFactory.Place((RectTransform)_card.transform, 0.05f, saves > 1 ? 0.525f : 0.56f, 0.36f, 0.72f);
            }
            bool corrupt = false;
            for (int i = 0; i < SaveSystem.Slots; i++) corrupt |= SaveSystem.Instance.SlotCorrupt(i);
            if (_warn != null) _warn.text = corrupt ? "A save file is unreadable. Its slot says so." : "";

            // Once the story is over, the picture and the line say so (for the run played last).
            int recent = SaveSlots.MostRecent(SaveSystem.Instance);
            RunState saved = recent >= 0 ? SaveSystem.Instance.Peek(recent) : null;
            bool after = saved != null && saved.endingSeen;
            if (_sub != null) _sub.text = Story.StoryDirector.MenuSubtitle(saved);
            if (_keyArt != null) _keyArt.Play(after ? KeyArtAfter : KeyArtBefore, seed: 5);
        }

        // Built when shown, not at type load: it reads StoryScript, whose cutscenes are assets.
        private static Story.Shot KeyArtBefore => new Story.Shot
        {
            Scene = "shop_night", Fx = Story.ShotFx.Rain, ZoomFrom = 1f, ZoomTo = 1.06f,
            Actors = new[]
            {
                new Story.CutsceneActor { Id = "nell", At = new Vector2(0.58f, 0.111f) },
                Fighter("rookie", 0.7f, Story.ActorMotion.Bob), Fighter("herbalist", 0.79f), Fighter("merchant", 0.88f),
            },
        };

        private static Story.Shot KeyArtAfter => new Story.Shot
        {
            Scene = "shop_dawn", ZoomFrom = 1f, ZoomTo = 1.06f,
            Actors = new[]
            {
                new Story.CutsceneActor { Id = "nell", At = new Vector2(0.58f, 0.111f) },
                Fighter("rookie", 0.7f), Fighter("herbalist", 0.79f), Fighter("merchant", 0.88f),
            },
        };

        /// <summary>
        /// A fighter on the title's floor, facing Nell. The title is a close shot of the shop
        /// (Nell at full size), so they stand at twice their fighting size: the same head
        /// shorter than her as in the story's wide shots.
        /// </summary>
        private static Story.CutsceneActor Fighter(string id, float x, Story.ActorMotion motion = Story.ActorMotion.Breathe)
        {
            var feet = new Vector2(x, Story.StoryScript.FighterFeet(0.111f, 2f));
            return new Story.CutsceneActor { Id = "hero_" + id, At = feet, From = feet, Flip = true, Motion = motion, Scale = 2f, Tint = Color.white };
        }

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

        /// <summary>A labelled 0..1 slider (shared with the pause menu).</summary>
        internal static void Slider(Transform parent, string label, System.Func<float> get, System.Action<float> set)
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

        /// <summary>A labelled ON/OFF switch (shared with the pause menu); it shows its new state when clicked.</summary>
        internal static void Toggle(Transform parent, string label, System.Func<bool> get, System.Action<bool> set)
        {
            var row = UIFactory.HStack(parent, 12f);
            row.gameObject.AddComponent<LayoutElement>().minHeight = 34;
            UIFactory.Label(row.transform, label, 18, UITheme.Parchment).rectTransform.sizeDelta = new Vector2(300, 30);
            Button btn = null;
            void Show()
            {
                bool on = get();
                UIFactory.SetButtonText(btn, on ? "ON" : "OFF");
                if (on) UIFactory.TintButton(btn, UITheme.Candle, UITheme.CandleHot, UITheme.Ink900);
                else UIFactory.TintButton(btn, UITheme.Wood, new Color(0.52f, 0.37f, 0.24f), UITheme.Parchment);
            }
            btn = UIFactory.Button(row.transform, get() ? "ON" : "OFF", () => { set(!get()); Show(); }, primary: get());
            var le = btn.gameObject.AddComponent<LayoutElement>(); le.minWidth = 90; le.minHeight = 30;
        }
    }

    // -------------------------------------------------------------------- Slots

    /// <summary>
    /// The three save slots, for NEW GAME (where to start) or CONTINUE (which run).
    /// Each card names the road, the nights slept and when it was last played. A new
    /// game over an existing save asks twice.
    /// </summary>
    public class SlotScreen : GameScreen
    {
        private static bool _newGame;
        private TextMeshProUGUI _title;
        private readonly TextMeshProUGUI[] _head = new TextMeshProUGUI[SaveSystem.Slots];
        private readonly TextMeshProUGUI[] _body = new TextMeshProUGUI[SaveSystem.Slots];
        private readonly Button[] _go = new Button[SaveSystem.Slots];
        private readonly Button[] _del = new Button[SaveSystem.Slots];
        private int _confirm = -1;
        // The DELETE confirmation: one modal over the cards, for the slot in _deleting.
        private RectTransform _deleteModal;
        private int _deleting = -1;

        /// <summary>The slot the DELETE confirmation is asking about, or -1 when it is closed.</summary>
        public int DeletePending => _deleting;

        /// <summary>True when the screen is choosing where to start a new game.</summary>
        public static bool NewGameMode => _newGame;

        public static void Open(bool newGame)
        {
            _newGame = newGame;
            // Already up (switching mode in place): Show would not refresh it.
            if (UIManager.Instance.Current == ScreenId.Slots && UIManager.Instance.ScreenOf(ScreenId.Slots) is SlotScreen up)
            {
                up._confirm = -1;
                up.Refresh();
                return;
            }
            UIManager.Instance.Show(ScreenId.Slots);
        }

        protected override void Build()
        {
            UIFactory.Box(transform, UITheme.Ground, Rt);
            _title = UIFactory.Title(transform, "", UITheme.SizeTitle, UITheme.Candle);
            UIFactory.Place(_title.rectTransform, 0.06f, 0.80f, 0.94f, 0.90f);

            for (int i = 0; i < SaveSystem.Slots; i++)
            {
                int slot = i;
                float x0 = 0.06f + i * 0.30f;
                var card = UIFactory.Panel(transform, UITheme.Surface, "Slot" + i);
                UIFactory.Place(card.rectTransform, x0, 0.26f, x0 + 0.28f, 0.74f);
                _head[i] = UIFactory.Heading(card.transform, $"Slot {i + 1}", UITheme.TextHi);
                UIFactory.Place(_head[i].rectTransform, 0.08f, 0.80f, 0.92f, 0.94f);
                _body[i] = UIFactory.Label(card.transform, "", UITheme.SizeBody, UITheme.TextMid, TextAlignmentOptions.TopLeft);
                UIFactory.Place(_body[i].rectTransform, 0.08f, 0.34f, 0.92f, 0.78f);
                _go[i] = UIFactory.Button(card.transform, "", () => Choose(slot));
                UIFactory.Place(_go[i].image.rectTransform, 0.08f, 0.06f, 0.92f, 0.22f);
                _del[i] = UIFactory.Button(card.transform, "DELETE", () => AskDelete(slot), primary: false);
                UIFactory.Place(_del[i].image.rectTransform, 0.56f, 0.24f, 0.92f, 0.32f);
            }

            BuildDeleteModal();

            var back = UIFactory.Button(transform, "BACK", () => UIManager.Instance.Show(ScreenId.MainMenu), primary: false);
            UIFactory.Place(back.image.rectTransform, 0.06f, 0.10f, 0.22f, 0.18f);
        }

        protected override void OnShow()
        {
            _confirm = -1;
            CancelDelete();
            Refresh();
        }

        private void BuildDeleteModal()
        {
            _deleteModal = UIFactory.Root(transform, "DeleteConfirm");
            var scrim = UIFactory.Panel(_deleteModal, UITheme.Alpha(UITheme.Ink900, 0.78f), "Scrim");
            UIFactory.Stretch(scrim.rectTransform);   // takes every click behind the question
            var box = UIFactory.Panel(_deleteModal, UITheme.Surface, "Question");
            UIFactory.Place(box.rectTransform, 0.30f, 0.38f, 0.70f, 0.62f);
            var q = UIFactory.Label(box.transform, "Delete this save? This cannot be undone.", UITheme.SizeBody,
                UITheme.TextHi, TextAlignmentOptions.Center, true);
            UIFactory.Place(q.rectTransform, 0.06f, 0.48f, 0.94f, 0.92f);
            var yes = UIFactory.Button(box.transform, "DELETE", ConfirmDelete);
            UIFactory.TintButton(yes, UITheme.Danger, UITheme.Alpha(UITheme.Danger, 0.8f), UITheme.TextHi);
            UIFactory.Place(yes.image.rectTransform, 0.06f, 0.10f, 0.46f, 0.38f);
            var no = UIFactory.Button(box.transform, "CANCEL", CancelDelete, primary: false);
            UIFactory.Place(no.image.rectTransform, 0.54f, 0.10f, 0.94f, 0.38f);
            _deleteModal.gameObject.SetActive(false);
        }

        private void Update()
        {
            if (_deleting >= 0 && Input.GetKeyDown(KeyCode.Escape)) CancelDelete();
        }

        private void AskDelete(int slot)
        {
            if (SaveSystem.Instance == null || !SaveSystem.Instance.SlotExists(slot)) return;
            _deleting = slot;
            _confirm = -1;
            _deleteModal.gameObject.SetActive(true);
            _deleteModal.SetAsLastSibling();
        }

        private void ConfirmDelete()
        {
            if (_deleting >= 0 && SaveSystem.Instance != null) SaveSystem.Instance.Delete(_deleting);
            CancelDelete();
            Refresh();
        }

        private void CancelDelete()
        {
            _deleting = -1;
            if (_deleteModal != null) _deleteModal.gameObject.SetActive(false);
        }

        private void Refresh()
        {
            SaveSystem save = SaveSystem.Instance;
            _title.text = _newGame ? "Start a new game in which slot?" : "Continue which game?";
            long now = System.DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            for (int i = 0; i < SaveSystem.Slots; i++)
            {
                bool exists = save != null && save.SlotExists(i);
                RunState s = exists ? save.Peek(i) : null;
                bool corrupt = exists && s == null;
                if (s != null)
                {
                    SaveSlots.SlotSummary sum = SaveSlots.Describe(s, now);
                    _body[i].text = $"<color=#{ColorUtility.ToHtmlStringRGB(UITheme.TextHi)}>{sum.Road}</color>\n{sum.Night}\n\n" +
                                    $"<size=85%>Last played\n{sum.LastPlayed}</size>";
                }
                else _body[i].text = corrupt ? "Unreadable save." : "Empty.";

                string label = _newGame
                    ? (exists ? (_confirm == i ? "Sure? Click again" : "OVERWRITE") : "START HERE")
                    : "CONTINUE";
                UIFactory.SetButtonText(_go[i], label);
                _del[i].gameObject.SetActive(exists);   // a corrupt file can be deleted too
                bool can = _newGame || s != null;
                _go[i].interactable = can;
                // Nothing to continue here: dark, like the title's CONTINUE.
                if (can) UIFactory.TintButton(_go[i], UITheme.Candle, UITheme.CandleHot, UITheme.Ink900);
                else { UIFactory.TintButton(_go[i], UITheme.SurfaceHi, UITheme.SurfaceHi, UITheme.TextLow); var c = _go[i].colors; c.disabledColor = Color.white; _go[i].colors = c; }
            }
        }

        private void Choose(int slot)
        {
            SaveSystem save = SaveSystem.Instance;
            if (save == null || GameLoopManager.Instance == null) return;
            if (!_newGame)
            {
                if (save.Peek(slot) != null) GameLoopManager.Instance.Continue(slot);
                return;
            }
            if (save.SlotExists(slot) && _confirm != slot)
            {
                _confirm = slot;   // an existing save is only overwritten on the second click
                Refresh();
                return;
            }
            GameLoopManager.Instance.StartNewGame(slot);
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
        private TextMeshProUGUI _whisper;

        protected override void Build()
        {
            UIFactory.Box(transform, UITheme.Ground, Rt);
            _whisper = UIFactory.Label(transform, "", UITheme.SizeBody, UITheme.TextLow, TextAlignmentOptions.Center);
            _whisper.fontStyle = FontStyles.Italic;
            UIFactory.Place(_whisper.rectTransform, 0.15f, 0.32f, 0.85f, 0.39f);
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
            _title.text = $"Day {(s != null ? s.day : 1)}: {BiomeLibrary.Name(biome)}";
            _flavour.text = s != null && s.IsReplayDay ? "A road you have walked before. Half the pay, but pay all the same."
                : BiomeFlavour(biome);
            _whisper.text = Story.StoryDirector.DayWhisper(s);
            StartCoroutine(Advance());
        }

        private IEnumerator Advance()
        {
            yield return UnscaledWait.Seconds(1.8f);

            // The story's overlay: the opening, once, between the first Day Intro and
            // the first morning; and anything a fight earned that was never watched
            // (the game closed mid-scene), before today begins.
            var s = SaveSystem.Instance.State;
            var due = new System.Collections.Generic.List<Story.Cutscene>();
            if (s != null && !s.openingCinematicSeen) due.Add(Story.StoryScript.Opening);
            due.AddRange(Story.StoryDirector.Due(s));
            if (due.Count > 0)
            {
                CutsceneScreen.Play(due, () => GameLoopManager.Instance.BeginMorning());
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
        private TextMeshProUGUI _sub;
        private RectTransform _row;

        protected override void Build()
        {
            UIFactory.Box(transform, UITheme.Ground, Rt);

            var title = UIFactory.Title(transform, "Today's party", UITheme.SizeTitle, UITheme.Candle,
                TextAlignmentOptions.Center);
            UIFactory.Place(title.rectTransform, 0f, 0.78f, 1f, 0.88f);
            _sub = UIFactory.Heading(transform, "", UITheme.TextLow, UITheme.SizeSmall,
                TextAlignmentOptions.Center);
            UIFactory.Place(_sub.rectTransform, 0f, 0.74f, 1f, 0.78f);

            // One card per fighter, side by side, each with the flask they carry.
            var row = UIFactory.HStack(transform, 18f);
            row.childAlignment = TextAnchor.MiddleCenter;
            _row = (RectTransform)row.transform;
            UIFactory.Place(_row, 0.06f, 0.33f, 0.94f, 0.72f);

            var begin = UIFactory.Button(transform, "BEGIN EXPEDITION", () => GameLoopManager.Instance.BeginAfternoon());
            UIFactory.Place(begin.image.rectTransform, 0.38f, 0.18f, 0.62f, 0.27f);
        }

        protected override void OnShow()
        {
            for (int i = _row.childCount - 1; i >= 0; i--) Destroy(_row.GetChild(i).gameObject);

            RunState s = SaveSystem.Instance != null ? SaveSystem.Instance.State : null;
            var cm = Systems.CraftingManager.Instance;
            var party = s != null ? s.DeployedParty() : new System.Collections.Generic.List<HeroRecord>();
            _sub.text = party.Count == 1
                ? $"what {party[0].displayName} carries out of the shop"
                : $"{party.Count} fighters, and the flask each of them carries";

            foreach (HeroRecord hero in party)
            {
                Systems.ActiveOrder order = cm != null ? cm.CarriedBy(hero.id) : null;
                // An order with no fighter behind it (a test) belongs to the lead.
                if (order == null && hero == party[0] && cm != null && cm.Orders.Count > 0
                    && string.IsNullOrEmpty(cm.Orders[0].heroId))
                    order = cm.Orders[0];
                BuildCard(hero, order, s != null ? s.ContractFor(hero.id) : null);
            }
        }

        private void BuildCard(HeroRecord hero, Systems.ActiveOrder order, ContractRecord job)
        {
            var card = UIKit.Surface(_row, out Transform box, UITheme.Surface, UITheme.Line, "FighterCard");
            UIFactory.Flex(card.gameObject, 1f, 1f, minWidth: 180f);
            var le = card.GetComponent<UnityEngine.UI.LayoutElement>() ?? card.gameObject.AddComponent<UnityEngine.UI.LayoutElement>();
            le.preferredWidth = 300f;

            var art = UIKit.Portrait(box, Art.PixelSprites.Fighter(hero.portraitId), 96f);
            UIFactory.Place((RectTransform)art.transform.parent.parent, 0.25f, 0.54f, 0.75f, 0.96f);

            var who = UIFactory.Title(box, hero.displayName, UITheme.SizeHeading, UITheme.TextHi, TextAlignmentOptions.Center);
            UIFactory.Place(who.rectTransform, 0.04f, 0.44f, 0.96f, 0.54f);

            var flask = UIFactory.Icon(box, Art.PixelSprites.Flask(order != null ? order.element : ElementType.Poison), 40f);
            UIFactory.Place(flask.rectTransform, 0.06f, 0.20f, 0.24f, 0.42f);

            var what = UIFactory.Label(box, "", UITheme.SizeSmall, UITheme.TextHi, TextAlignmentOptions.Left, true);
            UIFactory.Place(what.rectTransform, 0.27f, 0.20f, 0.96f, 0.42f);

            var terms = UIFactory.Label(box, "", UITheme.SizeTiny, UITheme.TextLow, TextAlignmentOptions.Left);
            UIFactory.Place(terms.rectTransform, 0.06f, 0.02f, 0.96f, 0.19f);

            if (order == null)
            {
                what.text = "Raw Sludge\n<color=#d64550>NOTHING FINISHED</color>";
                terms.text = "Never ordered today — goes out with the dregs.";
                return;
            }

            var grade = order.GetGrade();
            what.text = $"{order.potionName}\n<color=#{ColorUtility.ToHtmlStringRGB(UITheme.GradeColor(grade))}>" +
                        $"{grade.ToString()}</color>  <size=85%>{order.qualityScore} / 100" +
                        (order.deliveredTo == hero.id ? " · handed over" : order.Finished ? "" : " · unfinished, not handed over") + "</size>";
            terms.text = job != null && job.accepted
                ? job.Meets(grade)
                    ? $"Wanted {job.RequiredGrade.ToString()} or better — this clears it."
                    : $"Wanted {job.RequiredGrade.ToString()} or better. This is short: half pay."
                : "";
        }
    }

    // ---------------------------------------------------------------- Biome map

    public class BiomeMapScreen : GameScreen
    {
        private RectTransform _dynamic;
        private TMPro.TextMeshProUGUI _sub;

        protected override void Build()
        {
            UIFactory.Box(transform, UITheme.Ground, Rt);

            var road = UIFactory.Title(transform, "The forest road", UITheme.SizeTitle);
            UIFactory.Place(road.rectTransform, 0f, 0.88f, 1f, 0.97f, 34f);
            _sub = UIFactory.Heading(transform, "where Rookie walks tomorrow", UITheme.TextLow);
            UIFactory.Place(_sub.rectTransform, 0f, 0.84f, 1f, 0.88f, 36f);

            _dynamic = UIFactory.Root(transform, "Dynamic");
        }

        protected override void OnShow()
        {
            for (int i = _dynamic.childCount - 1; i >= 0; i--) Destroy(_dynamic.GetChild(i).gameObject);
            var s = SaveSystem.Instance.State;
            var party = s.DeployedParty();
            if (_sub != null)
                _sub.text = (party.Count <= 1
                    ? $"where {(party.Count == 1 ? party[0].displayName : "Rookie")} walks tomorrow"
                    : $"where the party of {party.Count} walks tomorrow");

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
            UIFactory.Place(next.rectTransform, 0.66f, 0.2f, 0.95f, 0.62f);
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

            // What the road asks of the party, against what the party has.
            int fighters = s.DeployedParty().Count, tier = UpgradeCatalog.RoadTier(s);
            int wantF = BiomeLibrary.FightersNeeded[target], wantT = BiomeLibrary.RoadTierNeeded[target];
            bool ready = fighters >= wantF && tier >= wantT;
            UIFactory.Label(nextBox, BiomeLibrary.Needs(target), UITheme.SizeSmall, UITheme.TextHi);
            string going = $"{fighters} fighter{(fighters == 1 ? "" : "s")} going out";
            string line;
            if (ready) line = $"Ready: {going}, {(tier >= 2 ? "every road upgrade" : tier == 1 ? "the first road upgrades" : "nothing it needs missing")}.";
            else
            {
                var missing = new System.Collections.Generic.List<string>();
                if (fighters < wantF) missing.Add($"{wantF - fighters} more fighter{(wantF - fighters == 1 ? "" : "s")} (hire at the Evening's Party tab)");
                if (tier < wantT) missing.Add(wantT >= 2 ? "the rest of the road upgrades" : "the first road upgrades (all four)");
                line = $"Still missing: {string.Join(" and ", missing)}. Now: {going}.";
            }
            UIFactory.Label(nextBox, line, UITheme.SizeSmall, ready ? UITheme.Ok : UITheme.Danger);

            var sleep = UIFactory.Button(_dynamic, "SLEEP", () => GameLoopManager.Instance.Sleep(-1));
            UIFactory.Place(sleep.image.rectTransform, 0.80f, 0.06f, 0.95f, 0.15f);
            // Back to the Evening without sleeping: nothing spent, the day unchanged.
            var back = UIFactory.Button(_dynamic, "BACK", GoBack, primary: false);
            UIFactory.Place(back.image.rectTransform, 0.66f, 0.06f, 0.78f, 0.15f);
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.Escape)) GoBack();
        }

        private void GoBack()
        {
            if (GameLoopManager.Instance != null && GameLoopManager.Instance.Phase == GamePhase.BiomeMap)
                GameLoopManager.Instance.ReturnToEvening();
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
