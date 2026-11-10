using System.Collections.Generic;
using UnityEngine;
using AlchemistsArsenal.Combat;
using AlchemistsArsenal.Data;
using AlchemistsArsenal.Systems;

namespace AlchemistsArsenal.Core
{
    /// <summary>
    /// Turns the morning's <see cref="ActiveOrder"/> — finished or not — into the
    /// one <see cref="AdventurerLoadout"/> the afternoon adventurer carries
    /// (DESIGN.md §7.6.4). Runs once, at <c>BeginAfternoon()</c>.
    ///
    /// There is no "fallback bomb" and no branch: a never-brewed order is just a
    /// score-25 Poor potion, and the grade math downstream
    /// (<see cref="UtilityAI_CombatController"/> passes the live quality,
    /// <see cref="BombProjectile2D"/> applies the multiplier) handles it.
    /// The runtime <see cref="AdventurerLoadout"/> and <see cref="BombData"/> are
    /// fresh instances — no ScriptableObject asset is ever written.
    /// </summary>
    public static class LoadoutBuilder
    {
        /// <summary>Ammo the bomb ships with, by grade — a cleaner brew fills more flasks.</summary>
        public static int AmmoFor(PotionGrade grade) => grade switch
        {
            PotionGrade.Perfect => 32,
            PotionGrade.Great => 28,
            PotionGrade.Okay => 24,
            _ => 20,
        };

        /// <summary>
        /// One loadout per hero going out. Each gets its <b>own</b> instance even
        /// when they are all carrying the same brew: the AI keeps a private ammo
        /// dictionary per controller, so handing one shared loadout to N heroes
        /// silently multiplied the party's total flasks by N.
        ///
        /// Heroes past the end of <paramref name="orders"/> carry the last brew
        /// available. Once the morning issues one order per slot that indexing
        /// becomes 1:1 on its own, with no change here.
        /// </summary>
        public static List<AdventurerLoadout> BuildAll(
            IReadOnlyList<ActiveOrder> orders, IReadOnlyList<HeroRecord> party)
        {
            var built = new List<AdventurerLoadout>();
            int slots = party != null ? Mathf.Max(1, party.Count) : 1;

            for (int i = 0; i < slots; i++)
            {
                ActiveOrder order = null;
                if (orders != null && orders.Count > 0)
                    order = orders[Mathf.Min(i, orders.Count - 1)];

                int level = party != null && i < party.Count ? party[i].level : 1;
                built.Add(Build(order, HeroCatalog.AmmoBonus(level)));
            }
            return built;
        }

        public static AdventurerLoadout Build(ActiveOrder order) => Build(order, 0);

        public static AdventurerLoadout Build(ActiveOrder order, int heroAmmoBonus)
        {
            ElementType element = order?.element ?? ElementType.Fire;
            PotionGrade grade = order != null ? order.GetGrade() : PotionGrade.Poor;
            // The recipe's own name ("Fireblood"), which is what the Prep bench showed
            // the player brewing. The order carries the contract's generic "Fire
            // Flask", so the Evening report used to name a different potion.
            string name = order == null ? "Raw Sludge"
                : order.Mixture?.Recipe?.Name ?? RecipeBook.For(order.element)?.Name
                  ?? (string.IsNullOrWhiteSpace(order.potionName) ? "Raw Sludge" : order.potionName);

            RunState s = SaveSystem.Instance != null ? SaveSystem.Instance.State : null;
            float dmgMult = HeroStats.KitDamage(s);
            float cdMult = s != null && s.HasUpgrade(UpgradeCatalog.QuickHands) ? 0.75f : 1f;
            int ammoBonus = HeroStats.KitFlasks(s) + Mathf.Max(0, heroAmmoBonus);

            // Day 1 is a first-timer's fight with whatever quality potion they
            // managed on their very first try at the cauldron — a flat "beginner's
            // luck" bonus keeps that fight winnable instead of a rough first
            // impression (playtest: "combat is all fucked" on day 1 specifically).
            if (s != null && s.day <= 1) dmgMult *= 1.5f;

            // Base stats are the "spec" of the potion; the 0..1 quality that scales
            // damage/blast/elemental is carried separately via the live ActiveOrder.
            // The day's other takes on a recipe throw a little differently: Broad bursts wider and
            // reaches less far, Far the other way. Damage, ammo and cooldown are the same for all.
            int variant = order?.Mixture?.Recipe?.Variant ?? 0;
            float blast = variant == 1 ? 3.0f : variant == 2 ? 2.3f : 2.6f;
            float reach = variant == 1 ? 12f : variant == 2 ? 15f : 13f;
            BombData bomb = BombData.Create(
                name, element,
                baseDamage: Mathf.RoundToInt(HeroStats.BaseDamage * dmgMult), blastRadius: blast, throwSpeed: 13f,
                idealRange: 6f, minSafeRange: 2f, maxRange: reach, cooldownSeconds: 1.4f * cdMult);

            var loadout = ScriptableObject.CreateInstance<AdventurerLoadout>();
            loadout.name = $"Loadout_{name}";
            loadout.SetSlots(AdventurerLoadout.Slot(bomb, AmmoFor(grade) + ammoBonus));
            // An order that was never brewed fights at the quality it was born
            // with, not at full. (The old global read returned 1f for a null
            // order, which quietly armed a never-made potion perfectly.)
            loadout.SetPotionQuality(order != null
                ? order.Quality01
                : ActiveOrder.StartingQuality / 100f);
            return loadout;
        }
    }
}
