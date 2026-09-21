using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using AlchemistsArsenal.Art;
using AlchemistsArsenal.Audio;
using AlchemistsArsenal.Combat;
using AlchemistsArsenal.Core;
using AlchemistsArsenal.Crafting;
using AlchemistsArsenal.Data;

namespace AlchemistsArsenal.UI.Stations
{
    /// <summary>
    /// The Prep bench — now the FIRST bench after the Counter, because you crush and
    /// add the leaves before there is anything in the pot to stir.
    ///
    /// Every potion has its own recipe (<see cref="RecipeBook"/>): a base doubled up
    /// and bound with a different element. You follow it leaf by leaf off the day's
    /// bench stock, each wrong one gives a named reaction rather than a silent
    /// penalty, and then you work the mortar. Finishing the grind settles the mix,
    /// and the mix decides how forgiving the Cauldron's heat band will be — which is
    /// what makes the two stations depend on each other rather than merely follow
    /// one another.
    /// </summary>
    public class PrepStation : StationPanel
    {
        private const int GrindTaps = 3;

        public override string RailName => "Prep";
        public override Sprite RailIcon => PixelSprites.Mortar();
        public override bool Complete => Mix != null && Mix.Ready;

        private static BrewMixture Mix =>
            Systems.CraftingManager.Instance != null ? Systems.CraftingManager.Instance.Mixture : null;

        private struct Ingredient
        {
            public string Name;
            public ElementType Element;
            public int Potency;   // 1..3
            public bool Wilted;
        }

        private readonly List<Ingredient> _stock = new List<Ingredient>();
        private readonly List<Button> _trayButtons = new List<Button>();
        private readonly List<Image> _stepChips = new List<Image>();
        private readonly List<Image> _stepIcons = new List<Image>();

        private Transform _trayTop, _trayBottom, _stepRow;
        private TextMeshProUGUI _recipeName, _recipeMethod, _hint, _grindHint, _reaction;
        private Image _grindBand, _grindNeedle;
        private Button _grindButton;

        private int _grindsLeft = GrindTaps;
        private float _grindHalfWidth = 0.075f;
        private int _rolledForDay = -1;

        protected override void BuildContent(RectTransform root)
        {
            // --- the recipe ---------------------------------------------------
            var recipeCard = UIKit.Card(root, "The recipe", out Transform recipe, spacing: 6f);
            UIFactory.Place(recipeCard.rectTransform, 0f, 0.70f, 1f, 1f);

            _recipeName = UIFactory.Title(recipe, "", UITheme.SizeHeading + 2, UITheme.Candle);
            UIFactory.FixedHeight(_recipeName.gameObject, 32f);
            _recipeMethod = UIFactory.Label(recipe, "", UITheme.SizeSmall, UITheme.TextMid);
            UIFactory.FixedHeight(_recipeMethod.gameObject, 24f);

            var steps = UIFactory.HStack(recipe, 10f);
            UIFactory.FixedHeight(steps.gameObject, 54f);
            _stepRow = steps.transform;

            // --- bench stock --------------------------------------------------
            var bench = UIKit.Card(root, "Bench stock", out Transform benchBox, spacing: 6f);
            UIFactory.Place(bench.rectTransform, 0f, 0.30f, 1f, 0.68f);

            _hint = UIFactory.Label(benchBox, "", UITheme.SizeSmall, UITheme.TextMid);
            UIFactory.FixedHeight(_hint.gameObject, 22f);

            var top = UIFactory.HStack(benchBox, 12f);
            UIFactory.Flex(top.gameObject, 1f, 1f, minHeight: 100f);
            _trayTop = top.transform;

            var bottom = UIFactory.HStack(benchBox, 12f);
            UIFactory.Flex(bottom.gameObject, 1f, 1f, minHeight: 100f);
            _trayBottom = bottom.transform;

            // --- mortar -------------------------------------------------------
            var mortar = UIKit.Card(root, "Mortar and pestle — three clean strikes", out Transform mortarBox,
                spacing: 6f);
            UIFactory.Place(mortar.rectTransform, 0f, 0f, 1f, 0.28f);
            mortar.rectTransform.offsetMax = new Vector2(0f, -10f);

            _grindHint = UIFactory.Label(mortarBox, "", UITheme.SizeSmall, UITheme.TextMid);
            UIFactory.FixedHeight(_grindHint.gameObject, 22f);

            var track = UIFactory.Panel(mortarBox, UITheme.Ground, "GrindTrack");
            UIFactory.FixedHeight(track.gameObject, 30f);

            _grindBand = UIFactory.Panel(track.transform, UITheme.Alpha(UITheme.Ok, 0.35f), "Band");
            _grindNeedle = UIFactory.Panel(track.transform, UITheme.CandleHot, "Needle");
            _grindNeedle.rectTransform.anchorMin = _grindNeedle.rectTransform.anchorMax = new Vector2(0f, 0.5f);
            _grindNeedle.rectTransform.sizeDelta = new Vector2(8f, 40f);
            ApplyBand();

            _grindButton = UIFactory.Button(mortarBox, "GRIND", Grind);
            UIFactory.FixedHeight(_grindButton.gameObject, 42f);

            _reaction = UIFactory.Label(mortarBox, "", UITheme.SizeSmall, UITheme.TextLow);
            UIFactory.FixedHeight(_reaction.gameObject, 22f);
        }

