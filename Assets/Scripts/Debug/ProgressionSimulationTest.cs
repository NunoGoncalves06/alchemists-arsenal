using System.Collections.Generic;
using UnityEngine;
using AlchemistsArsenal.Combat;
using AlchemistsArsenal.Core;
using AlchemistsArsenal.Data;

namespace AlchemistsArsenal.DebugTools
{
    /// <summary>
    /// The rules behind progression and pay: a hero's stats by level, the day's distinct
    /// recipes, and the speed bonus. Pure functions only; the live day is checked by the playtest.
    /// </summary>
    public class ProgressionSimulationTest : MonoBehaviour, ISimulationSuite
    {
        public bool Done { get; private set; }

        private int _pass, _fail;

        private void Start()
        {
            Debug.Log("<color=cyan><b>=== PROGRESSION SIMULATION ===</b></color>");
            TestHeroStats();
            TestRecipes();
            TestSpeedBonus();
            Done = true;
            Debug.Log($"<color=cyan><b>=== DONE — {_pass} pass, {_fail} fail ===</b></color>");
        }

        private void Check(bool ok, string what)
        {
            if (ok) { _pass++; Debug.Log($"<color=green>✔</color> {what}"); }
            else { _fail++; Debug.LogError($"❌ {what}"); }
        }

        private void TestHeroStats()
        {
            var bare = RunState.NewGame(0);
            bare.ownedUpgrades.Clear();
            HeroStats l1 = HeroStats.For(1, bare), l2 = HeroStats.For(2, bare), l5 = HeroStats.For(5, bare);
            Check(l1.Hp == 150 && l2.Hp == 162 && l5.Hp == 198, $"stats: HP by level 150/162/198 ({l1.Hp}/{l2.Hp}/{l5.Hp})");
            Check(l1.Damage == 22 && l2.Damage == 23 && l5.Damage == 27, $"stats: damage by level 22/23/27 ({l1.Damage}/{l2.Damage}/{l5.Damage})");
            Check(l1.FlaskBonus == 0 && l2.FlaskBonus == 2 && l5.FlaskBonus == 8, $"stats: extra flasks by level 0/2/8 ({l1.FlaskBonus}/{l2.FlaskBonus}/{l5.FlaskBonus})");
            Check(Mathf.Approximately(l2.DamageScale, HeroCatalog.DamageScale(2)), "stats: the damage scale is the one the fight is given");

            var kit = RunState.NewGame(0);
            foreach (var up in UpgradeCatalog.All) if (!up.IsBench) kit.ownedUpgrades.Add(up.Id);
            HeroStats k3 = HeroStats.For(3, kit);
            Check(k3.Hp == Mathf.RoundToInt(230 * HeroCatalog.HpScale(3)), $"stats: with the road kit, HP is the upgraded base times the level ({k3.Hp})");
            Check(k3.Damage == Mathf.RoundToInt(22 * 1.25f * 1.25f * HeroCatalog.DamageScale(3)), $"stats: with the road kit, damage too ({k3.Damage})");
            Check(k3.FlaskBonus == 13 + HeroCatalog.AmmoBonus(3), $"stats: Spare Vials and the Bandolier add to the level's flasks ({k3.FlaskBonus})");

            bool rising = true;
            for (int lv = 1; lv < HeroCatalog.MaxLevel; lv++)
            {
                HeroStats a = HeroStats.For(lv, bare), b = HeroStats.For(lv + 1, bare);
                rising &= b.Hp > a.Hp && b.Damage >= a.Damage && b.FlaskBonus > a.FlaskBonus;
            }
            Check(rising, "stats: every level raises HP and flasks, and never lowers damage");
        }

