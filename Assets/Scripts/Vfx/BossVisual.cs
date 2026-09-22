using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using AlchemistsArsenal.Art;
using AlchemistsArsenal.Combat;
using AlchemistsArsenal.Data;

namespace AlchemistsArsenal.Vfx
{
    /// <summary>
    /// A boss's body, as something you can read. The old boss was one sprite scaled
    /// 2.2x: it slid about, never wound up, never showed its phase, and blinked out
    /// when it died. This rig is built from <see cref="BossArt"/> parts and shows,
    /// without a single line of text:
    /// <list type="bullet">
    /// <item><b>Arrival</b>: the Woodwose tears up out of the ground; the Matriarch
    /// descends out of nothing.</item>
    /// <item><b>Life</b>: breathing, a heart-knot that beats, eyes that track the
    /// nearest hero, arms that never quite hang still.</item>
    /// <item><b>Intent</b>: every attack winds up in a pose that matches its shape
    /// (arms overhead for a slam, spread for a volley), and every spot it will hit
    /// is marked on the ground (<see cref="TelegraphDecal"/>) for the whole windup.</item>
    /// <item><b>Phase</b>: Enraged burns (cracks and sigils glow, eyes go red,
    /// embers rise); the Ward is a ring of runes in the warded element; Recovering
    /// slumps and sheds pieces of itself.</item>
    /// <item><b>Hurt</b>: a white flash and a stagger away from the blow.</item>
    /// <item><b>Death</b>: it comes apart into physics debris (<see cref="DebrisPiece"/>);
    /// the Matriarch's mask breaks first.</item>
    /// </list>
    /// Presentation only: it reads the boss (<see cref="BossPhaseManager"/>,
    /// <see cref="BossAttackExecutor"/>, <see cref="CombatantBody"/>) and never
    /// writes to it. The physics root stays unscaled; everything here hangs off an
    /// "Art" child that sits <see cref="ArtDrop"/> below it, so the collider sits in
    /// the lower body where the boss meets the ground.
    /// </summary>
    [DisallowMultipleComponent]
    public class BossVisual : MonoBehaviour
    {
        public const string Woodwose = "woodwose";
        public const string Matriarch = "matriarch";

        /// <summary>How far the feet are below the physics centre.</summary>
        public static float ArtDrop(string id) => id == Woodwose ? 1.05f : 1.2f;

        /// <summary>The physics footprint: well inside the drawn figure.</summary>
        public static float ColliderRadius(string id) => id == Woodwose ? 1.0f : 0.85f;

        /// <summary>Seconds the body stays after death, for the crumble to play out.</summary>
        public const float DeathSeconds = 3.2f;

        private sealed class Part
        {
            public Transform T;
            public SpriteRenderer Sr, Flash;
            public Vector3 Pos;
            public Color Tint = Color.white;
            public int Order;
        }

        private string _id;
        private Rigidbody2D _rb;
        private CombatantBody _body;
        private BossPhaseManager _boss;
        private BossAttackExecutor _exec;

        private Transform _art, _rig;
        private SortingGroup _group;
        private SpriteRenderer _shadow, _ward;
        private readonly List<Part> _parts = new List<Part>();
        private readonly List<TelegraphDecal> _decals = new List<TelegraphDecal>();

        // Woodwose
        private Part _wBody, _wHead, _wAntlers, _wArmF, _wArmB, _wRoots;
        private SpriteRenderer _wCracks, _wHeart;
        private readonly SpriteRenderer[] _wEyes = new SpriteRenderer[2];

        // Matriarch
        private Transform _mFloat;
        private Part _mRobe, _mHead, _mCrown;
        private readonly Part[] _mArms = new Part[6];
        private readonly Part[] _mEyes = new Part[7];
        private readonly SpriteRenderer[] _mMaskGlow = new SpriteRenderer[2];
        private SpriteRenderer _mSigils;
        private float _ringAngle;

        private static readonly Vector2[] MArmAnchor = { new Vector2(0.7f, 2.6f), new Vector2(0.95f, 2.25f), new Vector2(1.1f, 1.85f) };
        private static readonly float[] MArmRest = { 30f, 55f, 15f };
        private static readonly float[] MArmRaised = { 150f, 120f, 95f };
        private static readonly float[] MArmSpread = { 100f, 85f, 70f };
        private const float MRingCenterY = 3.46f, MRingRx = 2.1f, MRingRy = 0.55f;

        // state
        private enum Pose { Idle, Windup, Release }
        private Pose _pose;
        private float _poseT, _poseDur;
        private BossAttackShape _poseShape;
        private float _t, _walkPhase, _flashT, _staggerT, _deadT = -1f;
        private Vector2 _staggerDir;
        private float _enrage, _wardAmt, _slump, _track;
        private BossPhase _phase = BossPhase.Neutral;
        private Color _wardColor = Color.white;
        private int _groupOrder;
        private readonly System.Random _rng = new System.Random(77);

        // ================================================================= build

        /// <summary>Build the rig for <paramref name="def"/> on a boss body that is still inactive.</summary>
        public static BossVisual Attach(GameObject root, BossDefinition def)
        {
            var v = root.AddComponent<BossVisual>();
            v._id = def != null && def.VisualId == Woodwose ? Woodwose : Matriarch;
            v.Build();
            return v;
        }

