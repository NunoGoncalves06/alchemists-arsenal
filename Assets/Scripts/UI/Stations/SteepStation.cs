using UnityEngine;
using AlchemistsArsenal.Art;
using AlchemistsArsenal.Crafting;
using AlchemistsArsenal.Systems;

namespace AlchemistsArsenal.UI.Stations
{
    /// <summary>
    /// The Steep bench. Everything happens on the bench (<see cref="SteepBench"/>):
    /// hold on the sack to pour, drag the lid onto the jar to steep it, click the husks
    /// that float, click the jar to turn the bed, drag the sprouted malt into the sack
    /// for the kiln. The jar carries its own gauge. This panel adds one caption.
    /// </summary>
    public class SteepStation : StationPanel
    {
        public override string RailName => SteepBench.Station;
        public override Sprite RailIcon => MaltArt.Grain(false, 1);
        public override bool ShowsWorld => true;
        public override bool Complete => AllPast(BrewStage.Steep);

        private static SteepBench Bench => SteepBench.Instance;

        /// <summary>The order in the jar.</summary>
        protected override ActiveOrder Order => Bench != null ? Bench.TubOrder : null;

        private UIKit.CaptionView _caption;

        protected override void BuildContent(RectTransform root)
        {
            _caption = UIKit.Caption(root, "SteepCaption");
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
                    : AllPast(BrewStage.Steep) ? "All steeped. On to the Kiln." : "";
            ActiveOrder o = Order;
            _caption.Set(o != null && !string.IsNullOrEmpty(o.heroName) && !string.IsNullOrEmpty(b.Hint) ? o.heroName + ": " + hint : hint);
        }
    }
}
