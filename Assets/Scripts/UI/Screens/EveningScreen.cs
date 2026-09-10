using System.Text;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using AlchemistsArsenal.Core;
using AlchemistsArsenal.Combat;
using AlchemistsArsenal.Audio;

namespace AlchemistsArsenal.UI
{
    /// <summary>
    /// Evening shell — Phase 0 ships the Report + a Diary tab (DESIGN.md §7.9).
    /// The pay breakdown shows the grade multiplier explicitly (cat-1 legibility,
    /// reviewer W-R2b): a Poor potion visibly pays 40%.
    /// </summary>
    public class EveningScreen : GameScreen
    {
        private RectTransform _body;
        private TextMeshProUGUI _gold;

        protected override void Build()
        {
            UIFactory.Box(transform, UITheme.Ink900, Rt);

            var strip = UIFactory.HStack(transform, 0f);
            var srt = (RectTransform)strip.transform;
            srt.anchorMin = new Vector2(0f, 0.92f); srt.anchorMax = new Vector2(1f, 1f);
            srt.offsetMin = srt.offsetMax = Vector2.zero;
            Tab(strip.transform, "REPORT", ShowReport);
            Tab(strip.transform, "DIARY", () => { DiaryScreen.OpenEntryId = null; DiaryScreen.FromOpeningCinematic = false; UIManager.Instance.Show(ScreenId.Diary); });

            _gold = UIFactory.Label(strip.transform, "", 18, UITheme.Candle, TextAlignmentOptions.Right, true);
            _gold.gameObject.AddComponent<LayoutElement>().minWidth = 200;

            _body = UIFactory.Rect(transform, "Body", new Vector2(0f, 0.12f), new Vector2(1f, 0.92f),
                new Vector2(28, 0), new Vector2(-28, -12));

            var sleep = UIFactory.Button(transform, "SLEEP  ▶", () => GameLoopManager.Instance.BeginBiomeMap());
            var slrt = sleep.image.rectTransform;
            slrt.anchorMin = new Vector2(0.78f, 0.02f); slrt.anchorMax = new Vector2(0.97f, 0.1f);
            slrt.offsetMin = slrt.offsetMax = Vector2.zero;
        }

        private void Tab(Transform p, string t, System.Action a)
        {
            var b = UIFactory.Button(p, t, a, primary: false);
            var le = b.gameObject.AddComponent<LayoutElement>(); le.minWidth = 160;
        }

        protected override void OnShow()
        {
            _gold.text = $"{SaveSystem.Instance.State.gold} g";
            ShowReport();

            // Rookie reacts to the potion grade in Animalese (cat 7).
            var r = GameLoopManager.Instance != null ? GameLoopManager.Instance.LatestReport : null;
            if (r != null)
            {
                bool happy = r.won && r.craftedGrade is PotionGrade.Great or PotionGrade.Perfect;
                AudioManager.Speak(happy ? "ha ha gu-d fla-she wi-tch" : "nu gu-d dis wun to sla-dge", happy ? 1.2f : 0.85f);
            }
        }

        private void ShowReport()
        {
            foreach (Transform c in _body) Destroy(c.gameObject);
            var r = GameLoopManager.Instance != null ? GameLoopManager.Instance.LatestReport : null;
            if (r == null)
            {
                UIFactory.Label(_body, "No expedition on record.", 20, UITheme.ParchmentDim);
                return;
            }

            var cols = UIFactory.HStack(_body, 18f);
            UIFactory.Stretch((RectTransform)cols.transform);

            // outcome
            var c1 = Column(cols.transform, "OUTCOME");
            UIFactory.Label(c1, r.won ? "VICTORY" : "DEFEAT", 26, r.won ? UITheme.Ok : UITheme.Danger, TextAlignmentOptions.TopLeft, true);
            UIFactory.Label(c1, $"Waves cleared: {r.wavesCleared} / {r.totalWaves}\n" +
                                $"Boss: {(r.bossDefeated ? "defeated" : "—")}\n" +
                                $"Party: {r.partyTotal - r.partyDown}/{r.partyTotal} returned\n" +
                                $"Time: {r.durationSeconds:0}s\n" +
                                $"Grade: {new string('*', r.Stars)}", 16, UITheme.Parchment);

            // loot + pay
            var c2 = Column(cols.transform, "LOOT & PAY");
            var lb = new StringBuilder();
            foreach (var kv in r.herbDrops) lb.AppendLine($"  {kv.Key} ×{kv.Value}");
            if (r.herbDrops.Count == 0) lb.AppendLine("  (no herbs)");
            UIFactory.Label(c2, lb.ToString(), 15, UITheme.Parchment);
            var (paid, tip) = Economy.Payout(r.craftedGrade, replay: SaveSystem.Instance.State.IsReplayDay);
            float mult = CombatQuality.PaymentMultiplier(r.craftedGrade);
            UIFactory.Label(c2,
                $"Base fee: {Economy.BaseFee} g\n" +
                $"Potion grade ({r.craftedGrade}): ×{mult:0.00}\n" +
                (tip ? $"Perfect tip: +{Economy.PerfectTip} g\n" : "") +
                (SaveSystem.Instance.State.IsReplayDay ? "Replay: ×0.5\n" : "") +
                $"<b>Paid: {paid} g</b>\n" +
                $"Loot gold: {r.goldFromLoot} g", 16, UITheme.Candle);

            // potion performance
            var c3 = Column(cols.transform, "POTION PERFORMANCE");
            foreach (var b in r.bombs)
            {
                UIFactory.Label(c3,
                    $"<b>{b.name}</b>  <color=#{ColorUtility.ToHtmlStringRGB(UITheme.GradeColor(b.grade))}>{b.grade}</color>\n" +
                    $"  throws {b.throws} · hits {b.hits} · dmg {b.totalDamage}\n" +
                    $"  {(b.grade == PotionGrade.Poor ? "Poor: −50% damage, no ×2" : b.everHadAdvantage ? "elemental ×2 landed" : "no elemental advantage")}",
                    15, UITheme.Parchment);
            }
            if (r.bombs.Count == 0) UIFactory.Label(c3, "  (no bombs thrown)", 15, UITheme.ParchmentDim);
        }

        private Transform Column(Transform parent, string title)
        {
            var panel = UIFactory.FramedPanel(parent, "Col");
            var le = panel.gameObject.AddComponent<LayoutElement>(); le.flexibleWidth = 1;
            var v = UIFactory.VStack(panel.transform, 8f, new RectOffset(16, 16, 16, 16));
            UIFactory.Stretch((RectTransform)v.transform);
            UIFactory.Label(v.transform, title, 14, UITheme.ParchmentDim, TextAlignmentOptions.TopLeft, true);
            return v.transform;
        }
    }
}
