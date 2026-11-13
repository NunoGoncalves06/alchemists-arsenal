using UnityEngine;
using AlchemistsArsenal.Art;
using AlchemistsArsenal.Crafting;
using AlchemistsArsenal.Systems;

namespace AlchemistsArsenal.UI.Stations
{
    /// <summary>
    /// The Kiln bench. Everything happens on the bench (<see cref="KilnBench"/>): drag
    /// the waiting sack of green malt onto the tray, drag logs into the fire, keep the
    /// thermometer in the green while it dries. This panel adds one caption.
    /// </summary>
    public class KilnStation : StationPanel
    {
        public override string RailName => KilnBench.Station;
        public override Sprite RailIcon => MaltArt.Log();
        public override bool ShowsWorld => true;
        public override bool Complete => AllPast(BrewStage.Kiln);

        private static KilnBench Bench => KilnBench.Instance;

        /// <summary>The order drying on the tray, or else the next one waiting on the rack.</summary>
        protected override ActiveOrder Order => Bench == null ? null : Bench.KilnOrder ?? Bench.NextWaiting;

        private UIKit.CaptionView _caption;

        protected override void BuildContent(RectTransform root)
        {
            _caption = UIKit.Caption(root, "KilnCaption");
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
                    : AllPast(BrewStage.Kiln) ? "All malted. On to Prep." : "Waiting on the Steep bench.";
            ActiveOrder o = Order;
            _caption.Set(o != null && !string.IsNullOrEmpty(o.heroName) && !string.IsNullOrEmpty(b.Hint) ? o.heroName + ": " + hint : hint);
        }
    }
}
