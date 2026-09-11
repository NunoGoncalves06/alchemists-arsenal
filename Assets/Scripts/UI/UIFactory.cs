using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using AlchemistsArsenal.Art;
using AlchemistsArsenal.Combat;
using AlchemistsArsenal.Core;

namespace AlchemistsArsenal.UI
{
    /// <summary>
    /// Code builders for the Phase-0 component kit (DESIGN.md §8). Everything is
    /// rough uGUI with theme tokens; hard corners, flat tints, no blur. Real
    /// 9-slice sprites + prefab library are Phase 2.
    /// </summary>
    public static class UIFactory
    {
        public static RectTransform Root(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            Stretch(rt);
            return rt;
        }

        public static void Stretch(RectTransform rt, float pad = 0f)
        {
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(pad, pad); rt.offsetMax = new Vector2(-pad, -pad);
        }

        public static RectTransform Rect(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax,
            Vector2 offsetMin, Vector2 offsetMax)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = anchorMin; rt.anchorMax = anchorMax;
            rt.offsetMin = offsetMin; rt.offsetMax = offsetMax;
            return rt;
        }

        public static Image Panel(Transform parent, Color fill, string name = "Panel")
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            img.sprite = PixelArt.White;    // flat tinted fill; framed panels use FramedPanel
            img.type = Image.Type.Sliced;
            img.color = fill;
            return img;
        }

        /// <summary>A panel with the authored 9-slice wood/ink frame — for the main surfaces.</summary>
        public static Image FramedPanel(Transform parent, string name = "Panel")
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            img.sprite = PixelSprites.Panel9();
            img.type = Image.Type.Sliced;
            img.pixelsPerUnitMultiplier = 3f;
            img.color = Color.white;
            return img;
        }

        public static Image Box(Transform parent, Color fill, RectTransform frame, string name = "Box")
        {
            var img = Panel(parent, fill, name);
            var rt = img.rectTransform;
            rt.anchorMin = frame.anchorMin; rt.anchorMax = frame.anchorMax;
            rt.offsetMin = frame.offsetMin; rt.offsetMax = frame.offsetMax;
            return img;
        }

        public static TextMeshProUGUI Label(Transform parent, string text, int size, Color color,
            TextAlignmentOptions align = TextAlignmentOptions.TopLeft, bool bold = false)
        {
            var go = new GameObject("Label", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<TextMeshProUGUI>();
            if (UITheme.Font != null) t.font = UITheme.Font;
            t.text = text;
            t.fontSize = size * Mathf.Max(0.75f, SettingsService.TextScale);
            t.color = color;
            t.alignment = align;
            t.fontStyle = bold ? FontStyles.Bold : FontStyles.Normal;
            t.textWrappingMode = TextWrappingModes.Normal;
            t.raycastTarget = false;
            return t;
        }

        /// <summary>
        /// A single-line header label banded to the TOP edge of its parent, full
        /// width. Exists because the old pattern — <c>Label(...).rectTransform
        /// .offsetMin = new Vector2(x, -h)</c> with no anchors touched — left
        /// anchorMin == anchorMax at a bare RectTransform's default (a small fixed
        /// point, not a stretched rect), so the label collapsed to Unity's ~100px
        /// default box and TMP word-wrapped the text one character per line. That
        /// shipped in seven places (every Morning-screen station header, the
        /// Upgrades header, HandoffScreen's "TODAY'S PARTY", BiomeMapScreen's "THE
        /// FOREST ROAD" — the last one was caught by a headless-playtest screenshot
        /// rendering as a single vertical column of individual letters). Always
        /// anchor top-band labels through this helper instead of a bare Label +
        /// one-line offset tweak.
        /// </summary>
        public static TextMeshProUGUI TopLabel(Transform parent, string text, int size, Color color,
            float bandHeight = 44f, float padX = 16f, bool bold = true)
        {
            var label = Label(parent, text, size, color, TextAlignmentOptions.TopLeft, bold);
            var rt = label.rectTransform;
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.offsetMin = new Vector2(padX, -bandHeight);
            rt.offsetMax = new Vector2(-padX, 0f);
            return label;
        }

        public static Button Button(Transform parent, string text, Action onClick, bool primary = true)
        {
            var img = Panel(parent, primary ? UITheme.Candle : UITheme.Ink700, "Button");
            var btn = img.gameObject.AddComponent<Button>();
            btn.targetGraphic = img;
            var colors = btn.colors;
            colors.highlightedColor = primary ? UITheme.CandleHot : UITheme.Wood;
            colors.pressedColor = UITheme.Wood;
            colors.disabledColor = new Color(img.color.r, img.color.g, img.color.b, 0.4f);
            btn.colors = colors;
            if (onClick != null) btn.onClick.AddListener(() => onClick());

            var label = Label(img.transform, text, 20, primary ? UITheme.Ink900 : UITheme.Parchment,
                TextAlignmentOptions.Center, bold: true);
            Stretch(label.rectTransform, 6f);
            return btn;
        }

        /// <summary>A horizontal fill bar. Returns the fill Image — set <c>fillAmount</c> via a helper.</summary>
        public static Image Bar(Transform parent, Color track, Color fill, out Image fillImg)
        {
            var bg = Panel(parent, track, "Bar");
            fillImg = Panel(bg.transform, fill, "Fill");
            var rt = fillImg.rectTransform;
            rt.anchorMin = Vector2.zero; rt.anchorMax = new Vector2(1f, 1f);
            rt.offsetMin = new Vector2(2, 2); rt.offsetMax = new Vector2(-2, -2);
            fillImg.type = Image.Type.Filled;
            fillImg.fillMethod = Image.FillMethod.Horizontal;
            fillImg.fillOrigin = 0;
            fillImg.fillAmount = 1f;
            return bg;
        }

        /// <summary>Element identity = colour + shape (never colour alone — a11y, DESIGN.md §5.2).</summary>
        public static Image ElementBadge(Transform parent, ElementType element, float size)
        {
            var go = new GameObject("ElementBadge", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            img.sprite = PixelSprites.ElementIcon(element);
            img.rectTransform.sizeDelta = new Vector2(size, size);
            img.raycastTarget = false;
            return img;
        }

        public static VerticalLayoutGroup VStack(Transform parent, float spacing, RectOffset pad = null)
        {
            var go = new GameObject("VStack", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var v = go.AddComponent<VerticalLayoutGroup>();
            v.spacing = spacing;
            v.padding = pad ?? new RectOffset(0, 0, 0, 0);
            v.childControlWidth = true; v.childControlHeight = true;
            v.childForceExpandWidth = true; v.childForceExpandHeight = false;
            return v;
        }

        public static HorizontalLayoutGroup HStack(Transform parent, float spacing, RectOffset pad = null)
        {
            var go = new GameObject("HStack", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var h = go.AddComponent<HorizontalLayoutGroup>();
            h.spacing = spacing;
            h.padding = pad ?? new RectOffset(0, 0, 0, 0);
            h.childControlWidth = true; h.childControlHeight = true;
            h.childForceExpandWidth = false; h.childForceExpandHeight = false;
            return h;
        }
    }
}
