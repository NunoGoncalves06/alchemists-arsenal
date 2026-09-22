using System;
using System.Collections.Generic;
using UnityEngine;

namespace AlchemistsArsenal.PhysicsKit
{
    /// <summary>
    /// Things floating on the surface of a round vessel, as real Rigidbody2D bodies.
    ///
    /// <para>The cauldron is drawn in three-quarter view, so its surface on screen is
    /// an ellipse. A vortex is a circle, though, and forces computed on the squashed
    /// ellipse would make herbs speed up and slow down as they went round. So the
    /// surface is simulated here, in its own "surface space": a true circle of
    /// <see cref="Radius"/>, parked well away from everything else and never drawn.
    /// The pot projects each body onto its ellipse for display. Everything in here is
    /// physics: bodies with colliders and mass, a ring they bounce off, a kinematic
    /// spoon that shoves them, forces and torque in FixedUpdate.</para>
    ///
    /// <para>What the physics decides: how fast each herb is riding the vortex
    /// (which is how fast it dissolves into the brew), and whether an over-hard stir
    /// flings one clean out of the pot.</para>
    /// </summary>
    public class LiquidBody2D : MonoBehaviour
    {
        public sealed class Floater
        {
            public Rigidbody2D Body;
            public CircleCollider2D Collider;
            public object Tag;
            public float Dissolve01;
            public bool Dissolved, Splashed;
            public Vector2 Local => Body != null ? Body.position - (Vector2)Owner.transform.position : Vector2.zero;
            public float Spin => Body != null ? Body.rotation : 0f;
            public Vector2 Velocity => Body != null ? Body.linearVelocity : Vector2.zero;
            internal LiquidBody2D Owner;
        }

        [SerializeField] private float radius = 1.8f;
        [Tooltip("How much of the spoon's angular speed the liquid picks up.")]
        [SerializeField] private float drag = 0.55f;
        [Tooltip("Stir rate (deg/s) above which the pot starts slopping herbs out.")]
        [SerializeField] private float splashSpin = 640f;
        [SerializeField] private float dissolvePerSecond = 0.28f;

        public float Radius => radius;

        /// <summary>Signed stir rate this frame (deg/s, positive = anticlockwise). Set in Update.</summary>
        public float SpinDegPerSec { get; set; }

        /// <summary>True while the spoon is turning the right way (herbs only dissolve then).</summary>
        public bool StirringCorrectly { get; set; }

        /// <summary>The spoon's position in surface space, or null while it is out of the pot.</summary>
        public Vector2? Spoon { get; set; }

        public readonly List<Floater> Floaters = new List<Floater>();

        public event Action<Floater> OnSplashedOut;
        public event Action<Floater> OnDissolved;

        private Rigidbody2D _spoon;
        private float _overstir, _splashCooldown;

        /// <summary>
        /// Of every herb put in, how much has dissolved (0..1). Splashed herbs count
        /// as nothing, which is the lasting cost of slopping one out. An empty pot
        /// counts as fully dissolved, so a brew with no herbs is not held back.
        /// </summary>
        public float DissolvedFraction
        {
            get
            {
                if (Floaters.Count == 0) return 1f;
                float sum = 0f;
                foreach (var f in Floaters) sum += f.Splashed ? 0f : f.Dissolve01;
                return sum / Floaters.Count;
            }
        }

        public void Build(float surfaceRadius)
        {
            radius = surfaceRadius;

            // The ring the herbs bounce off: the inside of the pot's wall.
            var ringGo = new GameObject("Ring");
            ringGo.transform.SetParent(transform, false);
            var ring = ringGo.AddComponent<EdgeCollider2D>();
            const int n = 48;
            var pts = new Vector2[n + 1];
            for (int i = 0; i <= n; i++)
            {
                float a = i / (float)n * Mathf.PI * 2f;
                pts[i] = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius;
            }
            ring.points = pts;
            ring.edgeRadius = 0.04f;
            ring.sharedMaterial = PhysicsMaterials.Iron;
            GameLayers.Assign(ringGo, GameLayers.ShopStatic);

            // The spoon: kinematic, moved to wherever the player holds it.
            var spoonGo = new GameObject("Spoon");
            spoonGo.transform.SetParent(transform, false);
            _spoon = spoonGo.AddComponent<Rigidbody2D>();
            _spoon.bodyType = RigidbodyType2D.Kinematic;
            _spoon.useFullKinematicContacts = true;
            spoonGo.AddComponent<CircleCollider2D>().radius = 0.2f;
            GameLayers.Assign(spoonGo, GameLayers.ShopProp);
            _spoon.position = (Vector2)transform.position + Vector2.one * (radius * 3f);
        }

