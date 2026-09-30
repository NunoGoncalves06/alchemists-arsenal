using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;

namespace AlchemistsArsenal.UI
{
    /// <summary>
    /// The tutorial's pointer: a hole in a dark mask. The screen goes dark except a
    /// clean-edged circle over the one thing to touch next (round for a thing in
    /// the shop, an ellipse hugging a wide piece of UI such as a button). Each new
    /// lesson the hole starts wide at the centre of the screen and closes in on its
    /// target, then follows it if it moves (a leaf in the hand, a carried cork).
    ///
    /// <para>Smaller holes keep the bench's instruments lit (the gauges, the flask
    /// while it fills), so the dark never hides what tells the player when to stop.
    /// The caption goes wherever it covers none of the holes.</para>
    ///
    /// <para>Only the main hole takes clicks; the dark stops them, UI and bench alike
    /// (the benches ignore a press while the pointer is over UI, see
    /// <c>Pointer.OverUI</c>). Three clicks on the dark let the player out of the
    /// lesson, so a lesson can never trap anyone.</para>
    ///
    /// <para>The mask is a small texture redrawn when the holes move, stretched over
    /// the screen, so it lines up at any resolution (and in the harness, which
    /// re-renders every canvas at its own size for a screenshot).</para>
    /// </summary>
    public class Spotlight : MonoBehaviour
    {
        private const float Dim = 0.74f;
        private const float IntroSeconds = 0.9f;
        private const int MaskW = 320;
        private const float EdgePx = 1.6f;   // mask pixels of soft edge: a crisp rim, not a glow

        public event Action SkipRequested;

        /// <summary>The strip along the foot of the screen is taken (a bench's own caption lives there).</summary>
        public bool ReserveFoot = true;

        private RawImage _mask;
        private Texture2D _tex;
        private Color32[] _px;
        private int _maskH;
        private CanvasGroup _group;
        private Canvas _canvas;
        private UIKit.CaptionView _caption;
        private RectTransform _captionRt;

        private Func<Vector2?> _target;
        private Func<Vector2> _radius;
        private Func<string> _text;
        private Func<List<Rect>> _reveals;
        private float _introT = 1f, _fade;
        private int _darkClicks;
        private Vector2 _centre, _vel;
        private Vector2 _r;   // radii in x and y, pixels
        private readonly List<Rect> _revealNow = new List<Rect>();
        private string _drawnKey = "";

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
            var root = (RectTransform)transform;
            _canvas = GetComponent<Canvas>();
            _group = gameObject.AddComponent<CanvasGroup>();
            _group.alpha = 0f;
            _group.blocksRaycasts = false;

            var maskGo = new GameObject("Mask", typeof(RectTransform));
            maskGo.transform.SetParent(root, false);
            _mask = maskGo.AddComponent<RawImage>();
            _mask.color = Color.white;
            UIFactory.Stretch(_mask.rectTransform);
            maskGo.AddComponent<HoleFilter>().Owner = this;
            maskGo.AddComponent<DarkClick>().Owner = this;

            _caption = UIKit.Caption(root, "SpotCaption");
            _captionRt = _caption.Root.rectTransform;
            _captionRt.anchorMin = _captionRt.anchorMax = Vector2.zero;
            _captionRt.pivot = new Vector2(0.5f, 0.5f);
            _captionRt.sizeDelta = new Vector2(720f, 58f);
            _caption.Text.fontSizeMax = UITheme.SizeBody + 2;
            _caption.Text.alignment = TextAlignmentOptions.Center;
        }

        /// <summary>
        /// Light up <paramref name="target"/> (a screen point, or null while it is not
        /// on screen) with a hole of <paramref name="radius"/> pixels (x and y), keep the
        /// <paramref name="reveals"/> (screen rects) lit too, and say
        /// <paramref name="text"/> beside it. Restarts the closing-in animation.
        /// </summary>
        public void Show(Func<Vector2?> target, Func<Vector2> radius, Func<string> text, Func<List<Rect>> reveals = null)
        {
            _target = target;
            _radius = radius;
            _text = text;
            _reveals = reveals;
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
            _reveals = null;
        }

