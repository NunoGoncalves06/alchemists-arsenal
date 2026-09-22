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
    /// <item><b>Pour.</b> Hold, and the ladle tips on its hinge (a motor on a
    /// <see cref="HingeJoint2D"/>). The steeper it tips, the faster the brew runs
    /// off its lip as real droplets (<see cref="PourStream2D"/>) that fall into a
    /// glass flask built from edge colliders. Let go and it rights itself. How full
    /// the flask is, is how many droplets are in its bulb; pour past the line and
    /// they overtop the neck and run down the glass onto the bench.</item>
    /// <item><b>Seal.</b> A cork waits on a <see cref="SliderJoint2D"/> above the neck.
    /// Seal on the beat and its motor drives it home; miss and it glances off the
    /// lip and springs back.</item>
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

        private PourStream2D _stream;
        private Transform _flask;
        private Rigidbody2D _ladle, _cork;
        private HingeJoint2D _hinge;
        private SliderJoint2D _slider;
        private Camera _cam;
        private float _emitDebt, _sinceLastDrop = 99f, _corkTimer;
        private int _capacity = 98;
        private bool _pourScored;

        public bool Attended { get; set; }
        public Step Current { get; private set; } = Step.Pour;
        public int SealsLeft { get; private set; } = QualityBudget.SealAttempts;
        public float SealHalfWidth { get; private set; } = 0.09f;

        /// <summary>Held by the HUD button or by pressing on the ladle itself.</summary>
        public bool PourHeld { get; set; }

        /// <summary>0..1+ : droplets in the bulb over what fills it to the line.</summary>
        public float Fill01 { get; private set; }
        public int Spilled { get; private set; }
        public float Tilt01 => _ladle != null ? Mathf.Clamp01(_ladle.rotation / MaxTilt) : 0f;

        public Vector2 LadleWorld => _ladle != null ? _ladle.position : (Vector2)transform.position;
        public Vector2 FlaskMouthWorld { get; private set; }

        public event Action Changed;

        private static ActiveOrder Order => CraftingManager.Instance != null ? CraftingManager.Instance.CurrentOrder : null;

        private void Awake() => Instance = this;
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
            _stream.Build(150, 0.09f, 12);

            // What "full to the line" is: the bulb's area over what one droplet takes
            // up in a random pile (circle packing ~0.82 in 2D).
            float bulbR = (ShopArt.FlaskBodyR - 1f) / ShopArt.PPU * FlaskScale;
            _capacity = Mathf.RoundToInt(Mathf.PI * bulbR * bulbR / (Mathf.PI * 0.09f * 0.09f / 0.82f));
        }

        private Vector2 FlaskLocalToWorld(float px, float py) =>
            (Vector2)_flask.position + new Vector2((px - ShopArt.FlaskW * 0.5f) / ShopArt.PPU, (ShopArt.FlaskH - py) / ShopArt.PPU) * FlaskScale;

        private void BuildFlask(Vector2 local)
        {
            var go = new GameObject("Flask");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = local;
            _flask = go.transform;

            AddArt(go.transform, ShopArt.FlaskBack(), 10, "GlassBack", FlaskScale);
            AddArt(go.transform, ShopArt.FlaskFront(), 16, "GlassFront", FlaskScale);

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

            FlaskMouthWorld = FlaskLocalToWorld(ShopArt.FlaskCX, 3f);
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

        private void BuildCork()
        {
            Vector2 rest = FlaskMouthWorld + new Vector2(0f, 0.9f);
            var go = new GameObject("Cork");
            go.transform.SetParent(transform, false);
            go.transform.position = rest;
            AddArt(go.transform, ShopArt.Cork(), 17, "Art", 1.5f);

            _cork = go.AddComponent<Rigidbody2D>();
            _cork.gravityScale = 0f;
            _cork.mass = 0.3f;
            _cork.freezeRotation = true;
            var col = go.AddComponent<BoxCollider2D>();
            col.size = new Vector2(0.55f, 0.55f);
            col.sharedMaterial = PhysicsMaterials.Cork;

            _slider = go.AddComponent<SliderJoint2D>();
            _slider.autoConfigureConnectedAnchor = false;
            _slider.connectedAnchor = rest;
            _slider.angle = 90f;
            _slider.useLimits = true;
            _slider.limits = new JointTranslationLimits2D { min = -0.95f, max = 0f };
            _slider.useMotor = true;
            _slider.motor = new JointMotor2D { motorSpeed = 3f, maxMotorTorque = 40f };
            GameLayers.Assign(go, GameLayers.ShopProp);
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
            if (_cork != null) _cork.gameObject.SetActive(false);
            if (_ladle != null) { _ladle.gameObject.SetActive(true); _ladle.rotation = 0f; _ladle.angularVelocity = 0f; }
            Changed?.Invoke();
        }

        // ----------------------------------------------------------------- input

        private void Update()
        {
            var order = Order;
            if (order != null && _stream != null) _stream.SetColor(PixelArt.Element(order.element));

            // Pressing on the ladle itself pours too, not just the HUD button.
            if (Attended && Current == Step.Pour && order != null)
            {
                Vector2 p = Pointer.World(_cam);
                bool onLadle = Vector2.Distance(p, LadleWorld + new Vector2(0.6f, 0f)) < 1.3f;
                if (Pointer.PressedThisFrame && onLadle && !Pointer.OverUI) _pointerPour = true;
                if (!Pointer.Held) _pointerPour = false;
            }
            else _pointerPour = false;

            _corkTimer -= Time.deltaTime;
        }

        private bool _pointerPour;

        private bool Pouring => Attended && Current == Step.Pour && Order != null && (PourHeld || _pointerPour);

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

            // Scored once the ladle is back up and the flask has stopped moving.
            if (Current == Step.Pour && !_pourScored && !Pouring && _stream != null && _stream.Emitted > 0
                && tilt < 0.1f && _sinceLastDrop > 0.6f && _stream.Settled(0.6f))
                ScorePour();

            if (Current == Step.Seal && _corkSeating && _slider != null
                && _slider.limitState == JointLimitState2D.LowerLimit && _corkTimer <= 0f)
            {
                _corkSeating = false;
                AudioManager.Play(Sfx.Seal);
                if (VfxWorld.Active != null) VfxWorld.Active.Burst(FlaskMouthWorld, new Color(0.8f, 0.65f, 0.45f), 6, 1.2f);
            }
        }

        private void MeasureFill()
        {
            if (_stream == null || _flask == null) return;
            Vector2 bulbC = FlaskLocalToWorld(ShopArt.FlaskCX, ShopArt.FlaskBodyCY);
            float bulbR = (ShopArt.FlaskBodyR - 0.5f) / ShopArt.PPU * FlaskScale;
            Vector2 neckLo = FlaskLocalToWorld(ShopArt.NeckX0, 15f), neckHi = FlaskLocalToWorld(ShopArt.NeckX1 + 1f, 2f);

            int inBulb = _stream.Count(p => (p - bulbC).sqrMagnitude <= bulbR * bulbR);
            int inNeck = _stream.Count(p => p.x >= neckLo.x && p.x <= neckHi.x && p.y >= neckLo.y && p.y <= neckHi.y
                                            && (p - bulbC).sqrMagnitude > bulbR * bulbR);
            Fill01 = (inBulb + inNeck) / (float)Mathf.Max(1, _capacity);
            float benchY = transform.position.y + BenchTopY + 0.35f;
            Spilled = _stream.Count(p => p.y < benchY && Mathf.Abs(p.x - bulbC.x) > bulbR + 0.1f);
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
            if (_cork != null)
            {
                _cork.gameObject.SetActive(true);
                _slider.motor = new JointMotor2D { motorSpeed = 3f, maxMotorTorque = 40f };   // waiting, held up
            }
            Changed?.Invoke();
        }

        // ------------------------------------------------------------------ seal

        private bool _corkSeating;

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
                _corkSeating = true;
                _corkTimer = 0.05f;
                if (_slider != null) _slider.motor = new JointMotor2D { motorSpeed = -7f, maxMotorTorque = 80f };
            }
            else
            {
                order.ApplyDeduction(QualityBudget.SealMiss, "Bottling", "Wax set off-centre");
                AudioManager.Play(Sfx.Deny);
                SealHalfWidth = Mathf.Max(0.05f, SealHalfWidth - 0.02f);
                if (_cork != null)
                {
                    // Glances off the lip: a knock down and it springs straight back up.
                    _impulses.Add(_cork, Vector2.down * 1.2f);   // on the next physics step
                    CameraRig.Shake(0.06f);
                }
                if (SealsLeft <= 0)
                {
                    Current = Step.Label;   // out of attempts — move on, the damage is done
                    if (_slider != null) _slider.motor = new JointMotor2D { motorSpeed = -7f, maxMotorTorque = 80f };
                }
            }
            Changed?.Invoke();
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
