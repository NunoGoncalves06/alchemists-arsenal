using UnityEngine;
using AlchemistsArsenal.Art;
using AlchemistsArsenal.Combat;
using AlchemistsArsenal.Crafting;

namespace AlchemistsArsenal.UI.Stations
{
    /// <summary>
    /// The Bottling bench. Everything happens on the bench itself
    /// (<see cref="BottlingBench"/>): drag the ladle down to pour to the line etched
    /// on the glass, carry the cork to the neck, drag the right label onto the
    /// flask. This panel adds one caption saying what the bench wants next.
    /// </summary>
    public class BottlingStation : StationPanel
    {
        public override string RailName => "Bottling";
        public override Sprite RailIcon => PixelSprites.Flask(ElementType.Nature);
        public override bool ShowsWorld => true;
        public override bool Complete => AllPast(Systems.BrewStage.Bottling);

        private static BottlingBench Bench => BottlingBench.Instance;

        /// <summary>The order on the bottling bench (see <see cref="BottlingBench.Working"/>).</summary>
        protected override Systems.ActiveOrder Order => Bench != null ? Bench.Working : null;

        private UIKit.CaptionView _caption;
        private BottlingBench _hooked;

        protected override void BuildContent(RectTransform root)
        {
            _caption = UIKit.Caption(root, "BottlingCaption");
            // Low in the view, under the bench: never over the flask, the cork or a tag.
            UIFactory.Place(_caption.Root.rectTransform, 0.18f, 0.035f, 0.82f, 0.095f);
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
            if (bench == null) { _caption.Set(""); return; }
            if (order == null && (Systems.CraftingManager.Instance == null || Systems.CraftingManager.Instance.Orders.Count == 0))
            {
                _caption.Set("Take a job at the Counter first.");
                return;
            }
            string who = order != null && !string.IsNullOrEmpty(order.heroName) ? order.heroName + ": " : "";
            string hint = bench.Hint;
            _caption.Set(string.IsNullOrEmpty(hint) ? "" : who + hint);
        }

        public override void Tick() => Refresh();
    }
}
