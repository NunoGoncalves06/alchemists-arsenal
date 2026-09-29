using UnityEngine;
using UnityEngine.UI;
using TMPro;
using AlchemistsArsenal.Art;
using AlchemistsArsenal.Combat;
using AlchemistsArsenal.Crafting;

namespace AlchemistsArsenal.UI.Stations
{
    /// <summary>
    /// The Cauldron. Stir with the pointer on the pot's surface; the stir gauge on
    /// the bench beside it shows the band to hold (see <see cref="CauldronView"/>).
    /// This panel pins the brew's note up (what, for whom, which way round) and adds
    /// one caption for what the pot needs now.
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

        private Image _note;
        private TextMeshProUGUI _noteTitle, _noteDirection;
        private UIKit.CaptionView _caption;

        protected override void BuildContent(RectTransform root)
        {
            _note = UIFactory.Panel(root, UITheme.Parchment, "BrewNote");
            UIFactory.Place(_note.rectTransform, 0.02f, 0.84f, 0.30f, 0.92f);
            var stack = UIFactory.VStack(_note.transform, 2f, new RectOffset(12, 12, 8, 8));
            UIFactory.Stretch((RectTransform)stack.transform);
            _noteTitle = UIFactory.Label(stack.transform, "", UITheme.SizeSmall, UITheme.Ink900, TextAlignmentOptions.TopLeft, true);
            UIFactory.FixedHeight(_noteTitle.gameObject, 22f);
            _noteDirection = UIFactory.Label(stack.transform, "", UITheme.SizeSmall, UITheme.WoodDark, TextAlignmentOptions.TopLeft);
            UIFactory.FixedHeight(_noteDirection.gameObject, 22f);
            foreach (var g in _note.GetComponentsInChildren<Graphic>(true)) g.raycastTarget = false;

            _caption = UIKit.Caption(root, "CauldronCaption");
            UIFactory.Place(_caption.Root.rectTransform, 0.18f, 0.035f, 0.82f, 0.095f);
        }

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
            _note.gameObject.SetActive(order != null);
            if (order == null) return;
            _noteTitle.text = $"{order.potionName}" + (string.IsNullOrEmpty(order.heroName) ? "" : $" <size=80%>for {order.heroName}</size>");
            bool cw = pot == null || pot.RequiredClockwise;
            var mix = order.Mixture;
            _noteDirection.text = mix != null && !mix.Ready ? "Waiting on the Prep bench"
                : cw ? "Stir it clockwise" : "Stir it anticlockwise";
        }

        public override void Tick()
        {
            var pot = PhysicsCauldronManager.Instance;
            if (pot == null) return;
            _caption.Set(Status(pot));
        }

        private string Status(PhysicsCauldronManager pot)
        {
            if (!HasOrder)
                return Systems.CraftingManager.Instance != null && Systems.CraftingManager.Instance.Orders.Count > 0
                    ? "" : "Take a job at the Counter first.";
            if (pot.Working != null && pot.Working.stage > Systems.BrewStage.Cauldron)
                return CountAt(Systems.BrewStage.Cauldron) > 0 ? "Brewed. The next mash goes in." : "Brewed. Bottle it.";
            if (!pot.MixtureReady) return "Nothing in the pot yet. Crush the leaves at Prep.";
            if (pot.Burning) return "It's burning on the bottom. Stir faster.";
            if (pot.ClockworkTurning) return "The clockwork is brewing it. A hand stirs it better.";
            if (!pot.MouseOverCauldron)
                return pot.Sticking ? "It's catching. Get the spoon back in." : "Bring the spoon over the brew and stir.";
            if (pot.StirringBackwards) return pot.RequiredClockwise ? "Wrong way. Stir clockwise." : "Wrong way. Stir anticlockwise.";
            if (pot.TooFast) return pot.Slosh01 > 0.5f ? "It's about to slop over. Ease off." : "A little fast. Ease off.";
            if (pot.TooSlow) return pot.Sticking ? "It's sticking. Stir faster." : "Too slow. Stir faster.";
            return "Good. Keep it in the green.";
        }
    }
}