        private void Build()
        {
            _art = new GameObject("Art").transform;
            _art.SetParent(transform, false);
            _art.localPosition = new Vector3(0f, -ArtDrop(_id), 0f);
            _group = _art.gameObject.AddComponent<SortingGroup>();

            _rig = new GameObject("Rig").transform;
            _rig.SetParent(_art, false);

            var sh = new GameObject("Shadow");
            sh.transform.SetParent(transform, false);
            sh.transform.localPosition = new Vector3(0f, -ArtDrop(_id) + 0.05f, 0f);
            _shadow = sh.AddComponent<SpriteRenderer>();
            SetSprite(_shadow, BossArt.Shadow());
            _shadow.sortingOrder = TelegraphDecal.Order - 1;
            sh.transform.localScale = new Vector3(_id == Woodwose ? 1.8f : 1.5f, 1.5f, 1f);

            if (_id == Woodwose) BuildWoodwose(); else BuildMatriarch();

            var w = new GameObject("Ward");
            w.transform.SetParent(_art, false);
            w.transform.localPosition = new Vector3(0f, _id == Woodwose ? 2.3f : 2.7f, 0f);
            _ward = w.AddComponent<SpriteRenderer>();
            SetSprite(_ward, BossArt.WardRunes());
            _ward.sortingOrder = 60;
            _ward.enabled = false;
        }

        private void BuildWoodwose()
        {
            _wRoots = AddPart("Roots", _rig, BossArt.WoodwoseRoots(), new Vector3(0f, 1.0f), 2);
            _wBody = AddPart("Body", _rig, BossArt.WoodwoseBody(), new Vector3(0f, 0.8f), 6);
            _wCracks = Overlay(_wBody.T, BossArt.WoodwoseCracks(), Vector3.zero, 14);
            _wCracks.enabled = false;
            _wHeart = Glow(_wBody.T, BossArt.GlowAmber(), BossArt.WHeartLocal, 15, 1.3f);

            // The arms and the head hang off the body, so they follow its lean.
            _wArmB = AddPart("ArmBack", _wBody.T, BossArt.WoodwoseArm(), new Vector3(1.22f, 2.1f), 1, new Color(0.68f, 0.68f, 0.72f));
            _wArmB.T.localScale = new Vector3(-1f, 1f, 1f);
            _wHead = AddPart("Head", _wBody.T, BossArt.WoodwoseHead(), new Vector3(0f, 2.25f), 10);
            _wAntlers = AddPart("Antlers", _wHead.T, BossArt.WoodwoseAntlers(), new Vector3(0f, 0.95f), 9);
            for (int i = 0; i < 2; i++)
                _wEyes[i] = Glow(_wHead.T, BossArt.GlowAmber(), BossArt.WEyesLocal[i], 22, 0.5f);
            _wArmF = AddPart("ArmFront", _wBody.T, BossArt.WoodwoseArm(), new Vector3(-1.22f, 2.1f), 12);
        }

        private void BuildMatriarch()
        {
            _mFloat = new GameObject("Float").transform;
            _mFloat.SetParent(_rig, false);
            _mFloat.localPosition = new Vector3(0f, 0.45f, 0f);

            for (int i = 0; i < _mEyes.Length; i++)
                _mEyes[i] = AddPart("Eye" + i, _mFloat, BossArt.Eye(), Vector3.zero, 1);

            for (int k = 0; k < 3; k++)
                for (int s = 0; s < 2; s++)
                {
                    float side = s == 0 ? -1f : 1f;
                    int order = k == 0 ? 3 : 7 + k;
                    Color tint = k == 0 ? new Color(0.62f, 0.6f, 0.7f) : Color.white;
                    var arm = AddPart($"Arm{k}{(s == 0 ? "L" : "R")}", _mFloat, BossArt.MatriarchArm(),
                        new Vector3(MArmAnchor[k].x * side, MArmAnchor[k].y), order, tint);
                    arm.T.localScale = new Vector3(side < 0 ? 1f : -1f, 1f, 1f);
                    _mArms[k * 2 + s] = arm;
                }

            _mRobe = AddPart("Robe", _mFloat, BossArt.MatriarchRobe(), Vector3.zero, 5);
            _mSigils = Overlay(_mRobe.T, BossArt.MatriarchSigilsHot(), Vector3.zero, 12);
            _mSigils.enabled = false;
            _mHead = AddPart("Head", _mFloat, BossArt.MatriarchHead(), new Vector3(0f, BossArt.MShoulderY - 0.35f), 11);
            _mCrown = AddPart("Crown", _mHead.T, BossArt.MatriarchCrown(), new Vector3(0f, 1.55f), 10);
            for (int i = 0; i < 2; i++)
            {
                _mMaskGlow[i] = Glow(_mHead.T, BossArt.GlowRed(), new Vector3((i == 0 ? -3f : 3f) / BossArt.PPU, BossArt.MMaskY + 2f / BossArt.PPU), 24, 0.35f);
                _mMaskGlow[i].enabled = false;
            }
        }

        private Part AddPart(string name, Transform parent, Sprite sprite, Vector3 localPos, int order, Color? tint = null)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            var p = new Part { T = go.transform, Pos = localPos, Order = order, Tint = tint ?? Color.white };
            p.Sr = go.AddComponent<SpriteRenderer>();
            SetSprite(p.Sr, sprite);
            p.Sr.color = p.Tint;

            Sprite white = PixelCanvas.SilhouetteOf(sprite);
            if (white != null)
            {
                var f = new GameObject("Flash");
                f.transform.SetParent(go.transform, false);
                p.Flash = f.AddComponent<SpriteRenderer>();
                SetSprite(p.Flash, white);
                p.Flash.color = new Color(1f, 1f, 1f, 0f);
            }
            SetOrder(p, order);
            _parts.Add(p);
            return p;
        }

        private static void SetOrder(Part p, int order)
        {
            p.Order = order;
            p.Sr.sortingOrder = order * 2;
            if (p.Flash != null) p.Flash.sortingOrder = order * 2 + 1;
        }

