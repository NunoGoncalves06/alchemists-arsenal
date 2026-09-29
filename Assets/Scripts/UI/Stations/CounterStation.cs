using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using AlchemistsArsenal.Art;
using AlchemistsArsenal.Audio;
using AlchemistsArsenal.Combat;
using AlchemistsArsenal.Core;
using AlchemistsArsenal.Data;

namespace AlchemistsArsenal.UI.Stations
{
    /// <summary>
    /// The Counter — where the morning starts. The fighters come to it themselves,
    /// one after another in roster order: each is the one who will carry the flask
    /// out of the shop, so each orders their own. The one at the counter shows their
    /// portrait, name and perk, says their piece, and has three jobs on the board to
    /// choose between (see <see cref="ContractBoard.Offers"/>): the safe standing
    /// order, a guild commission that pays double but refuses a sloppy flask, and
    /// their own element, which they hit harder with but may be the wrong call for
    /// the road.
    ///
    /// <para>It used to be a paying customer who changed every day while the same
    /// fighter walked the road; the customer and the fighter are one person now. The
    /// day's visitor (a patron who backs the commission, and in whom the story
    /// speaks) still drops by, in the bubble under the fighter's own line.</para>
    ///
    /// <para>A fighter hired last night steps up after everyone who was here before
    /// them. Once all are served the board is done for the day.</para>
    /// </summary>
    public class CounterStation : StationPanel
    {
        public override string RailName => "Counter";
        public override Sprite RailIcon => PixelSprites.Bell();
        public override bool Complete => AllServed;

        private Transform _buyerBox, _roadBox, _offerRow;
        private TextMeshProUGUI _speech, _buyerName, _buyerTitle, _recommendation, _queue;
        private Image _portrait;

        private HeroRecord _fighter;
        private CustomerDefinition _visitor;
        private List<ContractRecord> _offers = new List<ContractRecord>();
        private Image _bookPage;
        private TextMeshProUGUI _bookText;
        private int _day = 1, _biome;

        /// <summary>The order of whoever was served last (for the dock and the rail).</summary>
        protected override Systems.ActiveOrder Order =>
            Systems.CraftingManager.Instance != null ? Systems.CraftingManager.Instance.CurrentOrder : null;

        private static RunState State => SaveSystem.Instance != null ? SaveSystem.Instance.State : null;

        /// <summary>The next fighter going out today who has not ordered yet, or null.</summary>
        public static HeroRecord NextFighter
        {
            get
            {
                RunState s = State;
                if (s == null) return null;
                foreach (HeroRecord h in s.DeployedParty())
                    if (s.ContractFor(h.id) == null) return h;
                return null;
            }
        }

        /// <summary>Every fighter going out today has ordered.</summary>
        public static bool AllServed => State != null && State.contracts != null && State.contracts.Count > 0 && NextFighter == null;

