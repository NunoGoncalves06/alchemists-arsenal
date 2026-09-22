using UnityEngine;
using TMPro;
using AlchemistsArsenal.Core;
using AlchemistsArsenal.Story;

namespace AlchemistsArsenal.UI
{
    /// <summary>
    /// "New diary page": a small card that slides in at the top right whenever a
    /// page unlocks, over whichever screen is up, and slides away again. Pages used
    /// to unlock in silence, so a clue earned on the road could sit unread in the
    /// diary for the rest of the run. Listens to <see cref="DiaryManager.OnEntryUnlocked"/>
    /// (which nothing listened to). Reduce Motion skips the slide.
    /// </summary>
    public class UnlockToast : MonoBehaviour
    {
        public const float Seconds = 3.4f;

        private RectTransform _card;
        private CanvasGroup _group;
        private TextMeshProUGUI _title;
        private float _t = -1f;

        public static UnlockToast Create(Transform canvas)
        {
            var root = UIFactory.Root(canvas, "UnlockToast");
            var t = root.gameObject.AddComponent<UnlockToast>();
            t.Build(root);
            return t;
        }

        private void Build(RectTransform root)
        {
            var card = UIFactory.Panel(root, UITheme.Alpha(UITheme.Parchment, 0.97f), "Card");
            _card = card.rectTransform;
            UIFactory.Place(_card, 0.72f, 0.80f, 0.985f, 0.885f);
            card.raycastTarget = false;
            _group = card.gameObject.AddComponent<CanvasGroup>();
            _group.blocksRaycasts = false;
            _group.interactable = false;

            var edge = UIFactory.Panel(card.transform, UITheme.Candle, "Edge");
            UIFactory.Place(edge.rectTransform, 0f, 0f, 0.015f, 1f);
            edge.raycastTarget = false;
            var head = UIFactory.Label(card.transform, "NEW DIARY PAGE", UITheme.SizeTiny, UITheme.WoodDark, TextAlignmentOptions.TopLeft, true);
            head.characterSpacing = 4f;
            UIFactory.Place(head.rectTransform, 0.05f, 0.55f, 0.98f, 0.92f);
            _title = UIFactory.Label(card.transform, "", UITheme.SizeBody, new Color(0.17f, 0.13f, 0.09f), TextAlignmentOptions.TopLeft, true);
            UIFactory.Place(_title.rectTransform, 0.05f, 0.08f, 0.98f, 0.58f);
            _group.alpha = 0f;
        }

        private void OnEnable() => DiaryManager.OnEntryUnlocked += OnUnlocked;
        private void OnDisable() => DiaryManager.OnEntryUnlocked -= OnUnlocked;

        private void OnUnlocked(DiaryEntryData entry)
        {
            if (entry == null || _title == null) return;
            // Only pages of the run being played: the event is global, and a test or
            // tool working on its own RunState must not announce pages to the player.
            RunState live = SaveSystem.Instance != null ? SaveSystem.Instance.State : null;
            if (live == null || !live.HasDiary(entry.id)) return;
            _title.text = entry.entryTitle;
            _t = 0f;
            transform.SetAsLastSibling();   // over whatever screen is up
        }

        private void Update()
        {
            if (_t < 0f) return;
            // Game time (so it is photographed the same under the harness), or wall
            // time if the game happens to be paused, so it never hangs on screen.
            float dt = Time.timeScale > 0f ? Time.deltaTime : Time.unscaledDeltaTime;
            _t += Mathf.Min(dt, 0.1f);
            float a = Mathf.Min(Mathf.Clamp01(_t / 0.25f), Mathf.Clamp01((Seconds - _t) / 0.5f));
            _group.alpha = a;
            float slide = SettingsService.ReduceMotion ? 0f : (1f - Mathf.Clamp01(_t / 0.3f)) * 60f;
            _card.anchoredPosition = new Vector2(slide, 0f);
            if (_t >= Seconds) { _t = -1f; _group.alpha = 0f; }
        }
    }
}
