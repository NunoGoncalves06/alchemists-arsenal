using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using AlchemistsArsenal.Art;
using AlchemistsArsenal.Core;
using AlchemistsArsenal.Story;

namespace AlchemistsArsenal.UI
{
    /// <summary>
    /// A 16:9 picture that plays one <see cref="Shot"/>: the scene's backdrop, the
    /// people in it at the scene's own pixel scale, a slow push-in and pan, actors
    /// walking in and moving (breathing, trembling, floating, fading), and the
    /// shot's effect (rain on the window, motes, a flash, the cure poured out).
    ///
    /// UI-only and on unscaled time, so it plays the same paused or at 2x, and
    /// with Reduce Motion it holds still: no camera move, no shake, no drift.
    /// Randomness is its own seeded stream, so the harness's screenshots are
    /// repeatable.
    ///
    /// Everything is placed with anchors in 0..1 picture space, never with
    /// positions worked out from the frame's size in pixels: the frame is sized by
    /// layout, and a picture built from last frame's size came apart (actors
    /// drifting off the floor, rain outside the window) whenever the layout
    /// changed under it, such as on a window resize.
    /// </summary>
    public class StoryStage : MonoBehaviour
    {
        private RectTransform _frame, _content, _actorLayer, _fxLayer;
        private Image _backdrop, _flash, _fade;
        private readonly List<ActorView> _actors = new List<ActorView>();
        private readonly List<Particle> _particles = new List<Particle>();
        private Shot _shot;
        private float _t;
        private System.Random _rng = new System.Random(11);

        /// <summary>Seconds after a shot starts before everyone is in place.</summary>
        public const float SettleSeconds = 0.9f;

        /// <summary>Headless harness: start every shot already settled (its clock is wall time there).</summary>
        public static bool SkipIntro;
        public bool Settled => _shot != null && _t >= SettleSeconds + MaxDelay();

        private sealed class ActorView
        {
            public CutsceneActor Data;
            public RectTransform Rt;
            public Image Img;
        }

        private struct Particle
        {
            public Image Img;
            public Vector2 Pos, Vel;
            public float Life, Age, Blink;
        }

        public static StoryStage Create(Transform parent, string name = "Stage")
        {
            var root = UIFactory.Root(parent, name);
            var stage = root.gameObject.AddComponent<StoryStage>();
            stage.Build(root);
            return stage;
        }

        private void Build(RectTransform root)
        {
            // Keep 16:9 whatever the space we are given; the letterbox is the parent's.
            _frame = UIFactory.Root(root, "Frame");
            var fit = _frame.gameObject.AddComponent<AspectRatioFitter>();
            fit.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
            fit.aspectRatio = StoryArt.W / (float)StoryArt.H;
            _frame.gameObject.AddComponent<RectMask2D>();

            _content = UIFactory.Root(_frame, "Content");
            _backdrop = Img(_content, "Backdrop");
            UIFactory.Stretch(_backdrop.rectTransform);
            _actorLayer = UIFactory.Root(_content, "Actors");
            _fxLayer = UIFactory.Root(_content, "Fx");

            _flash = Img(_frame, "Flash");
            UIFactory.Stretch(_flash.rectTransform);
            _flash.sprite = PixelArt.White;
            _flash.color = new Color(1f, 1f, 1f, 0f);
            _fade = Img(_frame, "Fade");
            UIFactory.Stretch(_fade.rectTransform);
            _fade.sprite = PixelArt.White;
            _fade.color = new Color(0f, 0f, 0f, 0f);
        }

        private static Image Img(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            img.raycastTarget = false;
            return img;
        }

        /// <summary>Start a shot. <paramref name="seed"/> keeps its particles the same every time.</summary>
        public void Play(Shot shot, int seed = 11)
        {
            _shot = shot;
            _t = 0f;
            if (SkipIntro) foreach (var a in shot.Actors) _t = Mathf.Max(_t, a.Delay);
            if (SkipIntro) _t += SettleSeconds;
            _rng = new System.Random(seed);

            _backdrop.sprite = StoryArt.ScenePicture(shot.Scene);
            foreach (var a in _actors) if (a.Rt != null) Destroy(a.Rt.gameObject);
            _actors.Clear();
            foreach (var p in _particles) if (p.Img != null) Destroy(p.Img.gameObject);
            _particles.Clear();

            foreach (CutsceneActor data in shot.Actors)
            {
                Sprite sprite = StoryArt.ActorPicture(data.Id);
                if (sprite == null) continue;
                var img = Img(_actorLayer, data.Id);
                img.sprite = sprite;
                var rt = img.rectTransform;
                rt.pivot = new Vector2(sprite.pivot.x / sprite.rect.width, sprite.pivot.y / sprite.rect.height);
                _actors.Add(new ActorView { Data = data, Rt = rt, Img = img });
            }

            SpawnFx(shot.Fx);
            _fade.color = new Color(0f, 0f, 0f, 1f);   // every shot comes up out of black
            Apply(0f);
        }

