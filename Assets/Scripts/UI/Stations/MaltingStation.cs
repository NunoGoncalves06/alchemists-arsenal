using UnityEngine;
using AlchemistsArsenal.Art;
using AlchemistsArsenal.Crafting;
using AlchemistsArsenal.Systems;

namespace AlchemistsArsenal.UI.Stations
{
    /// <summary>
    /// The Malting bench. Everything happens on the bench (<see cref="MaltingBench"/>):
    /// hold on the sack to pour, drag the lid onto the jar to steep it, click the
    /// husks that float, click the jar to turn the bed, drag the sprouted malt onto
    /// the kiln, drag logs into its fire. The jar and the kiln carry their own
    /// gauges. This panel adds one caption saying what the bench wants next.
    /// </summary>
    public class MaltingStation : StationPanel
    {
        public override string RailName => "Malting";
        public override Sprite RailIcon => MaltArt.Grain(false, 3);
        public override bool ShowsWorld => true;
        public override bool Complete => AllPast(BrewStage.Malting);

        private static MaltingBench Bench => MaltingBench.Instance;

        /// <summary>The order in the jar, or else the one in the kiln.</summary>
        protected override ActiveOrder Order => Bench == null ? null : Bench.TubOrder ?? Bench.KilnOrder;

        private UIKit.CaptionView _caption;

        protected override void BuildContent(RectTransform root)
        {
            _caption = UIKit.Caption(root, "MaltingCaption");
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
            if (Bench != null) { Bench.Attended = false; Bench.PourHeld = false; }
        }

        public override void Tick()
        {
            var b = Bench;
            if (b == null) return;
            var cm = CraftingManager.Instance;
            string hint = b.Hint;
            if (string.IsNullOrEmpty(hint))
                hint = cm == null || cm.Orders.Count == 0 ? "Take a job at the Counter first."
                    : AllPast(BrewStage.Malting) ? "All malted. On to Prep." : "";
            ActiveOrder o = Order;
            _caption.Set(o != null && !string.IsNullOrEmpty(o.heroName) && !string.IsNullOrEmpty(b.Hint) ? o.heroName + ": " + hint : hint);
        }
    }
}
