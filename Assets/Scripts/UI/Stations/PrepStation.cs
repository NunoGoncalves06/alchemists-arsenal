using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using AlchemistsArsenal.Art;
using AlchemistsArsenal.Combat;
using AlchemistsArsenal.Core;
using AlchemistsArsenal.Crafting;
using AlchemistsArsenal.Data;

namespace AlchemistsArsenal.UI.Stations
{
    /// <summary>
    /// The Prep bench's HUD. The bench itself is physical and lives in the shop world
    /// (<see cref="PrepBench"/>): leaves you drag or click into a mortar, a pestle you
    /// lift and drop. This panel only explains and reads it back:
    /// <list type="bullet">
    /// <item>the recipe, as slots that fill in as leaves settle in the bowl (top);</item>
    /// <item>what to do next, and what the leaf under the pointer is (bottom left);</item>
    /// <item>a strike meter: the clean band around the ideal speed, the last strike,
    /// and, while you hold on the mortar, the speed the pestle would land at if you
    /// let go now (bottom middle);</item>
    /// <item>the bench's last reaction (bottom right).</item>
    /// </list>
    /// Nothing here takes a raycast over the world view, so the bench is always reachable.
    /// </summary>
    public class PrepStation : StationPanel
    {
        private const float MeterMax = 8f;   // m/s shown across the strike meter

        public override string RailName => "Prep";
        public override Sprite RailIcon => PixelSprites.Mortar();
        public override bool ShowsWorld => true;
        public override bool Complete => Mix != null && Mix.Ready;

        private static BrewMixture Mix =>
            Systems.CraftingManager.Instance != null ? Systems.CraftingManager.Instance.Mixture : null;

        private TextMeshProUGUI _recipeName, _recipeMethod, _hint, _leafInfo, _reaction, _strikeLabel, _status;
        private Transform _stepRow;
        private Image _band, _last, _predicted, _track;
        private PrepBench _hooked;

        protected override void BuildContent(RectTransform root)
        {
            // --- the recipe, over the wall behind the bench -------------------
            var recipe = UIKit.Card(root, "The recipe", out Transform card, spacing: 4f);
            UIFactory.Place(recipe.rectTransform, 0f, 0.66f, 0.56f, 0.875f);
            _recipeName = UIFactory.Title(card, "", UITheme.SizeHeading, UITheme.Candle);
            UIFactory.FixedHeight(_recipeName.gameObject, 28f);
            _recipeMethod = UIFactory.Label(card, "", UITheme.SizeTiny, UITheme.TextMid);
            UIFactory.FixedHeight(_recipeMethod.gameObject, 18f);
            var steps = UIFactory.HStack(card, 8f);
            UIFactory.FixedHeight(steps.gameObject, 40f);
            _stepRow = steps.transform;
            PassThrough(recipe.transform);

            // What to do right now, big, in a band across the top of the view (clear
            // of the bowl, where the bench's own arrow points): the step that was
            // missed in play was "the leaves are in — now press the bowl".
            _status = UIFactory.Label(root, "", UITheme.SizeTitle, UITheme.TextLow, TextAlignmentOptions.Center, true);
            UIFactory.Place(_status.rectTransform, 0.02f, 0.885f, 0.98f, 1f);
            _status.raycastTarget = false;

            // --- the HUD strip under the world view ---------------------------
            var strip = UIKit.Surface(root, out Transform s, UITheme.Alpha(UITheme.Ground, 0.94f), UITheme.Line, "Hud");
            UIFactory.Place(strip.rectTransform, 0f, 0f, 1f, 0.255f);

            var left = UIFactory.VStack(s, 4f, new RectOffset(16, 8, 10, 10));
            UIFactory.Place((RectTransform)left.transform, 0f, 0f, 0.42f, 1f);
            _hint = UIFactory.Label(left.transform, "", UITheme.SizeBody, UITheme.TextHi, TextAlignmentOptions.TopLeft);
            UIFactory.Flex(_hint.gameObject, 1f, 1f, minHeight: 44f);
            _leafInfo = UIFactory.Label(left.transform, "", UITheme.SizeSmall, UITheme.TextMid, TextAlignmentOptions.TopLeft);
            UIFactory.Flex(_leafInfo.gameObject, 1f, 1f, minHeight: 40f);

            var mid = UIFactory.VStack(s, 6f, new RectOffset(8, 8, 10, 10));
            UIFactory.Place((RectTransform)mid.transform, 0.42f, 0f, 0.72f, 1f);
            var cap = UIFactory.Heading(mid.transform, "Strike speed", UITheme.TextLow, UITheme.SizeTiny);
            UIFactory.FixedHeight(cap.gameObject, 16f);
            _track = UIFactory.Panel(mid.transform, UITheme.Surface, "StrikeTrack");
            UIFactory.FixedHeight(_track.gameObject, 30f);
            _band = UIFactory.Panel(_track.transform, UITheme.Alpha(UITheme.Ok, 0.4f), "Band");
            _predicted = UIFactory.Panel(_track.transform, UITheme.Alpha(UITheme.CandleHot, 0.9f), "Predicted");
            _predicted.rectTransform.sizeDelta = new Vector2(6f, 0f);
            _last = UIFactory.Panel(_track.transform, UITheme.TextHi, "Last");
            _last.rectTransform.sizeDelta = new Vector2(4f, 0f);
            _strikeLabel = UIFactory.MonoLabel(mid.transform, "", UITheme.SizeTiny, UITheme.TextMid, TextAlignmentOptions.Left);
            UIFactory.Flex(_strikeLabel.gameObject, 1f, 1f, minHeight: 34f);

            var right = UIFactory.VStack(s, 4f, new RectOffset(8, 16, 10, 10));
            UIFactory.Place((RectTransform)right.transform, 0.72f, 0f, 1f, 1f);
            _reaction = UIFactory.Label(right.transform, "", UITheme.SizeSmall, UITheme.TextLow, TextAlignmentOptions.TopLeft);
            UIFactory.Flex(_reaction.gameObject, 1f, 1f);
        }

