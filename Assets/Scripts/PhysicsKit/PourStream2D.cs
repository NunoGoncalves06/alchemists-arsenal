using System.Collections.Generic;
using UnityEngine;
using AlchemistsArsenal.Art;

namespace AlchemistsArsenal.PhysicsKit
{
    /// <summary>
    /// A poured liquid as a pool of small physics droplets. Each droplet is a real
    /// circle body with gravity and no friction, so a pour arcs off the lip, splashes
    /// against the glass, and piles up in the flask the way a thick brew would. How
    /// full a vessel is, is simply how many droplets ended up inside it.
    ///
    /// Drawn as chunky square pixels in the brew's colour, which reads as a pixel-art
    /// liquid once they pile together.
    /// </summary>
    public class PourStream2D : MonoBehaviour
    {
        [SerializeField] private int poolSize = 150;
        [SerializeField] private float dropletRadius = 0.09f;
        [SerializeField] private int sortingOrder = 12;

        private readonly List<Rigidbody2D> _drops = new List<Rigidbody2D>();
        private readonly List<SpriteRenderer> _art = new List<SpriteRenderer>();
        private int _next;
        private Color _color = Color.white;

        public float DropletRadius => dropletRadius;
        public IReadOnlyList<Rigidbody2D> Droplets => _drops;
        public int Emitted { get; private set; }

        public void Build(int size, float radius, int order)
        {
            poolSize = size;
            dropletRadius = radius;
            sortingOrder = order;
            for (int i = 0; i < poolSize; i++)
            {
                var go = new GameObject("Drop");
                go.transform.SetParent(transform, false);
                var rb = go.AddComponent<Rigidbody2D>();
                rb.mass = 0.02f;
                rb.linearDamping = 0.15f;
                rb.freezeRotation = true;
                rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
                var col = go.AddComponent<CircleCollider2D>();
                col.radius = dropletRadius;
                col.sharedMaterial = PhysicsMaterials.Droplet;

                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = Core.PixelArt.White;
                sr.sharedMaterial = SpriteMaterials.For(sr.sprite);
                sr.sortingOrder = sortingOrder;
                go.transform.localScale = Vector3.one * (dropletRadius * 2.3f);

                GameLayers.Assign(go, GameLayers.Liquid);
                go.SetActive(false);
                _drops.Add(rb);
                _art.Add(sr);
            }
        }

        public void SetColor(Color c)
        {
            _color = c;
            for (int i = 0; i < _art.Count; i++) _art[i].color = Shade(i);
        }

        // A little per-droplet variation so the pile has texture, not a flat fill.
        private Color Shade(int i)
        {
            float k = 0.85f + ((i * 37) % 7) * 0.035f;
            return new Color(_color.r * k, _color.g * k, _color.b * k, 1f);
        }

        /// <summary>Release one droplet at <paramref name="pos"/> moving at <paramref name="vel"/>. Call from FixedUpdate.</summary>
        public bool Emit(Vector2 pos, Vector2 vel)
        {
            for (int tries = 0; tries < _drops.Count; tries++)
            {
                int i = (_next + tries) % _drops.Count;
                Rigidbody2D rb = _drops[i];
                if (rb.gameObject.activeSelf) continue;
                rb.gameObject.SetActive(true);
                rb.position = pos;
                rb.transform.position = pos;
                rb.linearVelocity = vel;
                _art[i].color = Shade(i);
                _next = (i + 1) % _drops.Count;
                Emitted++;
                return true;
            }
            return false;
        }

        public void ResetAll()
        {
            foreach (var rb in _drops) rb.gameObject.SetActive(false);
            Emitted = 0;
            _next = 0;
        }

        /// <summary>How many live droplets satisfy <paramref name="inside"/> (world position).</summary>
        public int Count(System.Func<Vector2, bool> inside)
        {
            int n = 0;
            foreach (var rb in _drops)
                if (rb.gameObject.activeSelf && inside(rb.position)) n++;
            return n;
        }

        /// <summary>True when no live droplet is still moving faster than <paramref name="speed"/>.</summary>
        public bool Settled(float speed)
        {
            float sq = speed * speed;
            foreach (var rb in _drops)
                if (rb.gameObject.activeSelf && rb.linearVelocity.sqrMagnitude > sq) return false;
            return true;
        }
    }
}
