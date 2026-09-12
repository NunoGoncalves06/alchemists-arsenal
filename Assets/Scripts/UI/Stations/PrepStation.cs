using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using AlchemistsArsenal.Art;
using AlchemistsArsenal.Audio;
using AlchemistsArsenal.Combat;
using AlchemistsArsenal.Core;
using AlchemistsArsenal.Crafting;

namespace AlchemistsArsenal.UI.Stations
{
    /// <summary>
    /// The Prep bench. Used to be five buttons labelled with element names; it is now
    /// a stocked bench you choose from and a mortar you actually work.
    ///
    /// Two decisions, both real: which three of today's six ingredients go in (they
    /// differ in element, potency and freshness, and a wrong element costs you), and
    /// then how cleanly you grind them (a three-tap timing pass). Everything picked
    /// is dropped into the cauldron as a physics body, so the Cauldron tab shows what
    /// the bench decided.
    /// </summary>
    public class PrepStation : StationPanel
    {
        private const int MaxPicks = 3;
        private const int GrindTaps = 3;

        public override string RailName => "Prep";
        public override Sprite RailIcon => PixelSprites.Mortar();
        public override bool Complete => _picksUsed > 0 && _grindsLeft == 0;

        private struct Ingredient
        {
            public string Name;
            public ElementType Element;
            public int Potency;   // 1..3
            public bool Wilted;
        }

        private readonly List<Ingredient> _stock = new List<Ingredient>();
        private Transform _trayTop, _trayBottom;
        private TextMeshProUGUI _hint, _grindHint;
        private Image _grindBand, _grindNeedle;
        private Button _grindButton;

        private int _picksUsed;
        private int _grindsLeft = GrindTaps;
        private float _grindHalfWidth = 0.075f;

        protected override void BuildContent(RectTransform root)
        {
            var bench = UIKit.Card(root, "Bench stock — pick three", out Transform benchBox);
            UIFactory.Place(bench.rectTransform, 0f, 0.34f, 1f, 1f);

            _hint = UIFactory.Label(benchBox, "", UITheme.SizeBody, UITheme.TextMid);
            UIFactory.FixedHeight(_hint.gameObject, 26f);

            var top = UIFactory.HStack(benchBox, 12f);
            UIFactory.Flex(top.gameObject, 1f, 1f, minHeight: 120f);
            _trayTop = top.transform;

            var bottom = UIFactory.HStack(benchBox, 12f);
            UIFactory.Flex(bottom.gameObject, 1f, 1f, minHeight: 120f);
            _trayBottom = bottom.transform;

            var mortar = UIKit.Card(root, "Mortar and pestle — three clean strikes", out Transform mortarBox);
            UIFactory.Place(mortar.rectTransform, 0f, 0f, 1f, 0.32f);
            mortar.rectTransform.offsetMax = new Vector2(0f, -12f);

            _grindHint = UIFactory.Label(mortarBox, "", UITheme.SizeBody, UITheme.TextMid);
            UIFactory.FixedHeight(_grindHint.gameObject, 26f);

            var track = UIFactory.Panel(mortarBox, UITheme.Ground, "GrindTrack");
            UIFactory.FixedHeight(track.gameObject, 34f);

            _grindBand = UIFactory.Panel(track.transform, UITheme.Alpha(UITheme.Ok, 0.35f), "Band");
            _grindNeedle = UIFactory.Panel(track.transform, UITheme.CandleHot, "Needle");
            _grindNeedle.rectTransform.anchorMin = _grindNeedle.rectTransform.anchorMax = new Vector2(0f, 0.5f);
            _grindNeedle.rectTransform.sizeDelta = new Vector2(8f, 44f);
            ApplyBand();

            _grindButton = UIFactory.Button(mortarBox, "GRIND", Grind);
            UIFactory.FixedHeight(_grindButton.gameObject, 46f);
        }

        public override void NewDay()
        {
            _picksUsed = 0;
            _grindsLeft = GrindTaps;
            _grindHalfWidth = 0.075f;
            ApplyBand();
            RollStock();
            BuildTray();
            Refresh();
        }

        public override void OnEnter()
        {
            if (_stock.Count == 0) NewDay();
            Refresh();
        }

        public override void Refresh()
        {
            var order = Order;
            if (_hint != null)
            {
                _hint.text = order == null
                    ? "Take a job at the Counter first — until then there's nothing to prep for."
                    : $"Brewing <b>{order.element}</b>. Matching ingredients raise quality; the wrong element " +
                      $"costs you. <b>{MaxPicks - _picksUsed}</b> pick(s) left.";
            }
            if (_grindHint != null)
            {
                _grindHint.text = order == null
                    ? ""
                    : _grindsLeft > 0
                        ? $"Click GRIND when the pestle is over the band. <b>{_grindsLeft}</b> strike(s) left — the band narrows each time."
                        : "Ground and ready.";
            }
            if (_grindButton != null) _grindButton.interactable = order != null && _grindsLeft > 0;
            SetTrayInteractable(order != null && _picksUsed < MaxPicks);
        }

