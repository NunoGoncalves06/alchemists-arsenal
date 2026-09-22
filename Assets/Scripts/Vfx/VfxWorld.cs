using UnityEngine;
using AlchemistsArsenal.Art;

namespace AlchemistsArsenal.Vfx
{
    /// <summary>
    /// A world's particle effects. Three code-built <see cref="ParticleSystem"/>s
    /// (sparks that fall, glows that add light, smoke that rises), fed one particle
    /// at a time through <see cref="ParticleSystem.Emit(ParticleSystem.EmitParams, int)"/>
    /// so every effect is a few lines here rather than a prefab.
    ///
    /// Lives under its world's root, so it is torn down with it and can never draw
    /// into the next world's first frame. Randomness comes from its own seeded
    /// stream, never UnityEngine.Random, so effects cannot shift a seeded fight.
    /// Particles have no collision module: they are pure presentation.
    /// </summary>
    public class VfxWorld : MonoBehaviour
    {
        public static VfxWorld Active { get; private set; }

        private ParticleSystem _sparks, _glow, _smoke;
        private System.Random _rng = new System.Random(4242);

        /// <summary>Sorting order effects draw at: above bodies, below health bars.</summary>
        public const int SortingOrder = 250;

        public static VfxWorld Create(Transform worldRoot, int seed = 4242)
        {
            var go = new GameObject("~Vfx");
            go.transform.SetParent(worldRoot, false);
            var v = go.AddComponent<VfxWorld>();
            v._rng = new System.Random(seed);
            return v;
        }

        private void Awake()
        {
            // Sparks stay square pixels. Glows and smoke are round: on the flat white
            // square every flask flash was an orange box the size of its blast.
            _sparks = Build("Sparks", SpriteMaterials.ParticleBlend.Alpha, null, gravity: 1.1f, SortingOrder, shrink: true);
            _glow = Build("Glow", SpriteMaterials.ParticleBlend.Additive, ParticleArt.GlowDot, gravity: 0f, SortingOrder + 1, shrink: false);
            _smoke = Build("Smoke", SpriteMaterials.ParticleBlend.Alpha, ParticleArt.Puff, gravity: -0.12f, SortingOrder - 1, shrink: false);
        }

        private void OnEnable() => Active = this;

        private void OnDestroy()
        {
            if (Active == this) Active = null;
        }

        private ParticleSystem Build(string name, SpriteMaterials.ParticleBlend blend, Texture texture, float gravity, int order,
            bool shrink)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.loop = false;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 1500;
            main.gravityModifier = gravity;
            main.startSpeed = 0f;
            main.startLifetime = 1f;

            var emission = ps.emission; emission.enabled = false;
            var shape = ps.shape; shape.enabled = false;

            var col = ps.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.55f), new GradientAlphaKey(0f, 1f) });
            col.color = new ParticleSystem.MinMaxGradient(g);

            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, shrink
                ? AnimationCurve.Linear(0f, 1f, 1f, 0.25f)
                : AnimationCurve.Linear(0f, 0.6f, 1f, 1.4f));

            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = SpriteMaterials.Particle(blend, texture);
            r.sortingOrder = order;
            r.renderMode = ParticleSystemRenderMode.Billboard;

            ps.Play();
            return ps;
        }

        // ------------------------------------------------------------ primitives

        private float R() => (float)_rng.NextDouble();
        private float R(float a, float b) => a + (b - a) * R();

        private Vector2 Dir()
        {
            float a = R() * Mathf.PI * 2f;
            return new Vector2(Mathf.Cos(a), Mathf.Sin(a));
        }

        private static void Emit(ParticleSystem ps, Vector2 pos, Vector2 vel, Color c, float size, float life)
        {
            if (ps == null) return;
            var p = new ParticleSystem.EmitParams
            {
                position = new Vector3(pos.x, pos.y, 0f),
                velocity = new Vector3(vel.x, vel.y, 0f),
                startColor = c,
                startSize = size,
                startLifetime = life,
                applyShapeToPosition = false,
            };
            ps.Emit(p, 1);
        }

        // ---------------------------------------------------------------- effects

        /// <summary>A flask bursting: a flash, a ring of sparks the size of the blast, smoke.</summary>
        public void Explosion(Vector2 pos, Color color, float radius)
        {
            float r = Mathf.Max(0.6f, radius);
            Emit(_glow, pos, Vector2.zero, WithAlpha(color, 0.7f), r * 2f, 0.22f);   // its rim is the blast edge
            Emit(_glow, pos, Vector2.zero, new Color(1f, 0.97f, 0.85f, 0.9f), r * 0.9f, 0.12f);
            int sparks = Mathf.RoundToInt(14 + r * 6);
            for (int i = 0; i < sparks; i++)
            {
                Vector2 d = Dir();
                Color c = i % 3 == 0 ? Color.Lerp(color, Color.white, 0.6f) : color;
                Emit(_sparks, pos + d * 0.1f, d * R(r * 2.2f, r * 4.6f) + Vector2.up * R(0.5f, 2f), c, R(0.08f, 0.16f), R(0.35f, 0.7f));
            }
            for (int i = 0; i < 5; i++)
                Emit(_smoke, pos + Dir() * R(0f, r * 0.5f), Dir() * R(0.2f, 0.8f),
                    new Color(0.24f, 0.21f, 0.27f, 0.42f), R(0.28f, 0.5f), R(0.6f, 1.1f));
        }

        /// <summary>A small burst: hits, splashes, dropped leaves.</summary>
        public void Burst(Vector2 pos, Color color, int count, float speed, float size = 0.1f, float life = 0.45f,
            bool glow = false)
        {
            for (int i = 0; i < count; i++)
                Emit(glow ? _glow : _sparks, pos, Dir() * R(speed * 0.4f, speed), color, R(size * 0.7f, size * 1.3f),
                    R(life * 0.7f, life * 1.2f));
        }

        /// <summary>Soft rising smoke / steam.</summary>
        public void Puff(Vector2 pos, Color color, int count = 4, float spread = 0.3f, float size = 0.6f)
        {
            for (int i = 0; i < count; i++)
                Emit(_smoke, pos + Dir() * R(0f, spread), new Vector2(R(-0.3f, 0.3f), R(0.3f, 0.9f)), color,
                    R(size * 0.7f, size * 1.3f), R(0.8f, 1.4f));
        }

        /// <summary>One drifting mote (embers, spores, snow, arcane dust).</summary>
        public void Mote(Vector2 pos, Vector2 drift, Color color, float size, float life, bool glow)
        {
            Emit(glow ? _glow : _smoke, pos, drift, color, size, life);
        }

        /// <summary>A glow that sits in place (eyes, a heart-knot, a fire's heart).</summary>
        public void Flash(Vector2 pos, Color color, float size, float life)
        {
            Emit(_glow, pos, Vector2.zero, color, size, life);
        }

        public float Random01() => R();

        private static Color WithAlpha(Color c, float a) { c.a = a; return c; }
    }
}
