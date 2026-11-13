using System.Collections.Generic;
using UnityEngine;
using AlchemistsArsenal.Combat;
using AlchemistsArsenal.Data;
using AlchemistsArsenal.Vfx;

namespace AlchemistsArsenal.DebugTools
{
    /// <summary>
    /// The rules behind the boss's attack motions and the tips on a lost road. Pure
    /// functions only; the live fight is checked and photographed by the playtest.
    /// </summary>
    public class CombatBossSimulationTest : MonoBehaviour, ISimulationSuite
    {
        public bool Done { get; private set; }

        private int _pass, _fail;

        private void Start()
        {
            Debug.Log("<color=cyan><b>=== COMBAT AND BOSS SIMULATION ===</b></color>");
            TestMotions();
            TestDefeatTips();
            TestDefeatMusic();
            Done = true;
            Debug.Log($"<color=cyan><b>=== DONE — {_pass} pass, {_fail} fail ===</b></color>");
        }

        private void Check(bool ok, string what)
        {
            if (ok) { _pass++; Debug.Log($"<color=green>✔</color> {what}"); }
            else { _fail++; Debug.LogError($"❌ {what}"); }
        }

        private void TestMotions()
        {
            Check(BossVisual.MotionOf(BossAttackShape.Strike, 3.8f) == BossVisual.Motion.Strike, "motion: a strike lunges");
            Check(BossVisual.MotionOf(BossAttackShape.Volley, 1.1f) == BossVisual.Motion.Volley, "motion: a volley is thrown");
            Check(BossVisual.MotionOf(BossAttackShape.Shockwave, 4.4f) == BossVisual.Motion.Slam, "motion: Root Slam slams the ground");
            Check(BossVisual.MotionOf(BossAttackShape.Shockwave, 5.0f) == BossVisual.Motion.Slam, "motion: Sundering Ring slams the ground");
            Check(BossVisual.MotionOf(BossAttackShape.Shockwave, 3.0f) == BossVisual.Motion.Spin, "motion: Briar Pulse is a spin");
            Check(BossVisual.MotionOf(BossAttackShape.Shockwave, 3.4f) == BossVisual.Motion.Spin, "motion: Ward Pulse is a spin");
        }

        private void TestDefeatMusic()
        {
            Check(Audio.AudioManager.AfternoonTrack(3, ExpeditionPhase.Lost, false, true, resultShown: false) != "defeat",
                "music: while the party is still walking off, the road music carries on (no defeat cue yet)");
            Check(Audio.AudioManager.AfternoonTrack(3, ExpeditionPhase.Lost, defeatHeard: false, hasDefeat: true) == "defeat",
                "music: a lost road turns to the defeat track at once");
            Check(Audio.AudioManager.AfternoonTrack(0, ExpeditionPhase.BossFight, false, true) == "boss_guardian",
                "music: the first guardian keeps its own track");
            Check(Audio.AudioManager.AfternoonTrack(4, ExpeditionPhase.BossFight, false, true) == "boss_final",
                "music: the last guardian keeps its own track");
            Check(Audio.AudioManager.AfternoonTrack(1, ExpeditionPhase.Waves, false, true) != "defeat",
                "music: no defeat track while the road is still being fought");
            Check(Audio.AudioManager.AfternoonTrack(1, ExpeditionPhase.Won, false, true) != "defeat",
                "music: no defeat track on a win");
            Check(Audio.AudioManager.AfternoonTrack(1, ExpeditionPhase.Lost, defeatHeard: true, hasDefeat: true) != "defeat",
                "music: the defeat cue plays once, not again");
            Check(Audio.AudioManager.AfternoonTrack(1, ExpeditionPhase.Lost, false, hasDefeat: false) != "defeat",
                "music: with no defeat file the road music carries on");
        }