        public override void Tick()
        {
            if (_grindNeedle == null) return;
            float t = Needle01();
            _grindNeedle.rectTransform.anchorMin = _grindNeedle.rectTransform.anchorMax = new Vector2(t, 0.5f);
            bool hot = Mathf.Abs(t - 0.5f) <= _grindHalfWidth;
            _grindNeedle.color = hot ? UITheme.Ok : UITheme.CandleHot;
        }

        // ----------------------------------------------------------------- stock

        private void RollStock()
        {
            RunState s = SaveSystem.Instance != null ? SaveSystem.Instance.State : null;
            int day = s != null ? s.day : 1;
            var rng = new System.Random(day * 7717 + 31);

            _stock.Clear();
            // One of every element, so whatever job was taken there is always a
            // correct answer on the bench — plus one wildcard to make it a choice.
            foreach (ElementType e in new[]
                     { ElementType.Nature, ElementType.Fire, ElementType.Water, ElementType.Poison, ElementType.Arcane })
                _stock.Add(Roll(e, rng));
            _stock.Add(Roll((ElementType)rng.Next(0, 5), rng));

            // Shuffle so the matching one isn't always in the same slot.
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

        private readonly List<Button> _trayButtons = new List<Button>();

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

            // Content stacked in the upper part, ADD anchored to the card's own
            // bottom — a stack nested inside the tray's stack pushed its last child
            // out past the card edge (see CounterStation for the same fix).
            var col = UIFactory.VStack(inner, 4f, new RectOffset(10, 10, 8, 4));
            UIFactory.Place((RectTransform)col.transform, 0f, 0.30f, 1f, 1f);

            var head = UIFactory.HStack(col.transform, 8f);
            head.childAlignment = TextAnchor.MiddleLeft;
            UIFactory.FixedHeight(head.gameObject, 48f);
            var leaf = UIFactory.Icon(head.transform, PixelSprites.Herb(ing.Element), 44f);
            UIFactory.Flex(leaf.gameObject, 0f, 0f, minWidth: 44f, minHeight: 44f);
            var name = UIFactory.Label(head.transform, ing.Name, UITheme.SizeSmall, UITheme.TextHi,
                TextAlignmentOptions.Left, true);
            UIFactory.Flex(name.gameObject, 1f, 1f);

            var meta = UIFactory.Label(col.transform,
                $"<color=#{ColorUtility.ToHtmlStringRGB(tint)}>{ing.Element}</color>   " +
                $"potency {new string('*', ing.Potency)}" + (ing.Wilted ? "\n<i>wilted — half effect</i>" : ""),
                UITheme.SizeTiny, UITheme.TextMid);
            UIFactory.Flex(meta.gameObject, 1f, 1f, minHeight: 30f);

            int captured = index;
            var add = UIFactory.Button(inner, "ADD", () => Pick(captured), primary: false);
            UIFactory.Place(add.image.rectTransform, 0.06f, 0.05f, 0.94f, 0.25f);
            UIFactory.TintButton(add, UITheme.Alpha(tint, 0.30f), UITheme.Alpha(tint, 0.55f), UITheme.TextHi);
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
            if (order == null || _picksUsed >= MaxPicks || index < 0 || index >= _stock.Count) return;

            Ingredient ing = _stock[index];
            bool match = ing.Element == order.element;
            float wilt = ing.Wilted ? 0.5f : 1f;

            if (match)
            {
                int gain = Mathf.RoundToInt((4 + 3 * ing.Potency) * wilt);
                order.ApplyBonus(gain, "Prep", $"{ing.Name} — {ing.Element} matches the order (+{gain})");
            }
            else
            {
                int loss = Mathf.RoundToInt(3 + 2 * ing.Potency);
                order.ApplyDeduction(loss, "Prep", $"{ing.Name} — {ing.Element} fights the {order.element} base");
            }

            var pot = PhysicsCauldronManager.Instance;
            if (pot != null) pot.DropIngredient(ing.Element);

            _picksUsed++;
            if (index < _trayButtons.Count && _trayButtons[index] != null)
            {
                _trayButtons[index].interactable = false;
                UIFactory.SetButtonText(_trayButtons[index], "IN THE POT");
            }

            AudioManager.Play(match ? Sfx.Confirm : Sfx.Deny);
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
            if (order == null || _grindsLeft <= 0) return;

            float dist = Mathf.Abs(Needle01() - 0.5f);
            bool hit = dist <= _grindHalfWidth;
            _grindsLeft--;

            if (hit)
            {
                int gain = Mathf.RoundToInt(Mathf.Lerp(9f, 5f, dist / Mathf.Max(0.001f, _grindHalfWidth)));
                order.ApplyBonus(gain, "Prep", $"Clean strike (+{gain})");
                AudioManager.Play(Sfx.Seal);
            }
            else
            {
                order.ApplyDeduction(5, "Prep", "Pestle skidded — bruised the mix");
                AudioManager.Play(Sfx.Deny);
            }

            // Each strike narrows the band: the third one is the one that counts.
            _grindHalfWidth = Mathf.Max(0.035f, _grindHalfWidth - 0.018f);
            ApplyBand();
            Refresh();
            Changed?.Invoke();
        }
    }
}
