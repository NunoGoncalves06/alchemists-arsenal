using System.IO;
using UnityEngine;
using AlchemistsArsenal.Core;
using AlchemistsArsenal.Combat;
using AlchemistsArsenal.Data;
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
    public class GameLoopSimulationTest : MonoBehaviour, ISimulationSuite
    {
        public bool Done { get; private set; }

        private int _pass, _fail;

        private void Start()
        {
            Debug.Log("<color=cyan><b>=== GAME LOOP / META SIMULATION ===</b></color>");
            TestQualityModel();
            TestQualityCeiling();
            TestLoadoutFloor();
            TestEconomy();
            TestStarRules();
            TestSaveRoundTrip();
            TestMigrationClamps();
            TestRosterRoundTrip();
            TestRosterMigration();
            TestInjuries();
            TestPerkSymmetry();
            TestCostCurve();
            TestStoryRoundTrip();
            TestStoryRules();
            Done = true;
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
            o.ApplyDeduction(40, "Cauldron", "burnt"); // 45
            Check(o.qualityScore == 45 && o.GetGrade() == PotionGrade.Poor, "a burnt bottom drops it back to Poor");
            o.ApplyBonus(200, "x", "clamp");
            Check(o.qualityScore == 100 && events == 3, "quality clamps at 100 and every change fired an event");
        }

        /// <summary>
        /// A structural guard on the morning's points budget. Before the Phase-0
        /// rebalance a flawless morning was worth 198 against a cap of 100, and
        /// the cauldron alone paid +60 - enough to reach Great without touching
        /// the other three stations, which made station depth worthless. The
        /// budget is now deliberately just over 100 so every station is load
        /// bearing and a botched step actually costs a grade.
        ///
        /// These numbers come from Data.QualityBudget, the same constants the
        /// benches pay out of, so a retune at a bench is checked here automatically
        /// (they used to be transcribed by hand and silently went stale).
        /// </summary>
        private void TestQualityCeiling()
        {
            const int start = ActiveOrder.StartingQuality;
            const int cauldron = QualityBudget.BrewTotal;
            int best = QualityBudget.FlawlessMorning();
            Check(best >= 95 && best <= 120,
                $"a flawless morning is worth {best} points - just over the 100 cap, not double it");

            // The single most important consequence: the pot can no longer carry
            // a morning by itself.
            int cauldronOnly = start + cauldron;
            Check(CombatQuality.GradeFor(cauldronOnly) == PotionGrade.Poor,
                $"the cauldron alone reaches only {cauldronOnly} ({CombatQuality.GradeFor(cauldronOnly)}) - the other stations are mandatory");

            // And a clean morning with one fumbled step should still cost a
            // grade. Measured from the clamped 100 a player can actually hold -
            // the slack above the cap is headroom, not spendable points.
            int fumbled = Mathf.Min(best, 100) - QualityBudget.PourOverflow;   // e.g. an overfilled flask
            Check(CombatQuality.GradeFor(fumbled) > PotionGrade.Perfect,
                $"one botched step drops a flawless morning to {CombatQuality.GradeFor(fumbled)}");
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

        /// <summary>
        /// Every star count has to be reachable. Win() now requires every wave
        /// cleared, so the old third star (<c>bossDefeated || wavesCleared &gt;=
        /// totalWaves</c>) came free with any win and a 1-star result could not
        /// happen. Each star must hang on something a win does not already imply.
        /// </summary>
        private void TestStarRules()
        {
            // A full clear by a two-hero party: all waves down, guardian felled.
            ExpeditionReport Run(bool won, int partyDown, PotionGrade grade) => new ExpeditionReport
            {
                won = won, wavesCleared = 3, totalWaves = 3, bossDefeated = won,
                partyTotal = 2, partyDown = partyDown, craftedGrade = grade,
            };

            var lost           = Run(false, 0, PotionGrade.Perfect);
            var oneStar        = Run(true,  1, PotionGrade.Okay);
            var downButGreat   = Run(true,  1, PotionGrade.Great);   // 2: road + flask
            var allBackButOkay = Run(true,  0, PotionGrade.Okay);    // 2: road + every hero back
            var three          = Run(true,  0, PotionGrade.Great);
            var perfect        = Run(true,  0, PotionGrade.Perfect);

            Check(lost.Stars == 0, "stars: a lost road is 0 stars, however good the flask");
            Check(oneStar.Stars == 1,
                $"stars: a full clear with the guardian felled, a hero down and an Okay flask is 1 star (got {oneStar.Stars}); " +
                "clearing every wave no longer pays the third star by itself");
            Check(downButGreat.Stars == 2 && downButGreat.StarFineFlask && !downButGreat.StarEveryHeroBack
                  && allBackButOkay.Stars == 2 && allBackButOkay.StarEveryHeroBack && !allBackButOkay.StarFineFlask,
                $"stars: either extra star alone makes 2 (hero down + Great = {downButGreat.Stars}, all back + Okay = {allBackButOkay.Stars})");
            Check(three.Stars == 3 && perfect.Stars == 3,
                $"stars: everyone home with a {ExpeditionReport.ThirdStarGrade} or Perfect flask is 3 " +
                $"(got {three.Stars} / {perfect.Stars}); PotionGrade counts down, so 'or better' must not flip");

            int agree = 0;
            foreach (var rep in new[] { lost, oneStar, downButGreat, allBackButOkay, three, perfect })
            {
                int lit = 0;
                for (int i = 0; i < ExpeditionReport.StarRules.Length; i++) if (rep.StarEarned(i)) lit++;
                if (lit == rep.Stars) agree++;
            }
            Check(agree == 6, "stars: the per-star lines the Evening report shows always add up to Stars");

            // One star is still enough to open the next road.
            var s = RunState.NewGame(0);
            s.RecordGrade(0, oneStar.Stars);
            Check(s.IsBiomeUnlocked(1) && !s.IsBiomeUnlocked(2),
                "stars: a 1-star win opens the next road, and only the next one");
        }

        private void TestSaveRoundTrip()
        {
            var s = RunState.NewGame(0);
            s.lastSavedUnixSeconds = 1;
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

        /// <summary>The story's memory survives a save, and an old save without it loads.</summary>
        private void TestStoryRoundTrip()
        {
            var s = RunState.NewGame(0);
            Story.StoryDirector.Set(s, Story.StoryDirector.EndingPending);
            s.endingSeen = true;
            var back = SaveSystem.Migrate(JsonUtility.FromJson<RunState>(JsonUtility.ToJson(s)));
            Check(back.storyFlags != null && back.storyFlags.Contains(Story.StoryDirector.EndingPending) && back.endingSeen,
                "story: flags and endingSeen survive a JSON round-trip");

            var old = new RunState { saveVersion = 1, storyFlags = null };
            var migrated = SaveSystem.Migrate(old);
            Check(migrated.storyFlags != null && !migrated.endingSeen && migrated.saveVersion == RunState.CurrentVersion,
                "story: a v1 save (no story fields) migrates to an empty story");
        }

        /// <summary>Which fight earns which scene, and that each plays once.</summary>
        private void TestStoryRules()
        {
            var s = RunState.NewGame(0);
            var win = new ExpeditionReport { won = true, bossDefeated = true };
            var noBoss = new ExpeditionReport { won = true, bossDefeated = false };

            Story.StoryDirector.OnDayResolved(s, 0, noBoss);
            Check(Story.StoryDirector.Due(s).Count == 0, "story: a boss-less win earns no scene");

            Story.StoryDirector.OnDayResolved(s, 0, win);
            var due = Story.StoryDirector.Due(s);
            Check(due.Count == 1 && due[0].Id == "woodwose", "story: the Woodwose's defeat earns its scene");
            Story.StoryDirector.Finished(s, due[0]);
            Story.StoryDirector.OnDayResolved(s, 0, win);
            Check(Story.StoryDirector.Due(s).Count == 0, "story: the Woodwose scene plays once, not on every replay");

            Story.StoryDirector.OnDayResolved(s, BiomeLibrary.Count - 1, win);
            due = Story.StoryDirector.Due(s);
            Check(due.Count == 3 && due[0].Id == "reveal" && due[1].Id == "ending" && due[2].Id == "credits",
                "story: the Peak earns reveal, ending, credits, in that order");
            Story.StoryDirector.Finished(s, due[0]);
            Story.StoryDirector.Finished(s, due[1]);
            due = Story.StoryDirector.Due(s);
            Check(due.Count == 1 && due[0].Id == "credits", "story: quitting before the credits replays only the credits");
            Story.StoryDirector.Finished(s, due[0]);
            Check(s.endingSeen && Story.StoryDirector.Due(s).Count == 0 && s.HasDiary("diary_end") && s.HasDiary("diary_after"),
                "story: the credits end the story and unlock the last pages");

            int entries = Story.DiaryManager.All.Count;
            var ids = new System.Collections.Generic.HashSet<string>();
            foreach (var e in Story.DiaryManager.All) ids.Add(e.id);
            Check(entries >= 11 && ids.Count == entries && ids.Contains("diary_00") && ids.Contains("diary_ww") && ids.Contains("diary_perfect"),
                $"story: {entries} diary entries, unique ids, the original three kept");
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

        /// <summary>
        /// The one test that proves the roster's whole data model works.
        /// <c>List&lt;HeroRecord&gt;</c> round-tripping through JsonUtility is the
        /// load-bearing assumption behind hiring, levelling and injury; JsonUtility
        /// silently drops anything it cannot handle rather than throwing, so this
        /// has to be asserted rather than assumed.
        /// </summary>
        private void TestRosterRoundTrip()
        {
            var s = RunState.NewGame(0);
            s.lastSavedUnixSeconds = 1;
            s.day = 6;
            s.bestGrades[0] = 2;
            s.roster = new System.Collections.Generic.List<HeroRecord>
            {
                HeroCatalog.NewHire("Ser Halden", 0, ElementType.Fire, "bulwark", "knight"),
                HeroCatalog.NewHire("Mira Thorn", 1, ElementType.Water, "marksman", "herbalist"),
                HeroCatalog.NewHire("Otho Vance", 2, ElementType.Arcane, "skirmisher", "merchant"),
            };
            s.roster[1].level = 4;
            s.roster[2].restUntilDay = 7;

            var back = SaveSystem.Migrate(JsonUtility.FromJson<RunState>(JsonUtility.ToJson(s)));

            Check(back.roster.Count == 3, "roster: all three heroes survive the JSON round-trip");
            Check(back.roster[0].affinity == ElementType.Fire
                  && back.roster[1].affinity == ElementType.Water
                  && back.roster[2].affinity == ElementType.Arcane,
                "roster: affinities survive intact");
            Check(back.roster[1].level == 4 && back.roster[0].level == 1,
                "roster: per-hero levels survive intact");
            Check(back.roster[1].archetypeId == "marksman" && back.roster[0].archetypeId == "bulwark",
                "roster: archetypes survive intact");
            Check(back.roster[2].restUntilDay == 7 && !back.roster[2].IsFit(6) && back.roster[2].IsFit(7),
                "roster: an injured hero is unfit on day 6 and fit again on day 7");
        }

        /// <summary>
        /// A hero who goes down misses exactly one expedition, and the road still
        /// always gets someone. restUntilDay existed with nothing ever setting it.
        /// </summary>
        private void TestInjuries()
        {
            var s = new RunState { day = 3, bestGrades = new int[5] };
            s.roster = new System.Collections.Generic.List<HeroRecord>
            {
                HeroCatalog.NewHire("A", 0), HeroCatalog.NewHire("B", 1),
            };
            foreach (var hero in s.roster) hero.deployed = true;   // both out today (the cap is RunState.MaxParty)
            string downId = s.roster[1].id;

            // Day 3 resolves with B down, then the day rolls over (as BeginEvening does).
            int benched = HeroCatalog.ApplyInjuries(s, new[] { downId }, s.day);
            s.day++;
            s.EnsureDeployment();

            HeroRecord b = s.FindHero(downId);
            Check(benched == 1 && b.restUntilDay == 5, $"injury: a hero down on day 3 rests until day 5 (got {b.restUntilDay})");
            Check(!b.IsFit(4) && b.IsFit(5), "injury: they miss exactly one expedition (day 4) and are back on day 5");
            var party = s.DeployedParty();
            Check(party.Count == 1 && party[0].id == s.roster[0].id,
                $"injury: day 4 goes out without them ({party.Count} going)");

            // The whole party down: someone still limps out, or the expedition never resolves.
            var solo = new RunState { day = 2, bestGrades = new int[5] };
            solo.roster = new System.Collections.Generic.List<HeroRecord> { HeroCatalog.NewHire("Rookie", 0) };
            HeroCatalog.ApplyInjuries(solo, new[] { solo.roster[0].id }, solo.day);
            solo.day++;
            solo.EnsureDeployment();
            Check(solo.DeployedParty().Count == 1, "injury: a lone downed hero still limps out the next day");
        }

        private void TestRosterMigration()
        {
            // A save written before the roster existed: empty list, legacy names.
            var legacy = new RunState { day = 3, bestGrades = new int[5] };
            legacy.roster.Clear();
            var seeded = SaveSystem.Migrate(legacy);
            Check(seeded.roster.Count == 1 && seeded.roster[0].displayName == "Rookie",
                "migrate: an empty roster is seeded from the legacy adventurer list");
            Check(seeded.roster[0].deployed, "migrate: the seeded hero is actually deployed");

            // Garbage values a hand-edited save could contain.
            var garbage = new RunState { day = 5, bestGrades = new int[5] };
            garbage.roster = new System.Collections.Generic.List<HeroRecord>
            {
                new HeroRecord { id = "", level = 99, affinity = (ElementType)57,
                                 archetypeId = "sorcerer", restUntilDay = 9999, deployed = true },
            };
            var fixedUp = SaveSystem.Migrate(garbage);
            HeroRecord h = fixedUp.roster[0];
            Check(h.level == HeroCatalog.MaxLevel, "migrate: level clamped to MaxLevel");
            Check((int)h.affinity >= 0 && (int)h.affinity <= 4, "migrate: affinity clamped into range");
            Check(h.archetypeId == HeroPerks.DefaultArchetype, "migrate: unknown archetype falls back");
            Check(h.restUntilDay <= fixedUp.day + 1,
                "migrate: restUntilDay clamped so a save can never bench someone forever");
            Check(!string.IsNullOrEmpty(h.id), "migrate: a blank hero id is backfilled");

            // Deployment must be trimmed to the cap, and must never end up empty:
            // ExpeditionManager reads an empty adventurer list as "nobody down
            // yet", so a party of nobody would leave the expedition running.
            var overfull = new RunState { day = 4, bestGrades = new int[5] };
            overfull.roster = new System.Collections.Generic.List<HeroRecord>
            {
                HeroCatalog.NewHire("A", 0), HeroCatalog.NewHire("B", 1), HeroCatalog.NewHire("C", 2),
                HeroCatalog.NewHire("D", 3), HeroCatalog.NewHire("E", 4),
            };
            foreach (var hero in overfull.roster) hero.deployed = true;
            var trimmed = SaveSystem.Migrate(overfull);
            int deployed = 0;
            foreach (var hero in trimmed.roster) if (hero.deployed) deployed++;
            Check(deployed == trimmed.DeployCap,
                $"migrate: deployment trimmed to the cap ({deployed} of {trimmed.roster.Count} going out)");

            var allResting = new RunState { day = 4, bestGrades = new int[5] };
            allResting.roster = new System.Collections.Generic.List<HeroRecord>
            {
                HeroCatalog.NewHire("A", 0), HeroCatalog.NewHire("B", 1),
            };
            foreach (var hero in allResting.roster) { hero.deployed = false; hero.restUntilDay = 99; }
            var rescued = SaveSystem.Migrate(allResting);
            Check(rescued.DeployedParty().Count >= 1,
                "migrate: a fully-resting roster still sends someone (no expedition soft-lock)");
        }

        /// <summary>
        /// Guards the core promise of the perk design: two heroes of the same level
        /// are equally powerful, and differ only in <i>when</i> they are good. If a
        /// future "small buff" gives one element or one archetype more raw power
        /// than another, this fails.
        /// </summary>
        private void TestPerkSymmetry()
        {
            bool sameAttunement = true;
            for (int e = 0; e < 5; e++)
            {
                var el = (ElementType)e;
                // Matching flask: always the same bonus, whatever the element.
                if (!Mathf.Approximately(HeroPerks.AttunementFor(el, el), HeroPerks.AttunementMultiplier))
                    sameAttunement = false;
                // Non-matching flask: always exactly nothing.
                var other = (ElementType)((e + 1) % 5);
                if (!Mathf.Approximately(HeroPerks.AttunementFor(el, other), 1f))
                    sameAttunement = false;
            }
            Check(sameAttunement, "perks: every element's attunement is worth exactly the same");

            // Archetypes are positioning only — no HP, damage or ammo may vary.
            bool sidegrade = HeroPerks.Archetypes.Length >= 3;
            foreach (var a in HeroPerks.Archetypes)
                if (a.IdealRange <= 0f || a.MaxRange < a.IdealRange) sidegrade = false;
            Check(sidegrade, "perks: archetypes are spacing sidegrades with sane ranges");

            // Levels must stay small enough that a perk still matters. The whole
            // level-1..5 damage climb should not exceed one attunement bonus.
            float fullClimb = HeroCatalog.DamageScale(HeroCatalog.MaxLevel) / HeroCatalog.DamageScale(1);
            Check(fullClimb <= HeroPerks.AttunementMultiplier + 0.05f,
                $"perks: the whole level climb (x{fullClimb:F2}) is worth about one perk " +
                $"(x{HeroPerks.AttunementMultiplier:F2}) - identity survives levelling");
        }

        private void TestCostCurve()
        {
            bool risingLevels = true;
            for (int lv = 1; lv < HeroCatalog.MaxLevel - 1; lv++)
                if (HeroCatalog.LevelUpCost(lv + 1) <= HeroCatalog.LevelUpCost(lv)) risingLevels = false;
            Check(risingLevels,
                $"costs: each level is dearer than the last ({HeroCatalog.LevelUpCost(1)} / " +
                $"{HeroCatalog.LevelUpCost(2)} / {HeroCatalog.LevelUpCost(3)} / {HeroCatalog.LevelUpCost(4)} g)");

            bool risingHires = true;
            for (int n = 1; n < HeroCatalog.MaxRoster - 1; n++)
                if (HeroCatalog.HireCost(n + 1) <= HeroCatalog.HireCost(n)) risingHires = false;
            Check(risingHires,
                $"costs: each hire is dearer than the last ({HeroCatalog.HireCost(1)} / " +
                $"{HeroCatalog.HireCost(2)} / {HeroCatalog.HireCost(3)} / {HeroCatalog.HireCost(4)} g)");

            // A day-1 player can hire nobody: no win banked yet.
            var day1 = RunState.NewGame(0);
            Check(!HeroCatalog.CanHire(day1), "costs: hiring is locked until a road is brought home");

            // Capacity is no longer for sale: a hire is all it takes to send another
            // fighter out, so the upgrade shelf must not sell party slots.
            bool sellsSlots = false;
            foreach (var up in UpgradeCatalog.All)
                if (up.Id == UpgradeCatalog.SecondPack || up.Id == UpgradeCatalog.ThirdPack) sellsSlots = true;
            Check(!sellsSlots && day1.DeployCap == RunState.MaxParty,
                $"costs: no upgrade sells party slots; a hire alone fills one (cap {day1.DeployCap})");
        }
    }
}
