using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
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
            img.sprite = PixelArt.White;
            img.type = Image.Type.Sliced;
            img.color = fill;
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
            img.sprite = PlaceholderArt.Make(ShapeFor(element), UITheme.Element(element), UITheme.Ink900);
            img.rectTransform.sizeDelta = new Vector2(size, size);
            img.raycastTarget = false;
            return img;
        }

        public static PlaceholderArt.Shape ShapeFor(ElementType e) => e switch
        {
            ElementType.Fire => PlaceholderArt.Shape.Star,     // stand-in until authored triangle/hex/droplet
            ElementType.Nature => PlaceholderArt.Shape.Disc,
            ElementType.Water => PlaceholderArt.Shape.Diamond,
            ElementType.Poison => PlaceholderArt.Shape.Diamond,
            ElementType.Arcane => PlaceholderArt.Shape.Star,
            _ => PlaceholderArt.Shape.Disc,
        };

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
