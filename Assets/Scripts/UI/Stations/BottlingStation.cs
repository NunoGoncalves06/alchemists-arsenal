using UnityEngine;
using UnityEngine.UI;
using TMPro;
using AlchemistsArsenal.Art;
using AlchemistsArsenal.Audio;
using AlchemistsArsenal.Combat;
using AlchemistsArsenal.Systems;

namespace AlchemistsArsenal.UI.Stations
{
    /// <summary>
    /// Bottling, in the three moves it actually takes: pour to the line, seal on the
    /// beat, label it right. It used to be a single needle-timing click.
    ///
    /// Each step is a different kind of input on purpose — a held press you have to
    /// let go of at the right moment, a tap against a moving needle, and a straight
    /// recognition check under no time pressure — so the station teaches three
    /// things rather than testing one three times.
    /// </summary>
    public class BottlingStation : StationPanel
    {
        private const float PourRate = 0.42f;      // flask fraction per second
        private const float PourTargetLow = 0.78f;
        private const float PourTargetHigh = 0.96f;
        private const int SealAttempts = 2;

        public override string RailName => "Bottling";
        public override Sprite RailIcon => PixelSprites.Flask(ElementType.Nature);
        public override bool Complete => _step == Step.Done;

        private enum Step { Pour, Seal, Label, Done }

        private Step _step = Step.Pour;
        private float _fill;
        private bool _pouring;
        private int _sealsLeft = SealAttempts;
        private float _sealHalfWidth = 0.09f;

        private Image _flaskArt, _fillBar, _pourBand, _sealBand, _sealNeedle;
        private TextMeshProUGUI _pourHint, _sealHint, _labelHint, _fillReadout;
        private HoldButton _pourButton;
        private Button _sealButton;
        private Transform _labelRow;
        private Image _pourCard, _sealCard, _labelCard;

        protected override void BuildContent(RectTransform root)
        {
            BuildFlaskColumn(root);

            // --- 1. pour ------------------------------------------------------
            _pourCard = UIKit.Card(root, "1 — pour to the line", out Transform pour, spacing: 6f);
            UIFactory.Place(_pourCard.rectTransform, 0.26f, 0.66f, 1f, 1f);

            _pourHint = UIFactory.Label(pour, "", UITheme.SizeBody, UITheme.TextMid);
            UIFactory.Flex(_pourHint.gameObject, 1f, 1f, minHeight: 40f);

            _pourButton = UIKit.Hold(pour, "HOLD TO POUR", StartPour, StopPour);
            UIFactory.FixedHeight(_pourButton.gameObject, 50f);

            // --- 2. seal ------------------------------------------------------
            _sealCard = UIKit.Card(root, "2 — seal on the beat", out Transform seal, spacing: 6f);
            UIFactory.Place(_sealCard.rectTransform, 0.26f, 0.33f, 1f, 0.63f);

            _sealHint = UIFactory.Label(seal, "", UITheme.SizeSmall, UITheme.TextMid);
            UIFactory.Flex(_sealHint.gameObject, 1f, 0f, minHeight: 20f);

            var track = UIFactory.Panel(seal, UITheme.Ground, "SealTrack");
            UIFactory.FixedHeight(track.gameObject, 32f);
            _sealBand = UIFactory.Panel(track.transform, UITheme.Alpha(UITheme.Ok, 0.35f), "Band");
            _sealNeedle = UIFactory.Panel(track.transform, UITheme.CandleHot, "Needle");
            _sealNeedle.rectTransform.anchorMin = _sealNeedle.rectTransform.anchorMax = new Vector2(0f, 0.5f);
            _sealNeedle.rectTransform.sizeDelta = new Vector2(8f, 40f);
            ApplySealBand();

            _sealButton = UIFactory.Button(seal, "SEAL", Seal);
            UIFactory.FixedHeight(_sealButton.gameObject, 46f);

            // --- 3. label -----------------------------------------------------
            _labelCard = UIKit.Card(root, "3 — label the flask", out Transform label, spacing: 6f);
            UIFactory.Place(_labelCard.rectTransform, 0.26f, 0f, 1f, 0.30f);

            _labelHint = UIFactory.Label(label, "", UITheme.SizeSmall, UITheme.TextMid);
            UIFactory.Flex(_labelHint.gameObject, 1f, 0f, minHeight: 20f);

            var row = UIFactory.HStack(label, 8f);
            UIFactory.FixedHeight(row.gameObject, 58f);
            _labelRow = row.transform;

            foreach (ElementType e in new[]
                     { ElementType.Nature, ElementType.Fire, ElementType.Water, ElementType.Poison, ElementType.Arcane })
            {
                ElementType captured = e;
                var b = UIFactory.Button(_labelRow, e.ToString().ToUpperInvariant(),
                    () => ApplyLabel(captured), primary: false);
                UIFactory.Flex(b.gameObject, 1f, 1f, minHeight: 52f);
                UIFactory.TintButton(b, UITheme.Alpha(UITheme.Element(e), 0.55f),
                    UITheme.Alpha(UITheme.Element(e), 0.85f), UITheme.TextHi);
            }
        }