        private static SpriteRenderer Overlay(Transform parent, Sprite sprite, Vector3 localPos, int sortingOrder)
        {
            var go = new GameObject(sprite.name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            var sr = go.AddComponent<SpriteRenderer>();
            SetSprite(sr, sprite);
            sr.sortingOrder = sortingOrder;
            return sr;
        }

        private static SpriteRenderer Glow(Transform parent, Sprite sprite, Vector3 localPos, int sortingOrder, float scale)
        {
            var go = new GameObject("Glow");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = Vector3.one * scale;
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.sharedMaterial = SpriteMaterials.Particle(SpriteMaterials.ParticleBlend.Additive, sprite.texture);
            sr.sortingOrder = sortingOrder;
            return sr;
        }

        private static void SetSprite(SpriteRenderer sr, Sprite sprite)
        {
            sr.sprite = sprite;
            Material m = SpriteMaterials.For(sprite);
            if (m != null) sr.sharedMaterial = m;
        }

        // ================================================================ wiring

        private void Awake()
        {
            _rb = GetComponent<Rigidbody2D>();
            _body = GetComponent<CombatantBody>();
            _boss = GetComponent<BossPhaseManager>();
            _exec = GetComponent<BossAttackExecutor>();
        }

        private void OnEnable()
        {
            if (_body != null) { _body.OnDamaged += OnHit; _body.OnDied += OnDied; }
            if (_boss != null) _boss.OnPhaseChanged += OnPhase;
            if (_exec != null)
            {
                _exec.OnTelegraph += OnTelegraph;
                _exec.OnTelegraphCancelled += OnCancelled;
                _exec.OnFire += OnFire;
                _exec.OnStrike += OnStrike;
                _exec.OnProjectileLaunched += Dress;
            }
        }

        private void OnDisable()
        {
            if (_body != null) { _body.OnDamaged -= OnHit; _body.OnDied -= OnDied; }
            if (_boss != null) _boss.OnPhaseChanged -= OnPhase;
            if (_exec != null)
            {
                _exec.OnTelegraph -= OnTelegraph;
                _exec.OnTelegraphCancelled -= OnCancelled;
                _exec.OnFire -= OnFire;
                _exec.OnStrike -= OnStrike;
                _exec.OnProjectileLaunched -= Dress;
            }
        }

        private Transform Arena => transform.parent != null ? transform.parent : null;
        private Vector2 Feet => (Vector2)transform.position + new Vector2(0f, -ArtDrop(_id));
        private float R(float a, float b) => a + (float)_rng.NextDouble() * (b - a);
        private static Color ElementColor(ElementType e) => PlaceholderArt.ElementColor(e);
        private Color Signature => _id == Woodwose ? (Color)BossArt.Amber : BossArt.HexHot;

        private void OnHit(DamageInfo info)
        {
            if (info.Amount <= 0 || _deadT >= 0f) return;
            _flashT = 1f;
            if (info.Amount >= 12)
            {
                _staggerT = 1f;
                Vector2 away = (Vector2)transform.position - info.SourcePoint;
                _staggerDir = away.sqrMagnitude > 0.0001f ? away.normalized : Vector2.right;
            }
        }

        private void OnPhase(BossPhase from, BossPhase to)
        {
            _phase = to;
            var vfx = VfxWorld.Active;
            Vector2 chest = (Vector2)transform.position + new Vector2(0f, 1.3f);
            switch (to)
            {
                case BossPhase.Enraged:
                    CameraRig.Shake(0.35f);
                    vfx?.Burst(chest, _id == Woodwose ? (Color)BossArt.Ember : BossArt.HexHot, 26, 5f, 0.14f, 0.7f, glow: true);
                    break;
                case BossPhase.ElementalWard:
                    _wardColor = ElementColor(_boss != null ? _boss.WardElement : ElementType.Water);
                    vfx?.Burst(chest, _wardColor, 22, 4f, 0.12f, 0.6f, glow: true);
                    break;
                case BossPhase.Recovering:
                    if (from == BossPhase.ElementalWard)
                        vfx?.Burst(chest, _wardColor, 30, 6f, 0.12f, 0.5f);   // the ward breaks
                    Shed(4);
                    break;
            }
        }

        private void OnTelegraph(BossAttackPattern pattern, Vector2 origin, IReadOnlyList<Vector2> spots, float windup)
        {
            if (_deadT >= 0f) return;
            _pose = Pose.Windup;
            _poseT = 0f;
            _poseDur = windup;
            _poseShape = pattern.Shape;

            Color fill = ElementColor(pattern.Element);
            Vector2 from = _exec != null ? _exec.LaunchPoint(origin) : origin;
            foreach (Vector2 s in spots)
            {
                float live = pattern.Shape == BossAttackShape.Volley
                    ? windup + BossAttackExecutor.FlightSeconds(pattern, from, s)
                    : windup;
                _decals.Add(TelegraphDecal.Circle(Arena, s, pattern.AreaRadius, fill, live));
            }
        }

        private void OnCancelled()
        {
            _pose = Pose.Idle;
            foreach (var d in _decals) if (d != null && !d.Landed) Destroy(d.gameObject);
            _decals.Clear();
        }

        private void OnFire(BossAttackPattern pattern, Vector2 at)
        {
            _pose = Pose.Release;
            _poseT = 0f;
            _poseDur = pattern.Shape == BossAttackShape.Volley ? 0.35f : 0.45f;
            if (pattern.Shape == BossAttackShape.Volley)
            {
                VfxWorld.Active?.Flash(at, Signature, 1.6f, 0.18f);
                CameraRig.Shake(0.1f);
            }
        }

        private void OnStrike(BossAttackPattern pattern, Vector2 at, int caught)
        {
            LandDecalAt(at);
            var vfx = VfxWorld.Active;
            Color c = ElementColor(pattern.Element);
            Color dust = _id == Woodwose ? new Color(0.36f, 0.28f, 0.2f, 0.55f) : new Color(0.3f, 0.18f, 0.34f, 0.5f);
            switch (pattern.Shape)
            {
                case BossAttackShape.Shockwave:
                    TelegraphDecal.Ripple(Arena, at, pattern.AreaRadius, c);
                    for (int i = 0; i < 14; i++)
                    {
                        float a = i / 14f * Mathf.PI * 2f;
                        vfx?.Puff(at + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * pattern.AreaRadius * 0.8f, dust, 1, 0.2f, 0.7f);
                    }
                    vfx?.Burst(at, c, 24, pattern.AreaRadius * 2.2f, 0.12f, 0.5f);
                    CameraRig.Shake(0.45f + 0.08f * caught);
                    if (_id == Woodwose) Shed(3, at);
                    break;
                case BossAttackShape.Volley:
                    vfx?.Burst(at, _id == Woodwose ? c : (Color)BossArt.HexHot, 10, 3f, 0.1f, 0.4f, glow: _id != Woodwose);
                    vfx?.Puff(at, dust, 2, 0.2f, 0.5f);
                    CameraRig.Shake(0.06f + 0.06f * caught);
                    break;
                default:
                    vfx?.Burst(at, c, 18, pattern.AreaRadius * 2.6f, 0.12f, 0.5f);
                    vfx?.Puff(at, dust, 5, pattern.AreaRadius * 0.5f, 0.8f);
                    CameraRig.Shake(0.28f + 0.08f * caught);
                    break;
            }
        }

        private void LandDecalAt(Vector2 at)
        {
            TelegraphDecal best = null;
            float bestSq = 1.2f * 1.2f;
            for (int i = _decals.Count - 1; i >= 0; i--)
            {
                TelegraphDecal d = _decals[i];
                if (d == null) { _decals.RemoveAt(i); continue; }
                if (d.Landed) continue;
                float sq = (d.Center - at).sqrMagnitude;
                if (sq < bestSq) { bestSq = sq; best = d; }
            }
            if (best != null) best.Land();
        }

        /// <summary>Dress a projectile this boss just threw.</summary>
        private void Dress(BossProjectile2D p)
        {
            if (p == null) return;
            bool thorn = _id == Woodwose;
            p.FaceVelocity = thorn;
            var art = new GameObject("Art");
            art.transform.SetParent(p.transform, false);
            var sr = art.AddComponent<SpriteRenderer>();
            SetSprite(sr, thorn ? BossArt.Thorn() : BossArt.HexOrb());
            sr.sortingOrder = VfxWorld.SortingOrder - 2;
            var glow = Glow(p.transform, thorn ? BossArt.GlowAmber() : BossArt.GlowHex(), Vector3.zero, VfxWorld.SortingOrder - 3, thorn ? 0.6f : 1.1f);
            glow.color = new Color(1f, 1f, 1f, thorn ? 0.45f : 0.8f);

            var trail = p.gameObject.AddComponent<TrailRenderer>();
            trail.time = thorn ? 0.12f : 0.28f;
            trail.startWidth = thorn ? 0.12f : 0.26f;
            trail.endWidth = 0f;
            trail.minVertexDistance = 0.05f;
            trail.sharedMaterial = SpriteMaterials.Particle(SpriteMaterials.ParticleBlend.Additive);
            Color tc = thorn ? new Color(0.9f, 0.8f, 0.55f, 0.5f) : new Color(0.95f, 0.5f, 0.9f, 0.7f);
            trail.startColor = tc;
            trail.endColor = new Color(tc.r, tc.g, tc.b, 0f);
            trail.sortingOrder = VfxWorld.SortingOrder - 4;
        }

        /// <summary>Knock a few pieces off: bark for the Woodwose, rags for the Matriarch.</summary>
        private void Shed(int count, Vector2? from = null)
        {
            Transform arena = Arena;
            if (arena == null) return;
            Vector2 at = from ?? (Vector2)transform.position + new Vector2(0f, 1.4f);
            DebrisPiece.Floor(arena, Feet + new Vector2(0f, -0.2f), 4f, 3f);
            for (int i = 0; i < count; i++)
            {
                Sprite s = _id == Woodwose ? BossArt.BarkChip(i) : BossArt.Tatter(i);
                Vector2 kick = new Vector2(R(-3.5f, 3.5f), R(2.5f, 5.5f));
                DebrisPiece.Spawn(arena, s, at + new Vector2(R(-0.6f, 0.6f), R(-0.3f, 0.5f)), _groupOrder + 20, kick * 0.3f, R(-0.4f, 0.4f), 1.6f);
            }
        }

        // ============================================================= animation

        private void Start()
        {
            // Arrival: dust and a shake, whichever way it comes in.
            var vfx = VfxWorld.Active;
            if (_id == Woodwose)
            {
                vfx?.Burst(Feet, new Color(0.42f, 0.32f, 0.22f, 1f), 30, 6f, 0.14f, 0.7f);
                for (int i = 0; i < 8; i++) vfx?.Puff(Feet + new Vector2(R(-2f, 2f), R(-0.4f, 0.4f)), new Color(0.3f, 0.25f, 0.2f, 0.55f), 1, 0.3f, 0.9f);
                Shed(5, Feet + new Vector2(0f, 0.3f));
            }
            else
            {
                vfx?.Flash((Vector2)transform.position + Vector2.up * 2f, BossArt.HexHot, 5f, 0.4f);
            }
            CameraRig.Shake(0.55f);
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            _t += dt;
            _poseT += dt;
            if (_pose != Pose.Idle && _poseT >= _poseDur + (_pose == Pose.Windup ? 0.25f : 0f)) _pose = Pose.Idle;

            bool alive = _body == null || _body.IsAlive;
            float Ease(float cur, float target, float rate) => Mathf.MoveTowards(cur, target, rate * dt);
            _enrage = Ease(_enrage, alive && _phase == BossPhase.Enraged ? 1f : 0f, 3f);
            _wardAmt = Ease(_wardAmt, alive && _phase == BossPhase.ElementalWard ? 1f : 0f, 4f);
            _slump = Ease(_slump, alive && _phase == BossPhase.Recovering ? 1f : 0f, 2.5f);
            _flashT = Mathf.Max(0f, _flashT - dt * 8f);
            _staggerT = Mathf.Max(0f, _staggerT - dt * 5f);

            // Look toward whoever is nearest.
            float want = 0f;
            ICombatant near = NearestHero();
            if (near != null) want = Mathf.Clamp((near.Position.x - transform.position.x) * 0.5f, -1f, 1f);
            _track = Ease(_track, want, 2f);

            float speed = _rb != null && alive ? _rb.linearVelocity.magnitude : 0f;
            _walkPhase += dt * (2f + speed * 2.5f);

            if (_deadT >= 0f) { _deadT += dt; }
            if (_id == Woodwose) AnimateWoodwose(speed); else AnimateMatriarch(speed);

            AnimateWard(dt);
            AnimateFlash();
            Motes(dt);

            // Y-sort against everyone else by where its feet are.
            _groupOrder = 100 - Mathf.RoundToInt((Feet.y + 0.5f) * 8f) + 5;
            _group.sortingOrder = _groupOrder;
        }

        private float PoseP => _poseDur > 0f ? Mathf.Clamp01(_poseT / _poseDur) : 1f;
        private static float Smooth(float x) => x * x * (3f - 2f * x);
        private static float OutBack(float x)
        {
            const float c1 = 1.70158f, c3 = c1 + 1f;
            float u = x - 1f;
            return 1f + c3 * u * u * u + c1 * u * u;
        }

        private float Entrance01 => _boss != null ? _boss.Entrance01 : 1f;

        private void AnimateWoodwose(float speed)
        {
            if (_deadT >= 0f) { DieWoodwose(); return; }

            float br = Mathf.Sin(_t * 3.4f);
            float moving = Mathf.Min(1f, speed / 1.2f);
            float bob = Mathf.Abs(Mathf.Sin(_walkPhase)) * 0.07f * moving;
            float sway = Mathf.Sin(_t * 0.9f) * 1.5f + Mathf.Sin(_walkPhase) * 3f * moving;

            float armF = 0f, armB = 0f, lean = 0f, heart = 0f, squash = 0f;
            float p = PoseP;
            if (_pose == Pose.Windup)
            {
                float e = Smooth(p);
                if (_poseShape == BossAttackShape.Volley) { armF = -70f * e; armB = 70f * e; lean = 7f * e; heart = e; }
                else { armF = -155f * e; armB = 155f * e; lean = 9f * e; squash = -0.04f * e; heart = 0.4f * e; }
            }
            else if (_pose == Pose.Release)
            {
                if (_poseShape == BossAttackShape.Volley)
                {
                    float e = 1f - p;
                    armF = -70f * e; armB = 70f * e; lean = 7f * e - 5f * Mathf.Sin(p * Mathf.PI);
                }
                else
                {
                    // The slam: over in a third of the release, then a slow recovery.
                    float hit = Mathf.Clamp01(p / 0.3f), back = Mathf.Clamp01((p - 0.3f) / 0.7f);
                    armF = p < 0.3f ? Mathf.Lerp(-155f, 25f, hit) : Mathf.Lerp(25f, 0f, back);
                    armB = -armF;
                    lean = p < 0.3f ? Mathf.Lerp(9f, -10f, hit) : Mathf.Lerp(-10f, 0f, back);
                    squash = p < 0.3f ? 0.07f * hit : 0.07f * (1f - back);
                }
            }

            float swing = Mathf.Sin(_t * 1.1f) * 3f + Mathf.Sin(_walkPhase) * 6f * moving;
            armF += swing + 8f * _slump;
            armB += -swing - 8f * _slump;

            // Lean back = away from the hero it faces.
            float faceSign = _track <= 0f ? 1f : -1f;
            float bodyRot = -lean * faceSign * 0.7f + sway * 0.4f;

            _wBody.T.localPosition = _wBody.Pos + new Vector3(0f, bob - 0.12f * _slump, 0f);
            _wBody.T.localRotation = Quaternion.Euler(0f, 0f, bodyRot + (_staggerT * _staggerT) * 4f * -Mathf.Sign(_staggerDir.x));
            _wBody.T.localScale = new Vector3((1f + 0.012f * br) * (1f - squash * 0.6f), (1f + 0.02f * br) * (1f + squash), 1f);

            _wHead.T.localPosition = _wHead.Pos + new Vector3(_track * 0.07f, 0.04f * br - 0.1f * _slump, 0f);
            _wHead.T.localRotation = Quaternion.Euler(0f, 0f, Mathf.Sin(_t * 0.7f + 1f) * 2f + 12f * _slump * faceSign - _track * 3f);
            _wArmF.T.localRotation = Quaternion.Euler(0f, 0f, armF);
            _wArmB.T.localRotation = Quaternion.Euler(0f, 0f, armB);
            _wRoots.T.localScale = new Vector3(1f + 0.05f * Mathf.Sin(_walkPhase) * moving, 1f, 1f);

            // Arrival: it tears up out of the ground, too big for a moment, then settles.
            float ent = Entrance01;
            float grow = ent >= 1f ? 1f : Mathf.Max(0.05f, OutBack(ent));
            _rig.localScale = new Vector3(Mathf.Lerp(1.3f, 1f, Mathf.Min(1f, ent * 1.2f)), grow, 1f);
            _rig.localPosition = new Vector3(_staggerDir.x * 0.14f * _staggerT * _staggerT, 0f, 0f);

            // The heart beats: twice, then a rest. Faster, and red, when enraged.
            float period = Mathf.Lerp(1.15f, 0.6f, _enrage);
            float ph = (_t % period) / period;
            float beat = Mathf.Max(Pulse(ph, 0.05f), 0.7f * Pulse(ph, 0.22f));
            Color heartCol = Color.Lerp(BossArt.Amber, BossArt.Blood, _enrage);
            _wHeart.color = new Color(heartCol.r, heartCol.g, heartCol.b, Mathf.Clamp01(0.45f + 0.5f * beat + 0.3f * heart) * (1f - 0.5f * _slump));
            _wHeart.transform.localScale = Vector3.one * (1.2f + 0.35f * beat + 0.9f * heart);

            float eyes = Mathf.Clamp01((ent - 0.6f) / 0.4f) * (1f - 0.5f * _slump) * (0.85f + 0.15f * Mathf.Sin(_t * 9f));
            Color eyeCol = Color.Lerp(BossArt.Amber, BossArt.Blood, _enrage);
            for (int i = 0; i < 2; i++)
            {
                _wEyes[i].color = new Color(eyeCol.r, eyeCol.g, eyeCol.b, eyes);
                _wEyes[i].transform.localPosition = (Vector3)BossArt.WEyesLocal[i] + new Vector3(_track * 0.04f, 0f, 0f);
            }

            _wCracks.enabled = _enrage > 0.01f;
            if (_wCracks.enabled) _wCracks.color = new Color(1f, 1f, 1f, _enrage * (0.75f + 0.25f * Mathf.Sin(_t * 11f)));
        }

        private static float Pulse(float ph, float at) => Mathf.Clamp01(1f - Mathf.Abs(ph - at) / 0.08f);

        private void AnimateMatriarch(float speed)
        {
            if (_deadT >= 0f) { DieMatriarch(); return; }

            float p = PoseP;
            float lift = 0f;
            float[] spread = { MArmRest[0], MArmRest[1], MArmRest[2] };
            if (_pose == Pose.Windup)
            {
                float e = Smooth(p);
                float[] to = _poseShape == BossAttackShape.Volley ? MArmSpread : MArmRaised;
                for (int k = 0; k < 3; k++) spread[k] = Mathf.Lerp(MArmRest[k], to[k], e);
                lift = 0.3f * e;
            }
            else if (_pose == Pose.Release)
            {
                float[] from = _poseShape == BossAttackShape.Volley ? MArmSpread : MArmRaised;
                float hit = Mathf.Clamp01(p / 0.3f), back = Mathf.Clamp01((p - 0.3f) / 0.7f);
                for (int k = 0; k < 3; k++)
                    spread[k] = p < 0.3f ? Mathf.Lerp(from[k], 8f, hit) : Mathf.Lerp(8f, MArmRest[k], back);
                lift = p < 0.3f ? Mathf.Lerp(0.3f, -0.1f, hit) : Mathf.Lerp(-0.1f, 0f, back);
            }

            // Arrival: she comes down out of nothing.
            float ent = Entrance01;
            float down = ent >= 1f ? 0f : (1f - ent) * (1f - ent) * 5f;
            float floatY = Mathf.Lerp(0.45f + 0.12f * Mathf.Sin(_t * 1.3f), 0.15f, _slump) + lift + down;
            _mFloat.localPosition = new Vector3(_staggerDir.x * 0.14f * _staggerT * _staggerT, floatY, 0f);
            float fade = Mathf.Clamp01(ent * 1.6f);

            _mRobe.T.localScale = new Vector3(1f + 0.025f * Mathf.Sin(_t * 1.9f), 1f + 0.015f * Mathf.Sin(_t * 1.3f + 1f), 1f);
            _mHead.T.localPosition = _mHead.Pos + new Vector3(_track * 0.05f, 0.03f * Mathf.Sin(_t * 1.3f + 0.5f) - 0.08f * _slump, 0f);
            _mHead.T.localRotation = Quaternion.Euler(0f, 0f, 3f * Mathf.Sin(_t * 0.6f) - _track * 5f + 12f * _slump * (_track <= 0f ? 1f : -1f));

            float twitch = Mathf.Lerp(7f, 13f, _enrage), rate = Mathf.Lerp(1.5f, 3.2f, _enrage);
            for (int k = 0; k < 3; k++)
                for (int s = 0; s < 2; s++)
                {
                    Part arm = _mArms[k * 2 + s];
                    float side = s == 0 ? -1f : 1f;
                    float a = spread[k] - 12f * _slump + twitch * Mathf.Sin(_t * rate + k * 1.7f + s * 2.3f);
                    // A hanging arm turned by -a swings out to the left, by +a to the right.
                    arm.T.localRotation = Quaternion.Euler(0f, 0f, a * side);
                }

            // The ring of eyes turns behind her head; the near half passes in front.
            _ringAngle += Time.deltaTime * Mathf.Lerp(0.6f, 1.6f, _enrage) * (1f - 0.7f * _slump);
            Color iris = Color.Lerp(Color.white, new Color(1f, 0.55f, 0.55f), _enrage) * Mathf.Lerp(1f, 0.6f, _slump);
            for (int i = 0; i < _mEyes.Length; i++)
            {
                Part eye = _mEyes[i];
                float a = _ringAngle + i * Mathf.PI * 2f / _mEyes.Length;
                float sin = Mathf.Sin(a);
                eye.T.localPosition = new Vector3(Mathf.Cos(a) * MRingRx, MRingCenterY + sin * MRingRy, 0f);
                SetOrder(eye, sin > 0f ? 1 : 13);
                // Each eye blinks on its own slow clock.
                float blink = ((_t + i * 1.37f) % 4.1f) < 0.12f ? 0.15f : 1f;
                float size = 1f - 0.18f * sin;
                eye.T.localScale = new Vector3(size, size * blink, 1f);
                eye.Tint = new Color(iris.r, iris.g, iris.b, 1f);
            }

            bool hot = _enrage > 0.01f || (_pose == Pose.Windup && p > 0.4f);
            for (int i = 0; i < 2; i++)
            {
                _mMaskGlow[i].enabled = hot;
                if (hot) _mMaskGlow[i].color = new Color(1f, 1f, 1f, Mathf.Max(_enrage, _pose == Pose.Windup ? p : 0f) * (0.7f + 0.3f * Mathf.Sin(_t * 13f)));
            }
            _mSigils.enabled = _enrage > 0.01f;
            if (_mSigils.enabled) _mSigils.color = new Color(1f, 1f, 1f, _enrage * (0.7f + 0.3f * Mathf.Sin(_t * 7f)));

            SetAlpha(fade);
            _shadow.transform.localScale = new Vector3(1.5f / (1f + 0.4f * Mathf.Max(0f, floatY - 0.45f)), 1.5f, 1f);
            _shadow.color = new Color(1f, 1f, 1f, fade);
        }

        private void SetAlpha(float a)
        {
            foreach (Part part in _parts)
            {
                if (part.Sr == null) continue;
                Color c = part.Tint;
                c.a *= a;
                part.Sr.color = c;
            }
        }

        private void AnimateWard(float dt)
        {
            _ward.enabled = _wardAmt > 0.01f;
            if (!_ward.enabled) return;
            _ward.transform.localRotation = Quaternion.Euler(0f, 0f, _t * 25f);
            float s = 2.6f * Mathf.Lerp(0.6f, 1f, _wardAmt) * (1f + 0.03f * Mathf.Sin(_t * 5f));
            _ward.transform.localScale = new Vector3(s, s * 0.92f, 1f);
            Color c = _wardColor;
            c.a = _wardAmt * (0.55f + 0.2f * Mathf.Sin(_t * 4f));
            _ward.color = c;
        }

        private void AnimateFlash()
        {
            float a = 0.85f * _flashT;
            foreach (Part part in _parts)
                if (part.Flash != null) part.Flash.color = new Color(1f, 1f, 1f, a);
        }

        private float _moteClock;

        private void Motes(float dt)
        {
            var vfx = VfxWorld.Active;
            if (vfx == null || _deadT >= 0f) return;
            float rate = _enrage > 0.5f ? 14f : _slump > 0.5f ? 0f : 3f;
            _moteClock += dt * rate;
            while (_moteClock >= 1f)
            {
                _moteClock -= 1f;
                Vector2 at = Feet + new Vector2(R(-1.1f, 1.1f), R(0.8f, 3.2f));
                Color c = _id == Woodwose
                    ? (_enrage > 0.5f ? (Color)BossArt.Ember : new Color(0.55f, 0.75f, 0.35f, 0.8f))
                    : (Color)BossArt.HexPink;
                vfx.Mote(at, new Vector2(R(-0.3f, 0.3f), R(0.5f, 1.2f)), c, R(0.06f, 0.12f), R(0.8f, 1.4f), glow: true);
            }
        }

        private ICombatant NearestHero()
        {
            var list = AdventurerRegistry.ActiveAdventurers;
            ICombatant best = null;
            float bestSq = float.MaxValue;
            Vector2 at = transform.position;
            for (int i = 0; i < list.Count; i++)
            {
                ICombatant c = list[i];
                if (c == null || !c.IsAlive) continue;
                float sq = (c.Position - at).sqrMagnitude;
                if (sq < bestSq) { bestSq = sq; best = c; }
            }
            return best;
        }

        // ================================================================= death

        private void OnDied(CombatantBody body)
        {
            _deadT = 0f;
            OnCancelled();
            _ward.enabled = false;
            // Out of the fight at once: nothing walks into it or throws at it now.
            PhysicsKit.GameLayers.Assign(gameObject, PhysicsKit.GameLayers.Debris, includeChildren: false);
            if (_rb != null) { _rb.linearVelocity = Vector2.zero; _rb.bodyType = RigidbodyType2D.Kinematic; }

            var vfx = VfxWorld.Active;
            Vector2 chest = (Vector2)transform.position + new Vector2(0f, _id == Woodwose ? 1.3f : 2.5f);
            vfx?.Explosion(chest, Signature, 1.4f);
            vfx?.Flash(chest, Color.white, 4f, 0.25f);
            CameraRig.Shake(0.7f);
            DebrisPiece.Floor(Arena, Feet + new Vector2(0f, -0.15f), 6f, DeathSeconds + 1f);
            _deathStep = 0;
        }

        private int _deathStep;

        private void DieWoodwose()
        {
            float t = _deadT;
            // The heart flares, the eyes go out, then it comes apart.
            _wHeart.transform.localScale = Vector3.one * Mathf.Lerp(2.6f, 0.2f, Mathf.Clamp01(t / 0.6f));
            _wHeart.color = new Color(1f, 0.9f, 0.6f, Mathf.Clamp01(1f - t / 0.6f));
            foreach (var e in _wEyes) e.color = new Color(1f, 0.8f, 0.4f, Mathf.Clamp01(1f - t / 0.3f));

            if (_deathStep == 0 && t >= 0.3f)
            {
                _deathStep = 1;
                Transform arena = Arena;
                int order = _groupOrder;
                Throw(_wAntlers, arena, order, new Vector2(R(-2f, 2f), 5f), R(-3f, 3f));
                Throw(_wHead, arena, order, new Vector2(R(-1.5f, 1.5f), 3.5f), R(-2f, 2f));
                Throw(_wArmF, arena, order, new Vector2(-3f, 2.5f), 2f);
                Throw(_wArmB, arena, order, new Vector2(3f, 2.5f), -2f);
                Throw(_wBody, arena, order, new Vector2(_track <= 0f ? 1f : -1f, 1.2f), _track <= 0f ? -1.2f : 1.2f);
                Throw(_wRoots, arena, order, new Vector2(0f, 0.6f), 0f);
                for (int i = 0; i < 14; i++)
                    DebrisPiece.Spawn(arena, BossArt.BarkChip(i), (Vector2)transform.position + new Vector2(R(-1f, 1f), R(0f, 2.4f)),
                        order + 20, new Vector2(R(-4f, 4f), R(2f, 6f)) * 0.35f, R(-0.5f, 0.5f), 2.4f);
                var vfx = VfxWorld.Active;
                for (int i = 0; i < 10; i++)
                    vfx?.Puff(Feet + new Vector2(R(-2.2f, 2.2f), R(-0.2f, 0.4f)), new Color(0.32f, 0.26f, 0.2f, 0.6f), 1, 0.3f, 1f);
                CameraRig.Shake(0.5f);
            }
        }

        private void DieMatriarch()
        {
            float t = _deadT;
            Transform arena = Arena;
            int order = _groupOrder;
            var vfx = VfxWorld.Active;

            if (_deathStep == 0)
            {
                // The mask breaks first.
                _deathStep = 1;
                Vector3 maskAt = _mHead.T.TransformPoint(new Vector3(0f, BossArt.MMaskY, 0f));
                // A new sprite is a new texture: swap the material with it (SetSprite).
                SetSprite(_mHead.Sr, BossArt.MatriarchHoodEmpty());
                if (_mHead.Flash != null) SetSprite(_mHead.Flash, PixelCanvas.SilhouetteOf(_mHead.Sr.sprite));
                foreach (var g in _mMaskGlow) g.enabled = false;
                Vector2[] kicks = { new Vector2(-2.6f, 3.2f), new Vector2(2.6f, 3.4f), new Vector2(0.4f, 1.5f) };
                for (int i = 0; i < 3; i++)
                    DebrisPiece.Spawn(arena, BossArt.MaskShard(i), maskAt, order + 30, kicks[i], (i == 0 ? 1f : -1f) * 0.6f, DeathSeconds);
                vfx?.Flash(maskAt, Color.white, 3f, 0.3f);
                vfx?.Burst(maskAt, new Color(0.97f, 0.93f, 0.86f), 20, 5f, 0.1f, 0.6f);
            }
            if (_deathStep == 1 && t >= 0.15f)
            {
                _deathStep = 2;
                Throw(_mCrown, arena, order, new Vector2(R(-1f, 1f), 2.5f), R(-1.5f, 1.5f));
            }
            if (_deathStep == 2 && t >= 0.35f)
            {
                _deathStep = 3;
                for (int i = 0; i < _mArms.Length; i++)
                {
                    float side = (i % 2) == 0 ? -1f : 1f;
                    Throw(_mArms[i], arena, order, new Vector2(side * R(1.5f, 3f), R(1f, 3f)), side * R(0.5f, 1.5f));
                }
                for (int i = 0; i < 6; i++)
                    DebrisPiece.Spawn(arena, BossArt.Tatter(i), (Vector2)transform.position + new Vector2(R(-1.2f, 1.2f), R(0.2f, 2.2f)),
                        order + 20, new Vector2(R(-3f, 3f), R(1.5f, 4f)) * 0.3f, R(-0.4f, 0.4f), 2.2f);
            }

            // The eyes go out one by one.
            for (int i = 0; i < _mEyes.Length; i++)
            {
                Part eye = _mEyes[i];
                if (eye.Sr == null || !eye.Sr.enabled || t < 0.2f + i * 0.14f) continue;
                vfx?.Burst(eye.T.position, BossArt.HexHot, 8, 2.5f, 0.08f, 0.4f, glow: true);
                eye.Sr.enabled = false;
                if (eye.Flash != null) eye.Flash.enabled = false;
            }

            // The robe folds in on itself, empty, and the hood comes down with it.
            float c = Smooth(Mathf.Clamp01((t - 0.3f) / 1.3f));
            _mFloat.localPosition = new Vector3(0f, Mathf.Lerp(0.45f, 0f, c), 0f);
            float sy = Mathf.Lerp(1f, 0.14f, c);
            _mRobe.T.localScale = new Vector3(1f + 0.25f * c, sy, 1f);
            _mHead.T.localPosition = new Vector3(0f, (BossArt.MShoulderY - 0.35f) * sy, 0f);
            _mHead.T.localRotation = Quaternion.Euler(0f, 0f, 25f * c);
            float fade = 1f - Mathf.Clamp01((t - 1.7f) / 0.8f);
            _mRobe.Tint = Color.white; _mHead.Tint = Color.white;
            _mRobe.Sr.color = new Color(1f, 1f, 1f, fade);
            _mHead.Sr.color = new Color(1f, 1f, 1f, fade);
            if (_mSigils != null) _mSigils.enabled = false;
            _shadow.color = new Color(1f, 1f, 1f, fade);
        }

        /// <summary>Tear a part off the rig and throw it (a no-op for one already gone).</summary>
        private void Throw(Part part, Transform arena, int groupOrder, Vector2 impulse, float spin)
        {
            if (part == null || part.T == null || part.T.GetComponent<DebrisPiece>() != null) return;
            // Out of the sorting group now, so keep its place in the draw order by hand.
            foreach (var sr in part.T.GetComponentsInChildren<SpriteRenderer>(true))
                sr.sortingOrder += groupOrder;
            DebrisPiece.Detach(part.T, arena, impulse * 0.5f, spin * 0.3f, DeathSeconds - 0.6f);
        }
    }
}
