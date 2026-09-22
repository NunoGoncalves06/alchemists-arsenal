using System;
using System.Collections.Generic;
using UnityEngine;
using AlchemistsArsenal.Art;
using AlchemistsArsenal.Audio;
using AlchemistsArsenal.Combat;
using AlchemistsArsenal.Core;
using AlchemistsArsenal.Data;
using AlchemistsArsenal.PhysicsKit;
using AlchemistsArsenal.Systems;
using AlchemistsArsenal.Vfx;

namespace AlchemistsArsenal.Crafting
{
    /// <summary>
    /// Bottling as a physical bench, in the same three moves as before.
    /// <list type="number">
    /// <item><b>Pour.</b> Hold, and the ladle tips on its hinge (a
    /// <see cref="HingeJoint2D"/> turned by torque). The steeper it tips, the faster
    /// the brew runs off its lip as real droplets (<see cref="PourStream2D"/>) that
    /// fall into a glass flask built from edge colliders. Let go and it rights itself.
    /// How full the flask is, is how many droplets are in it; the glass draws the
    /// brew they make as a level that rises with them, and hides each droplet once
    /// it is under the surface. Poured to the brim, what still comes runs over the
    /// lip and down the outside of the glass onto the bench.</item>
    /// <item><b>Seal.</b> A cork waits on a <see cref="SliderJoint2D"/> above the neck.
    /// Seal on the beat and it is driven home into the neck; miss and it glances off
    /// the lip and springs back.</item>
    /// <item><b>Label.</b> A choice, not a physical act, so it stays in the HUD.</item>
    /// </list>
    /// Points are the button version's (<see cref="QualityBudget"/>).
    /// </summary>
    public class BottlingBench : MonoBehaviour
    {
        public static BottlingBench Instance { get; private set; }

        public enum Step { Pour, Seal, Label, Done }

        public const float BenchTopY = -1.9f;
        public const float TargetLow = 0.78f, TargetHigh = 0.96f;
        private const float FlaskScale = 1.5f, LadleScale = 1.4f;
        /// <summary>How far the ladle tips about its bowl (degrees, anticlockwise tips the lip down).</summary>
        private const float MaxTilt = 90f;
        /// <summary>Tilt (0..1 of MaxTilt) at which the brew first reaches the lip.</summary>
        private const float PourStart = 0.42f;
        /// <summary>The bowl's pouring lip, in the ladle body's local units (pivot = middle of the rim).</summary>
        private static readonly Vector2 LipLocal = new Vector2(-0.56f, 0.05f);

        /// <summary>A droplet's collider (world radius): small, so the stream falls cleanly through the neck.</summary>
        private const float DropletRadius = 0.02f;
        /// <summary>How big a droplet is drawn (world units across).</summary>
        private const float DropletDraw = 0.2f;
        /// <summary>
        /// The brew one droplet stands for, as the radius of a drop packed in a pile
        /// (circle packing ~0.82 in 2D). This, not the collider, sets how many
        /// droplets fill the flask to the line.
        /// </summary>
        private const float DropletVolumeRadius = 0.09f;

        private const int OrderLiquid = 11, OrderDroplets = 12, OrderCork = 15, OrderGlass = 16;

        /// <summary>Cork rest height above the mouth while it waits.</summary>
        private const float CorkRestAbove = 0.9f;
        /// <summary>The cork's centre when seated, in flask art rows: it stands a little proud of the lip.</summary>
        private const float CorkSeatRow = 3.2f;

        private PourStream2D _stream;
        private Transform _flask;
        private Rigidbody2D _ladle, _cork;
        private HingeJoint2D _hinge;
        private SliderJoint2D _slider;
        private SpriteRenderer _liquidArt;
        private Camera _cam;
        private float _emitDebt, _sinceLastDrop = 99f;
        private int _capacity = 98;
        private int _level = ShopArt.FlaskH;
        private int _overSide;
        private bool _pourScored;
        private Color _brewColor = Color.white;