        private static DefeatTips.Facts Facts(PotionGrade grade = PotionGrade.Great, int fighters = 3, int needed = 3,
            int tier = 2, int tierNeeded = 2, ElementType flask = ElementType.Fire, ElementType counter = ElementType.Fire,
            int withdrew = 0, bool vials = true, bool bandolier = true, int lowestLevel = HeroCatalog.MaxLevel)
            => new DefeatTips.Facts(grade, fighters, needed, tier, tierNeeded, flask, counter, withdrew, vials, bandolier, lowestLevel);

        private void TestDefeatTips()
        {
            // Solo and bare on the Swamp: the two things that decide that road come first.
            var solo = DefeatTips.For(Facts(fighters: 1, needed: 3, tier: 0, tierNeeded: 2));
            Check(solo.Count >= 2 && solo[0] == DefeatTips.Tip.Hire && solo[1] == DefeatTips.Tip.RoadUpgrades,
                $"tips: solo and bare on the Swamp says hire, then road upgrades ({string.Join(",", solo)})");

            var weak = DefeatTips.For(Facts(grade: PotionGrade.Okay));
            Check(weak.Contains(DefeatTips.Tip.Flasks) && !weak.Contains(DefeatTips.Tip.Hire) && !weak.Contains(DefeatTips.Tip.RoadUpgrades),
                $"tips: a full kit party with Okay flasks is told about the flasks, not hiring or upgrades ({string.Join(",", weak)})");

            var wrong = DefeatTips.For(Facts(flask: ElementType.Water, counter: ElementType.Fire));
            Check(wrong.Count > 0 && wrong[0] == DefeatTips.Tip.Element, $"tips: the wrong element for the road is named ({string.Join(",", wrong)})");

            var dry = DefeatTips.For(Facts(withdrew: 1, vials: false, bandolier: false));
            Check(dry.Contains(DefeatTips.Tip.Ammo), $"tips: a fighter who ran dry without Spare Vials is told about ammo ({string.Join(",", dry)})");
            Check(DefeatTips.Text(DefeatTips.Tip.Ammo, Facts(withdrew: 1, vials: false)).Contains("Spare Vials"),
                "tips: the ammo tip names Spare Vials first");
            Check(DefeatTips.Text(DefeatTips.Tip.Ammo, Facts(withdrew: 1, vials: true, bandolier: false)).Contains("Bandolier"),
                "tips: then the Bandolier");

            bool neverAmmo = true;
            foreach (var g in new[] { PotionGrade.Perfect, PotionGrade.Great, PotionGrade.Okay, PotionGrade.Poor })
                if (DefeatTips.For(Facts(grade: g, vials: false, bandolier: false, fighters: 1, tier: 0)).Contains(DefeatTips.Tip.Ammo)) neverAmmo = false;
            Check(neverAmmo, "tips: no ammo tip when nobody ran out of flasks");

            var met = DefeatTips.For(Facts(grade: PotionGrade.Perfect));
            Check(met.Count >= 1 && !met.Contains(DefeatTips.Tip.Hire) && !met.Contains(DefeatTips.Tip.RoadUpgrades),
                $"tips: a party that meets every need still gets a tip, and nothing it already has ({string.Join(",", met)})");

            var everything = DefeatTips.For(Facts(grade: PotionGrade.Poor, fighters: 1, tier: 0, flask: ElementType.Water,
                withdrew: 1, vials: false, bandolier: false, lowestLevel: 1));
            Check(everything.Count == 3, $"tips: never more than three ({everything.Count})");
            Check(new HashSet<DefeatTips.Tip>(everything).Count == everything.Count, "tips: no tip twice");

            var two = DefeatTips.For(Facts(fighters: 1, needed: 2, lowestLevel: 1));
            Check(two.Count >= 2, $"tips: one real reason still comes with a second useful tip ({string.Join(",", two)})");
            foreach (DefeatTips.Tip t in System.Enum.GetValues(typeof(DefeatTips.Tip)))
                Check(!string.IsNullOrEmpty(DefeatTips.Text(t, Facts(withdrew: 1, vials: false))), $"tips: {t} has words");
        }
    }
}