        private void BuildFlaskColumn(RectTransform root)
        {
            Image column = UIKit.Surface(root, out Transform inner, UITheme.Surface, UITheme.Line, "Flask");
            UIFactory.Place(column.rectTransform, 0f, 0f, 0.23f, 1f);

            _flaskArt = UIFactory.Icon(inner, PixelSprites.Flask(ElementType.Nature), 120f);
            UIFactory.Place(_flaskArt.rectTransform, 0.1f, 0.58f, 0.9f, 0.96f);

            var track = UIFactory.Panel(inner, UITheme.Ground, "FillTrack");
            UIFactory.Place(track.rectTransform, 0.30f, 0.12f, 0.70f, 0.54f);

            _fillBar = UIFactory.Panel(track.transform, UITheme.Nature, "Fill");
            UIFactory.Stretch(_fillBar.rectTransform, 3f);
            _fillBar.type = Image.Type.Filled;
            _fillBar.fillMethod = Image.FillMethod.Vertical;
            _fillBar.fillOrigin = 0;   // bottom
            _fillBar.fillAmount = 0f;

            _pourBand = UIFactory.Panel(track.transform, UITheme.Alpha(UITheme.Ok, 0.45f), "Line");
            UIFactory.Place(_pourBand.rectTransform, 0f, PourTargetLow, 1f, PourTargetHigh);

            _fillReadout = UIFactory.MonoLabel(inner, "0%", UITheme.SizeBody, UITheme.TextMid,
                TextAlignmentOptions.Center);
            UIFactory.Place(_fillReadout.rectTransform, 0f, 0.03f, 1f, 0.10f);
        }

        // -------------------------------------------------------------- lifecycle

        public override void NewDay()
        {
            _step = Step.Pour;
            _fill = 0f;
            _pouring = false;
            _sealsLeft = SealAttempts;
            _sealHalfWidth = 0.09f;
            ApplySealBand();
            Refresh();
        }

        public override void OnEnter() => Refresh();

        public override void OnExit() => _pouring = false;

        public override void Refresh()
        {
            var order = Order;
            bool has = order != null;

            if (has && _flaskArt != null)
            {
                _flaskArt.sprite = PixelSprites.Flask(order.element);
                if (_fillBar != null) _fillBar.color = UITheme.Element(order.element);
            }

            _pourHint.text = !has
                ? "Take a job at the Counter first."
                : _step == Step.Pour
                    ? "Hold the button and let go between the marks. Overfill it and you'll spill."
                    : "Poured.";

            _sealHint.text = !has ? "" :
                _step == Step.Seal
                    ? $"Click SEAL while the wax is over the band. <b>{_sealsLeft}</b> attempt(s) left."
                    : _step == Step.Pour ? "Pour first." : "Sealed.";

            _labelHint.text = !has ? "" :
                _step == Step.Label
                    ? $"Which flask is this? Pick the label that matches <b>{order.element}</b>."
                    : _step == Step.Done ? "Labelled and ready to send." : "Seal it first.";

            if (_pourButton != null)
            {
                var b = _pourButton.GetComponent<Button>();
                if (b != null) b.interactable = has && _step == Step.Pour;
            }
            if (_sealButton != null) _sealButton.interactable = has && _step == Step.Seal;
            foreach (var b in _labelRow.GetComponentsInChildren<Button>(true))
                b.interactable = has && _step == Step.Label;

            Dim(_pourCard, _step == Step.Pour);
            Dim(_sealCard, _step == Step.Seal);
            Dim(_labelCard, _step == Step.Label);
        }

        private static void Dim(Image card, bool active)
        {
            if (card == null) return;
            card.color = active ? UITheme.Candle : UITheme.Line;
        }

