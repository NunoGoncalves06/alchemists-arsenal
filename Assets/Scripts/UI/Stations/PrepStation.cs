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
    /// The Prep bench. The work is all on the bench (<see cref="PrepBench"/>): drag or
    /// click leaves into the mortar, press and hold on the bowl to lift the pestle,
    /// let go to strike, with a strike dial on the bench. This panel pins the recipe
    /// up as a paper note and adds one caption for what to do next (or, while the
    /// pointer is on a leaf, what that leaf is).
    /// </summary>
    public class PrepStation : StationPanel
    {
        public override string RailName => "Prep";
        public override Sprite RailIcon => PixelSprites.Mortar();
        public override bool ShowsWorld => true;
        public override bool Complete => AllPast(Systems.BrewStage.Prep);

        /// <summary>The order on the Prep bench (see <see cref="PrepBench.Working"/>).</summary>
        protected override Systems.ActiveOrder Order => PrepBench.Instance != null ? PrepBench.Instance.Working : null;

        private BrewMixture Mix => Order?.Mixture;

        private Image _note;
        private TextMeshProUGUI _recipeName, _recipeMethod;
        private Transform _stepRow;
        private UIKit.CaptionView _caption;
        private PrepBench _hooked;
        private int _stepsSig = -1;

        // The note's leaf marks and what each one is, for the tooltip.
        private readonly System.Collections.Generic.List<RectTransform> _stepIcons = new System.Collections.Generic.List<RectTransform>();
        private readonly System.Collections.Generic.List<ElementType> _stepKinds = new System.Collections.Generic.List<ElementType>();
        private Image _tip;
        private TextMeshProUGUI _tipText;
        private int _tipAt = -1;

        /// <summary>The caption strip under the bench (what a hovered leaf is, or what to do next), or null when hidden.</summary>
        public string CaptionText => _caption != null && _caption.Root != null && _caption.Root.gameObject.activeSelf ? _caption.Text.text : null;

        /// <summary>What the leaf tooltip says right now, or null while it is hidden.</summary>
        public string TooltipText => _tip != null && _tip.gameObject.activeSelf ? _tipText.text : null;

        /// <summary>The note's leaf mark <paramref name="i"/>, or null.</summary>
        public RectTransform StepIcon(int i) => i >= 0 && i < _stepIcons.Count ? _stepIcons[i] : null;

        protected override void BuildContent(RectTransform root)
        {
            // The recipe, pinned to the wall above the bench: a paper note.
            _note = UIFactory.Panel(root, UITheme.Parchment, "RecipeNote");
            UIFactory.Place(_note.rectTransform, 0.02f, 0.78f, 0.40f, 0.92f);
            var stack = UIFactory.VStack(_note.transform, 4f, new RectOffset(14, 14, 10, 10));
            UIFactory.Stretch((RectTransform)stack.transform);
            _recipeName = UIFactory.Title(stack.transform, "", UITheme.SizeHeading, UITheme.Ink900);
            UIFactory.FixedHeight(_recipeName.gameObject, 26f);
            _recipeMethod = UIFactory.Label(stack.transform, "", UITheme.SizeTiny, UITheme.WoodDark);
            UIFactory.FixedHeight(_recipeMethod.gameObject, 30f);
            var steps = UIFactory.HStack(stack.transform, 6f);
            steps.childAlignment = TextAnchor.MiddleLeft;
            UIFactory.FixedHeight(steps.gameObject, 30f);
            _stepRow = steps.transform;
            PassThrough(_note.transform);

            _caption = UIKit.Caption(root, "PrepCaption");
            UIFactory.Place(_caption.Root.rectTransform, 0.18f, 0.035f, 0.82f, 0.095f);

            // The leaf tooltip: the leaf's kind, over the mark the pointer is on, at once.
            _tip = UIFactory.Panel(root, UITheme.Ink900, "LeafTip");
            var tr = _tip.rectTransform;
            tr.anchorMin = tr.anchorMax = new Vector2(0.5f, 0.5f);
            tr.pivot = new Vector2(0.5f, 0f);
            tr.sizeDelta = new Vector2(96f, 26f);
            _tipText = UIFactory.Label(_tip.transform, "", UITheme.SizeSmall, UITheme.TextHi, TextAlignmentOptions.Center, true);
            UIFactory.Stretch(_tipText.rectTransform, 2f);
            PassThrough(_tip.transform);
            _tip.gameObject.SetActive(false);
        }

        /// <summary>Overlays must never eat a click meant for the bench behind them.</summary>
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
            BrewMixture mix = Mix;
            var bench = PrepBench.Instance;
            if (bench != null && mix != null) bench.EnsureRecipeStock(mix);

            _note.gameObject.SetActive(mix != null);
            if (mix != null)
            {
                _recipeName.text = $"{mix.Recipe.Name} <size=60%>for {Order.heroName}</size>";
                _recipeMethod.text = mix.Recipe.Method;
                int sig = mix.Recipe.StepCount * 100 + mix.Added.Count;
                if (sig != _stepsSig) { _stepsSig = sig; BuildSteps(mix); }
            }
        }

        /// <summary>The recipe's leaves as little herb marks, ticked off as they go in.</summary>
        private void BuildSteps(BrewMixture mix)
        {
            Clear(_stepRow);
            _stepIcons.Clear();
            _stepKinds.Clear();
            _tipAt = -1;
            for (int i = 0; i < mix.Recipe.StepCount; i++)
            {
                ElementType want = mix.Recipe.Steps[i];
                bool filled = i < mix.Added.Count;
                ElementType got = filled ? mix.Added[i] : want;
                var icon = UIFactory.Icon(_stepRow, PixelSprites.Herb(got), 26f,
                    filled ? (got == want ? Color.white : UITheme.Alpha(UITheme.Danger, 0.9f)) : UITheme.Alpha(Color.white, 0.35f));
                UIFactory.Flex(icon.gameObject, 0f, 0f, minWidth: 28f, minHeight: 28f);
                _stepIcons.Add(icon.rectTransform);
                _stepKinds.Add(got);   // the mark shows what went in, so the tip names that
            }
            PassThrough(_stepRow);
        }

        public override void Tick()
        {
            TickTooltip();
            var bench = PrepBench.Instance;
            if (bench == null) return;
            var leaf = bench.Hovered;
            string line;
            if (leaf != null && !bench.Lifting)
                line = $"{leaf.Data.DisplayName}, {leaf.Data.Element}, potency {leaf.Data.Potency}" + (leaf.Data.Wilted ? ", wilted" : "");
            else if (Order == null)
                line = Systems.CraftingManager.Instance != null && Systems.CraftingManager.Instance.Orders.Count > 0
                    ? "" : "Take a job at the Counter first.";
            else
                line = !string.IsNullOrEmpty(bench.Reaction) && bench.StrikesLeft < QualityBudget.GrindStrikes && !bench.Lifting
                    ? bench.Reaction : bench.Hint;
            _caption.Set(line);
        }

        /// <summary>Shows the kind of the leaf mark under the pointer (no delay), hides it otherwise.</summary>
        private void TickTooltip()
        {
            if (_tip == null) return;
            int hit = -1;
            if (_note != null && _note.gameObject.activeInHierarchy)
            {
                Vector2 at = PhysicsKit.Pointer.Screen(Camera.main);
                for (int i = 0; i < _stepIcons.Count; i++)
                    if (_stepIcons[i] != null && RectTransformUtility.RectangleContainsScreenPoint(_stepIcons[i], at, null)) { hit = i; break; }
            }
            if (hit < 0) { _tipAt = -1; if (_tip.gameObject.activeSelf) _tip.gameObject.SetActive(false); return; }
            if (hit == _tipAt && _tip.gameObject.activeSelf) return;
            _tipAt = hit;
            _tipText.text = _stepKinds[hit].ToString();
            RectTransform icon = _stepIcons[hit];
            Vector3 top = icon.TransformPoint(new Vector3(icon.rect.center.x, icon.rect.yMax + 4f, 0f));
            _tip.rectTransform.position = top;
            if (!_tip.gameObject.activeSelf) _tip.gameObject.SetActive(true);
            _tip.rectTransform.SetAsLastSibling();
        }
    }
}
