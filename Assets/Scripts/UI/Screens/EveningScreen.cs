using System.Text;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using AlchemistsArsenal.Core;
using AlchemistsArsenal.Combat;
using AlchemistsArsenal.Data;
using AlchemistsArsenal.Audio;

namespace AlchemistsArsenal.UI
{
    /// <summary>
    /// Evening shell — Report / Upgrades / Diary (DESIGN.md §7.9). The report is the
    /// one place the whole loop is accounted for, so it reads as a ledger: what
    /// happened on the road, what the customer thought of the flask you sold them,
    /// and what the potion actually did in the fight. The pay breakdown shows the
    /// grade multiplier explicitly (cat-1 legibility): a Poor potion visibly pays 40%.
    /// </summary>
    public class EveningScreen : GameScreen
    {
        private RectTransform _body;
        private TextMeshProUGUI _gold, _title;
        private Button _reportTab, _upgradesTab;

        protected override void Build()
        {
            UIFactory.Box(transform, UITheme.Ground, Rt);

            // --- header ------------------------------------------------------
            var bar = UIFactory.Panel(transform, UITheme.Surface, "TopBar");
            UIFactory.Place(bar.rectTransform, 0f, 0.9f, 1f, 1f);
            var edge = UIFactory.Panel(bar.transform, UITheme.Line, "Edge");
            UIFactory.Place(edge.rectTransform, 0f, 0f, 1f, 0f);
            edge.rectTransform.sizeDelta = new Vector2(0f, 2f);

            _title = UIFactory.Title(bar.transform, "Evening", UITheme.SizeTitle, UITheme.Candle,
                TextAlignmentOptions.Left);
            UIFactory.Place(_title.rectTransform, 0.02f, 0f, 0.34f, 1f);

            var coin = UIFactory.Icon(bar.transform, Art.PixelSprites.Coin(), 24f);
            UIFactory.Place(coin.rectTransform, 0.88f, 0.32f, 0.905f, 0.68f);
            _gold = UIFactory.MonoLabel(bar.transform, "", UITheme.SizeHeading, UITheme.Candle,
                TextAlignmentOptions.Left);
            UIFactory.Place(_gold.rectTransform, 0.91f, 0f, 0.99f, 1f);

            _reportTab = Tab(0.36f, 0.50f, "REPORT", ShowReport);
            _upgradesTab = Tab(0.51f, 0.65f, "UPGRADES", ShowUpgrades);
            Tab(0.66f, 0.78f, "DIARY", () =>
            {
                DiaryScreen.OpenEntryId = null;
                DiaryScreen.FromOpeningCinematic = false;
                UIManager.Instance.Show(ScreenId.Diary);
            });

            // --- body --------------------------------------------------------
            _body = UIFactory.Rect(transform, "Body", new Vector2(0f, 0.11f), new Vector2(1f, 0.9f),
                new Vector2(26, 10), new Vector2(-26, -14));

            var sleep = UIFactory.Button(transform, "SLEEP", () => GameLoopManager.Instance.BeginBiomeMap());
            UIFactory.Place(sleep.image.rectTransform, 0.80f, 0.025f, 0.975f, 0.09f);
        }

        private Button Tab(float xMin, float xMax, string text, System.Action onClick)
        {
            var b = UIFactory.Button(transform, text, onClick, primary: false);
            UIFactory.Place(b.image.rectTransform, xMin, 0.915f, xMax, 0.985f);
            return b;
        }

        protected override void OnShow()
        {
            RunState s = SaveSystem.Instance.State;
            // BeginEvening already rolled the day over, so the run that just ended
            // was yesterday's.
            _title.text = $"Evening — day {Mathf.Max(1, s.day - 1)}";
            _gold.text = $"{s.gold} g";
            ShowReport();

            // Rookie reacts to the potion grade in Animalese (cat 7).
            var r = GameLoopManager.Instance != null ? GameLoopManager.Instance.LatestReport : null;
            if (r != null)
            {
                bool happy = r.won && r.craftedGrade is PotionGrade.Great or PotionGrade.Perfect;
                AudioManager.Speak(happy ? "ha ha gu-d fla-she wi-tch" : "nu gu-d dis wun to sla-dge", happy ? 1.2f : 0.85f);
            }
        }