        protected override void BuildContent(RectTransform root)
        {
            // --- the fighter at the counter ------------------------------------
            var buyerCard = UIKit.Card(root, "At the counter", out Transform buyer);
            UIFactory.Place(buyerCard.rectTransform, 0f, 0.44f, 0.34f, 1f);
            _buyerBox = buyer;

            _portrait = UIKit.Portrait(buyer, PixelSprites.Buyer("rookie"), 120f);
            var frame = _portrait.transform.parent.parent.gameObject;  // Portrait > Mat > Frame
            UIFactory.Flex(frame, 1f, 0f, minHeight: 120f);

            _buyerName = UIFactory.Title(buyer, "", UITheme.SizeHeading + 4, UITheme.TextHi);
            UIFactory.Flex(_buyerName.gameObject, 1f, 0f, minHeight: 30f);
            _buyerTitle = UIFactory.Heading(buyer, "", UITheme.TextLow, UITheme.SizeTiny);
            UIFactory.Flex(_buyerTitle.gameObject, 1f, 0f, minHeight: 18f);

            var bubble = UIFactory.Panel(buyer, UITheme.Parchment, "Bubble");
            UIFactory.Flex(bubble.gameObject, 1f, 1f, minHeight: 96f);
            _speech = UIFactory.Label(bubble.transform, "", UITheme.SizeSmall, UITheme.Ink900);
            UIFactory.Stretch(_speech.rectTransform, 10f);

            _queue = UIFactory.Label(buyer, "", UITheme.SizeTiny, UITheme.TextMid, TextAlignmentOptions.TopLeft);
            UIFactory.Flex(_queue.gameObject, 1f, 0f, minHeight: 34f);

            // --- today's road ------------------------------------------------
            var roadCard = UIKit.Card(root, "Today's road", out Transform road);
            UIFactory.Place(roadCard.rectTransform, 0.36f, 0.44f, 1f, 1f);
            _roadBox = road;

            // --- the board ---------------------------------------------------
            var boardCard = UIKit.Card(root, "Jobs on the board", out Transform board);
            UIFactory.Place(boardCard.rectTransform, 0f, 0f, 0.75f, 0.41f, 0f);
            boardCard.rectTransform.offsetMax = new Vector2(0f, -14f);

            // --- the order book: a job tag dragged onto it is taken -------------
            var cover = UIFactory.Panel(root, UITheme.WoodDark, "OrderBook");
            UIFactory.Place(cover.rectTransform, 0.765f, 0f, 1f, 0.41f, 0f);
            cover.rectTransform.offsetMax = new Vector2(0f, -14f);
            _bookPage = UIFactory.Panel(cover.transform, UITheme.Parchment, "Page");
            UIFactory.Stretch(_bookPage.rectTransform, 8f);
            var bookStack = UIFactory.VStack(_bookPage.transform, 6f, new RectOffset(14, 14, 10, 10));
            UIFactory.Stretch((RectTransform)bookStack.transform);
            var bookTitle = UIFactory.Title(bookStack.transform, "Order book", UITheme.SizeHeading, UITheme.Ink900);
            UIFactory.FixedHeight(bookTitle.gameObject, 28f);
            var rule = UIFactory.Panel(bookStack.transform, UITheme.ParchmentDim, "Rule");
            UIFactory.FixedHeight(rule.gameObject, 2f);
            _bookText = UIFactory.Label(bookStack.transform, "", UITheme.SizeSmall, UITheme.Ink800, TextAlignmentOptions.TopLeft);
            UIFactory.Flex(_bookText.gameObject, 1f, 1f);
            foreach (var g in cover.GetComponentsInChildren<Graphic>(true)) g.raycastTarget = g == _bookPage;

            _recommendation = UIFactory.Label(board, "", UITheme.SizeSmall, UITheme.CandleHot);
            UIFactory.FixedHeight(_recommendation.gameObject, 22f);

            var row = UIFactory.HStack(board, 14f);
            UIFactory.Flex(row.gameObject, 1f, 1f, minHeight: 150f);
            _offerRow = row.transform;
        }

        public override void NewDay()
        {
            RunState s = State;
            _day = s != null ? s.day : 1;
            _biome = s != null ? s.TargetBiomeIndex : 0;
            _visitor = CustomerCatalog.Visitor(_day, s);

            BuildRoad(_biome);
            StepUp();
        }

        public override void OnEnter()
        {
            if (_offers == null || _offers.Count == 0) NewDay();
            // The fighter speaks when you step up to the counter (cat 7).
            if (_fighter != null) AudioManager.Speak(CustomerCatalog.ForHero(_fighter).Speech, 1.05f);
        }

        public override void Refresh() => StepUp();

