using UnityEngine;
using TMPro;
using AlchemistsArsenal.Art;
using AlchemistsArsenal.Core;

namespace AlchemistsArsenal.Crafting
{
    /// <summary>
    /// An instrument fixed to a bench, drawn in the world instead of a HUD meter: a
    /// brass-framed slot with the good band in green, and either a needle that
    /// travels along it or a fill that rises in it (a thermometer). An optional line
    /// of chalk text sits beside it. Horizontal or vertical.
    /// </summary>
    public sealed class BenchGauge
    {
        private static readonly Color Brass = new Color(0.72f, 0.56f, 0.28f);
        private static readonly Color Slot = new Color(0.10f, 0.07f, 0.09f);
        private static readonly Color BandColor = new Color(0.31f, 0.68f, 0.35f, 0.75f);

        private Transform _root;
        private SpriteRenderer _band, _needle, _fill;
        private TextMeshPro _text;
        private float _len;
        private bool _vertical, _fillMode;

        public GameObject GameObject => _root != null ? _root.gameObject : null;

        private static readonly System.Collections.Generic.List<BenchGauge> LiveGauges =
            new System.Collections.Generic.List<BenchGauge>();

        /// <summary>
        /// Every gauge that is showing, and the world box it and its chalk take up.
        /// The tutorial's spotlight keeps these lit, so the dark never hides the
        /// instrument a lesson is about.
        /// </summary>
        public static System.Collections.Generic.List<Rect> VisibleWorldRects()
        {
            var rects = new System.Collections.Generic.List<Rect>();
            LiveGauges.RemoveAll(g => g._root == null);
            foreach (BenchGauge g in LiveGauges)
                if (g._root.gameObject.activeInHierarchy) rects.Add(g.WorldRect);
            return rects;
        }

        private Rect WorldRect
        {
            get
            {
                Vector2 c = _root.position;
                float along = _len + 0.3f, across = 0.6f;
                Vector2 size = _vertical ? new Vector2(across, along) : new Vector2(along, across);
                var r = new Rect(c - size * 0.5f, size);
                if (_text != null && _text.gameObject.activeInHierarchy && !string.IsNullOrEmpty(_text.text))
                {
                    Vector2 t = _text.transform.position;
                    r = Rect.MinMaxRect(Mathf.Min(r.xMin, t.x - 0.9f), Mathf.Min(r.yMin, t.y - 0.25f),
                                        Mathf.Max(r.xMax, t.x + 0.9f), Mathf.Max(r.yMax, t.y + 0.25f));
                }
                return r;
            }
        }

        /// <param name="length">World units along the gauge.</param>
        /// <param name="fillMode">A rising fill (a thermometer) instead of a needle.</param>
        public static BenchGauge Create(Transform parent, Vector2 world, float length, bool vertical, int order,
            bool fillMode = false, string name = "Gauge")
        {
            var g = new BenchGauge { _len = length, _vertical = vertical, _fillMode = fillMode };
            var root = new GameObject(name);
            root.transform.SetParent(parent, false);
            root.transform.position = world;
            g._root = root.transform;
            LiveGauges.Add(g);

            const float thick = 0.22f;
            Part(g._root, "Frame", Brass, order, g.Size(length + 0.14f, thick + 0.12f), Vector2.zero);
            Part(g._root, "Slot", Slot, order + 1, g.Size(length, thick), Vector2.zero);
            g._band = Part(g._root, "Band", BandColor, order + 2, g.Size(0f, thick), Vector2.zero);
            if (fillMode) g._fill = Part(g._root, "Fill", new Color(0.84f, 0.27f, 0.31f), order + 3, g.Size(0f, thick * 0.6f), Vector2.zero);
            else g._needle = Part(g._root, "Needle", new Color(0.96f, 0.85f, 0.45f), order + 4, g.Size(0.07f, thick + 0.16f), Vector2.zero);
            return g;
        }

        private Vector3 Size(float along, float across) => _vertical ? new Vector3(across, along, 1f) : new Vector3(along, across, 1f);
        private Vector3 At(float along) => _vertical ? new Vector3(0f, along, 0f) : new Vector3(along, 0f, 0f);

        private static SpriteRenderer Part(Transform parent, string name, Color c, int order, Vector3 scale, Vector2 local)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = local;
            go.transform.localScale = scale;
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = PixelArt.White;
            sr.sharedMaterial = SpriteMaterials.For(sr.sprite);
            sr.color = c;
            sr.sortingOrder = order;
            return sr;
        }

        /// <summary>The good band, as a fraction of the gauge (0 = left / bottom).</summary>
        public void SetBand(float min01, float max01)
        {
            if (_band == null) return;
            min01 = Mathf.Clamp01(min01); max01 = Mathf.Clamp01(Mathf.Max(min01, max01));
            float a = (min01 - 0.5f) * _len, b = (max01 - 0.5f) * _len;
            _band.transform.localPosition = At((a + b) * 0.5f);
            _band.transform.localScale = Size(b - a, 0.22f);
        }

        /// <summary>Where the needle (or the top of the fill) is, 0..1, and its colour.</summary>
        public void SetValue(float value01, Color? color = null)
        {
            value01 = Mathf.Clamp01(value01);
            if (_fillMode && _fill != null)
            {
                float top = (value01 - 0.5f) * _len, bottom = -0.5f * _len;
                _fill.transform.localPosition = At((top + bottom) * 0.5f);
                _fill.transform.localScale = Size(top - bottom, 0.13f);
                if (color.HasValue) _fill.color = color.Value;
            }
            else if (_needle != null)
            {
                _needle.transform.localPosition = At((value01 - 0.5f) * _len);
                if (color.HasValue) _needle.color = color.Value;
            }
        }

        /// <summary>A short line of chalk beside the gauge ("12 / 18"); empty hides it.</summary>
        public void SetText(string text, int order = 40)
        {
            if (_text == null)
            {
                if (string.IsNullOrEmpty(text)) return;
                var go = new GameObject("Chalk");
                go.transform.SetParent(_root, false);
                go.transform.localPosition = _vertical ? new Vector3(0f, _len * 0.5f + 0.32f, 0f) : new Vector3(0f, 0.38f, 0f);
                _text = go.AddComponent<TextMeshPro>();
                _text.font = UI.UITheme.Mono;
                _text.fontSize = 2.2f;
                _text.alignment = TextAlignmentOptions.Center;
                _text.color = new Color(0.95f, 0.91f, 0.81f);
                _text.textWrappingMode = TextWrappingModes.NoWrap;
                _text.rectTransform.sizeDelta = new Vector2(3f, 0.5f);
                _text.sortingOrder = order;
            }
            _text.text = text ?? "";
        }

        public void SetVisible(bool on)
        {
            if (_root != null && _root.gameObject.activeSelf != on) _root.gameObject.SetActive(on);
        }
    }
}
