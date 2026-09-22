using UnityEngine;
using UnityEngine.UI;
using TMPro;
using AlchemistsArsenal.Art;
using AlchemistsArsenal.Combat;
using AlchemistsArsenal.Crafting;

namespace AlchemistsArsenal.UI.Stations
{
    /// <summary>
    /// The Bottling bench's HUD. The pour, the flask and the cork are physical and
    /// live in the shop world (<see cref="BottlingBench"/>); this strip shows the fill
    /// against the line, holds the pour for anyone who prefers a button to pressing on
    /// the ladle, times the seal, and carries the label choice.
    /// </summary>
    public class BottlingStation : StationPanel
    {
        public override string RailName => "Bottling";
        public override Sprite RailIcon => PixelSprites.Flask(ElementType.Nature);
        public override bool ShowsWorld => true;
        public override bool Complete => Bench != null && Bench.Current == BottlingBench.Step.Done;

        private static BottlingBench Bench => BottlingBench.Instance;

        private Image _fill, _pourCard, _sealCard, _labelCard, _sealBand, _sealNeedle;
        private TextMeshProUGUI _fillText, _pourHint, _sealHint, _labelHint, _banner;
        private HoldButton _pourButton;
        private Button _sealButton;
        private Transform _labelRow;
        private BottlingBench _hooked;

        protected override void BuildContent(RectTransform root)
        {
            // Top-left caption, clear of the ladle and flask in the middle of the bench.
            _banner = UIFactory.Label(root, "", UITheme.SizeBody, UITheme.TextMid, TextAlignmentOptions.TopLeft, true);
            UIFactory.Place(_banner.rectTransform, 0.02f, 0.78f, 0.40f, 0.875f);
            _banner.raycastTarget = false;

            var strip = UIKit.Surface(root, out Transform s, UITheme.Alpha(UITheme.Ground, 0.94f), UITheme.Line, "Hud");
            UIFactory.Place(strip.rectTransform, 0f, 0f, 1f, 0.255f);

            // 1 — pour
            _pourCard = UIKit.Card(s, "1 — pour to the line", out Transform pour, spacing: 4f);
            UIFactory.Place(_pourCard.rectTransform, 0.01f, 0.04f, 0.33f, 0.96f);
            var track = UIFactory.Panel(pour, UITheme.Surface, "FillTrack");
            UIFactory.FixedHeight(track.gameObject, 22f);
            _fill = UIFactory.Panel(track.transform, UITheme.Nature, "Fill");
            _fill.rectTransform.anchorMin = Vector2.zero;
            _fill.rectTransform.anchorMax = new Vector2(0f, 1f);
            _fill.rectTransform.offsetMin = _fill.rectTransform.offsetMax = Vector2.zero;
            var line = UIFactory.Panel(track.transform, UITheme.Alpha(UITheme.Ok, 0.45f), "Line");
            UIFactory.Place(line.rectTransform, BottlingBench.TargetLow, 0f, BottlingBench.TargetHigh, 1f);
            _fillText = UIFactory.MonoLabel(pour, "", UITheme.SizeTiny, UITheme.TextMid, TextAlignmentOptions.Left);
            UIFactory.FixedHeight(_fillText.gameObject, 16f);
            _pourButton = UIKit.Hold(pour, "HOLD TO POUR", () => { if (Bench != null) Bench.PourHeld = true; },
                () => { if (Bench != null) Bench.PourHeld = false; });
            UIFactory.FixedHeight(_pourButton.gameObject, 40f);

            // 2 — seal
            _sealCard = UIKit.Card(s, "2 — seal on the beat", out Transform seal, spacing: 4f);
            UIFactory.Place(_sealCard.rectTransform, 0.34f, 0.04f, 0.63f, 0.96f);
            _sealHint = UIFactory.Label(seal, "", UITheme.SizeTiny, UITheme.TextMid);
            UIFactory.FixedHeight(_sealHint.gameObject, 16f);
            var sealTrack = UIFactory.Panel(seal, UITheme.Surface, "SealTrack");
            UIFactory.FixedHeight(sealTrack.gameObject, 22f);
            _sealBand = UIFactory.Panel(sealTrack.transform, UITheme.Alpha(UITheme.Ok, 0.35f), "Band");
            _sealNeedle = UIFactory.Panel(sealTrack.transform, UITheme.CandleHot, "Needle");
            _sealNeedle.rectTransform.anchorMin = _sealNeedle.rectTransform.anchorMax = new Vector2(0f, 0.5f);
            _sealNeedle.rectTransform.sizeDelta = new Vector2(8f, 30f);
            _sealButton = UIFactory.Button(seal, "SEAL", () => { if (Bench != null) Bench.Seal(); });
            UIFactory.FixedHeight(_sealButton.gameObject, 40f);

            // 3 — label
            _labelCard = UIKit.Card(s, "3 — label the flask", out Transform label, spacing: 4f);
            UIFactory.Place(_labelCard.rectTransform, 0.64f, 0.04f, 0.99f, 0.96f);
            _labelHint = UIFactory.Label(label, "", UITheme.SizeTiny, UITheme.TextMid);
            UIFactory.FixedHeight(_labelHint.gameObject, 16f);
            var row = UIFactory.HStack(label, 4f);
            UIFactory.FixedHeight(row.gameObject, 44f);
            _labelRow = row.transform;
            foreach (ElementType e in new[] { ElementType.Nature, ElementType.Fire, ElementType.Water, ElementType.Poison, ElementType.Arcane })
            {
                ElementType captured = e;
                var b = UIFactory.Button(_labelRow, e.ToString().ToUpperInvariant(),
                    () => { if (Bench != null) Bench.ApplyLabel(captured); }, primary: false);
                UIFactory.Flex(b.gameObject, 1f, 1f, minHeight: 40f);
                UIFactory.TintButton(b, UITheme.Alpha(UITheme.Element(e), 0.55f), UITheme.Alpha(UITheme.Element(e), 0.85f), UITheme.TextHi);
                // Five buttons share one strip: at the default size and letter-spacing
                // the longer names broke mid-word ("NATU / RE", "POISO / N"). One line,
                // shrunk to fit.
                var text = b.GetComponentInChildren<TextMeshProUGUI>();
                if (text != null)
                {
                    text.textWrappingMode = TextWrappingModes.NoWrap;
                    text.characterSpacing = 1f;
                    text.enableAutoSizing = true;
                    text.fontSizeMin = UITheme.SizeTiny;
                    text.fontSizeMax = UITheme.SizeBody;
                }
            }
        }

