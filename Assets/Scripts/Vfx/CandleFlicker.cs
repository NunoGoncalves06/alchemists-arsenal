using UnityEngine;

namespace AlchemistsArsenal.Vfx
{
    /// <summary>
    /// A candle's light that never quite holds still: its glow breathes and
    /// stutters on three out-of-step waves. Presentation only.
    /// </summary>
    public class CandleFlicker : MonoBehaviour
    {
        private SpriteRenderer _glow;
        private float _phase, _base;

        public static CandleFlicker Attach(SpriteRenderer glow, int seed)
        {
            var f = glow.gameObject.AddComponent<CandleFlicker>();
            f._glow = glow;
            f._phase = (seed * 0.618f) % 1f * 10f;
            f._base = glow.transform.localScale.x;
            return f;
        }

        private void Update()
        {
            if (_glow == null) return;
            float t = Time.time + _phase;
            float k = 0.8f + 0.12f * Mathf.Sin(t * 7.3f) + 0.08f * Mathf.Sin(t * 13.1f + 1.7f) + 0.05f * Mathf.Sin(t * 29f);
            Color c = _glow.color;
            c.a = Mathf.Clamp01(0.4f * k);
            _glow.color = c;
            _glow.transform.localScale = Vector3.one * (_base * (0.92f + 0.1f * k));
        }
    }
}