        /// <summary>Gone this frame: for leaving the morning, where a fade would drift into the next screen.</summary>
        public void HideNow()
        {
            Hide();
            if (_fade <= 0f) return;
            _fade = 0f;
            _group.alpha = 0f;
            _group.blocksRaycasts = false;
            _caption.Set("");
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

            Vector2 screen = new Vector2(Mathf.Max(1, Screen.width), Mathf.Max(1, Screen.height));
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

            // The instruments open up once the main hole has arrived.
            _revealNow.Clear();
            if (_introT >= 1f && _reveals != null)
            {
                List<Rect> extra = null;
                try { extra = _reveals(); } catch (Exception) { extra = null; }
                if (extra != null)
                    foreach (Rect r in extra)
                        if (r.width > 0f && r.height > 0f && r.Overlaps(new Rect(Vector2.zero, screen))) _revealNow.Add(r);
            }

            DrawMask(screen);
            PlaceCaption(screen);
        }

        // ---------------------------------------------------------------- mask

        private void DrawMask(Vector2 screen)
        {
            int h = Mathf.Clamp(Mathf.RoundToInt(MaskW * screen.y / screen.x), 16, 1024);
            if (_tex == null || _maskH != h)
            {
                if (_tex != null) Destroy(_tex);
                _maskH = h;
                _tex = new Texture2D(MaskW, h, TextureFormat.RGBA32, false)
                    { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
                _px = new Color32[MaskW * h];
                _mask.texture = _tex;
                _drawnKey = "";
            }

            // Redraw only when something moved by at least a fraction of a mask pixel.
            float sx = MaskW / screen.x, sy = _maskH / screen.y;
            var key = new System.Text.StringBuilder();
            key.Append(Mathf.RoundToInt(_centre.x * sx * 4f)).Append(',').Append(Mathf.RoundToInt(_centre.y * sy * 4f))
               .Append(',').Append(Mathf.RoundToInt(_r.x * sx * 4f)).Append(',').Append(Mathf.RoundToInt(_r.y * sy * 4f));
            foreach (Rect r in _revealNow)
                key.Append('|').Append(Mathf.RoundToInt(r.x * sx)).Append(',').Append(Mathf.RoundToInt(r.y * sy))
                   .Append(',').Append(Mathf.RoundToInt(r.width * sx)).Append(',').Append(Mathf.RoundToInt(r.height * sy));
            string k = key.ToString();
            if (k == _drawnKey) return;
            _drawnKey = k;

            // Everything in mask pixels.
            float cx = _centre.x * sx, cy = _centre.y * sy;
            float rx = Mathf.Max(1f, _r.x * sx), ry = Mathf.Max(1f, _r.y * sy);
            float edge = EdgePx / Mathf.Min(rx, ry);
            var rects = new List<Rect>(_revealNow.Count);
            foreach (Rect r in _revealNow)
                rects.Add(new Rect(r.x * sx, r.y * sy, r.width * sx, r.height * sy));

            byte full = (byte)(Dim * 255f);
            for (int y = 0; y < _maskH; y++)
            {
                float py = y + 0.5f;
                float dy = (py - cy) / ry;
                for (int x = 0; x < MaskW; x++)
                {
                    float px = x + 0.5f;
                    float dx = (px - cx) / rx;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    // 0 inside the hole, 1 in the dark, a pixel-wide rim between.
                    float a = Mathf.Clamp01((d - (1f - edge)) / edge);
                    for (int i = 0; i < rects.Count && a > 0f; i++)
                    {
                        Rect r = rects[i];
                        float ox = Mathf.Max(r.xMin - px, px - r.xMax, 0f);
                        float oy = Mathf.Max(r.yMin - py, py - r.yMax, 0f);
                        float outside = Mathf.Sqrt(ox * ox + oy * oy);
                        a = Mathf.Min(a, Mathf.Clamp01(outside / EdgePx));
                    }
                    _px[y * MaskW + x] = new Color32(0, 0, 0, (byte)(a * full));
                }
            }
            _tex.SetPixels32(_px);
            _tex.Apply(false, false);
        }

        // ------------------------------------------------------------- caption

        /// <summary>
        /// Under the hole, over it, beside it, or failing all those the top or foot of
        /// the screen: the first place that covers neither the hole nor anything kept
        /// lit (the flask being filled, a gauge).
        /// </summary>
        private void PlaceCaption(Vector2 screen)
        {
            _caption.Set(_text?.Invoke());
            float px = _canvas != null && _canvas.scaleFactor > 0f ? _canvas.scaleFactor : 1f;   // canvas unit -> pixel
            Vector2 size = _captionRt.sizeDelta * px;
            Vector2 half = size * 0.5f;
            const float gap = 18f;

            var hole = new Rect(_centre - _r, _r * 2f);
            var candidates = new[]
            {
                new Vector2(_centre.x, hole.yMin - gap - half.y),
                new Vector2(_centre.x, hole.yMax + gap + half.y),
                new Vector2(hole.xMax + gap + half.x, _centre.y),
                new Vector2(hole.xMin - gap - half.x, _centre.y),
                new Vector2(screen.x * 0.38f, half.y + 14f),   // the foot of the screen, left of any button there
                new Vector2(screen.x * 0.5f, screen.y * 0.84f),
                new Vector2(screen.x * 0.5f, screen.y * 0.16f),
            };

            // The strip along the foot of the screen holds the bench's own caption.
            var benchCaption = ReserveFoot ? new Rect(0f, 0f, screen.x, screen.y * 0.12f) : Rect.zero;
            Rect inner = Shrink(hole, 0.15f);

            // The first spot that covers nothing; if every spot covers something, the
            // one that covers least.
            Vector2 best = candidates[0];
            float bestCost = float.MaxValue;
            foreach (Vector2 c0 in candidates)
            {
                Vector2 c = new Vector2(Mathf.Clamp(c0.x, half.x + 12f, Mathf.Max(half.x + 12f, screen.x - half.x - 12f)),
                                        Mathf.Clamp(c0.y, half.y + 8f, Mathf.Max(half.y + 8f, screen.y - half.y - 8f)));
                var box = new Rect(c - half, size);
                float cost = Overlap(box, inner) * 2f + Overlap(box, benchCaption) * 4f;
                foreach (Rect r in _revealNow) cost += Overlap(box, r) * 3f;
                if (cost < bestCost - 0.5f) { best = c; bestCost = cost; }
                if (cost <= 0f) break;
            }
            _captionRt.anchorMin = _captionRt.anchorMax = new Vector2(best.x / screen.x, best.y / screen.y);
            _captionRt.anchoredPosition = Vector2.zero;
        }

        private static float Overlap(Rect a, Rect b)
        {
            float w = Mathf.Min(a.xMax, b.xMax) - Mathf.Max(a.xMin, b.xMin);
            float h = Mathf.Min(a.yMax, b.yMax) - Mathf.Max(a.yMin, b.yMin);
            return w > 0f && h > 0f ? w * h : 0f;
        }

        /// <summary>The hole's box pulled in a little: an ellipse's corners are dark anyway.</summary>
        private static Rect Shrink(Rect r, float frac) =>
            new Rect(r.x + r.width * frac * 0.5f, r.y + r.height * frac * 0.5f, r.width * (1f - frac), r.height * (1f - frac));

        // -------------------------------------------------------------- clicks

        private bool InsideHole(Vector2 screenPoint)
        {
            Vector2 d = screenPoint - _centre;
            float nx = d.x / Mathf.Max(1f, _r.x), ny = d.y / Mathf.Max(1f, _r.y);
            return nx * nx + ny * ny < 1f;
        }

        private void DarkClicked()
        {
            if (++_darkClicks >= 3) { _darkClicks = 0; SkipRequested?.Invoke(); }
        }

        private void OnDestroy()
        {
            if (_tex != null) Destroy(_tex);
        }

        /// <summary>The mask takes a click (and so stops it) everywhere but the main hole.</summary>
        private sealed class HoleFilter : MonoBehaviour, ICanvasRaycastFilter
        {
            public Spotlight Owner;
            public bool IsRaycastLocationValid(Vector2 sp, Camera cam) => Owner == null || !Owner.InsideHole(sp);
        }

        private sealed class DarkClick : MonoBehaviour, IPointerClickHandler
        {
            public Spotlight Owner;
            public void OnPointerClick(PointerEventData e) { if (Owner != null) Owner.DarkClicked(); }
        }
    }
}
