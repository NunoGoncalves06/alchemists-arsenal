using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using AlchemistsArsenal.Art;
using AlchemistsArsenal.Combat;

namespace AlchemistsArsenal.UI
{
    /// <summary>
    /// The composed component kit — cards, rails, meters, portraits, chips. Built
    /// out of <see cref="UIFactory"/> primitives, and the only place a screen should
    /// get structure from, so every screen inherits the same spacing, borders and
    /// type roles instead of re-inventing them per panel.
    /// </summary>
    public static class UIKit
    {
        public const float Gutter = 18f;   // between cards
        public const float PadCard = 18f;  // inside a card
        public const float Radius = 0f;    // pixel art: hard corners everywhere

        // ------------------------------------------------------------------ cards

        /// <summary>
        /// A bordered surface: hairline outer, surface fill inside. Returns the
        /// OUTER image — that is the rect the caller positions — and hands back the
        /// fill's transform in <paramref name="inner"/> to parent content to.
        /// </summary>
        public static Image Surface(Transform parent, out Transform inner, Color? fill = null,
            Color? border = null, string name = "Surface", float borderWidth = 2f)
        {
            var outer = UIFactory.Panel(parent, border ?? UITheme.Line, name);
            var fillImg = UIFactory.Panel(outer.transform, fill ?? UITheme.Surface, "Fill");
            UIFactory.Stretch(fillImg.rectTransform, borderWidth);
            inner = fillImg.transform;
            return outer;
        }

        /// <summary>
        /// A titled card: bordered surface, an uppercase heading band with a rule
        /// under it, and a padded vertical content stack. Returns the OUTER rect to
        /// position; <paramref name="content"/> is where everything else goes.
        /// </summary>
        public static Image Card(Transform parent, string title, out Transform content,
            float spacing = 10f, Color? accent = null)
        {
            Image outer = Surface(parent, out Transform fill, UITheme.Surface, UITheme.Line, "Card");

            var stack = UIFactory.VStack(fill, spacing,
                new RectOffset((int)PadCard, (int)PadCard, (int)(PadCard * 0.8f), (int)PadCard));
            UIFactory.Stretch((RectTransform)stack.transform);

            if (!string.IsNullOrEmpty(title))
            {
                var head = UIFactory.Heading(stack.transform, title, accent ?? UITheme.Candle);
                UIFactory.Flex(head.gameObject, 1f, 0f, minHeight: 22f);
                var rule = UIFactory.Rule(stack.transform, UITheme.LineSoft);
                UIFactory.FixedHeight(rule.gameObject, 2f);
            }

            content = stack.transform;
            return outer;
        }

        /// <summary>A small pill: tinted background, optional icon, short label.</summary>
        public static RectTransform Chip(Transform parent, string text, Color tint, Sprite icon = null,
            int fontSize = UITheme.SizeSmall)
        {
            var bg = UIFactory.Panel(parent, UITheme.Alpha(tint, 0.18f), "Chip");
            var row = UIFactory.HStack(bg.transform, 6f, new RectOffset(10, 10, 4, 4));
            row.childAlignment = TextAnchor.MiddleLeft;
            UIFactory.Stretch((RectTransform)row.transform);

            if (icon != null)
            {
                var img = UIFactory.Icon(row.transform, icon, 18f);
                UIFactory.Flex(img.gameObject, 0f, 0f, minWidth: 18f, minHeight: 18f);
            }
            var label = UIFactory.Label(row.transform, text, fontSize, tint, TextAlignmentOptions.Left, true);
            UIFactory.Flex(label.gameObject, 1f, 1f);
            return bg.rectTransform;
        }

        /// <summary>Label on the left, value on the right — the ledger row.</summary>
        public static TextMeshProUGUI KeyValue(Transform parent, string key, string value,
            Color? keyColor = null, Color? valueColor = null, int size = UITheme.SizeBody)
        {
            var row = UIFactory.HStack(parent, 8f);
            row.childAlignment = TextAnchor.MiddleLeft;
            UIFactory.FixedHeight(row.gameObject, size + 10f);

            var k = UIFactory.Label(row.transform, key, size, keyColor ?? UITheme.TextMid, TextAlignmentOptions.Left);
            UIFactory.Flex(k.gameObject, 1f, 1f);
            var v = UIFactory.MonoLabel(row.transform, value, size, valueColor ?? UITheme.TextHi,
                TextAlignmentOptions.Right);
            UIFactory.Flex(v.gameObject, 0.6f, 1f);
            return v;
        }

        // ----------------------------------------------------------------- meter