        /// <summary>Overlay cards must never eat a click meant for the bench behind them.</summary>
        private static void PassThrough(Transform t)
        {
            foreach (var g in t.GetComponentsInChildren<Graphic>(true)) g.raycastTarget = false;
        }

        // ------------------------------------------------------------- lifecycle

        public override void NewDay()
        {
            var bench = PrepBench.Instance;
            int day = SaveSystem.Instance != null && SaveSystem.Instance.State != null ? SaveSystem.Instance.State.day : 1;
            if (bench != null && !bench.RolledFor(day)) bench.NewDay(day);
            Refresh();
        }

        public override void OnEnter()
        {
            var bench = PrepBench.Instance;
            if (bench != null)
            {
                int day = SaveSystem.Instance != null && SaveSystem.Instance.State != null ? SaveSystem.Instance.State.day : 1;
                if (!bench.RolledFor(day)) bench.NewDay(day);
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
            if (PrepBench.Instance != null) PrepBench.Instance.Attended = false;
        }

        private void OnBenchChanged()
        {
            Refresh();
            Changed?.Invoke();
        }

        public override void Refresh()
        {
            BuildSteps();
            BrewMixture mix = Mix;
            var bench = PrepBench.Instance;
            if (bench != null && mix != null) bench.EnsureRecipeStock(mix);

            _recipeName.text = mix != null
                ? $"{mix.Recipe.Name}  <size=70%><color=#{ColorUtility.ToHtmlStringRGB(UITheme.TextLow)}>{mix.Recipe.Shorthand}</color></size>"
                : "No recipe yet";
            _recipeMethod.text = mix != null ? mix.Recipe.Method
                : "Take a job at the Counter — the customer's order decides the recipe.";

            _hint.text = mix == null ? "Nothing to prep until there is an order."
                : !mix.AllLeavesIn
                    ? $"Get <b>{mix.NextStep}</b> into the mortar next — drag it in, or click a leaf to toss it. " +
                      $"{mix.Remaining} to go."
                : !mix.Ground
                    ? "<b>Press and hold on the bowl</b> — the pestle rises over it. <b>Let go</b> and it drops and smashes the leaves. Three clean strikes."
                : "Ground and tipped into the pot. On to the Cauldron.";

            if (bench != null)
            {
                _reaction.text = bench.Reaction;
                _reaction.color = bench.ReactionColor;
            }
        }

        private void BuildSteps()
        {
            Clear(_stepRow);
            BrewMixture mix = Mix;
            if (mix == null) return;

            for (int i = 0; i < mix.Recipe.StepCount; i++)
            {
                ElementType want = mix.Recipe.Steps[i];
                bool filled = i < mix.Added.Count;
                ElementType got = filled ? mix.Added[i] : want;
                bool right = filled && got == want;

                Image slot = UIKit.Surface(_stepRow, out Transform inner,
                    filled ? UITheme.Alpha(UITheme.Element(got), 0.22f) : UITheme.SurfaceHi,
                    filled ? (right ? UITheme.Ok : UITheme.Danger) : UITheme.LineSoft, "Step");
                UIFactory.Flex(slot.gameObject, 1f, 1f, minWidth: 90f, minHeight: 36f);
                var icon = UIFactory.Icon(inner, PixelSprites.Herb(got), 24f,
                    filled ? Color.white : UITheme.Alpha(Color.white, 0.3f));
                UIFactory.Place(icon.rectTransform, 0.04f, 0.12f, 0.32f, 0.88f);
                var label = UIFactory.Label(inner, filled ? $"{got}" : $"<i>{want}</i>",
                    UITheme.SizeTiny, filled ? UITheme.TextHi : UITheme.TextLow, TextAlignmentOptions.Left, filled);
                UIFactory.Place(label.rectTransform, 0.36f, 0f, 0.98f, 1f);
            }
            PassThrough(_stepRow);
        }

        public override void Tick()
        {
            var bench = PrepBench.Instance;
            if (bench == null) return;
            UpdateStatus(bench);

            // The leaf under the pointer, spelled out: potency and wilt are also
            // visible on the bench (plumper, and drooping grey), but this names them.
            var leaf = bench.Hovered;
            _leafInfo.text = leaf == null ? "Point at a leaf to read it."
                : $"<b>{leaf.Data.DisplayName}</b>  <color=#{ColorUtility.ToHtmlStringRGB(UITheme.Element(leaf.Data.Element))}>{leaf.Data.Element}</color>" +
                  $"   potency {new string('*', leaf.Data.Potency)}" + (leaf.Data.Wilted ? "   <i>wilted — half as good</i>" : "");

            float band = bench.CurrentBand;
            Anchor(_band, (PrepBench.IdealStrike - band) / MeterMax, (PrepBench.IdealStrike + band) / MeterMax);
            bool lifting = bench.LiftReady;
            _predicted.gameObject.SetActive(lifting);
            if (lifting)
            {
                float v = bench.PredictedStrikeSpeed;
                Mark(_predicted, v / MeterMax);
                _predicted.color = Mathf.Abs(v - PrepBench.IdealStrike) <= band ? UITheme.Ok : UITheme.CandleHot;
            }
            _last.gameObject.SetActive(bench.LastStrikeSpeed >= 0f);
            if (bench.LastStrikeSpeed >= 0f) Mark(_last, bench.LastStrikeSpeed / MeterMax);

            _strikeLabel.text = lifting
                ? $"Lifting — would land at {bench.PredictedStrikeSpeed:0.0} m/s (clean: {PrepBench.IdealStrike - band:0.0}-{PrepBench.IdealStrike + band:0.0})"
                : bench.LastStrikeSpeed >= 0f
                    ? $"Last strike {bench.LastStrikeSpeed:0.0} m/s. {bench.StrikesLeft} left."
                    : $"{bench.StrikesLeft} strikes. Clean is {PrepBench.IdealStrike - band:0.0}-{PrepBench.IdealStrike + band:0.0} m/s.";
        }

        /// <summary>The one instruction that matters right now, pulsing when it is the pestle's turn.</summary>
        private void UpdateStatus(PrepBench bench)
        {
            if (_status == null) return;
            BrewMixture mix = Mix;
            string text; Color color;
            if (mix == null) { text = "TAKE A JOB AT THE COUNTER FIRST"; color = UITheme.TextLow; }
            else if (!mix.AllLeavesIn) { text = $"PUT THE LEAVES IN THE MORTAR — {mix.Remaining} TO GO"; color = UITheme.TextHi; }
            else if (mix.Ground) { text = "GROUND — ON TO THE CAULDRON"; color = UITheme.Ok; }
            else if (bench.LiftReady) { text = "LET GO WHEN THE SPEED IS IN THE GREEN"; color = UITheme.Candle; }
            else if (bench.Lifting) { text = "HOLD IT — THE PESTLE IS COMING OVER"; color = UITheme.Candle; }
            else
            {
                text = "NOW PRESS AND HOLD ON THE BOWL <size=65%>— the pestle rises; let go to smash the leaves</size>";
                float pulse = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 5f);
                color = Color.Lerp(UITheme.Candle, UITheme.CandleHot, pulse);
            }
            _status.text = text;
            _status.color = color;
        }

        private static void Anchor(Image img, float x0, float x1)
        {
            var rt = img.rectTransform;
            rt.anchorMin = new Vector2(Mathf.Clamp01(x0), 0f);
            rt.anchorMax = new Vector2(Mathf.Clamp01(x1), 1f);
            rt.offsetMin = rt.offsetMax = Vector2.zero;
        }

        private static void Mark(Image img, float x)
        {
            var rt = img.rectTransform;
            rt.anchorMin = new Vector2(Mathf.Clamp01(x), 0f);
            rt.anchorMax = new Vector2(Mathf.Clamp01(x), 1f);
            rt.anchoredPosition = Vector2.zero;
        }
    }
}
