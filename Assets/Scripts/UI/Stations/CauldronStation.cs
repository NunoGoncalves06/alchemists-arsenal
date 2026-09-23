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
    /// UI, so this panel is deliberately a HUD: a recipe card, the stir band, the
    /// spin needle and the brew bar, all pushed to the edges so the middle of the
    /// screen stays the pot.
    ///
    /// Three readouts because there are three things to hold: the stir speed inside
    /// a band that drifts (too slow and it catches on the bottom, too fast and it
    /// slops over the rim), the spoon turning the way the recipe asked, and the brew
    /// bar that only advances while both are true.
    /// </summary>
    public class CauldronStation : StationPanel
    {
        public override string RailName => "Cauldron";
        public override Sprite RailIcon => PixelSprites.Cauldron();
        public override bool ShowsWorld => true;
        public override bool Complete => AllPast(Systems.BrewStage.Cauldron);

        /// <summary>The order in the pot (see <see cref="PhysicsCauldronManager.Working"/>).</summary>
        protected override Systems.ActiveOrder Order =>
            PhysicsCauldronManager.Instance != null ? PhysicsCauldronManager.Instance.Working : null;

        private UIKit.MeterView _stir, _brew;
        private Image _spinNeedle, _spinTrack;
        private TextMeshProUGUI _status, _recipeDirection, _recipeElement, _ingredients;

        protected override void BuildContent(RectTransform root)
        {
            // Recipe card, top-left over the wall: clear of the pot in the middle. The
            // world view is the column above the HUD strip (the shop camera's viewport),
            // and nothing drawn over it takes a raycast.
            var recipe = UIKit.Card(root, "Recipe", out Transform card, spacing: 6f);
            UIFactory.Place(recipe.rectTransform, 0f, 0.60f, 0.25f, 0.875f);

            _recipeElement = UIFactory.Label(card, "", UITheme.SizeBody, UITheme.TextHi);
            UIFactory.Flex(_recipeElement.gameObject, 1f, 0f, minHeight: 24f);
            _recipeDirection = UIFactory.Label(card, "", UITheme.SizeBody, UITheme.CandleHot,
                TextAlignmentOptions.TopLeft, true);
            UIFactory.Flex(_recipeDirection.gameObject, 1f, 0f, minHeight: 24f);
            _ingredients = UIFactory.Label(card, "", UITheme.SizeSmall, UITheme.TextLow);
            UIFactory.Flex(_ingredients.gameObject, 1f, 1f, minHeight: 34f);

            foreach (var g in recipe.GetComponentsInChildren<Graphic>(true)) g.raycastTarget = false;

            // Status line along the top of the world view, above the pot.
            _status = UIFactory.Label(root, "ACCEPT AN ORDER FIRST", UITheme.SizeTitle, UITheme.TextLow,
                TextAlignmentOptions.Center, true);
            UIFactory.Place(_status.rectTransform, 0.26f, 0.80f, 1f, 0.875f);
            _status.raycastTarget = false;

            // Gauge deck: the HUD strip under the world view.
            var deckOuter = UIKit.Surface(root, out Transform deck, UITheme.Alpha(UITheme.Ground, 0.94f), UITheme.Line);
            UIFactory.Place(deckOuter.rectTransform, 0f, 0f, 1f, 0.255f);

            var stack = UIFactory.VStack(deck, 6f, new RectOffset(18, 18, 10, 10));
            UIFactory.Stretch((RectTransform)stack.transform);

            _stir = UIKit.Meter(stack.transform, "Stir speed — hold it inside the band", UITheme.Ok, withBand: true);
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
                ? $"Brewing: <b>{order.potionName}</b>" + (string.IsNullOrEmpty(order.heroName) ? "" : $" for {order.heroName}")
                : "No order in the pot.";

            bool cw = pot == null || pot.RequiredClockwise;
            _recipeDirection.text = order == null ? "" : cw ? "Turn CLOCKWISE" : "Turn ANTICLOCKWISE";

            var mix = order != null ? order.Mixture : null;
            if (mix != null && !mix.Ready)
                _recipeDirection.text = "Waiting on the Prep bench";

            if (_cwHalf != null && _ccwHalf != null && order != null)
            {
                _cwHalf.color = cw ? UITheme.Alpha(UITheme.Ok, 0.28f) : UITheme.Alpha(UITheme.Danger, 0.16f);
                _ccwHalf.color = cw ? UITheme.Alpha(UITheme.Danger, 0.16f) : UITheme.Alpha(UITheme.Ok, 0.28f);
            }

            if (mix == null)
            {
                _ingredients.text = "";
            }
            else if (!mix.AllLeavesIn)
            {
                _ingredients.text = $"<color=#{ColorUtility.ToHtmlStringRGB(UITheme.Danger)}>" +
                                    $"{mix.Remaining} leaf/leaves still to crush in.</color>" +
                                    "  " +
                                    $"Recipe: {mix.Recipe.Shorthand}";
            }
            else if (!mix.Ground)
            {
                _ingredients.text = $"<color=#{ColorUtility.ToHtmlStringRGB(UITheme.Candle)}>" +
                                    "Leaves are in — they still need grinding.</color>";
            }
            else
            {
                _ingredients.text = $"{mix.Added.Count} leaves ground in. " +
                                    $"<color=#{ColorUtility.ToHtmlStringRGB(UITheme.Ok)}>Ready to brew.</color>";
            }
        }

        public override void Tick()
        {
            var pot = PhysicsCauldronManager.Instance;
            if (pot == null) return;

            float stir = pot.StirPower01;
            _stir.Set(stir, PercentText.Of(stir));
            _stir.SetBand(pot.MinOptimalStir, pot.MaxOptimalStir);
            _brew.Set(pot.BrewProgress01, PercentText.Of(pot.BrewProgress01));

            if (_spinNeedle != null)
            {
                float x = Mathf.Clamp(-pot.Spin01, -1f, 1f) * 0.48f; // negative spin = clockwise = left
                var a = new Vector2(0.5f + x, 0.5f);
                _spinNeedle.rectTransform.anchorMin = _spinNeedle.rectTransform.anchorMax = a;
                _spinNeedle.color = pot.StirringCorrectly ? UITheme.Ok
                    : pot.StirringBackwards ? UITheme.Danger : UITheme.CandleHot;
            }

            bool inBand = pot.InBand;

            if (!HasOrder)
            {
                SetStatus("ACCEPT AN ORDER FIRST", UITheme.TextLow);
            }
            else if (pot.Working != null && pot.Working.stage > Systems.BrewStage.Cauldron)
            {
                SetStatus(CountAt(Systems.BrewStage.Cauldron) > 0 ? "BREWED — THE NEXT MASH GOES IN"
                    : "BREW READY — BOTTLE IT", UITheme.Candle);
            }
            else if (!pot.MixtureReady)
            {
                SetStatus("NOTHING IN THE POT — CRUSH THE LEAVES AT PREP FIRST", UITheme.Danger);
            }
            else if (pot.Burning)
            {
                SetStatus("BURNING ON THE BOTTOM — STIR FASTER", UITheme.Danger);
            }
            else if (!pot.MouseOverCauldron)
            {
                SetStatus(pot.Sticking ? "IT'S CATCHING — GET THE SPOON BACK IN" : "BRING THE SPOON OVER THE BREW",
                    pot.Sticking ? UITheme.Danger : UITheme.TextMid);
            }
            else if (pot.StirringBackwards)
            {
                SetStatus(pot.RequiredClockwise ? "WRONG WAY — TURN CLOCKWISE" : "WRONG WAY — TURN ANTICLOCKWISE",
                    UITheme.Danger);
            }
            else if (pot.TooFast)
            {
                if (pot.Slosh01 < 0.05f) SetStatus("A LITTLE FAST — EASE OFF", UITheme.Candle);
                else SetStatus(pot.Slosh01 > 0.5f ? "IT'S ABOUT TO GO OVER THE RIM — EASE OFF" : "TOO FAST — IT'S SLOSHING",
                    UITheme.Water);
            }
            else if (pot.TooSlow)
            {
                SetStatus(pot.Sticking ? "STICKING TO THE BOTTOM — STIR FASTER" : "TOO SLOW — STIR FASTER",
                    pot.Sticking ? UITheme.Danger : UITheme.CandleHot);
            }
            else if (!pot.StirringCorrectly)
            {
                SetStatus("IN THE BAND — KEEP IT TURNING", UITheme.Candle);
            }
            else
            {
                SetStatus("BREWING — QUALITY CLIMBING", UITheme.Ok);
            }

            // The band is the thing being chased; colour the fill by where the stir is:
            // amber below it (the bottom catches), blue above it (it slops).
            _stir.SetFillColor(inBand ? UITheme.Ok : pot.TooSlow ? UITheme.CandleHot : UITheme.Water);
        }

        private void SetStatus(string text, Color color)
        {
            if (_status == null) return;
            _status.text = text;
            _status.color = color;
        }
    }
}
