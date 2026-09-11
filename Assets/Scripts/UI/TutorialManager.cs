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
    /// morning budget, gates the Cauldron/Prep/Bottling tabs until the Counter order
    /// is accepted, and walks the player Counter → Cauldron with a top-docked coach
    /// bubble + pointer arrow. Runs once per save (<see cref="RunState.tutorialCompleted"/>).
    /// </summary>
    public class TutorialManager : MonoBehaviour
    {
        public static TutorialManager Instance { get; private set; }
        public static bool Active { get; private set; }

        /// <summary>Gates the Cauldron/Prep/Bottling tabs until the Day-1 Counter
        /// order is accepted (there's nothing for them to act on before that).</summary>
        public static bool StationsUnlocked { get; private set; } = true;

        private enum Step { Idle, Welcome, Counter, Cauldron, Done }
        private Step _step = Step.Idle;
        private Coroutine _runCoroutine;

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
            {
                StartTutorial();
            }
            else if (Active && phase != GamePhase.Morning)
            {
                // The player moved faster than the tutorial's own pacing — accepted
                // the order and hit SEND before the Cauldron/Done steps finished on
                // their own. Without this, the overlay (sorting order 200, above the
                // game UI's 100) stayed visible at full alpha straight through
                // Handoff and into the fight: stale coach-bubble text and a bobbing
                // arrow floating over combat (playtest: "combat is all fucked").
                CancelTutorial();
            }
        }

        private void StartTutorial()
        {
            Active = true;
            StationsUnlocked = false;
            if (GameLoopManager.Instance != null) GameLoopManager.Instance.BudgetRateMultiplier = 0.35f;
            _group.alpha = 1f;
            _runCoroutine = StartCoroutine(Run());
        }

        private void CancelTutorial()
        {
            if (_runCoroutine != null) { StopCoroutine(_runCoroutine); _runCoroutine = null; }
            Finish();
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

            StationsUnlocked = true;
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
                "done", new Vector2(0.86f, 0.14f));
            AudioManager.Play(Sfx.Chime);
            yield return WaitForClickOr(8f);

            Finish();
        }

        private void Finish()
        {
            _runCoroutine = null;
            _step = Step.Idle; // so a later NEW GAME (fresh save) can re-trigger its own tutorial
            Active = false;
            StationsUnlocked = true;
            if (GameLoopManager.Instance != null) GameLoopManager.Instance.BudgetRateMultiplier = 1f;
            var s = SaveSystem.Instance != null ? SaveSystem.Instance.State : null;
            if (s != null) { s.tutorialCompleted = true; SaveSystem.Instance.MarkDirty(); }
            _group.alpha = 0f;
        }

        // ------------------------------------------------------------- overlay

        // The coach bubble is docked to the TOP of the screen, right under the top
        // bar — every interactive control in the Morning screen (tabs on the far
        // left rail, ACCEPT/SEND/SEAL buttons near the bottom) lives well below this
        // strip, so the bubble can never sit on top of something the player needs to
        // click (playtest note: it previously did, twice). A small downward-pointing
        // arrow sits just above whatever the current step is about.

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

            _arrow = UIFactory.Panel(go.transform, new Color(0f, 0f, 0f, 0f), "Arrow").rectTransform;
            var ai = _arrow.GetComponent<Image>();
            ai.sprite = Art.PixelSprites.PointerArrow();
            ai.color = Color.white; // sprite is already tinted candle-gold
            ai.raycastTarget = false;
            _arrow.sizeDelta = new Vector2(36, 20);

            // Kept within x <= 0.70: the Morning screen's Order Dock starts at 0.72,
            // and the bubble previously reached to 0.86 — overlapping the dock's
            // Ticket panel and garbling its text behind the bubble (playtest
            // screenshot review).
            var bubble = UIFactory.Panel(go.transform, UITheme.Parchment, "Coach");
            _coach = bubble.rectTransform;
            _coach.anchorMin = new Vector2(0.10f, 0.79f);
            _coach.anchorMax = new Vector2(0.70f, 0.915f);
            _coach.offsetMin = _coach.offsetMax = Vector2.zero;
            bubble.raycastTarget = false;

            _bubble = UIFactory.Label(bubble.transform, "", 17, UITheme.Ink900, TextAlignmentOptions.Left);
            UIFactory.Stretch(_bubble.rectTransform, 16f);
            _dots = UIFactory.Label(bubble.transform, "", 12, UITheme.WoodDark, TextAlignmentOptions.BottomRight);
            UIFactory.Stretch(_dots.rectTransform, 8f);
        }

        private RectTransform _coach, _arrow;

        private void Show(string text, string dots, Vector2 pointAt)
        {
            _bubble.text = text;
            _dots.text = dots;
            _arrow.anchorMin = _arrow.anchorMax = pointAt;
        }

        private void Update()
        {
            if (_group == null || _group.alpha < 0.5f || _arrow == null) return;
            // Sits above the target and bobs, tip pointing down at it — never on it.
            float bob = Mathf.Sin(Time.unscaledTime * 5f) * 6f;
            _arrow.anchoredPosition = new Vector2(0f, 30f + bob);
        }
    }
}
