using UnityEngine;
using AlchemistsArsenal.Art;
using AlchemistsArsenal.Core;

namespace AlchemistsArsenal.Combat
{
    /// <summary>
    /// A world-space health bar that floats above a <see cref="CombatantBody"/>.
    ///
    /// The HUD only ever showed the party's health and the boss's, so every ordinary
    /// monster was an opaque blob: you could not tell a Treant that was one hit from
    /// dying from one at full health, which makes the whole fight read as random.
    /// Built from sprites rather than a world-space Canvas — a Canvas per monster is
    /// a layout rebuild per monster per frame.
    /// </summary>
    public class HealthBar2D : MonoBehaviour
    {
        private CombatantBody _body;
        private Transform _fill;
        private float _width = 0.9f;
        private float _height = 0.13f;
        private float _lift = 0.62f;
        private float _last = -1f;

        /// <summary>
        /// Attach a bar to <paramref name="body"/>. <paramref name="width"/> and
        /// <paramref name="lift"/> scale with the combatant — the boss needs a much
        /// wider bar, further above its head.
        /// </summary>
        public static HealthBar2D Attach(CombatantBody body, float width = 0.9f, float lift = 0.62f,
            int sortingOrder = 20)
        {
            if (body == null) return null;

            var go = new GameObject("HealthBar");
            go.transform.SetParent(body.transform, worldPositionStays: false);
            var bar = go.AddComponent<HealthBar2D>();
            bar._body = body;
            bar._width = width;
            bar._lift = lift;

            // Track behind, fill in front; the track is a touch larger so it reads as
            // a frame rather than two bars.
            bar.MakeQuad("Track", new Color(0.06f, 0.05f, 0.08f, 0.85f), sortingOrder,
                width + 0.08f, bar._height + 0.06f, Vector3.zero);
            bar._fill = bar.MakeQuad("Fill", UnityEngine.Color.white, sortingOrder + 1,
                width, bar._height, Vector3.zero);

            go.transform.localPosition = new Vector3(0f, lift, 0f);
            bar.Apply(1f);
            return bar;
        }

        private Transform MakeQuad(string name, Color color, int sortingOrder, float w, float h, Vector3 pos)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.transform.localPosition = pos;

            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = PixelArt.White;
            sr.color = color;
            sr.sortingOrder = sortingOrder;
            Material mat = PixelSprites.MaterialFor(sr.sprite);   // per-texture, never one shared material
            if (mat != null) sr.sharedMaterial = mat;

            // PixelArt.White is 4px at 4 PPU = 1 world unit, so scale IS the size.
            go.transform.localScale = new Vector3(w, h, 1f);
            return go.transform;
        }

        private void LateUpdate()
        {
            if (_body == null || _fill == null) { Destroy(gameObject); return; }

            if (!_body.IsAlive)
            {
                if (_last != 0f) Apply(0f);
                return;
            }

            float f = _body.MaxHP > 0 ? Mathf.Clamp01((float)_body.CurrentHP / _body.MaxHP) : 0f;
            if (!Mathf.Approximately(f, _last)) Apply(f);

            // The parent may be scaled (the boss is 2.2x) — keep the bar's own size
            // and keep it upright regardless of what the body does.
            Vector3 parentScale = transform.parent != null ? transform.parent.lossyScale : Vector3.one;
            float inv = parentScale.x > 0.001f ? 1f / parentScale.x : 1f;
            transform.localScale = new Vector3(inv, inv, 1f);
            transform.localPosition = new Vector3(0f, _lift * inv, 0f);
            transform.rotation = Quaternion.identity;
        }

        private void Apply(float fraction01)
        {
            _last = fraction01;

            // Shrink from the right: scale the fill and shift it left by half of what
            // it lost, so the bar empties toward the centre-left like a real gauge.
            float w = _width * fraction01;
            _fill.localScale = new Vector3(w, _height, 1f);
            _fill.localPosition = new Vector3(-(_width - w) * 0.5f, 0f, 0f);

            var sr = _fill.GetComponent<SpriteRenderer>();
            if (sr != null)
                sr.color = fraction01 > 0.5f ? new Color(0.33f, 0.75f, 0.36f)
                    : fraction01 > 0.25f ? new Color(0.90f, 0.72f, 0.25f)
                    : new Color(0.84f, 0.27f, 0.31f);
        }
    }
}
