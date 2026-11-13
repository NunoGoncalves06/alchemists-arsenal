using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;
using AlchemistsArsenal.Audio;
using AlchemistsArsenal.Core;
using AlchemistsArsenal.Story;

namespace AlchemistsArsenal.UI
{
    /// <summary>
    /// Plays the story (<see cref="StoryScript"/>): a letterboxed picture
    /// (<see cref="StoryStage"/>), one line at a time in a caption bar under it,
    /// typed out in time with the speaker's Animalese voice.
    ///
    /// Click, Space or Enter finishes the line, then moves on; Esc or SKIP skips
    /// the rest of the scene (it still counts as seen). Titles and credits move on
    /// by themselves. Everything runs on unscaled time; Reduce Motion shows each
    /// line whole and holds the picture still.
    ///
    /// It is an overlay, not a phase: whoever calls <see cref="Play"/> says where
    /// to go afterwards (the morning after the opening, back to the Evening after
    /// a fight earned a scene). Each scene is recorded as watched the moment it
    /// ends (<see cref="StoryDirector.Finished"/>) and saved, so quitting halfway
    /// through a chain only replays what was not yet seen.
    /// </summary>
    public class CutsceneScreen : GameScreen
    {
        /// <summary>Headless harness: lines appear whole and each shot moves on by itself once settled.</summary>
        public static bool AutoAdvance;
        public const float AutoHoldSeconds = 0.35f;
        private const float SecondsPerChar = 0.035f;

        private static CutsceneScreen _live;
        public static bool Playing => _live != null && _live._playing;

        /// <summary>"sceneid_NN" once the current shot is fully on screen (for screenshots), else null.</summary>
        public static string SettledShot =>
            _live != null && _live._playing && _live._typed && _live._stage != null && _live._stage.Settled
                ? $"{_live.Current.Id}_{_live._shot + 1:00}" : null;

        private StoryStage _stage;
        private RectTransform _captionBar, _titleBox;
        private TextMeshProUGUI _speaker, _line, _cont, _sceneTitle, _bigTitle, _bigSub;
        private Button _skip;

        private readonly List<Cutscene> _queue = new List<Cutscene>();
        private Action _onDone;
        private int _cut, _shot;
        private bool _playing, _typed;
        private float _typeT, _typedAt, _clock, _settledAt = -1f;

        private Cutscene Current => _cut < _queue.Count ? _queue[_cut] : null;
        private Shot CurrentShot => Current != null && _shot < Current.Shots.Length ? Current.Shots[_shot] : null;

        /// <summary>Play <paramref name="scenes"/> in order, then call <paramref name="onDone"/>.</summary>
        public static void Play(IReadOnlyList<Cutscene> scenes, Action onDone)
        {
            var ui = UIManager.Instance;
            var screen = ui != null ? ui.ScreenOf(ScreenId.Cutscene) as CutsceneScreen : null;
            if (screen == null || scenes == null || scenes.Count == 0) { onDone?.Invoke(); return; }
            screen._queue.Clear();
            screen._queue.AddRange(scenes);
            screen._onDone = onDone;
            ui.Show(ScreenId.Cutscene);
        }

