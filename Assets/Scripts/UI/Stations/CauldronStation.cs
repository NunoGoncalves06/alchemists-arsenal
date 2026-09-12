using UnityEngine;
using UnityEngine.UI;
using TMPro;
using AlchemistsArsenal.Art;
using AlchemistsArsenal.Core;
using AlchemistsArsenal.Crafting;

namespace AlchemistsArsenal.UI.Stations
{
    /// <summary>
    /// The Cauldron — the one station that plays out in the world rather than in the
    /// UI, so this panel is deliberately a HUD: a recipe card, the heat band, the
    /// spin needle and the brew bar, all pushed to the edges so the middle of the
    /// screen stays the pot.
    ///
    /// Three readouts because there are three things to hold: heat inside a band
    /// that drifts, the spoon turning the way the recipe asked, and the brew bar
    /// that only advances while both are true.
    /// </summary>
    public class CauldronStation : StationPanel
    {
        public override string RailName => "Cauldron";
        public override Sprite RailIcon => PixelSprites.Cauldron();
        public override bool ShowsWorld => true;
        public override bool Complete =>
            PhysicsCauldronManager.Instance != null && PhysicsCauldronManager.Instance.IsBrewComplete;

        private UIKit.MeterView _heat, _brew;
        private Image _spinNeedle, _spinTrack;
        private TextMeshProUGUI _status, _recipeDirection, _recipeElement, _ingredients;

        protected override void BuildContent(RectTransform root)
        {
            // Recipe card, middle-left: clear of the pot in the centre, clear of the
            // gauge deck below, and clear of the tutorial's top band.
            var recipe = UIKit.Card(root, "Recipe", out Transform card, spacing: 6f);
            UIFactory.Place(recipe.rectTransform, 0f, 0.34f, 0.28f, 0.66f);

            _recipeElement = UIFactory.Label(card, "", UITheme.SizeBody, UITheme.TextHi);
            UIFactory.Flex(_recipeElement.gameObject, 1f, 0f, minHeight: 24f);
            _recipeDirection = UIFactory.Label(card, "", UITheme.SizeBody, UITheme.CandleHot,
                TextAlignmentOptions.TopLeft, true);
            UIFactory.Flex(_recipeDirection.gameObject, 1f, 0f, minHeight: 24f);
            _ingredients = UIFactory.Label(card, "", UITheme.SizeSmall, UITheme.TextLow);
            UIFactory.Flex(_ingredients.gameObject, 1f, 1f, minHeight: 34f);

            // Status line sits above the gauges, under the pot.
            _status = UIFactory.Label(root, "ACCEPT AN ORDER FIRST", UITheme.SizeTitle, UITheme.TextLow,
                TextAlignmentOptions.Center, true);
            UIFactory.Place(_status.rectTransform, 0.08f, 0.27f, 0.92f, 0.36f);

            // Gauge deck, bottom.
            var deckOuter = UIKit.Surface(root, out Transform deck, UITheme.Alpha(UITheme.Ground, 0.88f), UITheme.Line);
            UIFactory.Place(deckOuter.rectTransform, 0.06f, 0.02f, 0.94f, 0.25f);

            var stack = UIFactory.VStack(deck, 8f, new RectOffset(18, 18, 12, 12));
            UIFactory.Stretch((RectTransform)stack.transform);

            _heat = UIKit.Meter(stack.transform, "Heat — hold it inside the band", UITheme.Ok, withBand: true);
            BuildSpinRow(stack.transform);
            _brew = UIKit.Meter(stack.transform, "Brew", UITheme.Candle);
        }

        /// <summary>
        /// A centre-out needle: it sits in the middle when the spoon is still and
        /// slides toward whichever side you are turning. The recipe's required side
        /// is tinted, so "you are stirring the wrong way" is visible without reading.
        /// </summary>
        private void BuildSpinRow(Transform parent)
        {
            var col = UIFactory.VStack(parent, 4f);
            UIFactory.Flex(col.gameObject, 1f, 0f, minHeight: 42f);

            var caption = UIFactory.Heading(col.transform, "Stir — turn the spoon in circles",
                UITheme.TextLow, UITheme.SizeTiny);
            UIFactory.Flex(caption.gameObject, 1f, 0f, minHeight: 16f);

            _spinTrack = UIFactory.Panel(col.transform, UITheme.Ground, "SpinTrack");
            UIFactory.Flex(_spinTrack.gameObject, 1f, 0f, minHeight: 20f);

            // Halves: left = clockwise, right = anticlockwise.
            var left = UIFactory.Panel(_spinTrack.transform, UITheme.Alpha(UITheme.Witch, 0.25f), "CW");
            UIFactory.Place(left.rectTransform, 0f, 0f, 0.5f, 1f);
            var right = UIFactory.Panel(_spinTrack.transform, UITheme.Alpha(UITheme.Witch, 0.25f), "CCW");
            UIFactory.Place(right.rectTransform, 0.5f, 0f, 1f, 1f);
            _cwHalf = left; _ccwHalf = right;

            var centre = UIFactory.Panel(_spinTrack.transform, UITheme.Line, "Centre");
            centre.rectTransform.anchorMin = new Vector2(0.5f, 0f);
            centre.rectTransform.anchorMax = new Vector2(0.5f, 1f);
            centre.rectTransform.sizeDelta = new Vector2(2f, 0f);

            _spinNeedle = UIFactory.Panel(_spinTrack.transform, UITheme.CandleHot, "Needle");
            _spinNeedle.rectTransform.anchorMin = _spinNeedle.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            _spinNeedle.rectTransform.sizeDelta = new Vector2(8f, 26f);
        }

