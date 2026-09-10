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
        private RectTransform _spotlight;

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
            Show("Day one. Time moves slowly today — take it in.\nStart at the Counter on the left.\n\n<size=70%>(click to continue)</size>", "1 / 3",
                new Vector2(0.16f, 0.5f), new Vector2(300, 480));
            yield return WaitForClickOr(6f);

            _step = Step.Counter;
            Show("Read the incoming waves, then press ACCEPT to choose what to brew.", "2 / 3",
                new Vector2(0.4f, 0.28f), new Vector2(760, 120));
            while (CraftingManager.Instance == null || CraftingManager.Instance.CurrentOrder == null)
                yield return null;

            CauldronUnlocked = true;
            _step = Step.Cauldron;
            Show("Now the Cauldron. Move the mouse in circles over the pot and keep the needle in the GREEN — quality climbs while you hold it.",
                "3 / 3", new Vector2(0.4f, 0.5f), new Vector2(560, 260));
            float held = 0f;
            while (held < 4f)
            {
                var o = CraftingManager.Instance != null ? CraftingManager.Instance.CurrentOrder : null;
                held += (o != null && o.qualityScore > ActiveOrder.StartingQuality + 8) ? Time.unscaledDeltaTime * 2f : 0f;
                if (o != null && o.qualityScore >= 60) break;
                yield return null;
            }

            _step = Step.Done;
            Show("You've got it. Seal it and send Rookie off. Time runs normal from tomorrow.\n\n<size=70%>(click to continue)</size>", "done",
                new Vector2(0.85f, 0.5f), new Vector2(300, 400));
            AudioManager.Play(Sfx.Chime);
            yield return WaitForClickOr(6f);

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
            _group.blocksRaycasts = false;

            var scrim = UIFactory.Panel(go.transform, new Color(0.05f, 0.03f, 0.06f, 0.55f), "Scrim");
            UIFactory.Stretch(scrim.rectTransform);
            scrim.raycastTarget = false;

            _spotlight = UIFactory.Panel(go.transform, new Color(0f, 0f, 0f, 0f), "Spot").rectTransform;
            var so = _spotlight.GetComponent<Image>();
            so.sprite = Combat.PlaceholderArt.Make(Combat.PlaceholderArt.Shape.Disc, new Color(0f, 0f, 0f, 0f), UITheme.Candle);
            so.raycastTarget = false;

            _arrow = UIFactory.Panel(go.transform, UITheme.Candle, "Arrow").rectTransform;
            var ai = _arrow.GetComponent<Image>();
            ai.sprite = Combat.PlaceholderArt.Make(Combat.PlaceholderArt.Shape.Diamond, UITheme.Candle, UITheme.Ink900);
            ai.raycastTarget = false;
            _arrow.sizeDelta = new Vector2(48, 48);

            var bubble = UIFactory.Panel(go.transform, UITheme.Parchment, "Coach");
            _coach = bubble.rectTransform;
            _bubble = UIFactory.Label(bubble.transform, "", 18, UITheme.Ink900, TextAlignmentOptions.TopLeft);
            UIFactory.Stretch(_bubble.rectTransform, 14f);
            _dots = UIFactory.Label(bubble.transform, "", 12, UITheme.WoodDark, TextAlignmentOptions.BottomRight);
            UIFactory.Stretch(_dots.rectTransform, 8f);
        }

        private RectTransform _coach, _arrow;
        private Vector2 _arrowAnchor;

        private void Show(string text, string dots, Vector2 anchorCenter, Vector2 size)
        {
            _bubble.text = text;
            _dots.text = dots;
            _coach.anchorMin = _coach.anchorMax = anchorCenter;
            _coach.sizeDelta = size;
            _coach.anchoredPosition = Vector2.zero;
            _spotlight.anchorMin = _spotlight.anchorMax = anchorCenter;
            _spotlight.sizeDelta = size * 1.4f;

            // Arrow sits just off the spotlight, nudged back toward screen centre so
            // it reads as "look here".
            Vector2 toCentre = (new Vector2(0.5f, 0.5f) - anchorCenter);
            _arrowAnchor = anchorCenter + toCentre.normalized * 0.08f;
            _arrow.anchorMin = _arrow.anchorMax = _arrowAnchor;
        }

        private void Update()
        {
            if (_group == null || _group.alpha < 0.5f || _arrow == null) return;
            float bob = Mathf.Sin(Time.unscaledTime * 6f) * 8f;
            _arrow.anchoredPosition = new Vector2(0f, bob);
        }
    }
}
