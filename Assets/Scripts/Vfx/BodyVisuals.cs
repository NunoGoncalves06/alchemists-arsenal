using System.Collections.Generic;
using UnityEngine;
using AlchemistsArsenal.Art;
using AlchemistsArsenal.Combat;
using AlchemistsArsenal.PhysicsKit;

namespace AlchemistsArsenal.Vfx
{
    /// <summary>
    /// Makes a combatant read as alive. Every sprite in the arena used to be a
    /// frozen image sliding around: no walk, no facing, no reaction to a hit, and a
    /// body that simply vanished 1.2 s after dying. This adds:
    /// <list type="bullet">
    /// <item>a walk hop scaled by speed, and facing the way it moves;</item>
    /// <item>a squash and a white flash when hit (a baked silhouette, because a tint
    /// can only darken a sprite);</item>
    /// <item>Y-sorting, so whoever stands lower on screen draws in front;</item>
    /// <item>on death, a real physics tumble: the corpse moves to the Debris layer
    /// (it still bounces off the arena walls but no longer blocks anyone), is
    /// shoved away from the blow and spun, then fades.</item>
    /// </list>
    /// All of it moves only the visual "Art" child, except the death tumble, which is
    /// forces on the body's own Rigidbody2D once it is out of the fight.
    /// </summary>
    [DisallowMultipleComponent]
    public class BodyVisuals : MonoBehaviour
    {
        private Rigidbody2D _rb;
        private CombatantBody _body;
        private Transform _art;
        private SpriteRenderer _sprite, _flash;
        private Vector3 _artPos, _artScale;
        private float _phase, _squash, _flashT, _deadT = -1f;
        private bool _faceLeft;
        private Vector2 _lastHitFrom;

        private readonly List<(SpriteRenderer sr, int baseOrder)> _sorted = new List<(SpriteRenderer, int)>();

        /// <summary>Wire it up while the body is still inactive (before Awake).</summary>
        public static BodyVisuals Attach(GameObject root, Transform art)
        {
            var v = root.AddComponent<BodyVisuals>();
            v._art = art;
            return v;
        }

        private void Awake()
        {
            _rb = GetComponent<Rigidbody2D>();
            _body = GetComponent<CombatantBody>();
            if (_art == null) _art = transform;
            _sprite = _art.GetComponent<SpriteRenderer>();
            _artPos = _art.localPosition;
            _artScale = _art.localScale;

            if (_sprite != null)
            {
                Sprite white = PixelSprites.Silhouette(_sprite.sprite);
                if (white != null)
                {
                    var f = new GameObject("Flash");
                    f.transform.SetParent(_art, false);
                    _flash = f.AddComponent<SpriteRenderer>();
                    _flash.sprite = white;
                    Material m = SpriteMaterials.For(white);
                    if (m != null) _flash.sharedMaterial = m;
                    _flash.color = new Color(1f, 1f, 1f, 0f);
                }
            }

            // Everything drawn for this body at spawn time (sprite, flash, marker);
            // health bars come later and sort on their own band, above the field.
            foreach (var sr in GetComponentsInChildren<SpriteRenderer>(true))
            {
                int baseOrder = sr == _flash && _sprite != null ? _sprite.sortingOrder + 1 : sr.sortingOrder;
                _sorted.Add((sr, baseOrder));
            }
            _faceLeft = _body != null && _body.Team == Team.Monster; // monsters walk in from the right
        }

        private void OnEnable()
        {
            if (_body == null) return;
            _body.OnDamaged += OnHit;
            _body.OnDied += OnDied;
        }

        private void OnDisable()
        {
            if (_body == null) return;
            _body.OnDamaged -= OnHit;
            _body.OnDied -= OnDied;
        }

        private void OnHit(DamageInfo info)
        {
            if (info.Amount <= 0) return;
            _squash = 1f;
            _flashT = 1f;
            _lastHitFrom = info.SourcePoint;
        }

        private void OnDied(CombatantBody body)
        {
            _deadT = 0f;
            if (_rb == null) return;

            // Out of the fight: onto the Debris layer (walls yes, people no), free to
            // spin, and shoved away from whatever landed the blow.
            GameLayers.Assign(gameObject, GameLayers.Debris);
            _rb.freezeRotation = false;
            _rb.linearDamping = 2.2f;
            _rb.angularDamping = 1.6f;
            Vector2 away = _rb.position - _lastHitFrom;
            if (away.sqrMagnitude < 0.0001f) away = _faceLeft ? Vector2.right : Vector2.left;
            away.Normalize();
            _rb.AddForce(away * (3.2f * _rb.mass), ForceMode2D.Impulse);
            _rb.AddTorque((away.x >= 0f ? -1f : 1f) * 0.35f * _rb.mass, ForceMode2D.Impulse);

            if (VfxWorld.Active != null)
                VfxWorld.Active.Puff(_rb.position, new Color(0.25f, 0.21f, 0.28f, 0.45f), 5, 0.35f, 0.4f);
        }

        private void LateUpdate()
        {
            float dt = Time.deltaTime;
            Vector2 v = _rb != null ? _rb.linearVelocity : Vector2.zero;
            bool alive = _body == null || _body.IsAlive;

            float hop = 0f;
            if (alive)
            {
                float speed = v.magnitude;
                _phase += dt * (5f + speed * 2.2f);
                hop = Mathf.Abs(Mathf.Sin(_phase)) * Mathf.Min(1f, speed / 3.5f) * 0.09f;
                if (v.x < -0.25f) _faceLeft = true;
                else if (v.x > 0.25f) _faceLeft = false;
            }

            _squash = Mathf.Max(0f, _squash - dt * 6f);
            float sq = _squash * _squash;
            float sx = 1f + 0.22f * sq, sy = 1f - 0.18f * sq;

            float fade = 1f, shrink = 1f;
            if (_deadT >= 0f)
            {
                _deadT += dt;
                fade = Mathf.Clamp01(1f - _deadT / 1.05f);
                shrink = Mathf.Lerp(0.7f, 1f, fade);
            }

            _art.localPosition = _artPos + new Vector3(0f, hop, 0f);
            _art.localScale = new Vector3(_artScale.x * (_faceLeft ? -1f : 1f) * sx * shrink,
                _artScale.y * sy * shrink, _artScale.z);

            _flashT = Mathf.Max(0f, _flashT - dt * 8f);
            if (_flash != null) _flash.color = new Color(1f, 1f, 1f, 0.9f * _flashT * fade);

            // Y-sort: lower on screen draws in front. 8 orders per world unit keeps
            // each body's own layering (marker < sprite < flash) intact.
            int ySort = 100 - Mathf.RoundToInt(transform.position.y * 8f);
            for (int i = 0; i < _sorted.Count; i++)
            {
                var (sr, baseOrder) = _sorted[i];
                if (sr == null) continue;
                sr.sortingOrder = ySort + baseOrder;
                if (_deadT >= 0f && sr != _flash)
                {
                    Color c = sr.color;
                    if (c.a > fade) { c.a = fade; sr.color = c; }
                }
            }
        }
    }
}