        /// <summary>
        /// A labelled meter: caption row above, track below, optional band overlay
        /// (the "hold it here" zone) and tick marks. Everything the crafting stations
        /// need to show a live value without each one hand-rolling three rects.
        /// </summary>
        public class MeterView
        {
            public RectTransform Root;
            public Image Fill;
            public Image Band;       // optional target-zone overlay
            public TextMeshProUGUI Caption, Value;

            public void Set(float value01, string value = null)
            {
                if (Fill != null) Fill.fillAmount = Mathf.Clamp01(value01);
                if (value != null && Value != null) Value.text = value;
            }

            public void SetFillColor(Color c) { if (Fill != null) Fill.color = c; }

            /// <summary>Move the target band (0..1 of the track).</summary>
            public void SetBand(float min01, float max01)
            {
                if (Band == null) return;
                Band.rectTransform.anchorMin = new Vector2(Mathf.Clamp01(min01), 0f);
                Band.rectTransform.anchorMax = new Vector2(Mathf.Clamp01(max01), 1f);
                Band.rectTransform.offsetMin = Band.rectTransform.offsetMax = Vector2.zero;
            }
        }

        public static MeterView Meter(Transform parent, string caption, Color fillColor,
            bool withBand = false, float height = 22f)
        {
            var view = new MeterView();
            var col = UIFactory.VStack(parent, 4f);
            view.Root = (RectTransform)col.transform;
            UIFactory.FixedHeight(col.gameObject, height + 26f);

            var head = UIFactory.HStack(col.transform, 8f);
            UIFactory.FixedHeight(head.gameObject, 18f);
            view.Caption = UIFactory.Heading(head.transform, caption, UITheme.TextLow, UITheme.SizeTiny);
            UIFactory.Flex(view.Caption.gameObject, 1f, 1f);
            view.Value = UIFactory.MonoLabel(head.transform, "", UITheme.SizeTiny, UITheme.TextMid,
                TextAlignmentOptions.Right);
            UIFactory.Flex(view.Value.gameObject, 0.5f, 1f);

            var track = UIFactory.Bar(col.transform, UITheme.Ground, fillColor, out Image fill);
            view.Fill = fill;
            UIFactory.FixedHeight(track.gameObject, height);

            if (withBand)
            {
                view.Band = UIFactory.Panel(track.transform, UITheme.Alpha(UITheme.Ok, 0.30f), "Band");
                view.Band.rectTransform.anchorMin = new Vector2(0.4f, 0f);
                view.Band.rectTransform.anchorMax = new Vector2(0.7f, 1f);
                view.Band.rectTransform.offsetMin = view.Band.rectTransform.offsetMax = Vector2.zero;
                view.Band.transform.SetAsLastSibling(); // reads over the fill, not under it
            }
            return view;
        }

        // ------------------------------------------------------------- portraits

        /// <summary>A framed character portrait — wood frame, ink mat, pixel sprite.</summary>
        public static Image Portrait(Transform parent, Sprite sprite, float size)
        {
            var frame = UIFactory.Panel(parent, UITheme.Wood, "Portrait");
            frame.rectTransform.sizeDelta = new Vector2(size, size);
            var mat = UIFactory.Panel(frame.transform, UITheme.Ink900, "Mat");
            UIFactory.Stretch(mat.rectTransform, 4f);
            var art = UIFactory.Icon(mat.transform, sprite, size - 20f);
            UIFactory.Stretch(art.rectTransform, 8f);
            art.preserveAspect = true;
            return art;
        }

        /// <summary>
        /// A parchment speech bubble with a downward tail. Returns the text so the
        /// caller can keep re-writing the same bubble.
        /// </summary>
        public static TextMeshProUGUI SpeechBubble(Transform parent, string text, int size = UITheme.SizeBody)
        {
            var bubble = UIFactory.Panel(parent, UITheme.Parchment, "Bubble");
            var t = UIFactory.Label(bubble.transform, text, size, UITheme.Ink900, TextAlignmentOptions.TopLeft);
            UIFactory.Stretch(t.rectTransform, 14f);

            var tail = UIFactory.Icon(bubble.transform, PixelSprites.PointerArrow(), 22f, UITheme.Parchment);
            tail.rectTransform.anchorMin = tail.rectTransform.anchorMax = new Vector2(0.12f, 0f);
            tail.rectTransform.anchoredPosition = new Vector2(0f, -9f);
            return t;
        }

        // ------------------------------------------------------------ station rail

        /// <summary>
        /// One station button on the morning rail: icon, name, and a state dot that
        /// says at a glance whether the station is locked, waiting for you, or done.
        /// </summary>
        public class RailTab
        {
            public Button Button;
            public Image Background, Dot, IconImage;
            public TextMeshProUGUI Label, Step;

