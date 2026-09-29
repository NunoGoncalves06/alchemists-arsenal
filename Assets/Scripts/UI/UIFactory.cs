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
    /// Code builders for the Phase-0 component kit (DESIGN.md §8) — the primitives
    /// only. Anything with structure (cards, rails, meters, portraits, speech
    /// bubbles) lives in <see cref="UIKit"/> and is built out of these.
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

        /// <summary>Anchor a rect to a 0..1 box of its parent, with optional pixel padding.</summary>
        public static void Place(RectTransform rt, float xMin, float yMin, float xMax, float yMax, float pad = 0f)
        {
            rt.anchorMin = new Vector2(xMin, yMin);
            rt.anchorMax = new Vector2(xMax, yMax);
            rt.offsetMin = new Vector2(pad, pad);
            rt.offsetMax = new Vector2(-pad, -pad);
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

        /// <summary>A 1px hairline (horizontal by default) for separating rows.</summary>
        public static Image Rule(Transform parent, Color? color = null, float thickness = 2f)
        {
            var img = Panel(parent, color ?? UITheme.LineSoft, "Rule");
            img.rectTransform.sizeDelta = new Vector2(0f, thickness);
            var le = img.gameObject.AddComponent<LayoutElement>();
            le.minHeight = thickness; le.preferredHeight = thickness; le.flexibleWidth = 1f;
            return img;
        }

        // ------------------------------------------------------------------ text

        public static TextMeshProUGUI Label(Transform parent, string text, int size, Color color,
            TextAlignmentOptions align = TextAlignmentOptions.TopLeft, bool bold = false,
            TMP_FontAsset font = null)
        {
            var go = new GameObject("Label", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<TextMeshProUGUI>();
            TMP_FontAsset face = font != null ? font : UITheme.Body;
            if (face != null) t.font = face;
            t.text = text;
            t.fontSize = size * Mathf.Max(0.75f, SettingsService.TextScale);
            t.color = color;
            t.alignment = align;
            t.fontStyle = bold ? FontStyles.Bold : FontStyles.Normal;
            t.textWrappingMode = TextWrappingModes.Normal;
            t.raycastTarget = false;
            return t;
        }

        /// <summary>Serif screen title. Big, tracked-in, warm.</summary>
        public static TextMeshProUGUI Title(Transform parent, string text, int size = UITheme.SizeTitle,
            Color? color = null, TextAlignmentOptions align = TextAlignmentOptions.TopLeft)
        {
            var t = Label(parent, text, size, color ?? UITheme.Candle, align, bold: false, font: UITheme.Display);
            t.characterSpacing = 1f;
            return t;
        }

        /// <summary>Small uppercase section heading — tracked out so caps breathe.</summary>
        public static TextMeshProUGUI Heading(Transform parent, string text, Color? color = null,
            int size = UITheme.SizeSmall, TextAlignmentOptions align = TextAlignmentOptions.TopLeft)
        {
            // Set in the serif, as written: a label on a drawer, not a shouted tag.
            var t = Label(parent, text, size + 3, color ?? UITheme.TextLow, align, bold: false, font: UITheme.Display);
            t.characterSpacing = 0.5f;
            return t;
        }

        /// <summary>Fixed-pitch text — ledgers, logs, anything with columns of figures.</summary>
        public static TextMeshProUGUI MonoLabel(Transform parent, string text, int size, Color color,
            TextAlignmentOptions align = TextAlignmentOptions.TopLeft)
            => Label(parent, text, size, color, align, bold: false, font: UITheme.Mono);

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

        // --------------------------------------------------------------- buttons

        /// <summary>
        /// An embossed plaque: brass for the main action, wood for the rest. A dark
        /// rim, a lit top edge and a shadowed foot, so it reads as a thing on the
        /// wall that can be pressed, not a flat rectangle. The caption is set in the
        /// serif, in sentence case.
        /// </summary>
        public static Button Button(Transform parent, string text, Action onClick, bool primary = true)
        {
            var img = Panel(parent, primary ? UITheme.Candle : UITheme.Wood, "Button");
            var btn = img.gameObject.AddComponent<Button>();
            btn.targetGraphic = img;
            var colors = btn.colors;
            colors.highlightedColor = primary ? UITheme.CandleHot : new Color(0.52f, 0.37f, 0.24f);
            colors.pressedColor = primary ? new Color(0.72f, 0.54f, 0.21f) : UITheme.WoodDark;
            colors.disabledColor = new Color(img.color.r, img.color.g, img.color.b, 0.35f);
            colors.fadeDuration = 0.08f;
            btn.colors = colors;
            if (onClick != null) btn.onClick.AddListener(() => onClick());

            var rim = img.gameObject.AddComponent<Outline>();
            rim.effectColor = UITheme.Alpha(UITheme.Ink900, 0.9f);
            rim.effectDistance = new Vector2(2f, -2f);
            Bevel(img.transform, "Lit", new Color(1f, 0.95f, 0.8f, 0.30f), top: true, 2f);
            Bevel(img.transform, "Foot", new Color(0f, 0f, 0f, 0.30f), top: false, 3f);

            var label = Label(img.transform, Soften(text), UITheme.SizeBody + 2,
                primary ? UITheme.Ink900 : UITheme.Parchment, TextAlignmentOptions.Center, bold: false, font: UITheme.Display);
            label.characterSpacing = 0.5f;
            Stretch(label.rectTransform, 6f);
            return btn;
        }

        private static void Bevel(Transform parent, string name, Color c, bool top, float px)
        {
            var edge = Panel(parent, c, name);
            edge.raycastTarget = false;
            RectTransform rt = edge.rectTransform;
            rt.anchorMin = new Vector2(0f, top ? 1f : 0f);
            rt.anchorMax = new Vector2(1f, top ? 1f : 0f);
            rt.pivot = new Vector2(0.5f, top ? 1f : 0f);
            rt.sizeDelta = new Vector2(0f, px);
            rt.anchoredPosition = Vector2.zero;
        }

        /// <summary>
        /// "SEND TO EXPEDITION" becomes "Send to expedition": the UI never shouts.
        /// Mixed-case text and rich text are left alone.
        /// </summary>
        public static string Soften(string s)
        {
            if (string.IsNullOrEmpty(s) || s.IndexOf('<') >= 0) return s;
            bool upper = false;
            foreach (char ch in s)
            {
                if (char.IsLower(ch)) return s;
                if (char.IsUpper(ch)) upper = true;
            }
            if (!upper) return s;
            char[] c = s.ToLowerInvariant().ToCharArray();
            bool start = true;
            for (int i = 0; i < c.Length; i++)
            {
                if (start && char.IsLetter(c[i])) { c[i] = char.ToUpperInvariant(c[i]); start = false; }
                else if (c[i] == '\n' || c[i] == '.' || c[i] == '!' || c[i] == '?') start = true;
            }
            return new string(c);
        }

        /// <summary>Change a button's caption without hunting for its child label.</summary>
        public static void SetButtonText(Button b, string text)
        {
            if (b == null) return;
            var t = b.GetComponentInChildren<TextMeshProUGUI>();
            if (t != null) t.text = Soften(text);
        }

        /// <summary>Recolour a button's idle + hover states together (selection, element tints).</summary>
        public static void TintButton(Button b, Color idle, Color hover, Color? content = null)
        {
            if (b == null || b.image == null) return;
            b.image.color = idle;
            var c = b.colors;
            c.highlightedColor = hover;
            c.pressedColor = hover;
            c.disabledColor = new Color(idle.r, idle.g, idle.b, 0.35f);
            b.colors = c;
            if (content.HasValue)
            {
                var t = b.GetComponentInChildren<TextMeshProUGUI>();
                if (t != null) t.color = content.Value;
            }
        }

        // ----------------------------------------------------------------- bars

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
            img.preserveAspect = true;
            img.rectTransform.sizeDelta = new Vector2(size, size);
            img.raycastTarget = false;
            return img;
        }

        /// <summary>A point-filtered pixel sprite as a UI image, aspect preserved.</summary>
        public static Image Icon(Transform parent, Sprite sprite, float size, Color? tint = null)
        {
            var go = new GameObject("Icon", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            img.sprite = sprite;
            img.preserveAspect = true;
            img.color = tint ?? Color.white;
            img.rectTransform.sizeDelta = new Vector2(size, size);
            img.raycastTarget = false;
            return img;
        }

        // --------------------------------------------------------------- layout

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

        /// <summary>
        /// Let a child of an H/VStack take its share of the cross axis. Layout groups
        /// here run with <c>childForceExpand*</c> off, so a child with no
        /// <see cref="LayoutElement"/> collapses to its sprite's tiny preferred size —
        /// the bug behind both the squished Report columns and the overlapping
        /// Upgrades rows. Call this on every stack child that should fill.
        /// </summary>
        public static LayoutElement Flex(GameObject go, float width = 1f, float height = 1f,
            float minHeight = -1f, float minWidth = -1f)
        {
            var le = go.GetComponent<LayoutElement>();
            if (le == null) le = go.AddComponent<LayoutElement>();
            le.flexibleWidth = width;
            le.flexibleHeight = height;
            if (minHeight >= 0f) le.minHeight = minHeight;
            if (minWidth >= 0f) le.minWidth = minWidth;
            return le;
        }

        /// <summary>
        /// Pin a stack child to an exact height. Controls that are not text — buttons,
        /// tracks, rules — have a preferred height of a few pixels (their sprite), so
        /// with a min but no preferred they end up sized by whatever is left over and
        /// can be squeezed to a sliver or pushed past the bottom of their card. Text
        /// keeps <see cref="Flex"/>, because TMP's own preferred height is what makes
        /// wrapping work.
        /// </summary>
        public static LayoutElement FixedHeight(GameObject go, float height, float flexibleWidth = 1f)
        {
            var le = go.GetComponent<LayoutElement>();
            if (le == null) le = go.AddComponent<LayoutElement>();
            le.minHeight = height;
            le.preferredHeight = height;
            le.flexibleHeight = 0f;
            le.flexibleWidth = flexibleWidth;
            return le;
        }
    }
}