        /// <summary>Whoever is next in line comes to the counter (or nobody, once all are served).</summary>
        private void StepUp()
        {
            RunState s = State;
            _fighter = NextFighter;
            HeroRecord shown = _fighter;
            if (shown == null && s != null)
            {
                // Everyone is served: keep the last one in view.
                var party = s.DeployedParty();
                if (party.Count > 0) shown = party[party.Count - 1];
            }

            CustomerDefinition voice = CustomerCatalog.ForHero(shown);
            _portrait.sprite = PixelSprites.Buyer(shown != null ? shown.portraitId : "rookie");
            _buyerName.text = shown != null ? shown.displayName : "Rookie";
            _buyerTitle.text = shown != null ? shown.Subtitle : "";

            var speech = new StringBuilder();
            if (_fighter == null)
                speech.Append("Everyone going out today has their order in. To the benches.");
            else if (voice.Greetings != null && voice.Greetings.Length > 0)
                speech.Append(voice.Greetings[(_day - 1 + _fighter.level) % voice.Greetings.Length]);

            // The day's visitor, and whatever the story has them say.
            if (_visitor != null)
            {
                string line = Story.StoryDirector.CounterLine(_visitor, s)
                              ?? (_visitor.Greetings != null && _visitor.Greetings.Length > 0
                                  ? _visitor.Greetings[(_day - 1) % _visitor.Greetings.Length] : null);
                if (!string.IsNullOrEmpty(line))
                    speech.Append($"\n\n<size=90%><color=#{ColorUtility.ToHtmlStringRGB(UITheme.WoodDark)}>" +
                                  $"<b>{_visitor.DisplayName}</b>, {_visitor.Title}, at the door: {line}</color></size>");
            }
            _speech.text = speech.ToString();

            _queue.text = QueueLine(s);
            _offers = _fighter != null ? ContractBoard.Offers(_day, _biome, _fighter, _visitor) : new List<ContractRecord>();
            // The first roads keep the board short: the safe job first, then the Guild's.
            int jobs = Complexity.JobsOffered(Complexity.Level(s));
            if (_offers.Count > jobs) _offers.RemoveRange(jobs, _offers.Count - jobs);
            BuildOffers();
        }

        /// <summary>"Served: Rookie (Fire) · Waiting: Mira Thorn, Otho Vance".</summary>
        private string QueueLine(RunState s)
        {
            if (s == null) return "";
            var served = new List<string>();
            var waiting = new List<string>();
            foreach (HeroRecord h in s.DeployedParty())
            {
                if (h == _fighter) continue;
                ContractRecord c = s.ContractFor(h.id);
                if (c != null)
                    served.Add($"{h.displayName} (<color=#{ColorUtility.ToHtmlStringRGB(UITheme.Element(c.element))}>{c.element}</color>)");
                else waiting.Add(h.displayName);
            }
            var sb = new StringBuilder();
            if (served.Count > 0) sb.Append("Served: ").Append(string.Join(", ", served));
            if (waiting.Count > 0)
            {
                if (sb.Length > 0) sb.Append("\n");
                sb.Append("Waiting in line: ").Append(string.Join(", ", waiting));
            }
            return sb.ToString();
        }

        // ------------------------------------------------------------------ road

        private void BuildRoad(int biomeIndex)
        {
            Clear(_roadBox);
            BiomeData biome = BiomeLibrary.Get(biomeIndex);
            int[] counts = ContractBoard.ThreatCounts(biome);
            ElementType dominant = ContractBoard.Dominant(counts);

            var header = UIFactory.HStack(_roadBox, 10f);
            UIFactory.Flex(header.gameObject, 1f, 0f, minHeight: 26f);
            var where = UIFactory.Title(header.transform, BiomeLibrary.Name(biomeIndex), UITheme.SizeHeading);
            UIFactory.Flex(where.gameObject, 1f, 1f);

            foreach (var wave in biome.Waves)
            {
                if (wave.monster == null) continue;
                var row = UIFactory.HStack(_roadBox, 10f, new RectOffset(4, 4, 2, 2));
                row.childAlignment = TextAnchor.MiddleLeft;
                UIFactory.Flex(row.gameObject, 1f, 0f, minHeight: 40f);

                var art = UIFactory.Icon(row.transform, PixelSprites.Monster(wave.monster.DisplayName, wave.monster.Element), 34f);
                UIFactory.Flex(art.gameObject, 0f, 0f, minWidth: 34f, minHeight: 34f);

                var count = UIFactory.MonoLabel(row.transform, $"x{Mathf.Max(1, wave.count)}",
                    UITheme.SizeBody, UITheme.Candle, TextAlignmentOptions.Left);
                UIFactory.Flex(count.gameObject, 0f, 1f, minWidth: 40f);

                var name = UIFactory.Label(row.transform, wave.monster.DisplayName, UITheme.SizeBody,
                    UITheme.TextHi, TextAlignmentOptions.Left);
                UIFactory.Flex(name.gameObject, 1f, 1f);

                var badge = UIFactory.ElementBadge(row.transform, wave.monster.Element, 22f);
                UIFactory.Flex(badge.gameObject, 0f, 0f, minWidth: 22f, minHeight: 22f);

                var elem = UIFactory.Label(row.transform, wave.monster.Element.ToString(), UITheme.SizeSmall,
                    UITheme.Element(wave.monster.Element), TextAlignmentOptions.Left);
                UIFactory.Flex(elem.gameObject, 0f, 1f, minWidth: 74f);
            }

            int total = 0; foreach (int c in counts) total += c;
            ElementType best = ContractBoard.Counter(dominant);
            _recommendation.text = total > 0
                ? $"Mostly <b>{dominant}</b> out there. A <b>{best}</b> flask hits them twice as hard."
                : "Quiet day. Brew whatever you like.";
        }

