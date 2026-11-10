using UnityEngine;
using AlchemistsArsenal.Art;
using AlchemistsArsenal.Combat;
using AlchemistsArsenal.Crafting;
using AlchemistsArsenal.Systems;

namespace AlchemistsArsenal.UI.Stations
{
    /// <summary>
    /// The Delivery bench (<see cref="DeliveryBench"/>): drag each sealed flask to the
    /// fighter who ordered it. This panel adds one caption.
    /// </summary>
    public class DeliveryStation : StationPanel
    {
        public override string RailName => "Delivery";
        public override Sprite RailIcon => PixelSprites.Flask(ElementType.Fire);
        public override bool ShowsWorld => true;
        public override bool Complete => AllPast(BrewStage.Delivery);

        private static DeliveryBench Bench => DeliveryBench.Instance;

        /// <summary>The next flask on the tray.</summary>
        protected override ActiveOrder Order => Bench != null ? Bench.Waiting : null;

        private UIKit.CaptionView _caption;

        protected override void BuildContent(RectTransform root)
        {
            _caption = UIKit.Caption(root, "DeliveryCaption");
            UIFactory.Place(_caption.Root.rectTransform, 0.18f, 0.035f, 0.82f, 0.095f);
        }

        public override void NewDay()
        {
            if (Bench != null) Bench.NewDay();
        }

        public override void OnEnter()
        {
            if (Bench != null) Bench.Attended = true;
        }

        public override void OnExit()
        {
            if (Bench != null) Bench.Attended = false;
        }

        public override void Tick()
        {
            var b = Bench;
            if (b == null) return;
            var cm = CraftingManager.Instance;
            string hint = b.Hint;
            if (string.IsNullOrEmpty(hint))
                hint = cm == null || cm.Orders.Count == 0 ? "Take a job at the Counter first."
                    : AllPast(BrewStage.Delivery) ? "Every flask is handed over." : "Waiting on the Bottling bench.";
            _caption.Set(hint);
        }
    }
}