        // Cached tests (no delegate allocated per step).
        private Func<Vector2, bool> _insideGlass, _underSurface, _onBench;
        private Func<Vector2, Vector2, bool> _comingDownNeck;
        private Func<Vector2, Vector2, (Vector2, Vector2)> _overLip;
        private float _surfaceY, _benchY;

        public bool Attended { get; set; }
        public Step Current { get; private set; } = Step.Pour;
        public int SealsLeft { get; private set; } = QualityBudget.SealAttempts;
        public float SealHalfWidth { get; private set; } = 0.09f;

        /// <summary>Held by the HUD button or by pressing on the ladle itself.</summary>
        public bool PourHeld { get; set; }

        /// <summary>0..1+ : droplets in the glass over what fills it to the line.</summary>
        public float Fill01 { get; private set; }
        public int Spilled { get; private set; }
        public float Tilt01 => _ladle != null ? Mathf.Clamp01(_ladle.rotation / MaxTilt) : 0f;

        /// <summary>The top row (flask art pixels) the drawn brew reaches; <see cref="ShopArt.FlaskH"/> is empty.</summary>
        public int LiquidLevelRow => _level;

        public Vector2 LadleWorld => _ladle != null ? _ladle.position : (Vector2)transform.position;
        public Vector2 FlaskMouthWorld => _flask != null ? FlaskLocalToWorld(ShopArt.FlaskCX, 3f) : (Vector2)transform.position;

        /// <summary>Where the cork is now, and where it sits when it is home in the neck.</summary>
        public Vector2 CorkWorld => _cork != null ? _cork.position : Vector2.zero;
        public Vector2 CorkSeatWorld => _flask != null ? FlaskLocalToWorld(ShopArt.FlaskCX, CorkSeatRow) : Vector2.zero;
        public bool CorkSeated { get; private set; }

        public event Action Changed;

        private static ActiveOrder Order => CraftingManager.Instance != null ? CraftingManager.Instance.CurrentOrder : null;

        private void Awake()
        {
            Instance = this;
            _insideGlass = InsideGlass;
            _underSurface = p => p.y < _surfaceY + 0.02f && InsideGlass(p);
            _onBench = p => p.y < _benchY && Mathf.Abs(p.x - BulbCentre.x) > BulbRadius + 0.1f;
            _comingDownNeck = (p, v) => v.y < 0f && InNeck(p) && p.y < _surfaceY + 0.1f;
            _overLip = (p, v) =>
            {
                float side = (_overSide++ & 1) == 0 ? 1f : -1f;
                float outside = ((ShopArt.NeckX1 + 1f - ShopArt.NeckX0) * 0.5f + 1.8f) / ShopArt.PPU * FlaskScale;
                Vector2 mouth = FlaskMouthWorld;
                return (new Vector2(mouth.x + side * outside, mouth.y + 0.08f), new Vector2(side * 0.9f, 0.3f));
            };
        }

        private void OnDestroy() { if (Instance == this) Instance = null; }

        // ------------------------------------------------------------------ build

        public void Build(Camera cam)
        {
            _cam = cam;

            var plank = new GameObject("Plank");
            plank.transform.SetParent(transform, false);
            plank.transform.localPosition = new Vector3(0f, BenchTopY, 0f);
            PixelArt.AddSprite(plank, ShopArt.Plank(), 2, 11.4f);
            var top = plank.AddComponent<EdgeCollider2D>();
            top.points = new[] { new Vector2(-6f, 0f), new Vector2(6f, 0f) };
            top.sharedMaterial = PhysicsMaterials.Wood;
            GameLayers.Assign(plank, GameLayers.ShopStatic);

            BuildFlask(new Vector2(0.6f, BenchTopY));
            BuildLadle();
            BuildCork();

            var streamGo = new GameObject("Stream");
            streamGo.transform.SetParent(transform, false);
            _stream = streamGo.AddComponent<PourStream2D>();
            _stream.Build(150, DropletRadius, DropletDraw, OrderDroplets);

            // What "full to the line" is: the bulb's area over what one droplet stands for.
            float bulbR = (ShopArt.FlaskBodyR - 1f) / ShopArt.PPU * FlaskScale;
            _capacity = Mathf.RoundToInt(Mathf.PI * bulbR * bulbR
                                         / (Mathf.PI * DropletVolumeRadius * DropletVolumeRadius / 0.82f));
        }

