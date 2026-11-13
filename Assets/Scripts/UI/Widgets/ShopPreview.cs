using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace AlchemistsArsenal.UI
{
    /// <summary>
    /// A shop card's picture put together from the real art, the way it stands on the
    /// bench: each layer at its own place and size in world units, drawn in order, then the
    /// whole thing scaled once to fit the card. Layers are never stretched to the card
    /// separately, which is what piled the gantry, the gear and the paddle on the pot.
    /// </summary>
    public sealed class ShopPreview : MonoBehaviour
    {
        /// <summary>One sprite: where its pivot is (world units from the object's origin) and how big it is drawn.</summary>
        public readonly struct Layer
        {
            public readonly Sprite Sprite;
            public readonly Vector2 World;
            public readonly float Scale;
            public Layer(Sprite sprite, Vector2 world, float scale = 1f) { Sprite = sprite; World = world; Scale = scale; }
        }

        private const float Margin = 0.92f;   // the picture fills this much of the card

        /// <summary>The scale and shift that centre <paramref name="content"/> in a card of size <paramref name="box"/>, keeping its shape.</summary>
        public static (float scale, Vector2 offset) Fit(Rect content, Vector2 box)
        {
            float scale = Mathf.Min(box.x * Margin / Mathf.Max(0.0001f, content.width),
                                    box.y * Margin / Mathf.Max(0.0001f, content.height));
            return (scale, -content.center * scale);
        }

        private RectTransform _group;
        private Rect _content;

        /// <summary>Build <paramref name="layers"/> (back to front) inside <paramref name="box"/>, refitting if the card is resized.</summary>
        public static void Compose(RectTransform box, IList<Layer> layers)
        {
            var view = box.gameObject.AddComponent<ShopPreview>();
            var groupGo = new GameObject("Picture", typeof(RectTransform));
            groupGo.transform.SetParent(box, false);
            view._group = (RectTransform)groupGo.transform;
            view._group.anchorMin = view._group.anchorMax = new Vector2(0.5f, 0.5f);
            view._group.sizeDelta = Vector2.zero;

            bool any = false;
            Rect content = default;
            foreach (Layer l in layers)
            {
                if (l.Sprite == null) continue;
                Vector2 size = l.Sprite.rect.size / l.Sprite.pixelsPerUnit * l.Scale;
                Vector2 pivot = l.Sprite.pivot / l.Sprite.rect.size;
                var go = new GameObject(l.Sprite.name, typeof(RectTransform));
                go.transform.SetParent(view._group, false);
                var rt = (RectTransform)go.transform;
                rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.pivot = pivot;
                rt.sizeDelta = size;
                rt.anchoredPosition = l.World;
                var img = go.AddComponent<Image>();
                img.sprite = l.Sprite;
                img.raycastTarget = false;

                var r = new Rect(l.World - Vector2.Scale(pivot, size), size);
                if (!any) { content = r; any = true; }
                else content = Rect.MinMaxRect(Mathf.Min(content.xMin, r.xMin), Mathf.Min(content.yMin, r.yMin),
                                               Mathf.Max(content.xMax, r.xMax), Mathf.Max(content.yMax, r.yMax));
            }
            view._content = content;
            view.Refit();
        }

        private void Refit()
        {
            Vector2 box = ((RectTransform)transform).rect.size;
            if (_group == null || box.x < 1f || box.y < 1f) return;
            var (scale, offset) = Fit(_content, box);
            _group.localScale = new Vector3(scale, scale, 1f);
            _group.anchoredPosition = offset;
        }

        private void OnRectTransformDimensionsChange() => Refit();

        private void Start() => Refit();
    }
}
