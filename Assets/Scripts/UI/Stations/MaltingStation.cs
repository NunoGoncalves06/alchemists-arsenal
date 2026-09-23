using UnityEngine;
using UnityEngine.UI;
using TMPro;
using AlchemistsArsenal.Art;
using AlchemistsArsenal.Core;
using AlchemistsArsenal.Crafting;
using AlchemistsArsenal.Data;
using AlchemistsArsenal.Systems;

namespace AlchemistsArsenal.UI.Stations
{
    /// <summary>
    /// The Malting bench's HUD. The bench itself is physical (<see cref="MaltingBench"/>):
    /// a sack you pour from, a steeping jar whose water sorts grain from husk, a kiln
    /// you feed with logs. This panel says what to do now across the top, and along
    /// the bottom reads the jar and the kiln back and offers every action as a button
    /// too (each of them is also a click on the bench itself).
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

        private TextMeshProUGUI _status, _jarTitle, _jarLine, _kilnTitle, _kilnLine;
        private UIKit.MeterView _grain, _jarClock, _heat, _dry;
        private Button _steep, _skim, _turn, _load, _log;
        private HoldButton _pour;

        protected override void BuildContent(RectTransform root)
        {
            _status = UIFactory.Label(root, "", UITheme.SizeTitle, UITheme.TextLow, TextAlignmentOptions.Center, true);
            UIFactory.Place(_status.rectTransform, 0.02f, 0.885f, 0.98f, 1f);
            _status.raycastTarget = false;

            var strip = UIKit.Surface(root, out Transform s, UITheme.Alpha(UITheme.Ground, 0.94f), UITheme.Line, "Hud");
            UIFactory.Place(strip.rectTransform, 0f, 0f, 1f, 0.255f);

            // --- the jar -------------------------------------------------------
            var jar = UIFactory.VStack(s, 4f, new RectOffset(16, 8, 8, 8));
            UIFactory.Place((RectTransform)jar.transform, 0f, 0f, 0.33f, 1f);
            _jarTitle = UIFactory.Label(jar.transform, "", UITheme.SizeSmall, UITheme.Candle, TextAlignmentOptions.Left, true);
            UIFactory.FixedHeight(_jarTitle.gameObject, 20f);
            _grain = UIKit.Meter(jar.transform, "Sound grain in the jar", UITheme.Candle, withBand: true, height: 14f);
            _jarClock = UIKit.Meter(jar.transform, "Soak / germination", UITheme.Ok, withBand: false, height: 14f);
            _jarLine = UIFactory.Label(jar.transform, "", UITheme.SizeTiny, UITheme.TextMid, TextAlignmentOptions.TopLeft);
            UIFactory.Flex(_jarLine.gameObject, 1f, 1f, minHeight: 18f);

            // --- the actions ---------------------------------------------------
            var mid = UIFactory.Rect(s, "Actions", new Vector2(0.34f, 0f), new Vector2(0.66f, 1f), new Vector2(6, 10), new Vector2(-6, -10));
            _pour = UIKit.Hold(mid, "HOLD TO POUR GRAIN", () => SetPour(true), () => SetPour(false));
            UIFactory.Place((RectTransform)_pour.transform, 0f, 0.68f, 0.49f, 1f);
            _steep = UIFactory.Button(mid, "STEEP IT", () => Bench?.Steep());
            UIFactory.Place(_steep.image.rectTransform, 0.51f, 0.68f, 1f, 1f);
            _skim = UIFactory.Button(mid, "SKIM A HUSK", () => Bench?.SkimNext(), primary: false);
            UIFactory.Place(_skim.image.rectTransform, 0f, 0.34f, 0.49f, 0.66f);
            _turn = UIFactory.Button(mid, "TURN THE BED", () => Bench?.Turn());
            UIFactory.Place(_turn.image.rectTransform, 0.51f, 0.34f, 1f, 0.66f);
            _load = UIFactory.Button(mid, "LOAD THE KILN", () => Bench?.LoadKiln());
            UIFactory.Place(_load.image.rectTransform, 0f, 0f, 0.49f, 0.32f);
            _log = UIFactory.Button(mid, "ADD A LOG", () => Bench?.Stoke(), primary: false);
            UIFactory.Place(_log.image.rectTransform, 0.51f, 0f, 1f, 0.32f);

            // --- the kiln ------------------------------------------------------
            var kiln = UIFactory.VStack(s, 4f, new RectOffset(8, 16, 8, 8));
            UIFactory.Place((RectTransform)kiln.transform, 0.67f, 0f, 1f, 1f);
            _kilnTitle = UIFactory.Label(kiln.transform, "", UITheme.SizeSmall, UITheme.Candle, TextAlignmentOptions.Left, true);
            UIFactory.FixedHeight(_kilnTitle.gameObject, 20f);
            _heat = UIKit.Meter(kiln.transform, "Kiln heat — hold it in the band", UITheme.Ok, withBand: true, height: 14f);
            _dry = UIKit.Meter(kiln.transform, "Drying", UITheme.Candle, withBand: false, height: 14f);
            _kilnLine = UIFactory.Label(kiln.transform, "", UITheme.SizeTiny, UITheme.TextMid, TextAlignmentOptions.TopLeft);
            UIFactory.Flex(_kilnLine.gameObject, 1f, 1f, minHeight: 18f);
        }