        private Image _cwHalf, _ccwHalf;

        public override void OnEnter()
        {
            var pot = PhysicsCauldronManager.Instance;
            if (pot != null) pot.Attended = true;   // the spoon is in your hand now
            Refresh();
        }

        public override void OnExit()
        {
            var pot = PhysicsCauldronManager.Instance;
            if (pot != null) pot.Attended = false;
        }

        public override void Refresh()
        {
            var pot = PhysicsCauldronManager.Instance;
            var order = Order;

            _recipeElement.text = order != null
                ? $"Brewing: <b>{order.potionName}</b>"
                : "No order yet.";

            bool cw = pot == null || pot.RequiredClockwise;
            _recipeDirection.text = order == null ? "" : cw ? "Turn CLOCKWISE" : "Turn ANTICLOCKWISE";

            if (_cwHalf != null && _ccwHalf != null && order != null)
            {
                _cwHalf.color = cw ? UITheme.Alpha(UITheme.Ok, 0.28f) : UITheme.Alpha(UITheme.Danger, 0.16f);
                _ccwHalf.color = cw ? UITheme.Alpha(UITheme.Danger, 0.16f) : UITheme.Alpha(UITheme.Ok, 0.28f);
            }

            int added = pot != null ? pot.IngredientCount : 0;
            _ingredients.text = added > 0
                ? $"{added} prepped ingredient{(added == 1 ? "" : "s")} in the pot."
                : "No prepped ingredients yet — see the Prep bench.";
        }

        public override void Tick()
        {
            var pot = PhysicsCauldronManager.Instance;
            if (pot == null) return;

            float heat = pot.Heat01;
            _heat.Set(heat, $"{heat:P0}");
            _heat.SetBand(pot.MinOptimalHeat, pot.MaxOptimalHeat);
            _brew.Set(pot.BrewProgress01, $"{pot.BrewProgress01:P0}");

            if (_spinNeedle != null)
            {
                float x = Mathf.Clamp(-pot.Spin01, -1f, 1f) * 0.48f; // negative spin = clockwise = left
                var a = new Vector2(0.5f + x, 0.5f);
                _spinNeedle.rectTransform.anchorMin = _spinNeedle.rectTransform.anchorMax = a;
                _spinNeedle.color = pot.StirringCorrectly ? UITheme.Ok
                    : pot.StirringBackwards ? UITheme.Danger : UITheme.CandleHot;
            }

            bool inBand = heat >= pot.MinOptimalHeat && heat <= pot.MaxOptimalHeat;

            if (!HasOrder)
            {
                SetStatus("ACCEPT AN ORDER FIRST", UITheme.TextLow);
            }
            else if (pot.IsBrewComplete)
            {
                SetStatus("BREW READY — BOTTLE IT", UITheme.Candle);
            }
            else if (!pot.MouseOverCauldron)
            {
                SetStatus("BRING THE SPOON OVER THE POT", UITheme.TextMid);
            }
            else if (pot.StirringBackwards)
            {
                SetStatus(pot.RequiredClockwise ? "WRONG WAY — TURN CLOCKWISE" : "WRONG WAY — TURN ANTICLOCKWISE",
                    UITheme.Danger);
            }
            else if (heat < pot.MinOptimalHeat)
            {
                SetStatus("TOO COLD — STIR FASTER", UITheme.Water);
            }
            else if (heat > pot.MaxOptimalHeat)
            {
                SetStatus("OVERHEATING — EASE OFF", UITheme.Danger);
            }
            else if (!pot.StirringCorrectly)
            {
                SetStatus("IN THE BAND — KEEP IT TURNING", UITheme.Candle);
            }
            else
            {
                SetStatus("BREWING — QUALITY CLIMBING", UITheme.Ok);
            }

            // The band is the thing being chased; colour the fill by whether it's in it.
            _heat.SetFillColor(inBand ? UITheme.Ok : heat < pot.MinOptimalHeat ? UITheme.Water : UITheme.Danger);
        }

        private void SetStatus(string text, Color color)
        {
            if (_status == null) return;
            _status.text = text;
            _status.color = color;
        }
    }
}