        private Vector2 FlaskLocalToWorld(float px, float py) =>
            (Vector2)_flask.position + new Vector2((px - ShopArt.FlaskW * 0.5f) / ShopArt.PPU, (ShopArt.FlaskH - py) / ShopArt.PPU) * FlaskScale;

        /// <summary>The flask art row the drawn brew reaches at a fill of <paramref name="fill01"/>.</summary>
        private static int RowAtFill(float fill01) => ShopArt.FlaskLevelRow(fill01 * ShopArt.FlaskBulbVolumePx);

        private void BuildFlask(Vector2 local)
        {
            var go = new GameObject("Flask");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = local;
            _flask = go.transform;

            AddArt(go.transform, ShopArt.FlaskBack(), 10, "GlassBack", FlaskScale);
            _liquidArt = AddArt(go.transform, ShopArt.FlaskLiquid(ShopArt.FlaskH), OrderLiquid, "Brew", FlaskScale);
            AddArt(go.transform, ShopArt.FlaskFront(), OrderGlass, "GlassFront", FlaskScale);
            // The line to pour to: ticks on the glass at the target band's two edges.
            AddArt(go.transform, ShopArt.FlaskMarks(RowAtFill(TargetLow), RowAtFill(TargetHigh)), OrderGlass + 1, "Line", FlaskScale);

            // The inside of the glass, lip to lip: down the neck, round the bulb, up the neck.
            var pts = new List<Vector2>();
            Vector2 P(float px, float py) => new Vector2((px - ShopArt.FlaskW * 0.5f) / ShopArt.PPU, (ShopArt.FlaskH - py) / ShopArt.PPU) * FlaskScale;
            float nl = ShopArt.NeckX0, nr = ShopArt.NeckX1 + 1f;
            pts.Add(P(nl - 1.5f, 1f)); pts.Add(P(nl, 3f)); pts.Add(P(nl, 14f));
            float r = ShopArt.FlaskBodyR;
            float a0 = Mathf.Atan2(14f - ShopArt.FlaskBodyCY, nl - ShopArt.FlaskCX);
            float a1 = Mathf.Atan2(14f - ShopArt.FlaskBodyCY, nr - ShopArt.FlaskCX);
            if (a0 < 0f) a0 += Mathf.PI * 2f;
            for (int i = 0; i <= 28; i++)
            {
                float a = Mathf.Lerp(a0, a1, i / 28f);
                pts.Add(P(ShopArt.FlaskCX + Mathf.Cos(a) * r, ShopArt.FlaskBodyCY + Mathf.Sin(a) * r));
            }
            pts.Add(P(nr, 14f)); pts.Add(P(nr, 3f)); pts.Add(P(nr + 1.5f, 1f));
            var edge = go.AddComponent<EdgeCollider2D>();
            edge.points = pts.ToArray();
            edge.edgeRadius = 0.04f;
            edge.sharedMaterial = PhysicsMaterials.Glass;
            GameLayers.Assign(go, GameLayers.ShopStatic);
        }