        // ------------------------------------------------------------- lifecycle

        public override void NewDay()
        {
            _grindsLeft = GrindTaps;
            _grindHalfWidth = 0.075f;
            ApplyBand();
            RollStock();
            BuildTray();
            if (_reaction != null) _reaction.text = "";
            Refresh();
        }

        public override void OnEnter()
        {
            int day = SaveSystem.Instance != null && SaveSystem.Instance.State != null
                ? SaveSystem.Instance.State.day : 1;
            if (_stock.Count == 0 || _rolledForDay != day) NewDay();
            Refresh();
        }

        public override void Refresh()
        {
            BuildSteps();

            BrewMixture mix = Mix;
            if (_recipeName != null)
                _recipeName.text = mix != null
                    ? $"{mix.Recipe.Name}  <size=70%><color=#{ColorUtility.ToHtmlStringRGB(UITheme.TextLow)}>{mix.Recipe.Shorthand}</color></size>"
                    : "No recipe yet";
            if (_recipeMethod != null)
                _recipeMethod.text = mix != null
                    ? mix.Recipe.Method
                    : "Take a job at the Counter — the customer's order decides the recipe.";

            if (_hint != null)
            {
                _hint.text = mix == null
                    ? "Nothing to prep until there is an order."
                    : mix.AllLeavesIn
                        ? "All the leaves are in. Work the mortar."
                        : $"Next the recipe wants <b>{mix.NextStep}</b>. " +
                          $"{mix.Remaining} leaf/leaves still to go.";
            }

            if (_grindHint != null)
            {
                _grindHint.text = mix == null ? ""
                    : !mix.AllLeavesIn ? "Add every leaf before you start grinding."
                    : mix.Ground ? "Ground and ready — the pot will take it now."
                    : $"Click GRIND when the pestle is over the band. <b>{_grindsLeft}</b> strike(s) left.";
            }

            if (_grindButton != null)
                _grindButton.interactable = mix != null && mix.AllLeavesIn && !mix.Ground && _grindsLeft > 0;

            SetTrayInteractable(mix != null && !mix.AllLeavesIn);
        }

        public override void Tick()
        {
            if (_grindNeedle == null) return;
            float t = Needle01();
            _grindNeedle.rectTransform.anchorMin = _grindNeedle.rectTransform.anchorMax = new Vector2(t, 0.5f);
            _grindNeedle.color = Mathf.Abs(t - 0.5f) <= _grindHalfWidth ? UITheme.Ok : UITheme.CandleHot;
        }

        // ------------------------------------------------------------- the recipe

        /// <summary>The recipe as a row of slots that fill in as leaves go in.</summary>
        private void BuildSteps()
        {
            Clear(_stepRow);
            _stepChips.Clear();
            _stepIcons.Clear();

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
                UIFactory.Flex(slot.gameObject, 1f, 1f, minWidth: 120f, minHeight: 48f);

                var icon = UIFactory.Icon(inner, PixelSprites.Herb(filled ? got : want), 30f,
                    filled ? Color.white : UITheme.Alpha(Color.white, 0.3f));
                UIFactory.Place(icon.rectTransform, 0.04f, 0.15f, 0.30f, 0.85f);

                var label = UIFactory.Label(inner,
                    filled ? $"{got}" : $"<i>{want}</i>",
                    UITheme.SizeSmall, filled ? UITheme.TextHi : UITheme.TextLow,
                    TextAlignmentOptions.Left, filled);
                UIFactory.Place(label.rectTransform, 0.33f, 0f, 0.98f, 1f);

                _stepChips.Add(slot);
                _stepIcons.Add(icon);
            }
        }

        // ----------------------------------------------------------------- stock