            public void SetState(bool active, bool locked, bool done)
            {
                if (Button != null) Button.interactable = !locked;
                if (Background != null)
                    Background.color = active ? UITheme.Candle
                        : locked ? UITheme.Alpha(UITheme.SurfaceHi, 0.45f)
                        : UITheme.SurfaceHi;
                if (Label != null)
                    Label.color = active ? UITheme.TextOnGold : locked ? UITheme.TextLow : UITheme.TextHi;
                if (Step != null)
                    Step.color = active ? UITheme.Alpha(UITheme.TextOnGold, 0.7f) : UITheme.TextLow;
                if (IconImage != null)
                    IconImage.color = locked ? UITheme.Alpha(Color.white, 0.35f) : Color.white;
                if (Dot != null)
                    Dot.color = done ? UITheme.Ok : locked ? UITheme.Alpha(UITheme.TextLow, 0.4f) : UITheme.Candle;
            }
        }

        public static RailTab StationTab(Transform parent, string label, string step, Sprite icon, Action onClick)
        {
            var view = new RailTab();
            var bg = UIFactory.Panel(parent, UITheme.SurfaceHi, "StationTab");
            view.Background = bg;
            var btn = bg.gameObject.AddComponent<Button>();
            btn.targetGraphic = bg;
            var colors = btn.colors;
            colors.highlightedColor = UITheme.SurfaceTop;
            colors.pressedColor = UITheme.Line;
            colors.disabledColor = UITheme.Alpha(UITheme.SurfaceHi, 0.35f);
            colors.fadeDuration = 0.08f;
            btn.colors = colors;
            if (onClick != null) btn.onClick.AddListener(() => onClick());
            view.Button = btn;
            UIFactory.Flex(bg.gameObject, 1f, 0f, minHeight: 86f);

            view.IconImage = UIFactory.Icon(bg.transform, icon, 34f);
            view.IconImage.rectTransform.anchorMin = new Vector2(0.5f, 1f);
            view.IconImage.rectTransform.anchorMax = new Vector2(0.5f, 1f);
            view.IconImage.rectTransform.anchoredPosition = new Vector2(0f, -26f);

            view.Label = UIFactory.Label(bg.transform, label, UITheme.SizeTiny, UITheme.TextHi,
                TextAlignmentOptions.Center, true);
            view.Label.characterSpacing = 4f;
            UIFactory.Place(view.Label.rectTransform, 0f, 0.02f, 1f, 0.30f);

            view.Step = UIFactory.Label(bg.transform, step, UITheme.SizeTiny, UITheme.TextLow,
                TextAlignmentOptions.TopLeft);
            UIFactory.Place(view.Step.rectTransform, 0f, 0.66f, 0.4f, 1f, 6f);

            view.Dot = UIFactory.Panel(bg.transform, UITheme.Candle, "Dot");
            view.Dot.rectTransform.anchorMin = new Vector2(1f, 1f);
            view.Dot.rectTransform.anchorMax = new Vector2(1f, 1f);
            view.Dot.rectTransform.sizeDelta = new Vector2(10f, 10f);
            view.Dot.rectTransform.anchoredPosition = new Vector2(-10f, -10f);
            return view;
        }

        // ------------------------------------------------------------ interaction

        /// <summary>A button that reports press and release separately (see <see cref="HoldButton"/>).</summary>
        public static HoldButton Hold(Transform parent, string text, Action onPress, Action onRelease,
            bool primary = true)
        {
            Button b = UIFactory.Button(parent, text, null, primary);
            var hold = b.gameObject.AddComponent<HoldButton>();
            hold.OnPress = onPress;
            hold.OnRelease = onRelease;
            return hold;
        }

        // ---------------------------------------------------------------- grades

        /// <summary>The 4-band grade strip — shows where the current score sits.</summary>
        public static RectTransform GradeScale(Transform parent, float height = 14f)
        {
            var row = UIFactory.HStack(parent, 2f);
            UIFactory.Flex(row.gameObject, 1f, 0f, minHeight: height);
            foreach (PotionGrade g in new[] { PotionGrade.Poor, PotionGrade.Okay, PotionGrade.Great, PotionGrade.Perfect })
            {
                var seg = UIFactory.Panel(row.transform, UITheme.Alpha(UITheme.GradeColor(g), 0.55f), "Seg");
                UIFactory.Flex(seg.gameObject, 1f, 1f, minHeight: height);
            }
            return (RectTransform)row.transform;
        }
    }
}
