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
            TestSharedPurse();
            TestStarRules();
            TestSaveRoundTrip();
            TestMigrationClamps();
            TestRosterRoundTrip();
            TestRosterMigration();
            TestInjuries();
            TestPerkSymmetry();
            TestCostCurve();
            TestCurveIsAffordable();
            TestGuardianRoads();
            TestRoadCounters();
            TestReplayIsTheSameFight();
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
            var win = new ExpeditionReport { won = true, bossDefeated = true, bossId = "woodwose", bossTier = 0 };
            var noBoss = new ExpeditionReport { won = true, bossDefeated = false };
            var returned = new ExpeditionReport { won = true, bossDefeated = true, bossId = "woodwose", bossTier = 1 };
            var broken = new ExpeditionReport { won = true, bossDefeated = true, bossId = "woodwose", bossTier = 2 };
            var matriarch = new ExpeditionReport { won = true, bossDefeated = true, bossId = "matriarch" };

            // Keyed on who fell, not on the road: a Woodwose first felled in the Caverns earns its scene too.
            var other = RunState.NewGame(0);
            Story.StoryDirector.OnDayResolved(other, 2, returned);
            var first = Story.StoryDirector.Due(other);
            Check(first.Count == 1 && first[0].Id == "woodwose", "story: the Woodwose scene follows the Woodwose, on whichever road it falls");

            Story.StoryDirector.OnDayResolved(s, 0, noBoss);
            Check(Story.StoryDirector.Due(s).Count == 0, "story: a boss-less win earns no scene");

            Story.StoryDirector.OnDayResolved(s, 0, win);
            var due = Story.StoryDirector.Due(s);
            Check(due.Count == 1 && due[0].Id == "woodwose", "story: the Woodwose's defeat earns its scene");
            Story.StoryDirector.Finished(s, due[0]);
            Story.StoryDirector.OnDayResolved(s, 0, win);
            Check(Story.StoryDirector.Due(s).Count == 0, "story: the Woodwose scene plays once, not on every replay");
            Story.StoryDirector.OnDayResolved(s, 2, returned);
            Check(Story.StoryDirector.Due(s).Count == 0, "story: the Woodwose back in the Caverns earns no new scene");

            Story.StoryDirector.OnDayResolved(s, 3, broken);
            Story.DiaryManager.EvaluateAfterExpedition(s, 3, broken);
            due = Story.StoryDirector.Due(s);
            Check(due.Count == 1 && due[0].Id == "woodwose_return" && s.HasDiary("diary_woodwose_broken"),
                "story: the broken Woodwose's fall in the Swamp earns its scene and its diary page");
            Story.StoryDirector.Finished(s, due[0]);
            Story.StoryDirector.OnDayResolved(s, 3, broken);
            Check(Story.StoryDirector.Due(s).Count == 0, "story: the broken Woodwose's scene plays once");

            // The doorway shows the run's own roster: the Rookie alone, then whoever was hired.
            var doorway = new System.Collections.Generic.List<string>();
            foreach (var a in Story.StoryScript.RosterHeroes(s, false)) doorway.Add(a.Id);
            Check(doorway.Count == 1 && doorway[0] == "hero_rookie", $"story: a run that hired nobody ends with the Rookie alone at the door ({string.Join(",", doorway)})");
            s.roster.Add(HeroCatalog.NewHire("Otho Vance", 1, ElementType.Water, HeroPerks.DefaultArchetype, "merchant"));
            doorway.Clear();
            foreach (var a in Story.StoryScript.RosterHeroes(s, false)) doorway.Add(a.Id);
            Check(doorway.Count == 2 && doorway.Contains("hero_merchant") && !doorway.Contains("hero_herbalist"),
                $"story: the doorway shows exactly who was hired ({string.Join(",", doorway)})");

            Story.StoryDirector.OnDayResolved(s, BiomeLibrary.Count - 1, matriarch);
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
                HeroCatalog.NewHire("Otho Vance", 0, ElementType.Fire, "bulwark", "merchant"),
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

        /// <summary>
        /// The day's contracts are one purse: the fighters' payouts averaged, times a
        /// scale that grows with the party but slower than a fighter a share. One
        /// fighter is paid exactly what their contract pays.
        /// </summary>
        private void TestSharedPurse()
        {
            Check(Economy.SharedPurse(new int[0]) == 0, "purse: no contracts, no pay");
            Check(Economy.SharedPurse(new[] { 120 }) == 120, "purse: one fighter is paid their contract in full");
            int two = Economy.SharedPurse(new[] { 100, 100 }), three = Economy.SharedPurse(new[] { 100, 100, 100 });
            Check(two == Mathf.RoundToInt(100 * Economy.PurseScale(2)) && three == Mathf.RoundToInt(100 * Economy.PurseScale(3)),
                $"purse: equal pays make pay x scale ({two}, {three})");
            Check(100 < two && two < three && two < 200 && three < 300,
                $"purse: a bigger party earns more, but not a full share more per fighter ({two}, {three})");
            Check(Economy.SharedPurse(new[] { 100, 50 }) < two, "purse: a refused (half-paid) contract lowers the purse");
        }

        /// <summary>
        /// The later roads are tuned to need hired fighters and the road upgrades (the
        /// harness's difficulty curve measures that). This checks the money side, with
        /// the day's contracts paid as one purse (<see cref="Economy.SharedPurse"/>) and
        /// only the least gold each monster can drop:
        /// <list type="bullet">
        /// <item>a good player (Great flasks, the best-paid job, average loot) meets every
        /// road's needs with at most two replay days, and owns everything the shop sells
        /// near the end of road 4: at most two Swamp replays after clearing it, before
        /// the last road;</item>
        /// <item>a weak player (Okay flasks, the safe jobs) still meets every road's
        /// needs, with at most four replay days before any road: slow, never stuck;</item>
        /// <item>a saver first affords a bench machine on the evening of day 3, and a
        /// machine for a later mechanic costs more.</item>
        /// </list>
        /// The gold by evening is logged for both, for the balance chart.
        /// </summary>
        /// <summary>How much of the morning a player has left, by how many bench machines they own.</summary>
        private delegate float MorningLeft(int benchesOwned);

        /// <summary>
        /// A guardian belongs to its road, not to a day: the Woods never have one, the
        /// Woodwose is first met on the second road as it always was (tier 0), comes back
        /// frozen in the Caverns and broken in the Swamp, and the Matriarch holds the Peak.
        /// The road is all there is to it, so a replay on any day meets the same thing.
        /// </summary>
        /// <summary>
        /// The Counter's flask for each road, the guardian counted by its health: the Woods want
        /// Fire; the Peaks and the Swamp, where the Woodwose (Nature) is most of the road, Fire;
        /// the Caverns, still mostly ice, Nature; the Peak Fire.
        /// </summary>
        private void TestRoadCounters()
        {
            ElementType[] want = { ElementType.Fire, ElementType.Fire, ElementType.Nature, ElementType.Fire, ElementType.Fire };
            for (int b = 0; b < BiomeLibrary.Count; b++)
            {
                ElementType got = ContractBoard.Counter(ContractBoard.Dominant(ContractBoard.ThreatCounts(BiomeLibrary.Get(b))));
                Check(got == want[b], $"counter: {BiomeLibrary.Name(b)} wants a {want[b]} flask ({got})");
            }
        }

        /// <summary>
        /// A road is as hard on a replay as the first time: the same order arms the same bomb on
        /// day 1 and day 5, and the spawner's seed is the road's alone.
        /// </summary>
        private void TestReplayIsTheSameFight()
        {
            // (The same flask on day 1 and day 5 is checked by the harness, which has a live run.)
            Check(ExpeditionWorld.SpawnSeed(2) == ExpeditionWorld.SpawnSeed(2) && ExpeditionWorld.SpawnSeed(1) != ExpeditionWorld.SpawnSeed(2),
                "replay: a road's monsters come the same way every time (seeded by the road alone)");
            Check(BiomeLibrary.FightersNeeded[3] == 2 && BiomeLibrary.FightersNeeded[4] == 3 && BiomeLibrary.RoadTierNeeded[2] == 1,
                $"curve: fighters {{{string.Join(",", BiomeLibrary.FightersNeeded)}}}, road tier {{{string.Join(",", BiomeLibrary.RoadTierNeeded)}}}");
        }

        private void TestGuardianRoads()
        {
            bool[] has = { false, true, true, true, true };
            string[] who = { "", "woodwose", "woodwose", "woodwose", "matriarch" };
            int[] tier = { -1, 0, 1, 2, 0 };
            for (int b = 0; b < BiomeLibrary.Count; b++)
            {
                BiomeData d = BiomeLibrary.Get(b);
                Check(d.HasBoss == has[b], $"guardians: {BiomeLibrary.Name(b)} {(has[b] ? "ends on a guardian" : "has no guardian")}");
                if (!d.HasBoss) continue;
                Check(d.Boss.StoryId == who[b] && d.Boss.Tier == tier[b],
                    $"guardians: {BiomeLibrary.Name(b)} meets {who[b]} tier {tier[b]} ({d.Boss.StoryId} tier {d.Boss.Tier})");
            }
            Check(BiomeLibrary.Get(1).Waves.Count == 3, $"guardians: the Woodwose takes the Cinder Peaks' last wave ({BiomeLibrary.Get(1).Waves.Count} waves)");
            Check(BiomeLibrary.Get(0).Waves.Count == 4, $"guardians: the Woods keep their four waves ({BiomeLibrary.Get(0).Waves.Count})");
            Check(BiomeLibrary.Get(1).GuardianBountyMin == 12 && BiomeLibrary.Get(1).GuardianBountyMax == 24 && BiomeLibrary.Get(0).GuardianBountyMax == 0,
                $"guardians: the Cinder Peaks pay back the lost wave's loot when the Woodwose falls ({BiomeLibrary.Get(1).GuardianBountyMin}-{BiomeLibrary.Get(1).GuardianBountyMax} g)");
        }

        private void TestCurveIsAffordable()
        {
            var loot = new System.Text.StringBuilder();
            for (int b = 0; b < BiomeLibrary.Count; b++) loot.Append($" b{b}:{Loot(b, false)}/{Loot(b, true)}");
            Debug.Log($"economy: loot least/average by road -{loot}");

            // The quick-hands bonus is proven at both ends, so no real play speed can break a rule:
            // never quick (0) and always the full +25 % (the whole morning left).
            foreach (var (label, left) in new (string, MorningLeft)[]
                     { ("no bonus", _ => 0f), ("+5 %", _ => 0.10f), ("+10 %", _ => 0.20f), ("+15 %", _ => 0.35f), ("full bonus", _ => 1f) })
            {
                EconomyRun good = RunEconomy(PotionGrade.Great, buyEverything: true, averageLoot: true, left);
                EconomyRun weak = RunEconomy(PotionGrade.Okay, buyEverything: false, averageLoot: false, left);
                Debug.Log($"economy ({label}): good player gold by evening -{good.Gold} | {good.Trace}");
                Debug.Log($"economy ({label}): weak player gold by evening -{weak.Gold} | {weak.Trace}");

                Check(good.WorstReplays <= 2, $"economy ({label}): a good player meets every road's needs with at most 2 replays (worst {good.WorstReplays}) -{good.Trace}");
                // Since the 2026-11-10 curve the Swamp asks only two fighters and the first road
                // kit, so a good player reaches it about two days sooner than the shop can be
                // bought out (six machines, about 1,800 g). The machines cannot be cheaper (the
                // saver rule below pins the cheapest), so the shop is owned before the last road:
                // bought out during the Swamp replays, at most five, before the Peak is taken.
                Check(good.OwnAllDay > 0 && good.OwnAllDay < good.PeakDay && good.OwnAllDay - good.Road4Day <= 5,
                    $"economy ({label}): a good player owns everything before the last road, within five Swamp replays (all on day {good.OwnAllDay}, road 4 cleared day {good.Road4Day}, the Peak on day {good.PeakDay})");
                Check(weak.WorstReplays <= 4, $"economy ({label}): a weak player is slow but never stuck, at most 4 replays before a road (worst {weak.WorstReplays}) -{weak.Trace}");
            }
            // A likely player, for the chart: the good one quicker with every machine, the weak one barely ahead of the clock.
            EconomyRun likelyGood = RunEconomy(PotionGrade.Great, true, true, owned => 0.15f + 0.06f * owned);
            EconomyRun likelyWeak = RunEconomy(PotionGrade.Okay, false, false, _ => 0.05f);
            Debug.Log($"economy (likely): good player gold by evening -{likelyGood.Gold} | {likelyGood.Trace}");
            Debug.Log($"economy (likely): weak player gold by evening -{likelyWeak.Gold} | {likelyWeak.Trace}");

            // A player who puts every coin toward a machine (one fighter, a new road each
            // day) first affords the cheapest on the evening of day 3, so it works on day 4.
            int cheapestMachine = int.MaxValue;
            foreach (var up in UpgradeCatalog.All)
                if (up.IsBench && up.Id != UpgradeCatalog.DryingRack) cheapestMachine = Mathf.Min(cheapestMachine, up.Cost);
            foreach (float left in new[] { 0f, 0.10f, 0.20f, 0.35f, 1f })
            {
                int saved = 0, affordOn = -1;
                var pays = new System.Text.StringBuilder();
                for (int d = 1; d <= 6 && affordOn < 0; d++)
                {
                    int pay = DayPay(d, Mathf.Min(d - 1, BiomeLibrary.Count - 1), 1, PotionGrade.Great, replay: false, averageLoot: false, left);
                    saved += pay;
                    pays.Append($" d{d}+{pay}={saved}");
                    if (saved >= cheapestMachine) affordOn = d;
                }
                Check(affordOn == 3, $"economy (+{Economy.SpeedBonus(left):P0}): saving for it, the first bench machine ({cheapestMachine} g) is affordable on the evening of day 3, for day 4 (evening {affordOn}:{pays})");
            }

            // Later mechanics cost more: the rack, then the day-one benches, then the malting ones.
            string[] byArrival = { UpgradeCatalog.DryingRack, UpgradeCatalog.GlassFunnel, UpgradeCatalog.BrassMortar,
                                   UpgradeCatalog.ClockworkStirrer, UpgradeCatalog.DraughtKiln, UpgradeCatalog.SteepingVat };
            bool rising = true;
            for (int i = 1; i < byArrival.Length; i++)
                rising &= UpgradeCatalog.Find(byArrival[i]).Value.Cost > UpgradeCatalog.Find(byArrival[i - 1]).Value.Cost;
            Check(rising, "economy: a bench machine for a later mechanic costs more");
        }

        private sealed class EconomyRun
        {
            public int WorstReplays, OwnAllDay = -1, Road4Day = -1, PeakDay = -1;
            public string Trace = "", Gold = "";
        }

        /// <summary>The waves' gold: each monster's least drop, or its average.</summary>
        private static int Loot(int biome, bool average)
        {
            float g = 0f;
            foreach (var w in BiomeLibrary.Get(biome).Waves)
                g += w.count * (average ? (w.monster.GoldMin + w.monster.GoldMax) * 0.5f : w.monster.GoldMin);
            BiomeData d = BiomeLibrary.Get(biome);
            return Mathf.RoundToInt(g + (average ? (d.GuardianBountyMin + d.GuardianBountyMax) * 0.5f : d.GuardianBountyMin));
        }

        /// <summary>One day's gold: the party's purse for the best-paid job each fighter's flask meets, and the loot.</summary>
        private static int DayPay(int day, int biome, int party, PotionGrade grade, bool replay, bool averageLoot, float morningLeft = 0f)
        {
            var fees = new System.Collections.Generic.List<int>();
            for (int i = 0; i < party; i++)
            {
                ContractRecord best = null;
                foreach (ContractRecord c in ContractBoard.Offers(day, biome, null))
                    if (c.Meets(grade) && (best == null || c.fee + c.bonus > best.fee + best.bonus)) best = c;
                if (best != null) { best.accepted = true; fees.Add(Economy.ContractPayout(grade, best, replay).paid); }
            }
            int purse = Economy.SharedPurse(fees);
            return purse + Economy.SpeedBonusGold(purse, morningLeft) + Loot(biome, averageLoot);
        }

        /// <summary>
        /// Play the roads in order: before each, hire and buy the road kit it needs,
        /// replaying the last road (half pay) until that is affordable. With
        /// buyEverything, whatever the next road's needs leave over goes on the rest of
        /// the shop, cheapest first: bench machines, more fighters, the second road tier;
        /// and before the last road it replays the Swamp until the shop is bought out.
        /// </summary>
        private static EconomyRun RunEconomy(PotionGrade grade, bool buyEverything, bool averageLoot, MorningLeft left)
        {
            var run = new EconomyRun();
            var need = new (int party, int kit)[BiomeLibrary.Count];
            for (int b = 0; b < BiomeLibrary.Count; b++)
                need[b] = (BiomeLibrary.FightersNeeded[b], UpgradeCatalog.RoadTierCost(BiomeLibrary.RoadTierNeeded[b]));
            int fullKit = UpgradeCatalog.RoadTierCost(2);
            var benches = new System.Collections.Generic.List<UpgradeDefinition>();
            foreach (var up in UpgradeCatalog.All) if (up.IsBench) benches.Add(up);
            var owned = new System.Collections.Generic.HashSet<string>();

            int gold = 0, dayN = 1, fighters = 1, kit = 0;
            var trace = new System.Text.StringBuilder();
            var goldLog = new System.Text.StringBuilder();

            bool OwnsAll() => owned.Count == benches.Count && kit >= fullKit && fighters >= RunState.MaxParty;

            void Shop(int nextRoad)
            {
                var n = need[Mathf.Min(nextRoad, BiomeLibrary.Count - 1)];
                while (fighters < n.party && gold >= HeroCatalog.HireCost(fighters)) { gold -= HeroCatalog.HireCost(fighters); fighters++; }
                if (fighters >= n.party && kit < n.kit && gold >= n.kit - kit) { gold -= n.kit - kit; kit = n.kit; }
                if (!buyEverything || fighters < n.party || kit < n.kit) return;   // the road comes first
                while (true)
                {
                    // The cheapest thing still on the shelf.
                    int cost = int.MaxValue; string what = null;
                    foreach (var up in benches) if (!owned.Contains(up.Id) && up.Cost < cost) { cost = up.Cost; what = up.Id; }
                    if (fighters < RunState.MaxParty && HeroCatalog.HireCost(fighters) < cost) { cost = HeroCatalog.HireCost(fighters); what = "hire"; }
                    if (kit < fullKit && fullKit - kit < cost) { cost = fullKit - kit; what = "kit"; }
                    if (what == null || gold < cost) return;
                    gold -= cost;
                    if (what == "hire") fighters++;
                    else if (what == "kit") kit = fullKit;
                    else owned.Add(what);
                }
            }

            void Evening(int day, int nextRoad)
            {
                Shop(nextRoad);
                goldLog.Append($" d{day}:{gold}");
                if (run.OwnAllDay < 0 && OwnsAll()) run.OwnAllDay = day;
            }

            for (int biome = 0; biome < BiomeLibrary.Count; biome++)
            {
                int replays = 0;
                // The whole shop before the last road: replay the Swamp until it is bought.
                for (int r = 0; buyEverything && biome == BiomeLibrary.Count - 1 && run.OwnAllDay < 0 && r < 6; r++)
                {
                    gold += DayPay(dayN, biome - 1, fighters, grade, replay: true, averageLoot, left(owned.Count));
                    Evening(dayN++, biome);
                }
                Shop(biome);
                while ((fighters < need[biome].party || kit < need[biome].kit) && replays < 8)
                {
                    gold += DayPay(dayN, Mathf.Max(0, biome - 1), fighters, grade, replay: true, averageLoot, left(owned.Count));
                    Evening(dayN++, biome);
                    replays++;
                }
                run.WorstReplays = Mathf.Max(run.WorstReplays, replays);
                trace.Append($" b{biome}:day{dayN} x{fighters} kit{kit}g r{replays}");
                gold += DayPay(dayN, biome, fighters, grade, replay: false, averageLoot, left(owned.Count));
                if (biome == 3) run.Road4Day = dayN;
                if (biome == BiomeLibrary.Count - 1) run.PeakDay = dayN;
                Evening(dayN++, biome + 1);
            }
            run.Trace = trace.ToString();
            run.Gold = goldLog.ToString();
            return run;
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
