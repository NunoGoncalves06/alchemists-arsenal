using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using AlchemistsArsenal.Core;
using AlchemistsArsenal.Systems;
using AlchemistsArsenal.Audio;

namespace AlchemistsArsenal.UI
{
    /// <summary>
    /// Day-1 guided tutorial FSM (DESIGN.md §7.11 / eval-audio-usability): slows the
    /// morning budget, gates the Cauldron tab until the order is accepted, and walks
    /// the player Counter → Cauldron with a spotlight + coach bubble. Runs once per
    /// save (<see cref="RunState.tutorialCompleted"/>).
    /// </summary>
    public class TutorialManager : MonoBehaviour
    {
        public static TutorialManager Instance { get; private set; }
        public static bool Active { get; private set; }
        public static bool CauldronUnlocked { get; private set; } = true;

        private enum Step { Idle, Welcome, Counter, Cauldron, Done }
        private Step _step = Step.Idle;

        private Canvas _canvas;
        private CanvasGroup _group;
        private TextMeshProUGUI _bubble, _dots;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            if (GameLoopManager.Instance != null) GameLoopManager.Instance.OnPhaseChanged -= OnPhase;
        }

        private void Start()
        {
            if (GameLoopManager.Instance != null) GameLoopManager.Instance.OnPhaseChanged += OnPhase;
            BuildOverlay();
        }

        private void OnPhase(GamePhase phase)
        {
            var s = SaveSystem.Instance != null ? SaveSystem.Instance.State : null;
            if (phase == GamePhase.Morning && s != null && !s.tutorialCompleted && _step == Step.Idle)
                StartTutorial();
        }

        private void StartTutorial()
        {
            Active = true;
            CauldronUnlocked = false;
            if (GameLoopManager.Instance != null) GameLoopManager.Instance.BudgetRateMultiplier = 0.35f;
            _group.alpha = 1f;
            StartCoroutine(Run());
        }

        private static IEnumerator WaitForClickOr(float seconds)
        {
            for (float t = 0f; t < seconds; t += Time.unscaledDeltaTime)
            {
                if (Input.GetMouseButtonDown(0) && t > 0.15f) yield break; // ignore the click that opened this
                yield return null;
            }
        }

        private IEnumerator Run()
        {
            _step = Step.Welcome;
            Show("Day one. Time is paused while we get you set up.\nWe run the shop in the morning, then send Rookie out to fight in the afternoon.\n\n<size=75%>(click to continue)</size>",
                "1 / 3", new Vector2(0.5f, 0.5f));
            yield return WaitForClickOr(8f);

            _step = Step.Counter;
            Show("COUNTER tab (left). Read the incoming waves, then press ACCEPT ORDER to pick what to brew.",
                "2 / 3", new Vector2(0.06f, 0.55f));
            while (CraftingManager.Instance == null || CraftingManager.Instance.CurrentOrder == null)
                yield return null;

            CauldronUnlocked = true;
            _step = Step.Cauldron;
            Show("CAULDRON tab. Hold the mouse over the pot and stir in circles — keep the gauge in the GREEN and the BREW bar fills. When it's READY, send it.",
                "3 / 3", new Vector2(0.4f, 0.45f));
            while (true)
            {
                var pot = Crafting.PhysicsCauldronManager.Instance;
                var o = CraftingManager.Instance != null ? CraftingManager.Instance.CurrentOrder : null;
                if (pot != null && pot.BrewProgress01 >= 0.4f) break;
                if (o != null && o.qualityScore >= 55) break;
                yield return null;
            }

            _step = Step.Done;
            Show("That's the loop. Finish the brew if you like, then SEND TO EXPEDITION.\nTime runs at normal speed from tomorrow.\n\n<size=75%>(click to continue)</size>",
                "done", new Vector2(0.85f, 0.35f));
            AudioManager.Play(Sfx.Chime);
            yield return WaitForClickOr(8f);

            Finish();
        }

        private void Finish()
        {
            Active = false;
            CauldronUnlocked = true;
            if (GameLoopManager.Instance != null) GameLoopManager.Instance.BudgetRateMultiplier = 1f;
            var s = SaveSystem.Instance != null ? SaveSystem.Instance.State : null;
            if (s != null) { s.tutorialCompleted = true; SaveSystem.Instance.MarkDirty(); }
            _group.alpha = 0f;
        }

        // ------------------------------------------------------------- overlay

        // The coach bubble is docked to the BOTTOM of the screen and kept short, so
        // it never sits over the Counter / Cauldron working area (playtest note).
        // A bobbing arrow points at whatever the current step is about.

        private void BuildOverlay()
        {
            var go = new GameObject("TutorialCanvas", typeof(RectTransform));
            go.transform.SetParent(transform, false);
            _canvas = go.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 200;
            go.AddComponent<GraphicRaycaster>();
            _group = go.AddComponent<CanvasGroup>();
            _group.alpha = 0f;
            _group.blocksRaycasts = false; // never eat clicks — the player still runs the shop

            _arrow = UIFactory.Panel(go.transform, UITheme.Candle, "Arrow").rectTransform;
            var ai = _arrow.GetComponent<Image>();
            ai.sprite = Combat.PlaceholderArt.Make(Combat.PlaceholderArt.Shape.Diamond, UITheme.Candle, UITheme.Ink900);
            ai.raycastTarget = false;
            _arrow.sizeDelta = new Vector2(40, 40);

            var bubble = UIFactory.Panel(go.transform, UITheme.Parchment, "Coach");
            _coach = bubble.rectTransform;
            _coach.anchorMin = new Vector2(0.18f, 0.02f);
            _coach.anchorMax = new Vector2(0.82f, 0.16f);
            _coach.offsetMin = _coach.offsetMax = Vector2.zero;
            bubble.raycastTarget = false;

            _bubble = UIFactory.Label(bubble.transform, "", 17, UITheme.Ink900, TextAlignmentOptions.Left);
            UIFactory.Stretch(_bubble.rectTransform, 16f);
            _dots = UIFactory.Label(bubble.transform, "", 12, UITheme.WoodDark, TextAlignmentOptions.BottomRight);
            UIFactory.Stretch(_dots.rectTransform, 8f);
        }

        private RectTransform _coach, _arrow;
        private Vector2 _pointAt = new Vector2(0.5f, 0.5f);

        private void Show(string text, string dots, Vector2 pointAt)
        {
            _bubble.text = text;
            _dots.text = dots;
            _pointAt = pointAt;
            _arrow.anchorMin = _arrow.anchorMax = pointAt;
        }

        private void Update()
        {
            if (_group == null || _group.alpha < 0.5f || _arrow == null) return;
            float bob = Mathf.Sin(Time.unscaledTime * 5f) * 7f;
            _arrow.anchoredPosition = new Vector2(0f, bob);
        }
    }
}