        /// <summary>Drop a herb onto the surface at <paramref name="local"/> (surface space).</summary>
        public Floater Add(Vector2 local, float bodyRadius, float mass, object tag, float initialSpin = 0f)
        {
            var go = new GameObject("Floater");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = Vector2.ClampMagnitude(local, radius - bodyRadius - 0.05f);

            var rb = go.AddComponent<Rigidbody2D>();
            rb.gravityScale = 0f;
            rb.mass = mass;
            rb.linearDamping = 1.3f;
            rb.angularDamping = 1.1f;
            var col = go.AddComponent<CircleCollider2D>();
            col.radius = bodyRadius;
            col.sharedMaterial = PhysicsMaterials.Herb;
            GameLayers.Assign(go, GameLayers.ShopProp);
            rb.angularVelocity = initialSpin;

            var f = new Floater { Body = rb, Collider = col, Tag = tag, Owner = this };
            Floaters.Add(f);
            return f;
        }

        public void ClearAll()
        {
            foreach (var f in Floaters)
                if (f.Body != null) Destroy(f.Body.gameObject);
            Floaters.Clear();
        }

        private void FixedUpdate()
        {
            float dt = Time.fixedDeltaTime;
            Vector2 centre = transform.position;

            if (_spoon != null)
                _spoon.MovePosition(Spoon.HasValue ? centre + Spoon.Value : centre + Vector2.one * (radius * 3f));

            float omega = SpinDegPerSec * Mathf.Deg2Rad * drag;

            // Over-stirring: sustained, far past a full-power stir, the surface heaves
            // and the outermost herb goes over the lip.
            bool frantic = Mathf.Abs(SpinDegPerSec) > splashSpin;
            _overstir = frantic ? _overstir + dt : Mathf.Max(0f, _overstir - dt * 2f);
            _splashCooldown -= dt;
            if (_overstir > 0.35f && _splashCooldown <= 0f) FlingOutermost(centre);

            for (int i = 0; i < Floaters.Count; i++)
            {
                Floater f = Floaters[i];
                if (f.Body == null || f.Splashed || f.Dissolved) continue;
                Rigidbody2D rb = f.Body;
                Vector2 rel = rb.position - centre;
                float r = rel.magnitude;

                if (r > radius + 0.4f)
                {
                    // Gone over the side.
                    f.Splashed = true;
                    OnSplashedOut?.Invoke(f);
                    Destroy(rb.gameObject);
                    f.Body = null;
                    continue;
                }
                if (r < 0.001f) continue;

                Vector2 radial = rel / r;
                Vector2 tangent = new Vector2(-radial.y, radial.x);   // anticlockwise

                // Drag the herb toward the liquid's own speed at that radius.
                float vt = Vector2.Dot(rb.linearVelocity, tangent);
                float wanted = omega * Mathf.Min(r, radius * 0.85f);
                rb.AddForce(tangent * ((wanted - vt) * 3.2f * rb.mass), ForceMode2D.Force);

                // A vortex pulls inward: enough to keep a herb circling instead of
                // drifting to the wall, and a gentle settle toward mid-radius.
                if (Mathf.Abs(omega) > 0.05f)
                {
                    float hold = vt * vt / Mathf.Max(0.3f, r) * 0.85f;
                    float settle = (radius * 0.55f - r) * 0.9f;
                    rb.AddForce(radial * ((settle - hold) * rb.mass), ForceMode2D.Force);
                }

                // Spin with the swirl, well under the solver's rotation cap.
                if (Mathf.Abs(rb.angularVelocity) < 420f)
                    rb.AddTorque(omega * 0.9f * rb.inertia * 10f, ForceMode2D.Force);

                // A herb only gives itself up to the brew while the liquid is actually
                // moving through it.
                if (StirringCorrectly)
                {
                    float motion = Mathf.Clamp01(rb.linearVelocity.magnitude / 1.1f);
                    f.Dissolve01 = Mathf.Min(1f, f.Dissolve01 + dt * dissolvePerSecond * (0.25f + 0.75f * motion));
                    if (f.Dissolve01 >= 1f)
                    {
                        f.Dissolved = true;
                        OnDissolved?.Invoke(f);
                        Destroy(rb.gameObject);
                        f.Body = null;
                    }
                }
            }
        }

        /// <summary>Heave the outermost herb up and over the lip (its ring contact is dropped).</summary>
        private void FlingOutermost(Vector2 centre)
        {
            Floater pick = null;
            float best = -1f;
            foreach (var f in Floaters)
            {
                if (f.Body == null || f.Splashed || f.Dissolved) continue;
                float r = (f.Body.position - centre).magnitude;
                if (r > best) { best = r; pick = f; }
            }
            if (pick == null) return;

            _splashCooldown = 0.7f;
            _overstir = 0.15f;
            Vector2 rel = pick.Body.position - centre;
            Vector2 dir = rel.sqrMagnitude > 0.0001f ? rel.normalized : Vector2.up;
            pick.Collider.excludeLayers |= GameLayers.MaskOf(GameLayers.ShopStatic);
            pick.Body.AddForce(dir * (5.5f * pick.Body.mass), ForceMode2D.Impulse);
        }
    }
}
