using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;

namespace AlchemistsArsenal.UI
{
    /// <summary>
    /// The tutorial's pointer: the screen goes dark except a soft circle over the
    /// one thing to touch next (round for a thing in the shop, an ellipse hugging a
    /// wide piece of UI such as a button). Each new lesson the circle starts wide at the centre
    /// of the screen and closes in on its target, then a gold ring pulses round it.
    /// It follows a moving target (a leaf in the hand, a carried cork).
    ///
    /// <para>Only the circle takes clicks; the dark stops them, UI and bench alike
    /// (the benches ignore a press while the pointer is over UI, see
    /// <c>Pointer.OverUI</c>). Three clicks on the dark let the player out of the
    /// lesson, so a lesson can never trap anyone.</para>
    ///
    /// <para>Everything is worked out in screen pixels (where the targets are) and
    /// then laid out as fractions of the canvas, so it lines up at any resolution,
    /// and in the harness, which re-renders every canvas at its own size for a
    /// screenshot.</para>
    /// </summary>
    public class Spotlight : MonoBehaviour
    {
        private const float Dim = 0.72f;
        private const float IntroSeconds = 0.9f;
        private const int Tex = 128;

        public event Action SkipRequested;

        private RectTransform _root, _hole, _ring;
        private Canvas _canvas;
        private readonly Image[] _dark = new Image[4];
        private Image _holeImg, _ringImg;
        private CanvasGroup _group;
        private UIKit.CaptionView _caption;
        private RectTransform _captionRt;

        private Func<Vector2?> _target;
        private Func<Vector2> _radius;
        private Func<string> _text;
        private float _introT = 1f, _fade;
        private int _darkClicks;
        private Vector2 _centre, _vel;
        private Vector2 _r;   // radii in x and y, pixels

        public bool Showing => _target != null;
        /// <summary>A target is on screen and the dark is up (the clock slows for this).</summary>
        public bool Visible => _group != null && _group.alpha > 0.05f;

