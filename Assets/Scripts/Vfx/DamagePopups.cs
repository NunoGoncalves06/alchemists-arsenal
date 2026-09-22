using System.Collections.Generic;
using UnityEngine;
using AlchemistsArsenal.Art;
using AlchemistsArsenal.Combat;

namespace AlchemistsArsenal.Vfx
{
    /// <summary>
    /// Floating damage numbers in chunky pixel digits. <see cref="CombatantBody.OnAnyDamaged"/>
    /// has been documented as feeding these since Phase 0, and nothing ever listened:
    /// a hit was indistinguishable from a miss unless you watched a health bar.
    /// White for a normal hit on a monster, gold for a big one, red for damage
    /// taken by the party. Pooled; lives under the arena root.
    /// </summary>
    public class DamagePopups : MonoBehaviour
    {
        private const int Pool = 40;
        private const float Life = 0.8f;
        private const int SortingOrder = 400;
        private const int BigHit = 45;

        private sealed class Popup
        {
            public Transform Root;
            public readonly SpriteRenderer[] Digits = new SpriteRenderer[4];
            public float Age = -1f;
            public Vector3 From;
            public Color Color;
            public float Scale;
        }

        private readonly List<Popup> _pool = new List<Popup>();
        private int _next;
        private float _jitter;

        public static DamagePopups Create(Transform worldRoot)
        {
            var go = new GameObject("~DamagePopups");
            go.transform.SetParent(worldRoot, false);
            return go.AddComponent<DamagePopups>();
        }

        private void Awake()
        {
            for (int i = 0; i < Pool; i++)
            {
                var p = new Popup { Root = new GameObject("Popup").transform };
                p.Root.SetParent(transform, false);
                for (int d = 0; d < p.Digits.Length; d++)
                {
                    var go = new GameObject("D" + d);
                    go.transform.SetParent(p.Root, false);
                    var sr = go.AddComponent<SpriteRenderer>();
                    sr.sortingOrder = SortingOrder;
                    p.Digits[d] = sr;
                }
                p.Root.gameObject.SetActive(false);
                _pool.Add(p);
            }
        }

        private void OnEnable() => CombatantBody.OnAnyDamaged += OnDamaged;
        private void OnDisable() => CombatantBody.OnAnyDamaged -= OnDamaged;

        private void OnDamaged(CombatantBody body, DamageInfo info)
        {
            if (body == null || info.Amount <= 0) return;
            Popup p = _pool[_next];
            _next = (_next + 1) % _pool.Count;

            string s = Mathf.Min(info.Amount, 9999).ToString();
            float w = PixelDigits.Width;
            for (int d = 0; d < p.Digits.Length; d++)
            {
                SpriteRenderer sr = p.Digits[d];
                bool used = d < s.Length;
                sr.enabled = used;
                if (!used) continue;
                Sprite glyph = PixelDigits.Digit(s[d] - '0');
                sr.sprite = glyph;
                Material m = SpriteMaterials.For(glyph);
                if (m != null) sr.sharedMaterial = m;
                sr.transform.localPosition = new Vector3((d - (s.Length - 1) * 0.5f) * w, 0f, 0f);
            }

            bool ally = body.Team == Team.Adventurer;
            p.Color = ally ? new Color(1f, 0.42f, 0.42f)
                : info.Amount >= BigHit ? new Color(1f, 0.84f, 0.32f)
                : Color.white;
            p.Scale = info.Amount >= BigHit ? 1.25f : 1f;
            // Spread consecutive numbers sideways a touch so a multi-hit reads as several.
            _jitter = (_jitter + 0.37f) % 1f;
            p.From = (Vector3)body.Position + new Vector3((_jitter - 0.5f) * 0.6f, 0.9f, 0f);
            p.Age = 0f;
            p.Root.gameObject.SetActive(true);
            Place(p);
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            for (int i = 0; i < _pool.Count; i++)
            {
                Popup p = _pool[i];
                if (p.Age < 0f) continue;
                p.Age += dt;
                if (p.Age >= Life)
                {
                    p.Age = -1f;
                    p.Root.gameObject.SetActive(false);
                    continue;
                }
                Place(p);
            }
        }

        private static void Place(Popup p)
        {
            float t = p.Age / Life;
            float rise = 1f - (1f - t) * (1f - t);           // ease out
            float pop = t < 0.12f ? Mathf.Lerp(1.5f, 1f, t / 0.12f) : 1f;
            p.Root.position = p.From + new Vector3(0f, rise * 0.75f, 0f);
            p.Root.localScale = Vector3.one * (p.Scale * pop);
            float a = t < 0.6f ? 1f : Mathf.Clamp01(1f - (t - 0.6f) / 0.4f);
            Color c = p.Color; c.a = a;
            for (int d = 0; d < p.Digits.Length; d++)
                if (p.Digits[d].enabled) p.Digits[d].color = c;
        }
    }

    /// <summary>
    /// A 3x5 pixel digit font with a 1-px dark outline (5x7 per glyph), baked once.
    /// The fill is white so a SpriteRenderer tint colours the number; the outline
    /// stays dark under any tint, which keeps it readable over any biome floor.
    /// </summary>
    public static class PixelDigits
    {
        private const float PPU = 20f;
        public static float Width => 4f / PPU;       // glyph advance (5 px, overlapping outlines by 1)

        private static readonly string[] Bitmaps =
        {
            "111101101101111", "010110010010111", "111001111100111", "111001111001111", "101101111001001",
            "111100111001111", "111100111101111", "111001010010010", "111101111101111", "111101111001111",
        };

        private static readonly Sprite[] _cache = new Sprite[10];

        public static Sprite Digit(int d)
        {
            d = Mathf.Clamp(d, 0, 9);
            if (_cache[d] != null) return _cache[d];

            const int w = 5, h = 7;
            var ink = new Color32(0x17, 0x11, 0x1c, 0xff);
            var fill = new Color32(0xff, 0xff, 0xff, 0xff);
            var px = new Color32[w * h];
            bool On(int x, int y) => x >= 0 && x < 3 && y >= 0 && y < 5 && Bitmaps[d][y * 3 + x] == '1';

            for (int ty = 0; ty < h; ty++)
            for (int tx = 0; tx < w; tx++)
            {
                int gx = tx - 1, gy = (h - 1 - ty) - 1;   // texture row 0 is the bottom
                if (On(gx, gy)) { px[ty * w + tx] = fill; continue; }
                bool edge = false;
                for (int oy = -1; oy <= 1 && !edge; oy++)
                    for (int ox = -1; ox <= 1 && !edge; ox++)
                        if (On(gx + ox, gy + oy)) edge = true;
                px[ty * w + tx] = edge ? ink : new Color32(0, 0, 0, 0);
            }

            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false)
            { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, name = "px_digit" + d };
            tex.SetPixels32(px);
            tex.Apply(false, true);
            _cache[d] = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), PPU);
            return _cache[d];
        }
    }
}