        // ---------------------------------------------------------------- offers

        private void BuildOffers()
        {
            Clear(_offerRow);
            BuildBook();

            if (_fighter == null)
            {
                string done = State != null && State.contracts != null && State.contracts.Count > 0
                    ? "Every fighter going out today has ordered. Their grain goes to the Malting bench first."
                    : "Nobody is going out today.";
                var label = UIFactory.Label(_offerRow, done, UITheme.SizeBody, UITheme.TextMid, TextAlignmentOptions.Center);
                UIFactory.Flex(label.gameObject, 1f, 1f);
                return;
            }

            foreach (ContractRecord offer in _offers)
                BuildOfferCard(offer);
        }

        private void BuildOfferCard(ContractRecord offer)
        {
            Color accent = UITheme.Element(offer.element);

            // A paper tag on a nail. The slot holds its place in the row; the paper
            // inside it is what the hand picks up (see DragTag).
            var slot = new GameObject("Offer", typeof(RectTransform));
            slot.transform.SetParent(_offerRow, false);
            UIFactory.Flex(slot, 1f, 1f, minWidth: 180f);

            Image paper = UIFactory.Panel(slot.transform, UITheme.Parchment, "Tag");
            UIFactory.Stretch(paper.rectTransform);
            var band = UIFactory.Panel(paper.transform, accent, "Band");
            band.rectTransform.anchorMin = new Vector2(0f, 1f);
            band.rectTransform.anchorMax = new Vector2(1f, 1f);
            band.rectTransform.sizeDelta = new Vector2(0f, 6f);
            band.rectTransform.anchoredPosition = new Vector2(0f, -3f);
            var nail = UIFactory.Panel(paper.transform, UITheme.Ink700, "Hole");
            nail.rectTransform.anchorMin = nail.rectTransform.anchorMax = new Vector2(0.5f, 1f);
            nail.rectTransform.sizeDelta = new Vector2(8f, 8f);
            nail.rectTransform.anchoredPosition = new Vector2(0f, -14f);

            var col = UIFactory.VStack(paper.transform, 5f, new RectOffset(14, 14, 24, 10));
            UIFactory.Stretch((RectTransform)col.transform);

            var titleRow = UIFactory.HStack(col.transform, 8f);
            titleRow.childAlignment = TextAnchor.MiddleLeft;
            UIFactory.FixedHeight(titleRow.gameObject, 26f);
            var badge = UIFactory.ElementBadge(titleRow.transform, offer.element, 24f);
            UIFactory.Flex(badge.gameObject, 0f, 0f, minWidth: 24f, minHeight: 24f);
            var title = UIFactory.Label(titleRow.transform, offer.title, UITheme.SizeBody, UITheme.Ink900,
                TextAlignmentOptions.Left, true);
            UIFactory.Flex(title.gameObject, 1f, 1f);

            var what = UIFactory.Label(col.transform,
                $"<b>{offer.PotionName}</b>, {offer.RequiredGrade} or better",
                UITheme.SizeSmall, UITheme.Ink800);
            UIFactory.Flex(what.gameObject, 1f, 0f, minHeight: 20f);

            var pay = UIFactory.MonoLabel(col.transform,
                $"{offer.fee} g   +{offer.bonus} g on delivery", UITheme.SizeBody, UITheme.WoodDark);
            UIFactory.Flex(pay.gameObject, 1f, 0f, minHeight: 22f);

            var note = UIFactory.Label(col.transform, offer.note, UITheme.SizeSmall, UITheme.Ink700);
            UIFactory.Flex(note.gameObject, 1f, 1f, minHeight: 28f);

            foreach (var g in paper.GetComponentsInChildren<Graphic>(true)) g.raycastTarget = g == paper;
            ContractRecord captured = offer;
            var tag = slot.AddComponent<DragTag>();
            tag.Paper = paper.rectTransform;
            tag.Target = (RectTransform)_bookPage.transform.parent;
            tag.OnDropped = () => Accept(captured);
            tag.OnClicked = () => Accept(captured);
            tag.OnHoverTarget = over => _bookPage.color = over ? UITheme.CandleHot : UITheme.Parchment;
        }