        protected override void Build()
        {
            UIFactory.Box(transform, Color.black, Rt);

            // Clicking anywhere moves the story on (the SKIP button sits above this).
            var hit = UIFactory.Panel(transform, new Color(0f, 0f, 0f, 0f), "Advance");
            UIFactory.Stretch(hit.rectTransform);
            var advance = hit.gameObject.AddComponent<Button>();
            advance.transition = Selectable.Transition.None;
            advance.onClick.AddListener(Advance);

            var stageArea = UIFactory.Root(transform, "StageArea");
            UIFactory.Place(stageArea, 0f, 0.19f, 1f, 0.93f);
            _stage = StoryStage.Create(stageArea);

            _sceneTitle = UIFactory.Label(transform, "", UITheme.SizeSmall, UITheme.TextLow, TextAlignmentOptions.Left, true);
            _sceneTitle.characterSpacing = 6f;
            UIFactory.Place(_sceneTitle.rectTransform, 0.03f, 0.94f, 0.7f, 0.99f);

            _skip = UIFactory.Button(transform, "SKIP", Skip, primary: false);
            UIFactory.Place(_skip.image.rectTransform, 0.88f, 0.94f, 0.985f, 0.99f);

            _captionBar = UIFactory.Root(transform, "Caption");
            UIFactory.Place(_captionBar, 0.14f, 0.02f, 0.86f, 0.18f);
            _speaker = UIFactory.Label(_captionBar, "", UITheme.SizeBody, UITheme.Candle, TextAlignmentOptions.TopLeft, true);
            _speaker.characterSpacing = 4f;
            UIFactory.Place(_speaker.rectTransform, 0f, 0.72f, 1f, 1f);
            _line = UIFactory.Label(_captionBar, "", UITheme.SizeHeading + 3, UITheme.TextHi, TextAlignmentOptions.TopLeft);
            UIFactory.Place(_line.rectTransform, 0f, 0f, 0.95f, 0.72f);
            _cont = UIFactory.Label(_captionBar, "v", UITheme.SizeHeading, UITheme.Candle, TextAlignmentOptions.BottomRight, true);
            UIFactory.Place(_cont.rectTransform, 0.95f, 0f, 1f, 0.4f);

            _titleBox = UIFactory.Root(transform, "TitleCard");
            UIFactory.Place(_titleBox, 0.1f, 0.4f, 0.9f, 0.72f);
            _bigTitle = UIFactory.Title(_titleBox, "", UITheme.SizeDisplay + 14, UITheme.Candle, TextAlignmentOptions.Center);
            UIFactory.Place(_bigTitle.rectTransform, 0f, 0.4f, 1f, 1f);
            _bigSub = UIFactory.Label(_titleBox, "", UITheme.SizeHeading, UITheme.TextMid, TextAlignmentOptions.Center, true);
            _bigSub.characterSpacing = 5f;
            UIFactory.Place(_bigSub.rectTransform, 0f, 0.05f, 1f, 0.4f);
        }

        protected override void OnShow()
        {
            _live = this;
            _cut = 0;
            _playing = _queue.Count > 0;
            if (!_playing) { Finish(); return; }
            BeginScene();
        }

        protected override void OnHide()
        {
            if (_live == this && !_playing) _live = null;
        }

        private void BeginScene()
        {
            _shot = 0;
            AudioManager.PlayStory(Current.Music);
            _sceneTitle.text = Current.Id == "credits" ? "" : Current.Title.ToUpperInvariant();
            BeginShot();
        }

        private void BeginShot()
        {
            Shot s = CurrentShot;
            _stage.Play(s, seed: 97 * _cut + _shot);
            bool caption = s.Style == ShotStyle.Caption;
            _captionBar.gameObject.SetActive(caption);
            _titleBox.gameObject.SetActive(!caption);

            if (caption)
            {
                bool narration = string.IsNullOrEmpty(s.Speaker);
                _speaker.text = narration ? "" : s.Speaker.ToUpperInvariant();
                _line.text = s.Line;
                _line.fontStyle = narration ? FontStyles.Italic : FontStyles.Normal;
                _line.color = narration ? UITheme.TextMid : UITheme.TextHi;
                _line.maxVisibleCharacters = 0;
                if (!string.IsNullOrEmpty(s.Line)) AudioManager.Speak(s.Line, s.Pitch);
            }
            else
            {
                _bigTitle.text = s.Line;
                _bigSub.text = s.Sub;
                _bigTitle.fontSize = s.Style == ShotStyle.Credits ? UITheme.SizeTitle + 6 : UITheme.SizeDisplay + 14;
                _bigTitle.color = s.Style == ShotStyle.Credits ? UITheme.TextHi : UITheme.Candle;
            }

            _typeT = 0f;
            _clock = 0f;
            _settledAt = -1f;
            // A title is "in" once it has faded up; a line, once it is typed.
            _typed = caption ? (AutoAdvance || SettingsService.ReduceMotion || string.IsNullOrEmpty(s.Line))
                             : SettingsService.ReduceMotion;
            if (_typed) { _line.maxVisibleCharacters = int.MaxValue; _typedAt = 0f; }
            _cont.gameObject.SetActive(false);
        }