        private void BuildLadle()
        {
            // The bowl hangs just above and right of the mouth; tipped between ~40 and
            // 90 degrees its lip stays over the neck.
            Vector2 pivot = FlaskMouthWorld + new Vector2(0.3f, 1.1f);
            var go = new GameObject("Ladle");
            go.transform.SetParent(transform, false);
            go.transform.position = pivot;
            AddArt(go.transform, ShopArt.Ladle(), 18, "Art", LadleScale);

            _ladle = go.AddComponent<Rigidbody2D>();
            _ladle.gravityScale = 0f;
            _ladle.mass = 1f;
            _ladle.inertia = 0.35f;
            _ladle.angularDamping = 4f;

            // Pinned to the world at the bowl by a hinge; the tilt is a torque (see
            // FixedUpdate), not a motor, so its direction does not depend on the joint's
            // angle convention.
            _hinge = go.AddComponent<HingeJoint2D>();
            _hinge.autoConfigureConnectedAnchor = false;
            _hinge.anchor = Vector2.zero;
            _hinge.connectedAnchor = pivot;         // no connected body: pinned to the world
            _hinge.useLimits = false;
            _hinge.useMotor = false;
        }

        private Vector2 CorkRestWorld => FlaskMouthWorld + new Vector2(0f, CorkRestAbove);

        private void BuildCork()
        {
            Vector2 rest = CorkRestWorld;
            var go = new GameObject("Cork");
            go.transform.SetParent(transform, false);
            go.transform.position = rest;
            // Between the glass's back and front: seated, the lip and the neck's glass
            // edge draw over it, so it reads as in the neck rather than on top of it.
            AddArt(go.transform, ShopArt.Cork(), OrderCork, "Art", 1.5f);

            _cork = go.AddComponent<Rigidbody2D>();
            _cork.gravityScale = 0f;
            _cork.mass = 0.3f;
            _cork.freezeRotation = true;
            var col = go.AddComponent<BoxCollider2D>();
            col.size = new Vector2(0.55f, 0.55f);
            col.sharedMaterial = PhysicsMaterials.Cork;

            // The slider only keeps the cork on the neck's axis; the drive is a
            // world-space force (DriveCork), like the ladle's torque, so nothing
            // depends on the joint's sign conventions. A slider auto-configures its
            // angle by default, which overrode the 90 degrees set here and ran the
            // cork off sideways, up and to the right of the flask, on every seal.
            _slider = go.AddComponent<SliderJoint2D>();
            _slider.autoConfigureAngle = false;
            _slider.angle = 90f;
            _slider.autoConfigureConnectedAnchor = false;
            _slider.connectedAnchor = rest;
            _slider.useLimits = false;
            _slider.useMotor = false;
            GameLayers.Assign(go, GameLayers.ShopProp);
            col.excludeLayers |= GameLayers.LiquidMask;   // the brew never holds the cork up
            go.SetActive(false);   // appears once the pour is done
        }

        private static SpriteRenderer AddArt(Transform parent, Sprite s, int order, string name, float scale)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localScale = Vector3.one * scale;
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = s;
            sr.sharedMaterial = SpriteMaterials.For(s);
            sr.sortingOrder = order;
            return sr;
        }

        // ------------------------------------------------------------------- day

        public void NewDay()
        {
            Current = Step.Pour;
            SealsLeft = QualityBudget.SealAttempts;
            SealHalfWidth = 0.09f;
            Fill01 = 0f;
            Spilled = 0;
            _pourScored = false;
            PourHeld = false;
            if (_stream != null) _stream.ResetAll();
            ResetCork(active: false);
            if (_ladle != null) { _ladle.gameObject.SetActive(true); _ladle.rotation = 0f; _ladle.angularVelocity = 0f; }
            SetLevel(ShopArt.FlaskH);
            Changed?.Invoke();
        }

        /// <summary>Put the cork back on its rest over the neck, unseated, its slider re-anchored there.</summary>
        private void ResetCork(bool active)
        {
            if (_cork == null) return;
            CorkSeated = false;
            _corkTarget = CorkTarget.Rest;
            _corkDip = 0f;
            _cork.gameObject.SetActive(active);
            _cork.bodyType = RigidbodyType2D.Dynamic;
            Vector2 rest = CorkRestWorld;
            _slider.connectedAnchor = rest;
            _cork.position = rest;
            _cork.transform.position = rest;
            _cork.linearVelocity = Vector2.zero;
        }

