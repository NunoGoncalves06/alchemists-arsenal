using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using AlchemistsArsenal.Core;

namespace AlchemistsArsenal.UI
{
    public enum ScreenId
    {
        Boot, MainMenu, Settings, DayIntro, Morning, Handoff, ExpeditionHud, Evening, Diary, BiomeMap
    }

    /// <summary>
    /// The screen router (DESIGN.md §6). One persistent Screen-Space canvas; each
    /// screen is a child <see cref="GameScreen"/> toggled with SetActive. Follows
    /// <see cref="GameLoopManager.OnPhaseChanged"/> for the phase-driven screens;
    /// <see cref="Show"/> is called directly for sub-screens (Diary, Settings).
    /// </summary>
    public class UIManager : MonoBehaviour
    {
        public static UIManager Instance { get; private set; }

        public Canvas Canvas { get; private set; }
        public ScreenId Current { get; private set; }

        private readonly Dictionary<ScreenId, GameScreen> _screens = new Dictionary<ScreenId, GameScreen>();

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            BuildCanvas();
            RegisterScreens();
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            if (GameLoopManager.Instance != null)
                GameLoopManager.Instance.OnPhaseChanged -= HandlePhase;
        }

        private void Start()
        {
            if (GameLoopManager.Instance != null)
                GameLoopManager.Instance.OnPhaseChanged += HandlePhase;
            Show(ScreenId.Boot);
        }

        private void BuildCanvas()
        {
            var canvasGo = new GameObject("UICanvas", typeof(RectTransform));
            canvasGo.transform.SetParent(transform, false);
            Canvas = canvasGo.AddComponent<Canvas>();
            Canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            Canvas.sortingOrder = 100;

            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            canvasGo.AddComponent<GraphicRaycaster>();

            if (EventSystem.current == null)
            {
                var es = new GameObject("EventSystem", typeof(EventSystem));
                es.AddComponent<StandaloneInputModule>();
                es.transform.SetParent(transform, false);
            }
        }

        private void RegisterScreens()
        {
            Register(ScreenId.Boot,        typeof(BootScreen));
            Register(ScreenId.MainMenu,    typeof(MainMenuScreen));
            Register(ScreenId.Settings,    typeof(SettingsScreen));
            Register(ScreenId.DayIntro,    typeof(DayIntroScreen));
            Register(ScreenId.Morning,     typeof(MorningScreen));
            Register(ScreenId.Handoff,     typeof(HandoffScreen));
            Register(ScreenId.ExpeditionHud, typeof(ExpeditionHudScreen));
            Register(ScreenId.Evening,     typeof(EveningScreen));
            Register(ScreenId.Diary,       typeof(DiaryScreen));
            Register(ScreenId.BiomeMap,    typeof(BiomeMapScreen));
        }

        private void Register(ScreenId id, Type screenType)
        {
            var go = new GameObject(id + "Screen", typeof(RectTransform));
            go.transform.SetParent(Canvas.transform, false);
            UIFactory.Stretch((RectTransform)go.transform);
            var screen = (GameScreen)go.AddComponent(screenType);
            screen.Id = id;
            go.SetActive(false);
            _screens[id] = screen;
        }

        public void Show(ScreenId id)
        {
            foreach (var kv in _screens)
            {
                bool on = kv.Key == id;
                if (kv.Value.gameObject.activeSelf != on)
                {
                    kv.Value.gameObject.SetActive(on);
                    if (on) kv.Value.NotifyShown(); else kv.Value.NotifyHidden();
                }
                else if (on)
                {
                    kv.Value.NotifyShown();
                }
            }
            Current = id;
        }

        public GameScreen ScreenOf(ScreenId id) => _screens.TryGetValue(id, out var s) ? s : null;

        private void HandlePhase(GamePhase phase)
        {
            switch (phase)
            {
                case GamePhase.MainMenu: Show(ScreenId.MainMenu); break;
                case GamePhase.DayIntro: Show(ScreenId.DayIntro); break;
                case GamePhase.Morning:  Show(ScreenId.Morning); break;
                case GamePhase.Handoff:  Show(ScreenId.Handoff); break;
                case GamePhase.Afternoon: Show(ScreenId.ExpeditionHud); break;
                case GamePhase.Evening:  Show(ScreenId.Evening); break;
                case GamePhase.BiomeMap: Show(ScreenId.BiomeMap); break;
            }
        }
    }

    /// <summary>Base for every screen. Builds its UI once, lazily, on first show.</summary>
    public abstract class GameScreen : MonoBehaviour
    {
        public ScreenId Id { get; internal set; }
        private bool _built;

        internal void NotifyShown()
        {
            if (!_built) { Build(); _built = true; }
            OnShow();
        }

        internal void NotifyHidden() => OnHide();

        protected abstract void Build();
        protected virtual void OnShow() { }
        protected virtual void OnHide() { }

        protected RectTransform Rt => (RectTransform)transform;
    }
}