        public override void NewDay()
        {
            if (Bench != null) Bench.NewDay();
            Refresh();
        }

        public override void OnEnter()
        {
            var bench = Bench;
            if (bench != null)
            {
                bench.Attended = true;
                if (_hooked != bench)
                {
                    if (_hooked != null) _hooked.Changed -= OnBenchChanged;
                    bench.Changed += OnBenchChanged;
                    _hooked = bench;
                }
            }
            Refresh();
        }

        public override void OnExit()
        {
            if (Bench != null) { Bench.Attended = false; Bench.PourHeld = false; }
        }

        private void OnBenchChanged()
        {
            Refresh();
            Changed?.Invoke();
        }

        public override void Refresh()
        {
            var order = Order;
            var bench = Bench;
            bool has = order != null && bench != null;
            var step = bench != null ? bench.Current : BottlingBench.Step.Pour;

            if (has) _fill.color = UITheme.Element(order.element);

            _banner.text = !has ? "TAKE A JOB AT THE COUNTER FIRST"
                : step == BottlingBench.Step.Pour ? "HOLD ON THE LADLE TO TIP IT — LET GO AT THE LINE"
                : step == BottlingBench.Step.Seal ? "SEAL IT ON THE BEAT"
                : step == BottlingBench.Step.Label ? $"LABEL IT — THIS IS A {order.element.ToString().ToUpperInvariant()} FLASK"
                : "SEALED AND LABELLED — READY TO SEND";

            _sealHint.text = !has ? "" : step == BottlingBench.Step.Seal
                ? $"Press SEAL while the needle is on the band. {bench.SealsLeft} attempt(s)."
                : step == BottlingBench.Step.Pour ? "Pour first." : "Sealed.";
            _labelHint.text = !has ? "" : step == BottlingBench.Step.Label ? "Which flask is this?"
                : step == BottlingBench.Step.Done ? "Labelled." : "Seal it first.";

            var holdBtn = _pourButton != null ? _pourButton.GetComponent<Button>() : null;
            if (holdBtn != null) holdBtn.interactable = has && step == BottlingBench.Step.Pour;
            _sealButton.interactable = has && step == BottlingBench.Step.Seal;
            foreach (var b in _labelRow.GetComponentsInChildren<Button>(true))
                b.interactable = has && step == BottlingBench.Step.Label;

            _pourCard.color = step == BottlingBench.Step.Pour ? UITheme.Candle : UITheme.Line;
            _sealCard.color = step == BottlingBench.Step.Seal ? UITheme.Candle : UITheme.Line;
            _labelCard.color = step == BottlingBench.Step.Label ? UITheme.Candle : UITheme.Line;

            if (bench != null)
            {
                var rt = _sealBand.rectTransform;
                rt.anchorMin = new Vector2(0.5f - bench.SealHalfWidth, 0f);
                rt.anchorMax = new Vector2(0.5f + bench.SealHalfWidth, 1f);
                rt.offsetMin = rt.offsetMax = Vector2.zero;
            }
        }

        public override void Tick()
        {
            var bench = Bench;
            if (bench == null) return;
            float f = Mathf.Clamp01(bench.Fill01);
            _fill.rectTransform.anchorMax = new Vector2(f, 1f);
            _fillText.text = bench.Fill01 > 1f || bench.Spilled > 8
                ? $"{PercentText.Of(f)} — SPILLING"
                : $"{PercentText.Of(f)}   tilt {PercentText.Of(bench.Tilt01)}";

            if (bench.Current == BottlingBench.Step.Seal)
            {
                float t = BottlingBench.SealNeedle01();
                _sealNeedle.rectTransform.anchorMin = _sealNeedle.rectTransform.anchorMax = new Vector2(t, 0.5f);
                _sealNeedle.color = Mathf.Abs(t - 0.5f) <= bench.SealHalfWidth ? UITheme.Ok : UITheme.CandleHot;
            }
        }
    }
}