        private void RollStock()
        {
            RunState s = SaveSystem.Instance != null ? SaveSystem.Instance.State : null;
            int day = s != null ? s.day : 1;
            _rolledForDay = day;
            var rng = new System.Random(day * 7717 + 31);

            _stock.Clear();
            // One of every element, so whatever recipe came off the Counter there is
            // always a correct answer on the bench — plus one wildcard.
            foreach (ElementType e in new[]
                     { ElementType.Nature, ElementType.Fire, ElementType.Water, ElementType.Poison, ElementType.Arcane })
                _stock.Add(Roll(e, rng));
            _stock.Add(Roll((ElementType)rng.Next(0, 5), rng));

            for (int i = _stock.Count - 1; i > 0; i--)
            {
                int j = rng.Next(0, i + 1);
                (_stock[i], _stock[j]) = (_stock[j], _stock[i]);
            }
        }

        private static Ingredient Roll(ElementType element, System.Random rng) => new Ingredient
        {
            Name = NameFor(element, rng.Next(0, 2)),
            Element = element,
            Potency = rng.Next(1, 4),
            Wilted = rng.Next(0, 100) < 25,
        };

        private static string NameFor(ElementType e, int variant) => e switch
        {
            ElementType.Nature => variant == 0 ? "Bark Shaving" : "Moss Cap",
            ElementType.Fire => variant == 0 ? "Ember Root" : "Cinder Pod",
            ElementType.Water => variant == 0 ? "Frost Lily" : "Deepwater Kelp",
            ElementType.Poison => variant == 0 ? "Bog Spore" : "Viper Leaf",
            _ => variant == 0 ? "Star Anise" : "Hexbloom",
        };

        private void BuildTray()
        {
            Clear(_trayTop);
            Clear(_trayBottom);
            _trayButtons.Clear();

            for (int i = 0; i < _stock.Count; i++)
                BuildIngredientCard(i < 3 ? _trayTop : _trayBottom, i, _stock[i]);
        }

        private void BuildIngredientCard(Transform parent, int index, Ingredient ing)
        {
            Color tint = UITheme.Element(ing.Element);
            Image card = UIKit.Surface(parent, out Transform inner, UITheme.Surface, UITheme.Line, "Ingredient");
            UIFactory.Flex(card.gameObject, 1f, 1f, minWidth: 140f);

            var col = UIFactory.VStack(inner, 2f, new RectOffset(10, 10, 6, 2));
            UIFactory.Place((RectTransform)col.transform, 0f, 0.34f, 1f, 1f);

            var head = UIFactory.HStack(col.transform, 8f);
            head.childAlignment = TextAnchor.MiddleLeft;
            UIFactory.FixedHeight(head.gameObject, 40f);
            var leaf = UIFactory.Icon(head.transform, PixelSprites.Herb(ing.Element), 36f);
            UIFactory.Flex(leaf.gameObject, 0f, 0f, minWidth: 36f, minHeight: 36f);
            var name = UIFactory.Label(head.transform, ing.Name, UITheme.SizeSmall, UITheme.TextHi,
                TextAlignmentOptions.Left, true);
            UIFactory.Flex(name.gameObject, 1f, 1f);

            var meta = UIFactory.Label(col.transform,
                $"<color=#{ColorUtility.ToHtmlStringRGB(tint)}>{ing.Element}</color>   " +
                $"potency {new string('*', ing.Potency)}" + (ing.Wilted ? "   <i>wilted</i>" : ""),
                UITheme.SizeTiny, UITheme.TextMid);
            UIFactory.Flex(meta.gameObject, 1f, 1f, minHeight: 24f);

            int captured = index;
            var add = UIFactory.Button(inner, "CRUSH IN", () => Pick(captured), primary: false);
            UIFactory.Place(add.image.rectTransform, 0.06f, 0.05f, 0.94f, 0.28f);
            UIFactory.TintButton(add, UITheme.Alpha(tint, 0.32f), UITheme.Alpha(tint, 0.6f), UITheme.TextHi);
            _trayButtons.Add(add);
        }

        private void SetTrayInteractable(bool on)
        {
            foreach (var b in _trayButtons)
                if (b != null && b.gameObject.activeInHierarchy) b.interactable = on;
        }