        private void Update()
        {
            if (!_playing || CurrentShot == null) return;
            // Keys are read below. A button left selected by a click would also take
            // Space/Enter as a Submit and move the story on twice.
            if (EventSystem.current != null && EventSystem.current.currentSelectedGameObject != null)
                EventSystem.current.SetSelectedGameObject(null);
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.1f);
            _clock += dt;
            Shot s = CurrentShot;

            if (!_typed)
            {
                _typeT += dt;
                int n = Mathf.FloorToInt(_typeT / SecondsPerChar);
                _line.maxVisibleCharacters = n;
                if (n >= s.Line.Length) { _typed = true; _typedAt = _clock; _line.maxVisibleCharacters = int.MaxValue; }
            }

            // The titles fade in; the caption's "more" mark blinks once the line is in.
            if (!_captionBar.gameObject.activeSelf)
            {
                bool instant = SettingsService.ReduceMotion;
                _bigTitle.alpha = instant ? 1f : Mathf.Clamp01(_clock / 0.8f);
                _bigSub.alpha = instant ? 1f : Mathf.Clamp01((_clock - 0.5f) / 0.8f);
                if (!_typed && _clock >= 1.3f) { _typed = true; _typedAt = _clock; }
            }
            _cont.gameObject.SetActive(_typed && s.Style == ShotStyle.Caption && s.Hold <= 0f);
            if (_cont.gameObject.activeSelf) _cont.alpha = 0.5f + 0.5f * Mathf.Sin(_clock * 5f);

            if (Input.GetKeyDown(KeyCode.Escape)) { Skip(); return; }
            if (Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.Return)) { Advance(); return; }

            // Holds count from the moment the whole shot is in (line typed, everyone
            // in place), so nothing moves on before it has been seen.
            if (_settledAt < 0f && _stage.Settled && _typed) _settledAt = _clock;
            if (_settledAt < 0f) return;
            if (s.Hold > 0f && _clock - _settledAt >= s.Hold) { Next(); return; }
            if (AutoAdvance && _clock - _settledAt >= AutoHoldSeconds) Next();
        }

        /// <summary>Finish the line if it is still typing; otherwise, the next shot.</summary>
        private void Advance()
        {
            if (!_playing) return;
            if (!_typed)
            {
                _typed = true;
                _typedAt = _clock;
                _line.maxVisibleCharacters = int.MaxValue;
                return;
            }
            Next();
        }

        private void Next()
        {
            _shot++;
            if (CurrentShot != null) { BeginShot(); return; }
            EndScene();
        }

        /// <summary>Skip the rest of this scene. It still counts as watched.</summary>
        private void Skip()
        {
            if (!_playing) return;
            EndScene();
        }

        private void EndScene()
        {
            RunState s = SaveSystem.Instance != null ? SaveSystem.Instance.State : null;
            StoryDirector.Finished(s, Current);
            if (SaveSystem.Instance != null) { SaveSystem.Instance.MarkDirty(); SaveSystem.Instance.AutoSave(); }

            _cut++;
            if (Current != null) { BeginScene(); return; }
            Finish();
        }

        private void Finish()
        {
            _playing = false;
            _queue.Clear();
            AudioManager.PlayStory(null);
            Action done = _onDone;
            _onDone = null;
            done?.Invoke();
        }
    }
}
