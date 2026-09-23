using System.Collections.Generic;
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
        private enum EveTab { Report, Upgrades, Roster }

        private readonly Button[] _tabButtons = new Button[3];

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

            _tabButtons[(int)EveTab.Report] = Tab(0.34f, 0.46f, "REPORT", ShowReport);
            _tabButtons[(int)EveTab.Upgrades] = Tab(0.47f, 0.59f, "UPGRADES", ShowUpgrades);
            _tabButtons[(int)EveTab.Roster] = Tab(0.60f, 0.72f, "PARTY", ShowRoster);
            Tab(0.73f, 0.85f, "DIARY", () =>
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

            // Whatever part of the story today's fight earned plays first, over the
            // Evening, and comes back here when it is done.
            var due = Story.StoryDirector.Due(s);
            if (due.Count > 0 && !CutsceneScreen.Playing)
            {
                CutsceneScreen.Play(due, () => UIManager.Instance.Show(ScreenId.Evening));
                return;
            }

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
            SelectTab(EveTab.Report);

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

            // Say WHY. A defeat with no explanation is what let "ran out of
            // flasks" look like a bug rather than a result.
            if (!string.IsNullOrEmpty(r.outcomeReason))
            {
                var why = UIFactory.Label(c, r.outcomeReason, UITheme.SizeSmall,
                    r.won ? UITheme.TextLow : UITheme.Danger, TextAlignmentOptions.TopLeft);
                UIFactory.Flex(why.gameObject, 1f, 0f, minHeight: 34f);
            }

            // One line per star, lit or dim, next to the rule it stands for. A bare
            // row of three icons never said which one was missed, so a two-star
            // run gave the player nothing to chase.
            for (int i = 0; i < ExpeditionReport.StarRules.Length; i++)
            {
                bool earned = r.StarEarned(i);
                var line = UIFactory.HStack(c, 8f);
                line.childAlignment = TextAnchor.MiddleLeft;
                UIFactory.FixedHeight(line.gameObject, 28f);
                var s = UIFactory.Icon(line.transform, Art.PixelSprites.Star(), 24f,
                    earned ? Color.white : UITheme.Alpha(Color.white, 0.18f));
                UIFactory.Flex(s.gameObject, 0f, 0f, minWidth: 24f, minHeight: 24f);
                var rule = UIFactory.Label(line.transform, ExpeditionReport.StarRules[i], UITheme.SizeBody,
                    earned ? UITheme.TextHi : UITheme.TextLow, TextAlignmentOptions.Left);
                UIFactory.Flex(rule.gameObject, 1f, 1f);
            }

            UIKit.KeyValue(c, "Waves cleared", $"{r.wavesCleared} / {r.totalWaves}");
            UIKit.KeyValue(c, "Boss", r.bossDefeated ? "defeated" : "—");
            UIKit.KeyValue(c, "Party returned", $"{r.partyTotal - r.partyDown} / {r.partyTotal}");
            if (r.downedHeroIds.Count > 0)
            {
                var fallen = new List<string>();
                foreach (string id in r.downedHeroIds)
                {
                    HeroRecord h = SaveSystem.Instance.State.FindHero(id);
                    if (h != null) fallen.Add(h.displayName);
                }
                var hurt = UIFactory.Label(c,
                    $"Fell on the road: {string.Join(", ", fallen)}. They sit tomorrow out.",
                    UITheme.SizeSmall, UITheme.Danger, TextAlignmentOptions.TopLeft);
                UIFactory.Flex(hurt.gameObject, 1f, 0f, minHeight: 34f);
            }
            UIKit.KeyValue(c, "Time on the road", $"{r.durationSeconds:0}s");
        }

        private void BuildJob(ExpeditionReport r)
        {
            if (r.contracts.Count > 1) { BuildJobs(r); return; }

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
                        ? r.contractMet ? (r.replayDay ? "Contract met — paid at the replay rate, half of it."
                                                        : "Contract met — bonus paid in full.")
                                        : "Below the grade they asked for. They paid half, and said so."
                        : $"{(string.IsNullOrEmpty(r.contractBuyer) ? "They" : r.contractBuyer)} didn't come back with the job done. No fee.",
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
            if (r.replayDay) UIKit.KeyValue(c, "Replayed road", "x0.50", valueColor: UITheme.Danger);
            UIKit.KeyValue(c, "PAID", $"{r.goldPaidByGrade} g", keyColor: UITheme.Candle, valueColor: UITheme.Candle);
        }

        /// <summary>Several fighters, several jobs: one line each — what they asked for, what they carried, what it paid.</summary>
        private void BuildJobs(ExpeditionReport r)
        {
            var card = UIKit.Card(_body, $"The jobs — {r.contracts.Count} fighters", out Transform c, spacing: 6f);
            UIFactory.Place(card.rectTransform, 0.34f, 0.34f, 0.66f, 1f);

            foreach (ExpeditionReport.ContractLine line in r.contracts)
            {
                Image row = UIKit.Surface(c, out Transform inner, UITheme.SurfaceHi, UITheme.LineSoft, "JobRow");
                UIFactory.FixedHeight(row.gameObject, 58f);
                var face = UIFactory.Icon(inner, Art.PixelSprites.Buyer(line.portraitId), 40f);
                UIFactory.Place(face.rectTransform, 0.01f, 0.1f, 0.15f, 0.9f);
                var who = UIFactory.Label(inner,
                    $"<b>{line.heroName}</b>  <size=85%>{line.title}</size>\n" +
                    $"<size=85%><color=#{ColorUtility.ToHtmlStringRGB(UITheme.Element(line.element))}>{line.element}</color> · wanted " +
                    $"<color=#{ColorUtility.ToHtmlStringRGB(UITheme.GradeColor(line.required))}>{line.required.ToString().ToUpperInvariant()}</color>, carried " +
                    $"<color=#{ColorUtility.ToHtmlStringRGB(UITheme.GradeColor(line.delivered))}>{line.delivered.ToString().ToUpperInvariant()}</color></size>",
                    UITheme.SizeSmall, UITheme.TextHi, TextAlignmentOptions.Left);
                UIFactory.Place(who.rectTransform, 0.17f, 0f, 0.76f, 1f);
                var paid = UIFactory.MonoLabel(inner, r.won ? $"{line.paid} g" : "0 g", UITheme.SizeBody,
                    r.won && line.met ? UITheme.Ok : UITheme.Danger, TextAlignmentOptions.Right);
                UIFactory.Place(paid.rectTransform, 0.76f, 0f, 0.97f, 1f);
            }

            var rule = UIFactory.Rule(c, UITheme.LineSoft);
            UIFactory.FixedHeight(rule.gameObject, 2f);
            if (!r.won)
            {
                var none = UIFactory.Label(c, "Nobody came back with the job done. No fees.", UITheme.SizeSmall, UITheme.Danger);
                UIFactory.Flex(none.gameObject, 1f, 0f, minHeight: 22f);
            }
            if (r.perfectTip) UIKit.KeyValue(c, "Perfect tips", "included", valueColor: UITheme.Ok);
            if (r.replayDay) UIKit.KeyValue(c, "Replayed road", "x0.50", valueColor: UITheme.Danger);
            UIKit.KeyValue(c, "PAID", $"{r.goldPaidByGrade} g", keyColor: UITheme.Candle, valueColor: UITheme.Candle);
        }

        private static string BuyerIdFor(ExpeditionReport r)
        {
            if (r.contracts.Count > 0) return r.contracts[0].portraitId;
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
                UIFactory.Label(c, "Never got a flask off out there.", UITheme.SizeBody, UITheme.TextLow);
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

        private string _justBought;

        /// <summary>
        /// Two shelves of cards: the benches (each rebuilt and working differently the
        /// next morning, shown as what it will look like) and the road (what the party
        /// carries). Buying one plays its installation on the card.
        /// </summary>
        private void ShowUpgrades()
        {
            Clear();
            SelectTab(EveTab.Upgrades);
            RunState s = SaveSystem.Instance.State;

            var card = UIKit.Card(_body, $"Upgrades — permanent, {s.gold} g on hand", out Transform c, spacing: 6f);
            UIFactory.Stretch(card.rectTransform);

            var benches = new List<UpgradeDefinition>();
            var road = new List<UpgradeDefinition>();
            foreach (var up in UpgradeCatalog.All) (up.IsBench ? benches : road).Add(up);

            Shelf(c, "THE BENCHES — each one rebuilt, and working differently from tomorrow", benches, s);
            Shelf(c, "THE ROAD — what the party carries; the later roads cannot be held without it", road, s);
            _justBought = null;
        }

        private void Shelf(Transform parent, string title, List<UpgradeDefinition> ups, RunState s)
        {
            var head = UIFactory.Heading(parent, title, UITheme.Candle, UITheme.SizeTiny);
            UIFactory.FixedHeight(head.gameObject, 18f);
            for (int i = 0; i < ups.Count; i += 4)
            {
                var row = UIFactory.HStack(parent, 10f);
                UIFactory.FixedHeight(row.gameObject, 124f);
                for (int j = 0; j < 4; j++)
                {
                    if (i + j < ups.Count) UpgradeCard(row.transform, ups[i + j], s);
                    else
                    {
                        var gap = UIFactory.Panel(row.transform, new Color(0f, 0f, 0f, 0f), "Gap");
                        UIFactory.Flex(gap.gameObject, 1f, 1f, minWidth: 200f);
                    }
                }
            }
        }

        private void UpgradeCard(Transform row, UpgradeDefinition up, RunState s)
        {
            bool owned = s.HasUpgrade(up.Id);
            bool available = UpgradeCatalog.IsAvailable(up.Id, s);
            Image card = UIKit.Surface(row, out Transform inner, owned ? UITheme.SurfaceTop : UITheme.SurfaceHi,
                owned ? UITheme.Ok : UITheme.LineSoft, "UpgradeCard");
            UIFactory.Flex(card.gameObject, 1f, 1f, minWidth: 200f);

            // What it installs, drawn as it will look.
            var box = UIFactory.Panel(inner, UITheme.Alpha(UITheme.Ground, 0.6f), "Preview");
            UIFactory.Place(box.rectTransform, 0.02f, 0.06f, 0.30f, 0.94f);
            foreach (Sprite sp in Preview(up.Id))
            {
                var icon = UIFactory.Icon(box.transform, sp, 64f);
                icon.preserveAspect = true;
                UIFactory.Place(icon.rectTransform, 0.04f, 0.04f, 0.96f, 0.96f);
                if (!owned && !available) icon.color = UITheme.Alpha(Color.white, 0.35f);
            }

            var name = UIFactory.Label(inner, up.DisplayName, UITheme.SizeBody, owned ? UITheme.Ok : UITheme.TextHi,
                TextAlignmentOptions.TopLeft, true);
            UIFactory.Place(name.rectTransform, 0.33f, 0.72f, 0.98f, 0.96f);
            var desc = UIFactory.Label(inner, !owned && !available ? UpgradeCatalog.UnlockHint(up.Id) : up.Description,
                UITheme.SizeTiny, UITheme.TextLow, TextAlignmentOptions.TopLeft);
            UIFactory.Place(desc.rectTransform, 0.33f, 0.30f, 0.98f, 0.72f);

            string id = up.Id; int cost = up.Cost;
            string caption = owned ? (up.IsBench ? $"INSTALLED · {up.Bench.ToUpperInvariant()}" : "OWNED")
                : !available ? "LOCKED" : $"BUY — {cost} g";
            var buy = UIFactory.Button(inner, caption,
                owned || !available ? (System.Action)null : () => BuyUpgrade(id, cost),
                primary: !owned && available);
            UIFactory.Place(buy.image.rectTransform, 0.33f, 0.05f, 0.98f, 0.28f);
            buy.interactable = !owned && available && s.gold >= cost;

            if (id == _justBought) UpgradeReveal.Play(card.rectTransform);
        }

        /// <summary>The sprites (back to front) that show what an upgrade installs.</summary>
        private static Sprite[] Preview(string id) => id switch
        {
            UpgradeCatalog.ClockworkStirrer => new[] { Art.ShopArt.PotBack(true), Art.ShopArt.Liquid(ElementType.Nature), Art.ShopArt.PotFront(true) },
            UpgradeCatalog.DraughtKiln => new[] { Art.MaltArt.Kiln(true) },
            UpgradeCatalog.SteepingVat => new[] { Art.MaltArt.JarBack(), Art.MaltArt.JarWater(12), Art.MaltArt.JarFront(true) },
            UpgradeCatalog.DryingRack => new[] { Art.ShopProps.HerbBundle(4) },
            UpgradeCatalog.BrassMortar => new[] { Art.ShopArt.MortarBack(true), Art.ShopArt.MortarFront(true) },
            UpgradeCatalog.GlassFunnel => new[] { Art.ShopArt.Funnel() },
            UpgradeCatalog.HeavierFlasks or UpgradeCatalog.TemperedGlass => new[] { Art.PixelSprites.Flask(ElementType.Fire) },
            UpgradeCatalog.SpareVials or UpgradeCatalog.Bandolier => new[] { Art.PixelSprites.Flask(ElementType.Water) },
            UpgradeCatalog.ThickBoots or UpgradeCatalog.HardLeathers => new[] { Art.PixelSprites.Fighter("knight") },
            _ => new[] { Art.PixelSprites.Star() },
        };

        private void BuyUpgrade(string id, int cost)
        {
            RunState s = SaveSystem.Instance.State;
            if (s.HasUpgrade(id) || s.gold < cost || !UpgradeCatalog.IsAvailable(id, s)) return;
            s.AddGold(-cost);
            s.ownedUpgrades.Add(id);
            SaveSystem.Instance.MarkDirty();
            AudioManager.Play(Sfx.Coin);
            AudioManager.Play(Sfx.Chime);
            _gold.text = $"{s.gold} g";
            _justBought = id;
            ShowUpgrades();
        }

        // ----------------------------------------------------------------- party

        /// <summary>
        /// Hire, level and pick tomorrow's party. Lives here rather than behind a
        /// new phase so it inherits the Evening's save timing (MarkDirty here is
        /// written to disk by Sleep) and so it reads as one shop with two shelves.
        ///
        /// Capped at <see cref="HeroCatalog.MaxRoster"/> rows on purpose: the
        /// project has no ScrollRect anywhere, and five rows plus the hire shelf
        /// is what the body can show without one.
        /// </summary>
        private void ShowRoster()
        {
            Clear();
            SelectTab(EveTab.Roster);
            RunState s = SaveSystem.Instance.State;

            int cap = s.DeployCap;
            int outToday = CountDeployed(s);

            var card = UIKit.Card(_body,
                $"Party — {outToday} / {cap} going out · {s.gold} g on hand",
                out Transform c, spacing: 8f);
            UIFactory.Stretch(card.rectTransform);

            foreach (HeroRecord hero in s.roster)
                BuildHeroRow(c, s, hero, outToday, cap);

            BuildHireRow(c, s);
        }

        private static int CountDeployed(RunState s)
        {
            int n = 0;
            foreach (HeroRecord h in s.roster) if (h.deployed && h.IsFit(s.day)) n++;
            return n;
        }

        private void BuildHeroRow(Transform parent, RunState s, HeroRecord hero, int outToday, int cap)
        {
            Image row = UIKit.Surface(parent, out Transform inner, UITheme.SurfaceHi, UITheme.LineSoft, "HeroRow");
            UIFactory.FixedHeight(row.gameObject, 96f);

            var portrait = UIKit.Portrait(inner, Art.PixelSprites.Fighter(hero.portraitId), 64f);
            UIFactory.Place((RectTransform)portrait.transform.parent.parent, 0.008f, 0.08f, 0.082f, 0.92f);

            var name = UIFactory.VStack(inner, 2f, new RectOffset(10, 8, 12, 12));
            UIFactory.Place((RectTransform)name.transform, 0.09f, 0f, 0.46f, 1f);
            var who = UIFactory.Label(name.transform, hero.displayName, UITheme.SizeBody, UITheme.TextHi,
                TextAlignmentOptions.TopLeft, true);
            UIFactory.FixedHeight(who.gameObject, 24f);
            var sub = UIFactory.Label(name.transform, hero.Subtitle, UITheme.SizeSmall,
                UITheme.Element(hero.affinity), TextAlignmentOptions.TopLeft);
            UIFactory.Flex(sub.gameObject, 1f, 1f);

            var perk = UIFactory.VStack(inner, 2f, new RectOffset(8, 8, 12, 12));
            UIFactory.Place((RectTransform)perk.transform, 0.46f, 0f, 0.725f, 1f);
            var atk = UIFactory.Label(perk.transform, HeroPerks.AttunementBlurb(hero.affinity),
                UITheme.SizeSmall, UITheme.TextMid, TextAlignmentOptions.TopLeft);
            UIFactory.Flex(atk.gameObject, 1f, 1f);
            var def = UIFactory.Label(perk.transform, HeroPerks.WardBlurb(hero.affinity),
                UITheme.SizeSmall, UITheme.TextLow, TextAlignmentOptions.TopLeft);
            UIFactory.Flex(def.gameObject, 1f, 1f);

            // Capture into locals before the lambdas: closing over the foreach
            // variable is the classic bug in a row builder like this.
            string heroId = hero.id;
            bool maxed = !HeroCatalog.CanLevel(hero);
            int levelCost = HeroCatalog.LevelUpCost(hero.level);

            var lvl = UIFactory.Button(inner,
                maxed ? "MAX LEVEL" : $"LEVEL UP — {levelCost} g",
                maxed ? (System.Action)null : () => LevelHero(heroId, levelCost),
                primary: !maxed);
            UIFactory.Place(lvl.image.rectTransform, 0.735f, 0.52f, 0.99f, 0.94f);
            lvl.interactable = !maxed && s.gold >= levelCost;

            bool resting = !hero.IsFit(s.day);
            bool wouldExceed = !hero.deployed && outToday >= cap;
            // The last hero going out cannot be stood down: the road always takes
            // someone (RunState.DeployedParty would quietly send them anyway), so
            // the button says so instead of showing "0 / 1 going out".
            bool leads = hero.deployed && !resting && outToday <= 1;
            string deployCaption = resting && hero.deployed ? "LIMPS OUT — nobody else is fit"
                : resting ? $"RESTING — back day {hero.restUntilDay}"
                : leads ? "LEADS THE PARTY"
                : hero.deployed ? "GOING OUT"
                : "SEND OUT";
            var dep = UIFactory.Button(inner, deployCaption,
                resting || leads ? (System.Action)null : () => ToggleDeploy(heroId),
                primary: hero.deployed && !resting);
            UIFactory.Place(dep.image.rectTransform, 0.735f, 0.06f, 0.99f, 0.48f);
            dep.interactable = !resting && !wouldExceed && !leads;
        }

        private void BuildHireRow(Transform parent, RunState s)
        {
            Image row = UIKit.Surface(parent, out Transform inner, UITheme.Surface, UITheme.LineSoft, "HireRow");
            UIFactory.FixedHeight(row.gameObject, 96f);

            bool full = s.roster.Count >= HeroCatalog.MaxRoster;
            bool unlocked = HeroCatalog.CanHire(s);
            HeroRecord candidate = full ? null : HeroCatalog.CandidateFor(s.day, s.roster);
            int cost = HeroCatalog.HireCost(s.roster.Count);

            if (candidate != null)
            {
                var portrait = UIKit.Portrait(inner, Art.PixelSprites.Buyer(candidate.portraitId), 64f);
                UIFactory.Place((RectTransform)portrait.transform.parent.parent, 0.008f, 0.08f, 0.082f, 0.92f);
            }

            var text = UIFactory.VStack(inner, 2f, new RectOffset(10, 8, 12, 12));
            UIFactory.Place((RectTransform)text.transform, 0.09f, 0f, 0.725f, 1f);

            string headline = full ? "The notice board is empty"
                : !unlocked ? "Nobody will sign on yet"
                : $"{candidate.displayName} is looking for work";
            var head = UIFactory.Label(text.transform, headline, UITheme.SizeBody, UITheme.TextHi,
                TextAlignmentOptions.TopLeft, true);
            UIFactory.FixedHeight(head.gameObject, 24f);

            string detail = full ? $"You are keeping {HeroCatalog.MaxRoster} already."
                : !unlocked ? "Come back once you have brought a road home."
                : $"{candidate.Subtitle} · {HeroPerks.AttunementBlurb(candidate.affinity)}";
            var body = UIFactory.Label(text.transform, detail, UITheme.SizeSmall, UITheme.TextLow,
                TextAlignmentOptions.TopLeft);
            UIFactory.Flex(body.gameObject, 1f, 1f);

            bool canBuy = !full && unlocked && s.gold >= cost;
            var hire = UIFactory.Button(inner, full || !unlocked ? "—" : $"HIRE — {cost} g",
                canBuy ? () => HireCandidate(cost) : (System.Action)null, primary: canBuy);
            UIFactory.Place(hire.image.rectTransform, 0.735f, 0.28f, 0.99f, 0.72f);
            hire.interactable = canBuy;
        }

        // --- transactions: guard, charge, mutate, MarkDirty, rebuild.
        //     Same shape as BuyUpgrade, deliberately.

        private void LevelHero(string heroId, int cost)
        {
            RunState s = SaveSystem.Instance.State;
            HeroRecord hero = s.FindHero(heroId);
            if (hero == null || !HeroCatalog.CanLevel(hero) || s.gold < cost) return;

            s.AddGold(-cost);
            hero.level++;
            SaveSystem.Instance.MarkDirty();
            AudioManager.Play(Sfx.Coin);
            _gold.text = $"{s.gold} g";
            ShowRoster();
        }

        private void HireCandidate(int cost)
        {
            RunState s = SaveSystem.Instance.State;
            if (!HeroCatalog.CanHire(s) || s.gold < cost) return;

            HeroRecord hired = HeroCatalog.CandidateFor(s.day, s.roster);
            // Hiring is all it takes: they go out tomorrow if the party has room, and
            // come to the Counter after everyone who was already here (roster order).
            hired.deployed = CountDeployed(s) < s.DeployCap;
            s.AddGold(-cost);
            s.roster.Add(hired);
            SaveSystem.Instance.MarkDirty();
            AudioManager.Play(Sfx.Coin);
            _gold.text = $"{s.gold} g";
            ShowRoster();
        }

        private void ToggleDeploy(string heroId)
        {
            RunState s = SaveSystem.Instance.State;
            HeroRecord hero = s.FindHero(heroId);
            if (hero == null || !hero.IsFit(s.day)) return;

            if (hero.deployed)
            {
                if (CountDeployed(s) <= 1) return;   // someone always walks the road
                hero.deployed = false;
            }
            else
            {
                if (CountDeployed(s) >= s.DeployCap) return;
                hero.deployed = true;
            }

            SaveSystem.Instance.MarkDirty();
            AudioManager.Play(Sfx.Confirm);
            ShowRoster();
        }

        // ----------------------------------------------------------------- shared

        private void Clear()
        {
            for (int i = _body.childCount - 1; i >= 0; i--) Destroy(_body.GetChild(i).gameObject);
        }

        private void SelectTab(EveTab active)
        {
            for (int i = 0; i < _tabButtons.Length; i++)
            {
                Button b = _tabButtons[i];
                if (b == null) continue;
                bool on = i == (int)active;
                UIFactory.TintButton(b,
                    on ? UITheme.Candle : UITheme.SurfaceHi,
                    on ? UITheme.CandleHot : UITheme.SurfaceTop,
                    on ? UITheme.TextOnGold : UITheme.TextHi);
            }
        }
    }
}