        private void Pick(int index)
        {
            var order = Order;
            BrewMixture mix = Mix;
            if (order == null || mix == null || mix.AllLeavesIn) return;
            if (index < 0 || index >= _stock.Count) return;

            Ingredient ing = _stock[index];
            bool onCue = mix.IsNextStep(ing.Element);
            bool inRecipe = System.Array.IndexOf(mix.Recipe.Steps, ing.Element) >= 0;
            float wilt = ing.Wilted ? 0.5f : 1f;

            mix.Added.Add(ing.Element);

            if (onCue)
            {
                int gain = Mathf.RoundToInt((2 + ing.Potency) * wilt);
                order.ApplyBonus(gain, "Prep", $"{ing.Name} in on cue (+{gain})");
                SetReaction($"{ing.Name} goes in cleanly.", UITheme.Ok);
            }
            else if (inRecipe)
            {
                int gain = Mathf.Max(1, Mathf.RoundToInt((1f + ing.Potency * 0.35f) * wilt));
                order.ApplyBonus(gain, "Prep", $"{ing.Name} — right leaf, out of order (+{gain})");
                SetReaction($"{ing.Name} belongs here, just not yet.", UITheme.Candle);
            }
            else
            {
                int loss = 6 + 2 * ing.Potency;
                string reaction = RecipeBook.Reaction(mix.Recipe.Result, ing.Element);
                order.ApplyDeduction(loss, "Prep", $"{ing.Name} — {reaction}");
                SetReaction($"{ing.Name}: {reaction}", UITheme.Danger);
            }

            var pot = PhysicsCauldronManager.Instance;
            if (pot != null) pot.DropIngredient(ing.Element);

            if (index < _trayButtons.Count && _trayButtons[index] != null)
            {
                _trayButtons[index].interactable = false;
                UIFactory.SetButtonText(_trayButtons[index], "IN THE POT");
            }

            AudioManager.Play(onCue || inRecipe ? Sfx.Confirm : Sfx.Deny);
            Refresh();
            Changed?.Invoke();
        }

        // ----------------------------------------------------------------- grind

        private static float Needle01() => Mathf.PingPong(Time.unscaledTime * 0.9f, 1f);

        private void ApplyBand()
        {
            if (_grindBand == null) return;
            _grindBand.rectTransform.anchorMin = new Vector2(0.5f - _grindHalfWidth, 0f);
            _grindBand.rectTransform.anchorMax = new Vector2(0.5f + _grindHalfWidth, 1f);
            _grindBand.rectTransform.offsetMin = _grindBand.rectTransform.offsetMax = Vector2.zero;
        }

        private void Grind()
        {
            var order = Order;
            BrewMixture mix = Mix;
            if (order == null || mix == null || !mix.AllLeavesIn || mix.Ground || _grindsLeft <= 0) return;

            float dist = Mathf.Abs(Needle01() - 0.5f);
            bool hit = dist <= _grindHalfWidth;
            _grindsLeft--;

            if (hit)
            {
                int gain = Mathf.RoundToInt(Mathf.Lerp(4f, 2f, dist / Mathf.Max(0.001f, _grindHalfWidth)));
                order.ApplyBonus(gain, "Prep", $"Clean strike (+{gain})");
                AudioManager.Play(Sfx.Seal);
            }
            else
            {
                order.ApplyDeduction(6, "Prep", "Pestle skidded — bruised the mix");
                AudioManager.Play(Sfx.Deny);
            }

            _grindHalfWidth = Mathf.Max(0.035f, _grindHalfWidth - 0.018f);
            ApplyBand();

            if (_grindsLeft <= 0) FinishMix(order, mix);

            Refresh();
            Changed?.Invoke();
        }

        /// <summary>
        /// Settle the mixture: score it against the recipe, pay out (or charge for)
        /// the result, and hand the Cauldron the band width it has earned.
        /// </summary>
        private void FinishMix(Systems.ActiveOrder order, BrewMixture mix)
        {
            mix.Ground = true;

            MixOutcome outcome = mix.Evaluate();
            int delta = RecipeBook.QualityDelta(outcome);
            if (delta > 0) order.ApplyBonus(delta, "Prep", $"{outcome} mix — {mix.Recipe.Name}");
            else if (delta < 0) order.ApplyDeduction(-delta, "Prep", $"{outcome} mix — {mix.Recipe.Name}");

            var pot = PhysicsCauldronManager.Instance;
            if (pot != null) pot.ApplyMix(outcome);

            SetReaction(RecipeBook.Describe(outcome),
                outcome == MixOutcome.Perfect ? UITheme.Ok
                : outcome == MixOutcome.Close ? UITheme.Candle : UITheme.Danger);
            AudioManager.Play(outcome >= MixOutcome.Close ? Sfx.Chime : Sfx.Deny);
        }

        private void SetReaction(string text, Color color)
        {
            if (_reaction == null) return;
            _reaction.text = text;
            _reaction.color = color;
        }
    }
}