        // ----------------------------------------------------------------- report

        private void ShowReport()
        {
            Clear();
            SelectTab(report: true);

            var r = GameLoopManager.Instance != null ? GameLoopManager.Instance.LatestReport : null;
            if (r == null)
            {
                UIFactory.Label(_body, "No expedition on record.", UITheme.SizeHeading, UITheme.TextLow);
                return;
            }

            BuildOutcome(r);
            BuildJob(r);
            BuildPerformance(r);
            BuildLedgerStrip(r);
        }

        private void BuildOutcome(ExpeditionReport r)
        {
            var card = UIKit.Card(_body, "On the road", out Transform c, spacing: 8f);
            UIFactory.Place(card.rectTransform, 0f, 0.34f, 0.32f, 1f);

            var headline = UIFactory.Title(c, r.won ? "Victory" : "Defeat", UITheme.SizeTitle + 6,
                r.won ? UITheme.Ok : UITheme.Danger);
            UIFactory.FixedHeight(headline.gameObject, 46f);

            var stars = UIFactory.HStack(c, 4f);
            UIFactory.FixedHeight(stars.gameObject, 30f);
            for (int i = 0; i < 3; i++)
            {
                var s = UIFactory.Icon(stars.transform, Art.PixelSprites.Star(), 26f,
                    i < r.Stars ? Color.white : UITheme.Alpha(Color.white, 0.18f));
                UIFactory.Flex(s.gameObject, 0f, 0f, minWidth: 26f, minHeight: 26f);
            }

            UIKit.KeyValue(c, "Waves cleared", $"{r.wavesCleared} / {r.totalWaves}");
            UIKit.KeyValue(c, "Boss", r.bossDefeated ? "defeated" : "—");
            UIKit.KeyValue(c, "Party returned", $"{r.partyTotal - r.partyDown} / {r.partyTotal}");
            UIKit.KeyValue(c, "Time on the road", $"{r.durationSeconds:0}s");
        }

        private void BuildJob(ExpeditionReport r)
        {
            var card = UIKit.Card(_body, "The job", out Transform c, spacing: 8f);
            UIFactory.Place(card.rectTransform, 0.34f, 0.34f, 0.66f, 1f);

            bool hasJob = !string.IsNullOrEmpty(r.contractTitle);
            if (hasJob)
            {
                var who = UIFactory.HStack(c, 10f);
                who.childAlignment = TextAnchor.MiddleLeft;
                UIFactory.FixedHeight(who.gameObject, 56f);
                var portrait = UIKit.Portrait(who.transform, Art.PixelSprites.Buyer(BuyerIdFor(r)), 52f);
                UIFactory.Flex(portrait.transform.parent.parent.gameObject, 0f, 0f, minWidth: 52f, minHeight: 52f);
                var name = UIFactory.Label(who.transform, $"<b>{r.contractBuyer}</b>\n<size=80%>{r.contractTitle}</size>",
                    UITheme.SizeBody, UITheme.TextHi, TextAlignmentOptions.Left);
                UIFactory.Flex(name.gameObject, 1f, 1f);

                var asked = UIFactory.Label(c,
                    $"Asked for <color=#{ColorUtility.ToHtmlStringRGB(UITheme.GradeColor(r.contractRequired))}>" +
                    $"{r.contractRequired.ToString().ToUpperInvariant()}</color> or better — you delivered " +
                    $"<color=#{ColorUtility.ToHtmlStringRGB(UITheme.GradeColor(r.craftedGrade))}>" +
                    $"{r.craftedGrade.ToString().ToUpperInvariant()}</color>.",
                    UITheme.SizeBody, UITheme.TextMid);
                UIFactory.Flex(asked.gameObject, 1f, 0f, minHeight: 44f);

                var verdict = UIFactory.Label(c,
                    r.won
                        ? r.contractMet ? "Contract met — bonus paid in full."
                                        : "Below the grade they asked for. They paid half, and said so."
                        : "Rookie didn't come back with the job done. No fee.",
                    UITheme.SizeBody, r.won && r.contractMet ? UITheme.Ok : UITheme.Danger);
                UIFactory.Flex(verdict.gameObject, 1f, 0f, minHeight: 44f);
            }

            var rule = UIFactory.Rule(c, UITheme.LineSoft);
            UIFactory.FixedHeight(rule.gameObject, 2f);

            float mult = CombatQuality.PaymentMultiplier(r.craftedGrade);
            UIKit.KeyValue(c, "Fee", $"{(hasJob ? r.contractFee : Economy.BaseFee)} g");
            UIKit.KeyValue(c, $"Grade ({r.craftedGrade})", $"x{mult:0.00}");
            if (hasJob && r.contractMet && r.contractBonus > 0)
                UIKit.KeyValue(c, "Delivery bonus", $"+{r.contractBonus} g", valueColor: UITheme.Ok);
            if (hasJob && !r.contractMet && r.won)
                UIKit.KeyValue(c, "Below grade", "x0.50", valueColor: UITheme.Danger);
            if (r.perfectTip) UIKit.KeyValue(c, "Perfect tip", $"+{Economy.PerfectTip} g", valueColor: UITheme.Ok);
            if (SaveSystem.Instance.State.IsReplayDay) UIKit.KeyValue(c, "Replay", "x0.50");
            UIKit.KeyValue(c, "PAID", $"{r.goldPaidByGrade} g", keyColor: UITheme.Candle, valueColor: UITheme.Candle);
        }

