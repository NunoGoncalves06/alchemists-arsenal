using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using AlchemistsArsenal.Core;
using AlchemistsArsenal.Combat;
using AlchemistsArsenal.Story;

namespace AlchemistsArsenal.UI
{
    /// <summary>
    /// The visual diary (DESIGN.md §7.9.4). Phase 0: a two-page book, prev/next
    /// through unlocked entries, a procedural cutscene illustration (no authored
    /// frames yet), and animated text. Also plays the opening cinematic
    /// (<c>diary_00</c>) after the first Day Intro.
    /// </summary>
    public class DiaryScreen : GameScreen
    {
        public static string OpenEntryId;
        /// <summary>Set by the Day-Intro card so CLOSE returns to the morning, not the Evening.</summary>
        public static bool FromOpeningCinematic;

        private RectTransform _cut, _proceduralScene;
        private Image _frameImg;
        private Coroutine _frameAnim;
        private TextMeshProUGUI _title, _text, _pageOf;
        private Image[] _sketchLayers;
        private List<string> _ids = new List<string>();
        private int _index;
        private Coroutine _typing;
        private string _fullText = "";

        protected override void Build()
        {
            UIFactory.Box(transform, UITheme.Ink900, Rt);
            var book = UIFactory.Panel(transform, UITheme.WoodDark, "Book");
            var brt = book.rectTransform;
            brt.anchorMin = new Vector2(0.08f, 0.1f); brt.anchorMax = new Vector2(0.92f, 0.9f);
            brt.offsetMin = brt.offsetMax = Vector2.zero;

            var left = UIFactory.Panel(book.transform, UITheme.Parchment, "LeftPage");
            left.rectTransform.anchorMin = new Vector2(0.02f, 0.04f); left.rectTransform.anchorMax = new Vector2(0.49f, 0.96f);
            left.rectTransform.offsetMin = left.rectTransform.offsetMax = Vector2.zero;
            _cut = UIFactory.Rect(left.transform, "Cut", new Vector2(0.06f, 0.06f), new Vector2(0.94f, 0.94f), Vector2.zero, Vector2.zero);
            UIFactory.Box(_cut, new Color(0.11f, 0.15f, 0.20f), _cut);

            // Authored frame (shown when DiaryEntryData.cutsceneFrames is non-empty — reviewer G1)
            _frameImg = UIFactory.Panel(_cut, Color.white, "Frame");
            UIFactory.Stretch(_frameImg.rectTransform);
            _frameImg.type = Image.Type.Simple;   // authored frames are whole sprites, not 9-slice
            _frameImg.preserveAspect = true;
            _frameImg.gameObject.SetActive(false);

            _proceduralScene = UIFactory.Root(_cut, "Procedural");
            BuildProceduralScene(_proceduralScene);

            var right = UIFactory.Panel(book.transform, UITheme.Parchment, "RightPage");
            right.rectTransform.anchorMin = new Vector2(0.51f, 0.04f); right.rectTransform.anchorMax = new Vector2(0.98f, 0.96f);
            right.rectTransform.offsetMin = right.rectTransform.offsetMax = Vector2.zero;
            _title = UIFactory.Label(right.transform, "", 20, new Color(0.17f, 0.13f, 0.09f), TextAlignmentOptions.TopLeft, true);
            _title.rectTransform.anchorMin = new Vector2(0.08f, 0.86f); _title.rectTransform.anchorMax = new Vector2(0.92f, 0.98f);
            _title.rectTransform.offsetMin = _title.rectTransform.offsetMax = Vector2.zero;
            _text = UIFactory.Label(right.transform, "", 16, new Color(0.2f, 0.15f, 0.1f), TextAlignmentOptions.TopLeft);
            _text.rectTransform.anchorMin = new Vector2(0.08f, 0.36f); _text.rectTransform.anchorMax = new Vector2(0.92f, 0.85f);
            _text.rectTransform.offsetMin = _text.rectTransform.offsetMax = Vector2.zero;

            var nav = UIFactory.HStack(transform, 10f);
            var nrt = (RectTransform)nav.transform;
            nrt.anchorMin = new Vector2(0.3f, 0.02f); nrt.anchorMax = new Vector2(0.7f, 0.09f);
            nrt.offsetMin = nrt.offsetMax = Vector2.zero;
            Btn(nav.transform, "◄ PREV", () => Step(-1));
            _pageOf = UIFactory.Label(nav.transform, "", 16, UITheme.ParchmentDim, TextAlignmentOptions.Center);
            _pageOf.gameObject.AddComponent<LayoutElement>().minWidth = 80;
            Btn(nav.transform, "NEXT ►", () => Step(1));
            Btn(nav.transform, "CLOSE", Close);
        }

        private void Btn(Transform p, string t, System.Action a)
        {
            var b = UIFactory.Button(p, t, a, primary: false);
            b.gameObject.AddComponent<LayoutElement>().minWidth = 110;
        }