        public static Spotlight Create(Transform parent, int sortingOrder)
        {
            var go = new GameObject("Spotlight", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = sortingOrder;
            var scaler = go.AddComponent<CanvasScaler>();   // the caption scales like the rest of the UI
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            go.AddComponent<GraphicRaycaster>();
            var s = go.AddComponent<Spotlight>();
            s.Build();
            return s;
        }

        private void Build()
        {
            _root = (RectTransform)transform;
            _canvas = GetComponent<Canvas>();
            _group = gameObject.AddComponent<CanvasGroup>();
            _group.alpha = 0f;
            _group.blocksRaycasts = false;

            for (int i = 0; i < 4; i++)
            {
                _dark[i] = Corner(UIFactory.Panel(_root, new Color(0f, 0f, 0f, Dim), "Dark" + i));
                _dark[i].gameObject.AddComponent<DarkClick>().Owner = this;
            }

            _holeImg = Corner(UIFactory.Panel(_root, new Color(0f, 0f, 0f, Dim), "Hole"));
            _holeImg.sprite = Disc(soft: true);
            _hole = _holeImg.rectTransform;
            _hole.gameObject.AddComponent<HoleFilter>().Owner = this;
            _hole.gameObject.AddComponent<DarkClick>().Owner = this;

            _ringImg = Corner(UIFactory.Panel(_root, UITheme.CandleHot, "Ring"));
            _ringImg.sprite = Disc(soft: false);
            _ringImg.raycastTarget = false;
            _ring = _ringImg.rectTransform;

            _caption = UIKit.Caption(_root, "SpotCaption");
            _captionRt = _caption.Root.rectTransform;
            _captionRt.anchorMin = _captionRt.anchorMax = Vector2.zero;
            _captionRt.pivot = new Vector2(0.5f, 0.5f);
            _captionRt.sizeDelta = new Vector2(560f, 58f);
            _caption.Text.fontSizeMax = UITheme.SizeBody + 2;
            _caption.Text.alignment = TextAlignmentOptions.Center;
        }

        private static Image Corner(Image img)
        {
            RectTransform rt = img.rectTransform;
            rt.anchorMin = rt.anchorMax = Vector2.zero;
            rt.pivot = Vector2.zero;
            return img;
        }

        /// <summary>
        /// Light up <paramref name="target"/> (a screen point, or null while it is not
        /// on screen) with a circle of <paramref name="radius"/> pixels, and say
        /// <paramref name="text"/> beside it. Restarts the closing-in animation.
        /// </summary>
        public void Show(Func<Vector2?> target, Func<Vector2> radius, Func<string> text)
        {
            _target = target;
            _radius = radius;
            _text = text;
            _introT = 0f;
            _darkClicks = 0;
            _centre = new Vector2(Screen.width, Screen.height) * 0.5f;
            _vel = Vector2.zero;
        }

        public void Hide()
        {
            _target = null;
            _radius = null;
            _text = null;
        }

        private void Update()
        {
            Vector2? at = _target?.Invoke();
            bool on = at.HasValue;
            _fade = Mathf.MoveTowards(_fade, on ? 1f : 0f, Time.unscaledDeltaTime / 0.25f);
            _group.alpha = _fade;
            _group.blocksRaycasts = on && _fade > 0.5f;
            if (_fade <= 0f) { _caption.Set(""); return; }
            if (!on) return;   // fading out where it last was

            Vector2 screen = new Vector2(Screen.width, Screen.height);
            Vector2 want = Vector2.Max(Vector2.one * 24f, _radius != null ? _radius() : Vector2.one * 60f);

            if (_introT < 1f)
            {
                _introT = Mathf.Min(1f, _introT + Time.unscaledDeltaTime / IntroSeconds);
                float k = _introT < 0.5f ? 4f * _introT * _introT * _introT
                                         : 1f - Mathf.Pow(-2f * _introT + 2f, 3f) * 0.5f;
                _centre = Vector2.Lerp(screen * 0.5f, at.Value, k);
                _r = Vector2.Lerp(Vector2.one * screen.magnitude * 0.55f, want, k);
            }
            else
            {
                _centre = Vector2.SmoothDamp(_centre, at.Value, ref _vel, 0.12f, Mathf.Infinity, Time.unscaledDeltaTime);
                _r = Vector2.Lerp(_r, want, 1f - Mathf.Exp(-12f * Time.unscaledDeltaTime));
            }
            Layout(_centre, _r, screen);

            // The ring breathes once the circle has arrived: grows a little and fades.
            float beat = _introT < 1f ? 0f : Mathf.Repeat(Time.unscaledTime * 0.9f, 1f);
            Vector2 ringR = _r * (1f + 0.16f * beat);
            Place(_ring, _centre.x - ringR.x, _centre.y - ringR.y, 2f * ringR.x, 2f * ringR.y, screen);
            _ringImg.color = UITheme.Alpha(UITheme.CandleHot, _introT < 1f ? 0f : 0.9f * (1f - beat));

            _caption.Set(_text?.Invoke());
            // The caption sits under the circle, or over it when the circle is low.
            float px = _canvas != null && _canvas.scaleFactor > 0f ? _canvas.scaleFactor : 1f;   // canvas unit -> pixel
            float half = _captionRt.sizeDelta.y * 0.5f * px;
            float y = _centre.y - _r.y - 18f - half;
            if (y - half < 12f) y = _centre.y + _r.y + 18f + half;
            float w = _captionRt.sizeDelta.x * 0.5f * px + 12f;
            float x = Mathf.Clamp(_centre.x, w, Mathf.Max(w, screen.x - w));
            y = Mathf.Clamp(y, half + 8f, Mathf.Max(half + 8f, screen.y - half - 8f));
            _captionRt.anchorMin = _captionRt.anchorMax = new Vector2(x / screen.x, y / screen.y);
            _captionRt.anchoredPosition = Vector2.zero;
        }

        /// <summary>The hole square, and the four dark slabs round it.</summary>
        private void Layout(Vector2 c, Vector2 r, Vector2 screen)
        {
            float x0 = c.x - r.x, x1 = c.x + r.x, y0 = c.y - r.y, y1 = c.y + r.y;
            Place(_hole, x0, y0, 2f * r.x, 2f * r.y, screen);
            Slab(0, 0f, y1, screen.x, Mathf.Max(0f, screen.y - y1));        // above
            Slab(1, 0f, 0f, screen.x, Mathf.Max(0f, y0));                  // below
            Slab(2, 0f, y0, Mathf.Max(0f, x0), y1 - y0);                    // left
            Slab(3, x1, y0, Mathf.Max(0f, screen.x - x1), y1 - y0);         // right
        }

        private void Slab(int i, float x, float y, float w, float h)
        {
            Place(_dark[i].rectTransform, x, y, w, h, new Vector2(Screen.width, Screen.height));
        }

        /// <summary>Lay a rect given in screen pixels out as a fraction of the canvas.</summary>
        private static void Place(RectTransform rt, float x, float y, float w, float h, Vector2 screen)
        {
            rt.anchorMin = new Vector2(x / screen.x, y / screen.y);
            rt.anchorMax = new Vector2((x + w) / screen.x, (y + h) / screen.y);
            rt.offsetMin = rt.offsetMax = Vector2.zero;
        }

        private bool InsideCircle(Vector2 screenPoint)
        {
            Vector2 d = screenPoint - _centre;
            float nx = d.x / Mathf.Max(1f, _r.x), ny = d.y / Mathf.Max(1f, _r.y);
            return nx * nx + ny * ny < 1f;
        }

        private void DarkClicked()
        {
            if (++_darkClicks >= 3) { _darkClicks = 0; SkipRequested?.Invoke(); }
        }

        // ------------------------------------------------------------ textures

        private static Sprite _soft, _ringSprite;

        /// <summary>A clear disc fading to dark at its rim (soft), or a thin ring.</summary>
        private static Sprite Disc(bool soft)
        {
            if (soft && _soft != null) return _soft;
            if (!soft && _ringSprite != null) return _ringSprite;
            var tex = new Texture2D(Tex, Tex, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[Tex * Tex];
            float h = Tex * 0.5f;
            for (int y = 0; y < Tex; y++)
                for (int x = 0; x < Tex; x++)
                {
                    float d = new Vector2(x + 0.5f - h, y + 0.5f - h).magnitude / h;
                    float a = soft ? Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.82f, 1f, d))
                                   : Mathf.Clamp01(1f - Mathf.Abs(d - 0.955f) / 0.035f);
                    px[y * Tex + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                }
            tex.SetPixels32(px);
            tex.Apply(false, true);
            var sprite = Sprite.Create(tex, new Rect(0, 0, Tex, Tex), new Vector2(0.5f, 0.5f), 100f);
            if (soft) _soft = sprite; else _ringSprite = sprite;
            return sprite;
        }

        // -------------------------------------------------------------- clicks

        /// <summary>The hole square only takes a click outside its circle.</summary>
        private sealed class HoleFilter : MonoBehaviour, ICanvasRaycastFilter
        {
            public Spotlight Owner;
            public bool IsRaycastLocationValid(Vector2 sp, Camera cam) => Owner == null || !Owner.InsideCircle(sp);
        }

        private sealed class DarkClick : MonoBehaviour, IPointerClickHandler
        {
            public Spotlight Owner;
            public void OnPointerClick(PointerEventData e) { if (Owner != null) Owner.DarkClicked(); }
        }
    }
}