        private static string BuyerIdFor(ExpeditionReport r)
        {
            foreach (var cust in CustomerCatalog.All)
                if (cust.DisplayName == r.contractBuyer) return cust.PortraitId;
            return "rookie";
        }

        private void BuildPerformance(ExpeditionReport r)
        {
            var card = UIKit.Card(_body, "What the potion did", out Transform c, spacing: 8f);
            UIFactory.Place(card.rectTransform, 0.68f, 0.34f, 1f, 1f);

            foreach (var b in r.bombs)
            {
                var head = UIFactory.HStack(c, 8f);
                head.childAlignment = TextAnchor.MiddleLeft;
                UIFactory.FixedHeight(head.gameObject, 30f);
                var badge = UIFactory.ElementBadge(head.transform, b.element, 24f);
                UIFactory.Flex(badge.gameObject, 0f, 0f, minWidth: 24f, minHeight: 24f);
                var name = UIFactory.Label(head.transform,
                    $"<b>{b.name}</b>  <color=#{ColorUtility.ToHtmlStringRGB(UITheme.GradeColor(b.grade))}>{b.grade}</color>",
                    UITheme.SizeBody, UITheme.TextHi, TextAlignmentOptions.Left);
                UIFactory.Flex(name.gameObject, 1f, 1f);

                UIKit.KeyValue(c, "Throws / hits", $"{b.throws} / {b.hits}", size: UITheme.SizeSmall);
                UIKit.KeyValue(c, "Damage dealt", b.totalDamage.ToString(), size: UITheme.SizeSmall);

                var note = UIFactory.Label(c,
                    b.grade == PotionGrade.Poor ? "Poor: half damage, and the elemental x2 never applies."
                    : b.everHadAdvantage ? "The elemental x2 landed out there."
                    : "Right grade, wrong element for what showed up.",
                    UITheme.SizeSmall, UITheme.TextLow);
                UIFactory.Flex(note.gameObject, 1f, 0f, minHeight: 40f);
            }
            if (r.bombs.Count == 0)
                UIFactory.Label(c, "Rookie never got a flask off.", UITheme.SizeBody, UITheme.TextLow);
        }