        private void BuildProceduralScene(RectTransform parent)
        {
            // flat blocks: moon, hill, witch, cursed loved one, curse ring, + a 5-layer boss sketch
            Block(parent, new Vector2(0.7f, 0.72f), new Vector2(0.85f, 0.9f), new Color(0.9f, 0.88f, 0.78f));
            Block(parent, new Vector2(0f, 0f), new Vector2(1f, 0.28f), new Color(0.15f, 0.2f, 0.12f));
            Block(parent, new Vector2(0.12f, 0.22f), new Vector2(0.22f, 0.6f), UITheme.Witch);
            Block(parent, new Vector2(0.62f, 0.2f), new Vector2(0.72f, 0.5f), new Color(0.42f, 0.44f, 0.33f));
            var ring = Block(parent, new Vector2(0.56f, 0.22f), new Vector2(0.8f, 0.55f), new Color(0f, 0f, 0f, 0f));
            var ri = ring.GetComponent<Image>();
            ri.sprite = PlaceholderArt.Make(PlaceholderArt.Shape.Star, new Color(UITheme.Arcane.r, UITheme.Arcane.g, UITheme.Arcane.b, 0.5f), UITheme.Arcane);

            _sketchLayers = new Image[5];
            for (int i = 0; i < 5; i++)
            {
                var s = Block(parent, new Vector2(0.34f + i * 0.02f, 0.30f), new Vector2(0.5f + i * 0.02f, 0.62f - i * 0.03f),
                    new Color(0.85f, 0.78f, 0.62f, 0.9f));
                _sketchLayers[i] = s.GetComponent<Image>();
                _sketchLayers[i].gameObject.SetActive(false);
            }
        }

        private RectTransform Block(RectTransform parent, Vector2 min, Vector2 max, Color c)
        {
            var img = UIFactory.Panel(parent, c, "B");
            img.rectTransform.anchorMin = min; img.rectTransform.anchorMax = max;
            img.rectTransform.offsetMin = img.rectTransform.offsetMax = Vector2.zero;
            return img.rectTransform;
        }

        protected override void OnShow()
        {
            var s = SaveSystem.Instance.State;
            _ids = new List<string>();
            foreach (var e in DiaryManager.All)
                if (s.HasDiary(e.id)) _ids.Add(e.id);

            if (!string.IsNullOrEmpty(OpenEntryId) && _ids.Contains(OpenEntryId))
                _index = _ids.IndexOf(OpenEntryId);
            else
                _index = Mathf.Max(0, _ids.Count - 1);

            // boss sketch grows with biomes cleared
            int cleared = 0;
            foreach (int g in s.bestGrades) if (g > 0) cleared++;
            if (_sketchLayers != null)
                for (int i = 0; i < _sketchLayers.Length; i++)
                    _sketchLayers[i].gameObject.SetActive(i < cleared);

            Render();
        }

        protected override void OnHide()
        {
            if (_typing != null) StopCoroutine(_typing);
        }

        private void Step(int d)
        {
            if (_ids.Count == 0) return;
            _index = Mathf.Clamp(_index + d, 0, _ids.Count - 1);
            Render();
        }

        private void Render()
        {
            if (_ids.Count == 0)
            {
                _title.text = "The diary is blank.";
                _text.text = "";
                _pageOf.text = "0/0";
                return;
            }
            var entry = DiaryManager.Get(_ids[_index]);
            _title.text = entry.entryTitle;
            _pageOf.text = $"{_index + 1}/{_ids.Count}";
            if (_typing != null) StopCoroutine(_typing);
            _typing = StartCoroutine(TypeOut(entry.entryText));

            // Authored frames take over the illustration when present.
            if (_frameAnim != null) { StopCoroutine(_frameAnim); _frameAnim = null; }
            bool hasFrames = entry.cutsceneFrames != null && entry.cutsceneFrames.Length > 0;
            _frameImg.gameObject.SetActive(hasFrames);
            _proceduralScene.gameObject.SetActive(!hasFrames);
            if (hasFrames)
                _frameAnim = StartCoroutine(PlayFrames(entry.cutsceneFrames, Mathf.Max(0.5f, entry.frameRate)));
        }

        private IEnumerator PlayFrames(Sprite[] frames, float fps)
        {
            var wait = new WaitForSecondsRealtime(1f / fps);
            int i = 0;
            while (true)
            {
                _frameImg.sprite = frames[i % frames.Length];
                i++;
                if (i >= frames.Length && SettingsService.ReduceMotion) yield break; // hold the last frame
                yield return wait;
            }
        }

        private void Update()
        {
            // Click anywhere while typing → snap the page to full text (reviewer P15).
            if (_typing != null && Input.GetMouseButtonDown(0))
            {
                StopCoroutine(_typing);
                _typing = null;
                _text.text = _fullText;
            }
        }

        private IEnumerator TypeOut(string full)
        {
            _fullText = full;
            if (SettingsService.ReduceMotion) { _text.text = full; _typing = null; yield break; }
            _text.text = "";
            var wait = new WaitForSecondsRealtime(0.012f);
            for (int i = 0; i < full.Length; i++)
            {
                _text.text += full[i];
                if (i % 2 == 0) yield return wait;
            }
            _typing = null;
        }

        private void Close()
        {
            OpenEntryId = null;
            if (FromOpeningCinematic)
            {
                FromOpeningCinematic = false;
                GameLoopManager.Instance.BeginMorning();
            }
            else
            {
                UIManager.Instance.Show(ScreenId.Evening);
            }
        }
    }
}