        private void TestRecipes()
        {
            bool distinct = true, sameResult = true, noCounter = true;
            var bad = new List<string>();
            foreach (ElementType e in new[] { ElementType.Fire, ElementType.Water, ElementType.Nature, ElementType.Poison, ElementType.Arcane })
                for (int tier = 0; tier < 4; tier++)
                {
                    var v = new PotionRecipe[3];
                    for (int i = 0; i < 3; i++) v[i] = RecipeBook.For(e, tier, i);
                    for (int i = 0; i < 3; i++)
                    {
                        sameResult &= v[i].Result == e && v[i].Variant == i;
                        foreach (ElementType s in v[i].Steps) if (!Pantry(e).Contains(s)) { noCounter = false; bad.Add($"{v[i].Name} uses {s}"); }
                        for (int j = i + 1; j < 3; j++)
                            if (string.Join(",", v[i].Steps) == string.Join(",", v[j].Steps) || v[i].Name == v[j].Name)
                            { distinct = false; bad.Add($"{e} t{tier}: v{i} = v{j}"); }
                    }
                }
            Check(distinct, $"recipes: for every element and tier the three variants differ in leaves and name {string.Join("; ", bad)}");
            Check(sameResult, "recipes: every variant still brews the same element");
            Check(noCounter, "recipes: a variant only uses leaves the element's own recipes already use");
            Check(RecipeBook.For(ElementType.Fire, 1, 0).Name == RecipeBook.For(ElementType.Fire, 1).Name,
                "recipes: variant 0 is today's recipe, unchanged");

            Check(RecipeBook.VariantFor(ElementType.Fire, new[] { ElementType.Fire, ElementType.Fire }) == 2, "recipes: the third Fire order of the day is variant 2");
            Check(RecipeBook.VariantFor(ElementType.Water, new[] { ElementType.Fire, ElementType.Fire }) == 0, "recipes: another element starts at variant 0");

            // A day with three fighters on one road: three recipes, three names.
            var today = new List<ElementType>();
            var names = new HashSet<string>();
            var steps = new HashSet<string>();
            for (int f = 0; f < 3; f++)
            {
                PotionRecipe r = RecipeBook.For(ElementType.Fire, 1, RecipeBook.VariantFor(ElementType.Fire, today));
                today.Add(ElementType.Fire);
                names.Add(r.Name);
                steps.Add(string.Join(",", r.Steps));
            }
            Check(names.Count == 3 && steps.Count == 3, $"recipes: three fighters on one road get three recipes and three names ({string.Join(" / ", names)})");
        }

        /// <summary>Every leaf any tier of <paramref name="e"/>'s recipes uses.</summary>
        private static HashSet<ElementType> Pantry(ElementType e)
        {
            var leaves = new HashSet<ElementType>();
            for (int tier = 0; tier < 4; tier++) foreach (ElementType s in RecipeBook.For(e, tier).Steps) leaves.Add(s);
            return leaves;
        }

        private void TestSpeedBonus()
        {
            Check(Economy.SpeedBonus(0f) == 0f && Economy.SpeedBonus(0.099f) == 0f, "speed: under a tenth of the morning left, no bonus");
            Check(Mathf.Approximately(Economy.SpeedBonus(0.10f), 0.05f), "speed: a tenth left is +5 %");
            Check(Mathf.Approximately(Economy.SpeedBonus(0.20f), 0.10f), "speed: a fifth left is +10 %");
            Check(Mathf.Approximately(Economy.SpeedBonus(0.35f), 0.15f), "speed: 35 % left is +15 %");
            Check(Mathf.Approximately(Economy.SpeedBonus(0.50f), 0.25f) && Mathf.Approximately(Economy.SpeedBonus(1f), 0.25f), "speed: half the morning left or more is the full +25 %");
            bool bounded = true, rising = true;
            float last = -1f;
            for (int i = -2; i <= 102; i++)
            {
                float b = Economy.SpeedBonus(i / 100f);
                bounded &= b >= 0f && b <= 0.25f;
                rising &= b >= last;
                last = b;
            }
            Check(bounded, "speed: the bonus is never a penalty and never above +25 %");
            Check(rising, "speed: more time left never pays less");
            Check(Economy.SpeedBonusGold(0, 1f) == 0, "speed: no pay, nothing to add to");
            Check(Economy.SpeedBonusGold(200, 0.4f) == 30, $"speed: 200 g at 40 % left is +30 g ({Economy.SpeedBonusGold(200, 0.4f)})");
        }
    }
}