        /// <summary>Where a job tag goes; for the harness and the tutorial spotlight.</summary>
        public RectTransform OrderBook => _bookPage != null ? (RectTransform)_bookPage.transform.parent : null;

        /// <summary>The first job tag on the board, or null (the tutorial points at it).</summary>
        public RectTransform FirstOfferTag =>
            _offerRow != null && _offerRow.childCount > 0 ? (RectTransform)_offerRow.GetChild(0) : null;

        /// <summary>Today's orders so far, and who is next.</summary>
        private void BuildBook()
        {
            if (_bookText == null) return;
            RunState s = State;
            var sb = new StringBuilder();
            if (s != null)
                foreach (HeroRecord h in s.DeployedParty())
                {
                    ContractRecord c = s.ContractFor(h.id);
                    if (c != null) sb.Append(h.displayName).Append(": ").Append(c.PotionName).Append('\n');
                }
            if (_fighter != null)
            {
                if (sb.Length > 0) sb.Append('\n');
                sb.Append($"<color=#{ColorUtility.ToHtmlStringRGB(UITheme.WoodDark)}>Drag a job here for {_fighter.displayName}.</color>");
            }
            _bookText.text = sb.ToString();
        }

        /// <summary>
        /// Take the first job on the board for whoever is at the counter, exactly as
        /// clicking it would. The single entry point for anything that needs to
        /// accept without a click (the headless playtest driver, and
        /// MorningScreen.AcceptOrder). Returns false once everyone is served.
        /// </summary>
        public bool AcceptFirstOffer()
        {
            if (_fighter == null || _offers == null || _offers.Count == 0) StepUp();
            if (_fighter == null || _offers.Count == 0) return false;
            return Accept(_offers[0]);
        }

        private bool Accept(ContractRecord offer)
        {
            if (_fighter == null || GameLoopManager.Instance == null) return false;

            Systems.ActiveOrder order = GameLoopManager.Instance.AcceptContract(offer.Clone());
            AudioManager.Play(Sfx.Confirm);

            // Reward reading the road: a job whose element counters today's dominant
            // threat starts the flask above the floor. Deliberately small — a head
            // start, not a shortcut past the benches that actually make the potion.
            if (order != null)
            {
                BiomeData road = BiomeLibrary.Get(_biome);
                ElementType dominant = ContractBoard.Dominant(ContractBoard.ThreatCounts(road));
                if (offer.element == ContractBoard.Counter(dominant))
                    order.ApplyBonus(QualityBudget.CounterRead, "Counter",
                        $"Took the {offer.element} job against a {dominant} road");
            }

            StepUp();   // the next fighter comes to the counter
            if (_fighter != null) AudioManager.Speak(CustomerCatalog.ForHero(_fighter).Speech, 1.05f);
            Changed?.Invoke();
            return order != null;
        }
    }
}