        private float MaxDelay()
        {
            float d = 0f;
            if (_shot != null) foreach (var a in _shot.Actors) d = Mathf.Max(d, a.Delay);
            return d;
        }

        private void Update()
        {
            if (_shot == null) return;
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.1f);
            _t += dt;
            Apply(dt);
        }

        /// <summary>Stage pixels per art pixel, from the frame's current width.</summary>
        private float PixelScale => _frame.rect.width > 1f ? _frame.rect.width / StoryArt.W : 8f;

        private void Apply(float dt)
        {
            bool still = SettingsService.ReduceMotion;
            float w = _frame.rect.width, h = _frame.rect.height, px = PixelScale;

            // The camera: a slow push-in and drift across the whole shot.
            float k = still ? 0f : Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(_t / 7f));
            float zoom = still ? 1f : Mathf.Lerp(_shot.ZoomFrom, _shot.ZoomTo, k);
            Vector2 pan = still ? Vector2.zero : Vector2.Lerp(_shot.PanFrom, _shot.PanTo, k);
            Vector2 shake = Vector2.zero;
            if (!still && _shot.Fx == ShotFx.Shake && _t < 0.6f)
                shake = new Vector2(Mathf.Sin(_t * 71f), Mathf.Cos(_t * 53f)) * px * 2f * (1f - _t / 0.6f);
            _content.localScale = new Vector3(zoom, zoom, 1f);
            _content.anchorMin = -pan;
            _content.anchorMax = Vector2.one - pan;
            _content.anchoredPosition = shake;

            _fade.color = new Color(0f, 0f, 0f, Mathf.Clamp01(1f - _t / 0.35f));

            if (_shot.Fx == ShotFx.Flash)
            {
                float f = _t < 0.12f ? _t / 0.12f : Mathf.Clamp01(1f - (_t - 0.12f) / 0.6f);
                _flash.color = new Color(0.95f, 0.62f, 0.95f, 0.75f * f);
            }
            else _flash.color = new Color(1f, 1f, 1f, 0f);