        public override void Tick()
        {
            if (_pouring && _step == Step.Pour)
            {
                _fill += Time.unscaledDeltaTime * PourRate;
                if (_fill >= 1f) Overflow();
            }

            if (_fillBar != null) _fillBar.fillAmount = Mathf.Clamp01(_fill);
            if (_fillReadout != null) _fillReadout.text = $"{Mathf.Clamp01(_fill):P0}";

            if (_step == Step.Seal && _sealNeedle != null)
            {
                float t = SealNeedle01();
                _sealNeedle.rectTransform.anchorMin = _sealNeedle.rectTransform.anchorMax = new Vector2(t, 0.5f);
                _sealNeedle.color = Mathf.Abs(t - 0.5f) <= _sealHalfWidth ? UITheme.Ok : UITheme.CandleHot;
            }
        }

        // ------------------------------------------------------------------ pour

        private void StartPour()
        {
            if (Order == null || _step != Step.Pour) return;
            _pouring = true;
        }

        private void StopPour()
        {
            if (!_pouring) return;
            _pouring = false;

            var order = Order;
            if (order == null || _step != Step.Pour) return;

            if (_fill >= PourTargetLow && _fill <= PourTargetHigh)
            {
                float centre = (PourTargetLow + PourTargetHigh) * 0.5f;
                float off = Mathf.Abs(_fill - centre) / ((PourTargetHigh - PourTargetLow) * 0.5f);
                int gain = Mathf.RoundToInt(Mathf.Lerp(9f, 4f, off));
                order.ApplyBonus(gain, "Bottling", $"Poured to the line ({_fill:P0})");
                AudioManager.Play(Sfx.Confirm);
            }
            else if (_fill < PourTargetLow)
            {
                order.ApplyDeduction(10, "Bottling", $"Short measure ({_fill:P0}) — the flask is half air");
                AudioManager.Play(Sfx.Deny);
            }

            _step = Step.Seal;
            Refresh();
            Changed?.Invoke();
        }

        private void Overflow()
        {
            _pouring = false;
            _fill = 1f;
            var order = Order;
            if (order != null)
            {
                order.ApplyDeduction(16, "Bottling", "Overfilled — brew all over the bench");
                AudioManager.Play(Sfx.Deny);
            }
            _step = Step.Seal;
            Refresh();
            Changed?.Invoke();
        }

        // ------------------------------------------------------------------ seal

        private static float SealNeedle01() => Mathf.PingPong(Time.unscaledTime * 0.75f, 1f);

        private void ApplySealBand()
        {
            if (_sealBand == null) return;
            _sealBand.rectTransform.anchorMin = new Vector2(0.5f - _sealHalfWidth, 0f);
            _sealBand.rectTransform.anchorMax = new Vector2(0.5f + _sealHalfWidth, 1f);
            _sealBand.rectTransform.offsetMin = _sealBand.rectTransform.offsetMax = Vector2.zero;
        }

        private void Seal()
        {
            var order = Order;
            if (order == null || _step != Step.Seal || _sealsLeft <= 0) return;

            float dist = Mathf.Abs(SealNeedle01() - 0.5f);
            bool hit = dist <= _sealHalfWidth;
            _sealsLeft--;

            if (hit)
            {
                int gain = Mathf.RoundToInt(Mathf.Lerp(9f, 4f, dist / Mathf.Max(0.001f, _sealHalfWidth)));
                order.ApplyBonus(gain, "Bottling", $"Sealed clean (+{gain})");
                AudioManager.Play(Sfx.Seal);
                _step = Step.Label;
            }
            else
            {
                order.ApplyDeduction(11, "Bottling", "Wax set off-centre");
                AudioManager.Play(Sfx.Deny);
                _sealHalfWidth = Mathf.Max(0.05f, _sealHalfWidth - 0.02f);
                ApplySealBand();
                if (_sealsLeft <= 0) _step = Step.Label; // out of attempts — move on, the damage is done
            }

            Refresh();
            Changed?.Invoke();
        }

        // ----------------------------------------------------------------- label

        private void ApplyLabel(ElementType element)
        {
            var order = Order;
            if (order == null || _step != Step.Label) return;

            if (element == order.element)
            {
                order.ApplyBonus(4, "Bottling", $"Labelled {element} — correct");
                AudioManager.Play(Sfx.Chime);
            }
            else
            {
                order.ApplyDeduction(14, "Bottling",
                    $"Labelled {element} on a {order.element} flask — Rookie will grab the wrong one");
                AudioManager.Play(Sfx.Deny);
            }

            _step = Step.Done;
            if (CraftingManager.Instance != null) CraftingManager.Instance.CompleteActiveOrder();
            Refresh();
            Changed?.Invoke();
        }
    }
}