        // ----------------------------------------------------------------- input

        private void Update()
        {
            var order = Order;
            if (order != null)
            {
                _brewColor = PixelArt.Element(order.element);
                if (_stream != null) _stream.SetColor(_brewColor);
            }

            // Pressing on the ladle itself pours too, not just the HUD button.
            if (Attended && Current == Step.Pour && order != null)
            {
                Vector2 p = Pointer.World(_cam);
                bool onLadle = Vector2.Distance(p, LadleWorld + new Vector2(0.6f, 0f)) < 1.3f;
                if (Pointer.PressedThisFrame && onLadle && !Pointer.OverUI) _pointerPour = true;
                if (!Pointer.Held) _pointerPour = false;
            }
            else _pointerPour = false;

            DrawBrew();
        }

        private bool _pointerPour;

        private bool Pouring => Attended && Current == Step.Pour && Order != null && (PourHeld || _pointerPour);

        /// <summary>
        /// The brew in the glass: a level that stands for the droplets in it, in the
        /// element's colour. A droplet is drawn only until it goes under that surface.
        /// It used to be the droplets alone, and with their colliders shrunk to a fifth
        /// of their size (see <see cref="PourStream2D"/>) a full flask was a thin film.
        /// </summary>
        private void DrawBrew()
        {
            if (_liquidArt == null || _flask == null) return;
            SetLevel(RowAtFill(Fill01));
            _liquidArt.color = _brewColor;
            _surfaceY = FlaskLocalToWorld(ShopArt.FlaskCX, _level).y;
            if (_stream != null) _stream.SetHidden(_underSurface);
        }

        private void SetLevel(int level)
        {
            if (_liquidArt == null || (level == _level && _liquidArt.sprite != null)) return;
            _level = level;
            _liquidArt.sprite = ShopArt.FlaskLiquid(level);
            _liquidArt.sharedMaterial = SpriteMaterials.For(_liquidArt.sprite);
        }

        private Vector2 BulbCentre => FlaskLocalToWorld(ShopArt.FlaskCX, ShopArt.FlaskBodyCY);
        private float BulbRadius => (ShopArt.FlaskBodyR - 0.5f) / ShopArt.PPU * FlaskScale;

        private bool InBulb(Vector2 p) => (p - BulbCentre).sqrMagnitude <= BulbRadius * BulbRadius;

        private bool InNeck(Vector2 p)
        {
            Vector2 lo = FlaskLocalToWorld(ShopArt.NeckX0, 15f), hi = FlaskLocalToWorld(ShopArt.NeckX1 + 1f, 2f);
            return p.x >= lo.x && p.x <= hi.x && p.y >= lo.y && p.y <= hi.y;
        }

        private bool InsideGlass(Vector2 p) => InBulb(p) || InNeck(p);

        // --------------------------------------------------------------- physics

        private readonly ImpulseQueue _impulses = new ImpulseQueue();