        private void BuildLedgerStrip(ExpeditionReport r)
        {
            var card = UIKit.Card(_body, "Brought home", out Transform c, spacing: 6f);
            UIFactory.Place(card.rectTransform, 0f, 0f, 1f, 0.31f);

            var row = UIFactory.HStack(c, 26f);
            UIFactory.Flex(row.gameObject, 1f, 1f, minHeight: 60f);

            var gold = UIFactory.Label(row.transform,
                $"<size=160%><b>{r.TotalGold} g</b></size>\n<size=85%>{r.goldPaidByGrade} g fee  ·  {r.goldFromLoot} g loot</size>",
                UITheme.SizeBody, UITheme.Candle, TextAlignmentOptions.TopLeft);
            UIFactory.Flex(gold.gameObject, 0.6f, 1f, minWidth: 220f);

            var sb = new StringBuilder();
            foreach (var kv in r.herbDrops) sb.Append($"{kv.Key} x{kv.Value}    ");
            var herbs = UIFactory.Label(row.transform,
                sb.Length > 0 ? $"<size=85%>HERBS</size>\n{sb}" : "<size=85%>HERBS</size>\nNothing worth picking up.",
                UITheme.SizeBody, UITheme.TextMid, TextAlignmentOptions.TopLeft);
            UIFactory.Flex(herbs.gameObject, 1f, 1f);

            var next = UIFactory.Label(row.transform,
                "<size=85%>NEXT</size>\nSpend it on UPGRADES, then SLEEP to pick tomorrow's road.",
                UITheme.SizeBody, UITheme.TextLow, TextAlignmentOptions.TopLeft);
            UIFactory.Flex(next.gameObject, 1f, 1f);
        }

        // --------------------------------------------------------------- upgrades

        private void ShowUpgrades()
        {
            Clear();
            SelectTab(report: false);
            RunState s = SaveSystem.Instance.State;

            var card = UIKit.Card(_body, $"Upgrades — permanent, {s.gold} g on hand", out Transform c, spacing: 10f);
            UIFactory.Stretch(card.rectTransform);

            foreach (var up in UpgradeCatalog.All)
            {
                Image row = UIKit.Surface(c, out Transform inner, UITheme.SurfaceHi, UITheme.LineSoft, "UpgradeRow");
                UIFactory.FixedHeight(row.gameObject, 82f);

                var text = UIFactory.VStack(inner, 2f, new RectOffset(16, 16, 12, 12));
                UIFactory.Place((RectTransform)text.transform, 0f, 0f, 0.72f, 1f);
                var titleLabel = UIFactory.Label(text.transform, up.DisplayName, UITheme.SizeBody, UITheme.TextHi,
                    TextAlignmentOptions.TopLeft, true);
                UIFactory.FixedHeight(titleLabel.gameObject, 24f);
                var desc = UIFactory.Label(text.transform, up.Description, UITheme.SizeSmall, UITheme.TextLow);
                UIFactory.Flex(desc.gameObject, 1f, 1f);

                bool owned = s.HasUpgrade(up.Id);
                bool canAfford = s.gold >= up.Cost;
                string id = up.Id; int cost = up.Cost;
                var buy = UIFactory.Button(inner, owned ? "OWNED" : $"BUY — {cost} g",
                    owned ? (System.Action)null : () => BuyUpgrade(id, cost), primary: !owned);
                UIFactory.Place(buy.image.rectTransform, 0.74f, 0.18f, 0.97f, 0.82f);
                buy.interactable = !owned && canAfford;
            }
        }

        private void BuyUpgrade(string id, int cost)
        {
            RunState s = SaveSystem.Instance.State;
            if (s.HasUpgrade(id) || s.gold < cost) return;
            s.AddGold(-cost);
            s.ownedUpgrades.Add(id);
            SaveSystem.Instance.MarkDirty();
            AudioManager.Play(Sfx.Coin);
            _gold.text = $"{s.gold} g";
            ShowUpgrades();
        }

        // ----------------------------------------------------------------- shared

        private void Clear()
        {
            for (int i = _body.childCount - 1; i >= 0; i--) Destroy(_body.GetChild(i).gameObject);
        }

        private void SelectTab(bool report)
        {
            if (_reportTab != null)
                UIFactory.TintButton(_reportTab, report ? UITheme.Candle : UITheme.SurfaceHi,
                    report ? UITheme.CandleHot : UITheme.SurfaceTop, report ? UITheme.TextOnGold : UITheme.TextHi);
            if (_upgradesTab != null)
                UIFactory.TintButton(_upgradesTab, !report ? UITheme.Candle : UITheme.SurfaceHi,
                    !report ? UITheme.CandleHot : UITheme.SurfaceTop, !report ? UITheme.TextOnGold : UITheme.TextHi);
        }
    }
}
