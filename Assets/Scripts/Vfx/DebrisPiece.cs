using UnityEngine;
using AlchemistsArsenal.Art;
using AlchemistsArsenal.PhysicsKit;

namespace AlchemistsArsenal.Vfx
{
    /// <summary>
    /// A piece that has come off something: a boss's arm, a shard of its mask, a
    /// splinter of bark. A real body on the Debris layer (it hits the arena walls,
    /// the floor it was given and other debris, never a combatant), thrown, spun
    /// and left to gravity, then faded out. Nothing reads it; it is there so a boss
    /// comes apart like a thing with weight instead of blinking out.
    ///
    /// The arena has no floor (its plane is the ground seen from above), so a
    /// crumble lays a temporary one at the boss's feet with <see cref="Floor"/>.
    /// </summary>
    public class DebrisPiece : MonoBehaviour
    {
        private float _life, _t;
        private SpriteRenderer[] _renderers;
        private float[] _alpha;

        /// <summary>Tear <paramref name="part"/> off whatever it hangs from and throw it.</summary>
        public static Rigidbody2D Detach(Transform part, Transform newParent, Vector2 impulse, float spin, float life,
            float gravity = 1.6f)
        {
            if (part == null) return null;
            part.SetParent(newParent, worldPositionStays: true);
            GameObject go = part.gameObject;

            var rb = go.AddComponent<Rigidbody2D>();
            rb.gravityScale = gravity;
            rb.linearDamping = 0.15f;
            rb.angularDamping = 0.5f;
            rb.interpolation = RigidbodyInterpolation2D.Interpolate;

            var box = go.AddComponent<BoxCollider2D>();
            var sr = go.GetComponent<SpriteRenderer>();
            if (sr != null && sr.sprite != null)
            {
                Bounds b = sr.sprite.bounds;
                box.size = Vector2.Max(new Vector2(0.12f, 0.12f), (Vector2)b.size * 0.7f);
                box.offset = b.center;
            }
            box.sharedMaterial = PhysicsMaterials.Wood;
            GameLayers.Assign(go, GameLayers.Debris);

            rb.AddForce(impulse * rb.mass, ForceMode2D.Impulse);
            rb.AddTorque(spin * rb.mass, ForceMode2D.Impulse);

            var d = go.AddComponent<DebrisPiece>();
            d._life = life;
            return rb;
        }

        /// <summary>A loose chip (a new sprite, not a torn-off part) thrown from <paramref name="at"/>.</summary>
        public static Rigidbody2D Spawn(Transform parent, Sprite sprite, Vector2 at, int order, Vector2 impulse, float spin,
            float life, Color tint = default)
        {
            var go = new GameObject("Debris_" + (sprite != null ? sprite.name : "chip"));
            go.transform.SetParent(parent, false);
            go.transform.position = new Vector3(at.x, at.y, 0f);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.sortingOrder = order;
            if (tint != default) sr.color = tint;
            Material m = SpriteMaterials.For(sprite);
            if (m != null) sr.sharedMaterial = m;
            return Detach(go.transform, parent, impulse, spin, life);
        }

        /// <summary>
        /// A floor only debris can stand on, <paramref name="halfWidth"/> either side of
        /// <paramref name="center"/>, gone after <paramref name="life"/> seconds.
        /// </summary>
        public static GameObject Floor(Transform parent, Vector2 center, float halfWidth, float life)
        {
            var go = new GameObject("~DebrisFloor");
            go.transform.SetParent(parent, false);
            go.transform.position = new Vector3(center.x, center.y, 0f);
            var edge = go.AddComponent<EdgeCollider2D>();
            edge.points = new[] { new Vector2(-halfWidth, 0f), new Vector2(halfWidth, 0f) };
            edge.sharedMaterial = PhysicsMaterials.Stone;
            GameLayers.Assign(go, GameLayers.Debris);
            Destroy(go, life);
            return go;
        }

        private void Start()
        {
            _renderers = GetComponentsInChildren<SpriteRenderer>();
            _alpha = new float[_renderers.Length];
            for (int i = 0; i < _renderers.Length; i++) _alpha[i] = _renderers[i].color.a;
        }

        private void Update()
        {
            _t += Time.deltaTime;
            if (_t < _life) return;
            float f = 1f - (_t - _life) / 0.6f;
            for (int i = 0; i < _renderers.Length; i++)
            {
                if (_renderers[i] == null) continue;
                Color c = _renderers[i].color;
                c.a = Mathf.Min(c.a, _alpha[i] * Mathf.Max(0f, f));
                _renderers[i].color = c;
            }
            if (f <= 0f) Destroy(gameObject);
        }
    }
}
