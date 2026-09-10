using System.IO;
using UnityEngine;
using AlchemistsArsenal.Core;
using AlchemistsArsenal.Combat;
using AlchemistsArsenal.Systems;

namespace AlchemistsArsenal.DebugTools
{
    /// <summary>
    /// Headless checks for the Phase-0 meta layer — save round-trip, migration
    /// clamping, grade→payout math, the loadout floor, and the born-at-25 quality
    /// model (reviewer G5). Does NOT drive the full UI/expedition loop (that needs
    /// play mode + a scene); it covers the pure logic those transitions rely on.
    /// Attach to a GameObject in an empty scene and press Play.
    /// </summary>
    public class GameLoopSimulationTest : MonoBehaviour
    {
        private int _pass, _fail;

        private void Start()
        {
            Debug.Log("<color=cyan><b>=== GAME LOOP / META SIMULATION ===</b></color>");
            TestQualityModel();
            TestLoadoutFloor();
            TestEconomy();
            TestSaveRoundTrip();
            TestMigrationClamps();
            Debug.Log($"<color=cyan><b>=== DONE — {_pass} pass, {_fail} fail ===</b></color>");
        }

        private void Check(bool ok, string what)
        {
            if (ok) { _pass++; Debug.Log($"<color=green>✔</color> {what}"); }
            else { _fail++; Debug.LogError($"❌ {what}"); }
        }

        private void TestQualityModel()
        {
            var o = new ActiveOrder("T", "Test Flask", ElementType.Water);
            Check(o.qualityScore == 25 && o.GetGrade() == PotionGrade.Poor, "order is born at 25 / Poor");

            int events = 0; o.OnQualityChanged += _ => events++;
            o.ApplyBonus(60, "Cauldron", "clean");   // 85
            Check(o.qualityScore == 85 && o.GetGrade() == PotionGrade.Great, "clean brewing raises to Great");
            o.ApplyDeduction(40, "Cauldron", "hot"); // 45
            Check(o.qualityScore == 45 && o.GetGrade() == PotionGrade.Poor, "overheating drops back to Poor");
            o.ApplyBonus(200, "x", "clamp");
            Check(o.qualityScore == 100 && events == 3, "quality clamps at 100 and every change fired an event");
        }

        private void TestLoadoutFloor()
        {
            var lo = LoadoutBuilder.Build(null); // never accepted an order
            Check(lo != null && lo.Slots.Count == 1 && lo.Slots[0].bomb != null && lo.Slots[0].count >= 1,
                "LoadoutBuilder(null) still yields one usable bomb (no empty loadout)");

            var order = new ActiveOrder("T", "Firebloom Flask", ElementType.Fire);
            order.ApplyBonus(70, "c", "great brew"); // 95 -> Perfect
            var lo2 = LoadoutBuilder.Build(order);
            Check(lo2.Slots[0].bomb.Element == ElementType.Fire && lo2.Slots[0].count >= 7,
                "a Perfect Fire brew arms a Fire bomb with more ammo");
        }

        private void TestEconomy()
        {
            var poor = Economy.Payout(PotionGrade.Poor);
            var great = Economy.Payout(PotionGrade.Great);
            var perfect = Economy.Payout(PotionGrade.Perfect);
            var replay = Economy.Payout(PotionGrade.Great, replay: true);
            Check(poor.paid == 20 && !poor.tip, "Poor pays 40% of base (20 g), no tip");
            Check(great.paid == 50 && !great.tip, "Great pays full base (50 g)");
            Check(perfect.paid == 50 + Economy.PerfectTip && perfect.tip, "Perfect pays base + tip");
            Check(replay.paid == 25, "replay halves the fee");
        }

        private void TestSaveRoundTrip()
        {
            var s = RunState.NewGame(0);
            s.day = 4; s.gold = 137; s.currentBiomeIndex = 2;
            s.bestGrades[0] = 3; s.bestGrades[1] = 2;
            s.unlockedDiary.Add("diary_ww");
            s.lastResolvedDay = 3;

            string path = Path.Combine(Application.persistentDataPath, "slot_test.json");
            File.WriteAllText(path, JsonUtility.ToJson(s));
            var back = SaveSystem.Migrate(JsonUtility.FromJson<RunState>(File.ReadAllText(path)));
            File.Delete(path);

            Check(back.day == 4 && back.gold == 137 && back.currentBiomeIndex == 2
                  && back.bestGrades[0] == 3 && back.unlockedDiary.Contains("diary_ww") && back.lastResolvedDay == 3,
                "RunState survives a JSON round-trip intact");
        }

        private void TestMigrationClamps()
        {
            var garbage = new RunState
            {
                saveVersion = 1, day = 0, gold = -999, currentBiomeIndex = 99,
                bestGrades = new[] { 7, -2 }, ownedAdventurers = new System.Collections.Generic.List<string>()
            };
            var fixedUp = SaveSystem.Migrate(garbage);
            Check(fixedUp.day >= 1, "migrate: day clamped to >= 1");
            Check(fixedUp.gold == 0, "migrate: negative gold clamped to 0");
            Check(fixedUp.currentBiomeIndex <= BiomeLibrary.Count - 1, "migrate: biome index clamped in range");
            Check(fixedUp.bestGrades.Length == BiomeLibrary.Count && fixedUp.bestGrades[0] == 3,
                "migrate: bestGrades resized + values clamped 0..3");
            Check(fixedUp.ownedAdventurers.Contains("Rookie"), "migrate: empty roster re-seeds Rookie");
        }
    }
}