        private void FixedUpdate()
        {
            _impulses.Flush();
            float dt = Time.fixedDeltaTime;
            // Tip the ladle with a spring-damper torque toward the held / resting angle.
            if (_ladle != null)
            {
                bool pouring = Pouring;
                float target = pouring ? MaxTilt : 0f;
                // Critically damped in radians (torque = I * alpha, kd = 2 sqrt(kp)).
                // Tipping is steady (the error is capped); letting go rights it fast,
                // so the flow stops close to when you release.
                float kp = pouring ? 40f : 160f;
                float cap = pouring ? 35f : 90f;
                float err = Mathf.Clamp(Mathf.DeltaAngle(_ladle.rotation, target), -cap, cap) * Mathf.Deg2Rad;
                float w = _ladle.angularVelocity * Mathf.Deg2Rad;
                _ladle.AddTorque((kp * err - 2f * Mathf.Sqrt(kp) * w) * _ladle.inertia, ForceMode2D.Force);
            }

            // The brew runs off the lip once it is tipped far enough to reach it, and
            // faster the steeper the tilt. Below ~27 degrees nothing comes out (and the
            // lip would still be outside the neck).
            float tilt = Tilt01;
            if (Current == Step.Pour && tilt > PourStart && Order != null)
            {
                _emitDebt += dt * Mathf.Lerp(8f, 36f, Mathf.InverseLerp(PourStart, 1f, tilt));
                Vector2 lip = _ladle.transform.TransformPoint(LipLocal);
                while (_emitDebt >= 1f)
                {
                    _emitDebt -= 1f;
                    float j = ((_stream.Emitted * 37) % 11) / 11f - 0.5f;   // deterministic scatter
                    _stream.Emit(lip + new Vector2(j * 0.06f, -0.05f), new Vector2(j * 0.2f, -0.6f));
                }
                _sinceLastDrop = 0f;
            }
            else _sinceLastDrop += dt;

            MeasureFill();
            if (Current == Step.Pour) Overtop();

            // Scored once the ladle is back up and the flask has stopped moving.
            if (Current == Step.Pour && !_pourScored && !Pouring && _stream != null && _stream.Emitted > 0
                && tilt < 0.1f && _sinceLastDrop > 0.6f && _stream.Settled(0.6f))
                ScorePour();

            DriveCork(dt);
        }

        private void MeasureFill()
        {
            if (_stream == null || _flask == null) return;
            Fill01 = _stream.Count(_insideGlass) / (float)Mathf.Max(1, _capacity);
            _benchY = transform.position.y + BenchTopY + 0.35f;
            Spilled = _stream.Count(_onBench);
        }

        /// <summary>
        /// Brimful: the neck has no room left, so whatever still comes down it goes
        /// over the lip instead, alternately either side, and runs down the glass.
        /// </summary>
        private void Overtop()
        {
            if (_stream == null || _level > ShopArt.FlaskBrimRow) return;
            _surfaceY = FlaskLocalToWorld(ShopArt.FlaskCX, _level).y;
            _stream.Redirect(_comingDownNeck, _overLip);
        }

        private void ScorePour()
        {
            _pourScored = true;
            var order = Order;
            if (order == null) return;
            float fill = Fill01;
            int pct = Mathf.RoundToInt(fill * 100f);

            if (fill > 1f || Spilled > 8)
            {
                order.ApplyDeduction(QualityBudget.PourOverflow, "Bottling", "Overfilled — brew all over the bench");
                AudioManager.Play(Sfx.Deny);
            }
            else if (fill >= TargetLow && fill <= TargetHigh)
            {
                float centre = (TargetLow + TargetHigh) * 0.5f;
                float off = Mathf.Abs(fill - centre) / ((TargetHigh - TargetLow) * 0.5f);
                int gain = Mathf.RoundToInt(Mathf.Lerp(QualityBudget.PourMax, QualityBudget.PourMin, off));
                order.ApplyBonus(gain, "Bottling", $"Poured to the line ({pct}%)");
                AudioManager.Play(Sfx.Confirm);
            }
            else if (fill < TargetLow)
            {
                order.ApplyDeduction(QualityBudget.PourShort, "Bottling", $"Short measure ({pct}%) — the flask is half air");
                AudioManager.Play(Sfx.Deny);
            }

            Current = Step.Seal;
            // The ladle is put away so the cork can come down on the neck.
            if (_ladle != null) _ladle.gameObject.SetActive(false);
            ResetCork(active: true);
            Changed?.Invoke();
        }

        // ------------------------------------------------------------------ seal

        private enum CorkTarget { Rest, Seat }
        private CorkTarget _corkTarget = CorkTarget.Rest;
        private float _corkDip;

        /// <summary>The beat needle, 0..1 (the HUD draws it; the seal is judged on it).</summary>
        public static float SealNeedle01() => Mathf.PingPong(Time.unscaledTime * 0.75f, 1f);