            foreach (ActorView a in _actors) PlaceActor(a, w, h, px, still);
            StepFx(dt, w, h, px, still);
        }

        private void PlaceActor(ActorView a, float w, float h, float px, bool still)
        {
            CutsceneActor d = a.Data;
            Sprite s = a.Img.sprite;
            float t = Mathf.Max(0f, _t - d.Delay);
            float enter = d.From.HasValue && !still ? Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / SettleSeconds)) : 1f;
            Vector2 at = d.From.HasValue ? Vector2.Lerp(d.From.Value, d.At, enter) : d.At;

            // Size and place in picture space (art pixels over the picture's size).
            Vector2 size01 = new Vector2(s.rect.width * d.Scale / StoryArt.W, s.rect.height * d.Scale / StoryArt.H);
            Vector2 offset = Vector2.zero;   // in art pixels
            float rot = d.Rotation, sx = 1f, sy = 1f, alpha = Mathf.Clamp01(t / 0.3f);
            if (!still)
            {
                switch (d.Motion)
                {
                    case ActorMotion.Breathe: sy = 1f + 0.012f * Mathf.Sin(_t * 2.2f); break;
                    case ActorMotion.Bob: offset.y = Mathf.Abs(Mathf.Sin(_t * 5f)) * 1.5f; break;
                    case ActorMotion.Float: offset.y = Mathf.Sin(_t * 1.4f) * 2.5f; break;
                    case ActorMotion.Tremble: offset.x = (((int)(_t * 30f)) % 2 == 0 ? 1f : -1f) * 0.6f; break;
                    case ActorMotion.Tilt: rot += 70f * Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / 1.2f)); break;   // tipped toward the hearth
                    case ActorMotion.FadeOut:
                        float f = Mathf.Clamp01((t - 1.2f) / 2.4f);
                        alpha *= 1f - f;
                        offset.y = f * 10f;
                        break;
                }
            }
            else if (d.Motion == ActorMotion.Tilt) rot += 70f;
            else if (d.Motion == ActorMotion.FadeOut) alpha *= t > 2f ? 0.35f : 1f;

            Vector2 feet = at + new Vector2(offset.x / StoryArt.W, offset.y / StoryArt.H);
            Vector2 min = feet - Vector2.Scale(a.Rt.pivot, size01);
            a.Rt.anchorMin = min;
            a.Rt.anchorMax = min + size01;
            a.Rt.offsetMin = a.Rt.offsetMax = Vector2.zero;
            a.Rt.localRotation = Quaternion.Euler(0f, 0f, d.Flip ? -rot : rot);
            a.Rt.localScale = new Vector3(d.Flip ? -sx : sx, sy, 1f);
            Color c = d.Tint;
            c.a *= alpha;
            a.Img.color = c;
        }

        // ------------------------------------------------------------------ fx

        private float R(float a, float b) => a + (float)_rng.NextDouble() * (b - a);

        private void SpawnFx(ShotFx fx)
        {
            int n = fx switch
            {
                ShotFx.Rain => 42, ShotFx.Motes => 26, ShotFx.Embers => 20, ShotFx.Fireflies => 16, ShotFx.Pour => 18, ShotFx.Flash => 18,
                _ => 0,
            };
            for (int i = 0; i < n; i++) _particles.Add(NewParticle(fx, i, spawnAnywhere: true));
        }

        private Particle NewParticle(ShotFx fx, int i, bool spawnAnywhere)
        {
            var img = Img(_fxLayer, "p");
            img.sprite = PixelArt.White;
            var p = new Particle { Img = img, Blink = R(0f, 6.28f) };
            switch (fx)
            {
                case ShotFx.Rain:
                    // On the window glass only (the panes are x 137..175, y 17..49 of 192x108).
                    p.Pos = new Vector2(R(0.72f, 0.9f), spawnAnywhere ? R(0.56f, 0.83f) : 0.83f);
                    p.Vel = new Vector2(-0.01f, -0.35f);
                    p.Life = 99f;
                    img.color = new Color(0.62f, 0.72f, 0.9f, 0.55f);
                    break;
                case ShotFx.Embers:
                    p.Pos = new Vector2(R(0.2f, 0.32f), spawnAnywhere ? R(0.12f, 0.5f) : 0.12f);
                    p.Vel = new Vector2(R(-0.01f, 0.01f), R(0.04f, 0.08f));
                    p.Life = R(2f, 4f);
                    img.color = new Color(1f, 0.6f, 0.25f, 0.9f);
                    break;
                case ShotFx.Fireflies:
                    p.Pos = new Vector2(R(0.05f, 0.95f), R(0.15f, 0.7f));
                    p.Vel = new Vector2(R(-0.02f, 0.02f), R(-0.01f, 0.01f));
                    p.Life = 99f;
                    img.color = new Color(0.85f, 0.95f, 0.45f, 1f);
                    break;
                case ShotFx.Pour:
                    // From the tipped flask's mouth down into the fire.
                    p.Pos = new Vector2(R(0.236f, 0.25f), spawnAnywhere ? R(0.13f, 0.24f) : 0.24f);
                    p.Vel = new Vector2(R(-0.005f, 0.005f), -0.16f);
                    p.Life = 99f;
                    img.color = new Color(0.86f, 0.4f, 0.8f, 0.95f);
                    break;
                default: // motes, and the sparks of a flash
                    p.Pos = new Vector2(R(0.1f, 0.9f), spawnAnywhere ? R(0.05f, 0.8f) : 0.05f);
                    p.Vel = new Vector2(R(-0.01f, 0.01f), R(0.015f, 0.05f));
                    p.Life = R(3f, 6f);
                    p.Age = spawnAnywhere ? R(0f, p.Life) : 0f;
                    img.color = new Color(0.96f, 0.6f, 0.9f, 0.85f);
                    break;
            }
            return p;
        }

        private void StepFx(float dt, float w, float h, float px, bool still)
        {
            if (_particles.Count == 0) return;
            ShotFx fx = _shot.Fx;
            for (int i = 0; i < _particles.Count; i++)
            {
                Particle p = _particles[i];
                if (!still)
                {
                    p.Pos += p.Vel * dt;
                    p.Age += dt;
                }
                bool gone = p.Age > p.Life || p.Pos.y < 0f || p.Pos.y > 1f
                            || (fx == ShotFx.Rain && p.Pos.y < 0.56f) || (fx == ShotFx.Pour && p.Pos.y < 0.12f);
                if (gone && !still)
                {
                    Destroy(p.Img.gameObject);
                    p = NewParticle(fx, i, spawnAnywhere: false);
                }

                float size = fx == ShotFx.Rain ? 1f : fx == ShotFx.Fireflies ? 1.5f : 1f;
                var rt = p.Img.rectTransform;
                rt.sizeDelta = fx == ShotFx.Rain ? new Vector2(px, px * 3f) : new Vector2(px * size, px * size);
                rt.anchorMin = rt.anchorMax = p.Pos;
                rt.anchoredPosition = Vector2.zero;
                Color c = p.Img.color;
                if (fx == ShotFx.Fireflies) c.a = 0.35f + 0.65f * Mathf.Abs(Mathf.Sin(_t * 1.7f + p.Blink));
                else if (fx == ShotFx.Motes || fx == ShotFx.Flash || fx == ShotFx.Embers)
                    c.a = 0.85f * Mathf.Clamp01(Mathf.Min(p.Age / 0.5f, (p.Life - p.Age) / 1f));
                p.Img.color = c;
                _particles[i] = p;
            }
        }
    }
}
