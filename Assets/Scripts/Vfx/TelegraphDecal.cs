using UnityEngine;
using AlchemistsArsenal.Art;

namespace AlchemistsArsenal.Vfx
{
    /// <summary>
    /// A mark on the ground where a boss blow is going to land. The ring shows the
    /// whole reach from the first frame (so there is no guessing how far to step),
    /// the fill grows toward it as the blow comes and the ring beats faster; when the
    /// blow lands the mark flashes white and is gone. A ripple is the same mark
    /// running outward after a shockwave.
    ///
    /// It sits on the ground, under every body, and belongs to the arena, not to the
    /// boss, so it stays put while the boss moves and dies with the world.
    /// </summary>
    public class TelegraphDecal : MonoBehaviour
    {
        public const int Order = -4;

        /// <summary>The warning colour every mark shares, whatever the element.</summary>
        public static readonly Color Danger = new Color(1f, 0.36f, 0.26f, 1f);

        private SpriteRenderer _fill, _ring;
        private Color _color;
        private float _radius, _seconds, _t, _landT = -1f;
        private bool _ripple;

        public Vector2 Center => transform.position;
        public bool Landed => _landT >= 0f;

        public static TelegraphDecal Circle(Transform parent, Vector2 center, float radius, Color element, float seconds)
        {
            var d = Make(parent, center, "Telegraph");
            d._radius = Mathf.Max(0.2f, radius);
            d._seconds = Mathf.Max(0.05f, seconds);
            d._color = element;
            d.Apply(0f);
            return d;
        }

        /// <summary>A ring racing out to <paramref name="radius"/>: a shockwave's front.</summary>
        public static TelegraphDecal Ripple(Transform parent, Vector2 center, float radius, Color color)
        {
            var d = Make(parent, center, "Ripple");
            d._radius = Mathf.Max(0.2f, radius);
            d._seconds = 0.32f;
            d._color = color;
            d._ripple = true;
            d._fill.enabled = false;
            d.Apply(0f);
            return d;
        }

        private static TelegraphDecal Make(Transform parent, Vector2 center, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.position = new Vector3(center.x, center.y, 0f);
            var d = go.AddComponent<TelegraphDecal>();
            d._fill = Layer(go.transform, BossArt.Disc(), Order);
            d._ring = Layer(go.transform, BossArt.Ring(), Order + 1);
            return d;
        }

        private static SpriteRenderer Layer(Transform parent, Sprite sprite, int order)
        {
            var go = new GameObject(sprite.name);
            go.transform.SetParent(parent, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.sortingOrder = order;
            Material m = SpriteMaterials.For(sprite);
            if (m != null) sr.sharedMaterial = m;
            return sr;
        }

        /// <summary>The blow has landed on this mark.</summary>
        public void Land()
        {
            if (_landT < 0f) _landT = 0f;
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            if (_ripple)
            {
                _t += dt;
                float p = Mathf.Clamp01(_t / _seconds);
                _ring.transform.localScale = Vector3.one * (_radius * Mathf.Lerp(0.25f, 1f, 1f - (1f - p) * (1f - p)));
                _ring.color = WithAlpha(Color.Lerp(_color, Color.white, 0.5f), 0.9f * (1f - p));
                if (p >= 1f) Destroy(gameObject);
                return;
            }

            if (_landT < 0f)
            {
                _t += dt;
                Apply(Mathf.Clamp01(_t / _seconds));
                // A mark whose blow never came (the boss died with a volley in the air).
                if (_t > _seconds + 0.5f) Land();
                return;
            }

            _landT += dt;
            float f = 1f - _landT / 0.3f;
            _fill.transform.localScale = Vector3.one * _radius;
            _fill.color = new Color(1f, 1f, 1f, 0.5f * Mathf.Max(0f, f));
            _ring.transform.localScale = Vector3.one * (_radius * (1f + 0.15f * (1f - f)));
            _ring.color = new Color(1f, 1f, 1f, 0.9f * Mathf.Max(0f, f));
            if (f <= 0f) Destroy(gameObject);
        }

        private void Apply(float p)
        {
            _fill.transform.localScale = Vector3.one * (_radius * Mathf.Lerp(0.12f, 1f, p));
            _fill.color = WithAlpha(_color, Mathf.Lerp(0.14f, 0.36f, p));
            float beat = 0.5f + 0.5f * Mathf.Sin(_t * Mathf.Lerp(7f, 24f, p));
            _ring.transform.localScale = Vector3.one * _radius;
            _ring.color = WithAlpha(Danger, Mathf.Lerp(0.5f, 1f, beat * (0.4f + 0.6f * p)));
        }

        private static Color WithAlpha(Color c, float a) { c.a = a; return c; }
    }
}