        /// <summary>Drive the cork down. On the beat it seats; off it, it glances off and springs back.</summary>
        public void Seal()
        {
            var order = Order;
            if (order == null || Current != Step.Seal || SealsLeft <= 0) return;

            float dist = Mathf.Abs(SealNeedle01() - 0.5f);
            bool hit = dist <= SealHalfWidth;
            SealsLeft--;

            if (hit)
            {
                int gain = Mathf.RoundToInt(Mathf.Lerp(QualityBudget.SealMax, QualityBudget.SealMin, dist / Mathf.Max(0.001f, SealHalfWidth)));
                order.ApplyBonus(gain, "Bottling", $"Sealed clean (+{gain})");
                Current = Step.Label;
                _corkTarget = CorkTarget.Seat;
            }
            else
            {
                order.ApplyDeduction(QualityBudget.SealMiss, "Bottling", "Wax set off-centre");
                AudioManager.Play(Sfx.Deny);
                SealHalfWidth = Mathf.Max(0.05f, SealHalfWidth - 0.02f);
                // Glances off the lip: knocked down onto it, then straight back up.
                _corkDip = 0.14f;
                CameraRig.Shake(0.06f);
                if (SealsLeft <= 0)
                {
                    Current = Step.Label;   // out of attempts: it goes in anyway, the damage is done
                    _corkTarget = CorkTarget.Seat;
                }
            }
            Changed?.Invoke();
        }

        /// <summary>
        /// A critically damped world-space drive along the neck's axis toward the
        /// cork's target: the rest above the neck, the lip for a glancing miss, or the
        /// seat. Home, it is fixed exactly in place (kinematic), the way a pressed cork
        /// stays put.
        /// </summary>
        private void DriveCork(float dt)
        {
            if (_cork == null || !_cork.gameObject.activeInHierarchy || CorkSeated) return;

            Vector2 seat = CorkSeatWorld;
            float targetY;
            if (_corkTarget == CorkTarget.Seat) targetY = seat.y;
            else if (_corkDip > 0f) { targetY = FlaskMouthWorld.y + 0.34f; _corkDip -= dt; }
            else targetY = CorkRestWorld.y;

            const float kp = 420f;
            float kd = 2f * Mathf.Sqrt(kp);
            float err = targetY - _cork.position.y;
            float accel = Mathf.Clamp(kp * err - kd * _cork.linearVelocity.y, -160f, 160f);
            _cork.AddForce(new Vector2(0f, accel * _cork.mass), ForceMode2D.Force);

            if (_corkTarget == CorkTarget.Seat && Mathf.Abs(err) < 0.012f && Mathf.Abs(_cork.linearVelocity.y) < 0.25f)
            {
                CorkSeated = true;
                _cork.linearVelocity = Vector2.zero;
                _cork.bodyType = RigidbodyType2D.Kinematic;
                _cork.position = seat;
                _cork.transform.position = seat;
                AudioManager.Play(Sfx.Seal);
                if (VfxWorld.Active != null) VfxWorld.Active.Burst(FlaskMouthWorld, new Color(0.8f, 0.65f, 0.45f), 6, 1.2f);
                Changed?.Invoke();
            }
        }

        // ----------------------------------------------------------------- label

        public void ApplyLabel(ElementType element)
        {
            var order = Order;
            if (order == null || Current != Step.Label) return;

            if (element == order.element)
            {
                order.ApplyBonus(QualityBudget.LabelRight, "Bottling", $"Labelled {element} — correct");
                AudioManager.Play(Sfx.Chime);
            }
            else
            {
                order.ApplyDeduction(QualityBudget.LabelWrong, "Bottling",
                    $"Labelled {element} on a {order.element} flask — the party will grab the wrong one");
                AudioManager.Play(Sfx.Deny);
            }

            Current = Step.Done;
            if (CraftingManager.Instance != null) CraftingManager.Instance.CompleteActiveOrder();
            Changed?.Invoke();
        }
    }
}
