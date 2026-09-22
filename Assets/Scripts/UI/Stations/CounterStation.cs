using System.Collections.Generic;
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
    /// The Counter — where the morning actually starts now. A customer is standing
    /// there: portrait, name, a line of their own, and three jobs on the board. The
    /// old screen auto-picked the element and offered one ACCEPT button; the choice
    /// now is which job to take, and the jobs disagree — the safe standing order,
    /// the commission that pays double but refuses a sloppy flask, and whatever the
    /// customer personally wants, which is often the wrong element for today's road.
    /// </summary>
    public class CounterStation : StationPanel
    {
        public override string RailName => "Counter";
        public override Sprite RailIcon => PixelSprites.Bell();
        public override bool Complete => HasOrder;

        private Transform _buyerBox, _roadBox, _offerRow;
        private TextMeshProUGUI _speech, _buyerName, _buyerTitle, _recommendation;
        private Image _portrait;

        private CustomerDefinition _buyer;
        private List<ContractRecord> _offers = new List<ContractRecord>();
        private readonly List<Button> _offerButtons = new List<Button>();

        protected override void BuildContent(RectTransform root)
        {
            // --- the customer ------------------------------------------------
            var buyerCard = UIKit.Card(root, "At the counter", out Transform buyer);
            UIFactory.Place(buyerCard.rectTransform, 0f, 0.44f, 0.34f, 1f);
            _buyerBox = buyer;

            _portrait = UIKit.Portrait(buyer, PixelSprites.Buyer("rookie"), 132f);
            var frame = _portrait.transform.parent.parent.gameObject;  // Portrait > Mat > Frame
            UIFactory.Flex(frame, 1f, 0f, minHeight: 132f);

            _buyerName = UIFactory.Title(buyer, "", UITheme.SizeHeading + 4, UITheme.TextHi);
            UIFactory.Flex(_buyerName.gameObject, 1f, 0f, minHeight: 30f);
            _buyerTitle = UIFactory.Heading(buyer, "", UITheme.TextLow, UITheme.SizeTiny);
            UIFactory.Flex(_buyerTitle.gameObject, 1f, 0f, minHeight: 18f);

            var bubble = UIFactory.Panel(buyer, UITheme.Parchment, "Bubble");
            UIFactory.Flex(bubble.gameObject, 1f, 1f, minHeight: 96f);
            _speech = UIFactory.Label(bubble.transform, "", UITheme.SizeBody, UITheme.Ink900);
            UIFactory.Stretch(_speech.rectTransform, 12f);

            // --- today's road ------------------------------------------------
            var roadCard = UIKit.Card(root, "Today's road — what they will meet out there", out Transform road);
            UIFactory.Place(roadCard.rectTransform, 0.36f, 0.44f, 1f, 1f);
            _roadBox = road;

            // --- the board ---------------------------------------------------
            var boardCard = UIKit.Card(root, "Jobs on the board — take one", out Transform board);
            UIFactory.Place(boardCard.rectTransform, 0f, 0f, 1f, 0.41f, 0f);
            boardCard.rectTransform.offsetMax = new Vector2(0f, -14f);

            _recommendation = UIFactory.Label(board, "", UITheme.SizeSmall, UITheme.CandleHot);
            UIFactory.FixedHeight(_recommendation.gameObject, 22f);

            var row = UIFactory.HStack(board, 14f);
            UIFactory.Flex(row.gameObject, 1f, 1f, minHeight: 150f);
            _offerRow = row.transform;
        }

        public override void NewDay()
        {
            RunState s = SaveSystem.Instance != null ? SaveSystem.Instance.State : null;
            int day = s != null ? s.day : 1;
            int biome = s != null ? s.TargetBiomeIndex : 0;

            _buyer = CustomerCatalog.ForDay(day);
            _offers = ContractBoard.Offers(day, biome);

            _portrait.sprite = PixelSprites.Buyer(_buyer.PortraitId);
            _buyerName.text = _buyer.DisplayName;
            _buyerTitle.text = _buyer.Title;
            // Once the story has moved on, the people in it say so.
            _speech.text = Story.StoryDirector.CounterLine(_buyer, s)
                           ?? (_buyer.Greetings != null && _buyer.Greetings.Length > 0
                               ? _buyer.Greetings[(day - 1) % _buyer.Greetings.Length]
                               : "");

            BuildRoad(biome);
            BuildOffers();
        }

        public override void OnEnter()
        {
            if (_buyer == null) NewDay();
            // The customer speaks when you step up to the counter (cat 7).
            if (!HasOrder && _buyer != null) AudioManager.Speak(_buyer.Speech, 1.05f);
        }

        public override void Refresh() => BuildOffers();

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
                ? $"Mostly <b>{dominant}</b> out there — a <b>{best}</b> flask lands the x2."
                : "Quiet day. Brew whatever you like.";
        }

        // ---------------------------------------------------------------- offers

        private void BuildOffers()
        {
            Clear(_offerRow);
            _offerButtons.Clear();
            if (_offers == null) return;

            ContractRecord taken = SaveSystem.Instance != null && SaveSystem.Instance.State != null
                ? SaveSystem.Instance.State.contract : null;
            bool anyTaken = HasOrder;

            foreach (ContractRecord offer in _offers)
            {
                bool isTaken = anyTaken && taken != null && taken.accepted
                               && taken.title == offer.title && taken.element == offer.element;
                BuildOfferCard(offer, anyTaken, isTaken);
            }
        }

        private void BuildOfferCard(ContractRecord offer, bool anyTaken, bool isTaken)
        {
            Color accent = UITheme.Element(offer.element);
            Image card = UIKit.Surface(_offerRow, out Transform inner,
                isTaken ? UITheme.SurfaceTop : UITheme.Surface,
                isTaken ? UITheme.Candle : UITheme.Line, "Offer");
            UIFactory.Flex(card.gameObject, 1f, 1f, minWidth: 180f);

            // The button is anchored to the card, NOT stacked inside it. A layout
            // group nested inside another layout group negotiates its size a frame
            // late, and the last child of the inner stack ended up drawn below the
            // card's own bottom edge — the TAKE THIS JOB row was sliced off by the
            // screen edge (caught on a headless-playtest screenshot). Anything that
            // must stay inside a card gets anchored.
            var col = UIFactory.VStack(inner, 6f, new RectOffset(14, 14, 12, 6));
            UIFactory.Place((RectTransform)col.transform, 0f, 0.26f, 1f, 1f);

            var titleRow = UIFactory.HStack(col.transform, 8f);
            titleRow.childAlignment = TextAnchor.MiddleLeft;
            UIFactory.FixedHeight(titleRow.gameObject, 26f);
            var badge = UIFactory.ElementBadge(titleRow.transform, offer.element, 24f);
            UIFactory.Flex(badge.gameObject, 0f, 0f, minWidth: 24f, minHeight: 24f);
            var title = UIFactory.Label(titleRow.transform, offer.title, UITheme.SizeBody, accent,
                TextAlignmentOptions.Left, true);
            UIFactory.Flex(title.gameObject, 1f, 1f);

            var what = UIFactory.Label(col.transform,
                $"<b>{offer.PotionName}</b>  ·  wants <color=#{ColorUtility.ToHtmlStringRGB(UITheme.GradeColor(offer.RequiredGrade))}>{offer.RequiredGrade.ToString().ToUpperInvariant()}</color> or better",
                UITheme.SizeSmall, UITheme.TextHi);
            UIFactory.Flex(what.gameObject, 1f, 0f, minHeight: 20f);

            var pay = UIFactory.MonoLabel(col.transform,
                $"{offer.fee} g   +{offer.bonus} g on delivery", UITheme.SizeBody, UITheme.Candle);
            UIFactory.Flex(pay.gameObject, 1f, 0f, minHeight: 22f);

            var note = UIFactory.Label(col.transform, offer.note, UITheme.SizeSmall, UITheme.TextLow);
            UIFactory.Flex(note.gameObject, 1f, 1f, minHeight: 28f);

            ContractRecord captured = offer;
            var button = UIFactory.Button(inner,
                isTaken ? "TAKEN" : anyTaken ? "—" : "TAKE THIS JOB",
                isTaken || anyTaken ? (System.Action)null : () => Accept(captured),
                primary: !anyTaken);
            UIFactory.Place(button.image.rectTransform, 0.06f, 0.04f, 0.94f, 0.21f);
            button.interactable = !anyTaken;
            if (isTaken) UIFactory.TintButton(button, UITheme.Candle, UITheme.CandleHot, UITheme.TextOnGold);
            _offerButtons.Add(button);
        }

        /// <summary>
        /// Take the first job on the board, exactly as clicking it would. The
        /// single entry point for anything that needs to accept without a click
        /// (the headless playtest driver, and MorningScreen.AcceptOrder).
        /// </summary>
        public bool AcceptFirstOffer()
        {
            // Normally NewDay has already stocked the board. Re-stock if a caller
            // reaches here first, so this can never silently no-op.
            if (_offers == null || _offers.Count == 0)
            {
                RunState s = SaveSystem.Instance != null ? SaveSystem.Instance.State : null;
                _offers = ContractBoard.Offers(s != null ? s.day : 1, s != null ? s.TargetBiomeIndex : 0);
            }
            if (_offers.Count == 0) return false;

            Accept(_offers[0]);   // Accept clones before handing it to the loop
            return HasOrder;
        }

        private void Accept(ContractRecord offer)
        {
            if (HasOrder || GameLoopManager.Instance == null) return;

            GameLoopManager.Instance.AcceptContract(offer.Clone());
            AudioManager.Play(Sfx.Confirm);

            // Reward reading the road: a job whose element counters today's
            // dominant threat starts the flask above the floor. Deliberately
            // small - it is a head start, not a shortcut past the three
            // stations that actually make the potion.
            var order = Order;
            if (order != null)
            {
                BiomeData road = BiomeLibrary.Get(SaveSystem.Instance != null
                    ? SaveSystem.Instance.State.TargetBiomeIndex : 0);
                ElementType dominant = ContractBoard.Dominant(ContractBoard.ThreatCounts(road));
                if (offer.element == ContractBoard.Counter(dominant))
                    order.ApplyBonus(QualityBudget.CounterRead, "Counter",
                        $"Took the {offer.element} job against a {dominant} road");
            }

            // The pot is cold again and today's recipe direction is set here, not
            // wherever the cauldron happened to be left from yesterday.
            var pot = Crafting.PhysicsCauldronManager.Instance;
            if (pot != null) pot.BeginBrew(SaveSystem.Instance != null ? SaveSystem.Instance.State.day : 1);


            BuildOffers();
            Changed?.Invoke();
        }
    }
}