        private static void SetPour(bool on)
        {
            if (Bench != null) Bench.PourHeld = on;
        }

        private static bool HasOrders =>
            CraftingManager.Instance != null && CraftingManager.Instance.Orders.Count > 0;

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

            ActiveOrder tub = b.TubOrder, kiln = b.KilnOrder;
            MaltStep step = tub != null ? tub.maltStep : MaltStep.Waiting;

            // ---- what to do now -------------------------------------------------
            string text; Color color = UITheme.TextHi;
            if (b.TurnDue) { text = "THE BED IS MATTING — TURN IT (CLICK THE JAR)"; color = PulseColor(); }
            else if (kiln != null && b.Heat01 > MaltingBench.HeatHigh) { text = "THE KILN IS TOO HOT — LET IT BURN DOWN"; color = UITheme.Danger; }
            else if (kiln != null && b.Heat01 < MaltingBench.HeatLow && !b.Draught)
            { text = "FEED THE KILN — CLICK THE LOGS"; color = PulseColor(); }
            else if (b.CanLoadKiln) { text = "IT HAS SPROUTED — LOAD THE KILN (CLICK THE JAR)"; color = PulseColor(); }
            else if (tub != null && step == MaltStep.Filling)
                text = b.GoodInJar < QualityBudget.GrainTarget - QualityBudget.GrainSlack
                    ? "HOLD ON THE SACK TO POUR GRAIN INTO THE JAR"
                    : "THAT'S THE LINE — STEEP IT";
            else if (tub != null && step == MaltStep.Soaking)
                text = b.HusksInJar > 0 ? $"SOAKING — SKIM THE {b.HusksInJar} FLOATING HUSK{(b.HusksInJar == 1 ? "" : "S")}" : "SOAKING — CLEAN STEEP";
            else if (tub != null && step == MaltStep.Germinating) { text = "GERMINATING — GO WORK ANOTHER BENCH"; color = UITheme.Ok; }
            else if (tub != null && step == MaltStep.Green) { text = "SPROUTED — WAITING FOR THE KILN"; color = UITheme.Candle; }
            else if (kiln != null) { text = "DRYING IN THE KILN — KEEP IT IN THE BAND"; color = UITheme.Ok; }
            else if (HasOrders) { text = AllPast(BrewStage.Malting) ? "ALL MALTED — ON TO PREP" : "NOTHING WAITING HERE"; color = UITheme.Ok; }
            else { text = "TAKE A JOB AT THE COUNTER FIRST"; color = UITheme.TextLow; }
            _status.text = text;
            _status.color = color;

            // ---- the jar --------------------------------------------------------
            _jarTitle.text = tub != null ? $"The steep — {tub.heroName}'s grain" : "The steep — empty";
            float target = QualityBudget.GrainTarget, slack = QualityBudget.GrainSlack, full = target * 1.6f;
            _grain.Set(Mathf.Clamp01(b.GoodInJar / full), $"{b.GoodInJar} / {QualityBudget.GrainTarget}");
            _grain.SetBand((target - slack) / full, (target + slack * 2f) / full);
            _grain.SetFillColor(b.GoodInJar < target - slack ? UITheme.CandleHot
                : b.GoodInJar > target + slack * 2f ? UITheme.Danger : UITheme.Ok);
            float clock = step == MaltStep.Soaking ? b.SoakProgress01 : step >= MaltStep.Germinating ? b.GerminateProgress01 : 0f;
            _jarClock.Set(clock, step == MaltStep.Soaking ? $"soak {PercentText.Of(clock)}"
                : step >= MaltStep.Germinating ? $"sprouting {PercentText.Of(clock)}" : "—");
            _jarLine.text = tub == null ? "" : $"Husks floating: {b.HusksInJar}" + (b.Vat ? "   ·   steeping vat" : "");

            // ---- the kiln -------------------------------------------------------
            _kilnTitle.text = kiln != null ? $"The kiln — {kiln.heroName}'s malt" : "The kiln — cold and empty";
            _heat.Set(b.Heat01, $"{Mathf.RoundToInt(b.Heat01 * 100)}°");
            _heat.SetBand(MaltingBench.HeatLow, MaltingBench.HeatHigh);
            _heat.SetFillColor(b.Heat01 > MaltingBench.HeatHigh ? UITheme.Danger
                : b.Heat01 < MaltingBench.HeatLow ? UITheme.Water : UITheme.Ok);
            _dry.Set(kiln != null ? b.KilnProgress01 : 0f, kiln != null ? PercentText.Of(b.KilnProgress01) : "—");
            _kilnLine.text = $"Logs burning: {b.LogsBurning}" + (b.Draught ? "   ·   draught kiln tends itself" : "");

            // ---- the buttons ----------------------------------------------------
            var pourBtn = _pour.GetComponent<Button>();
            if (pourBtn != null) pourBtn.interactable = tub != null && step == MaltStep.Filling;
            _steep.interactable = b.CanSteep;
            _skim.interactable = tub != null && (step == MaltStep.Filling || step == MaltStep.Soaking) && b.HusksInJar > 0;
            _turn.interactable = b.TurnDue;
            _load.interactable = b.CanLoadKiln;
            _log.interactable = b.CanStoke;
        }

        private static Color PulseColor()
        {
            float pulse = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 5f);
            return Color.Lerp(UITheme.Candle, UITheme.CandleHot, pulse);
        }
    }
}
