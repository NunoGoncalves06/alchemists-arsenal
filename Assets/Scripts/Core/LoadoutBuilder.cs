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
        private static int AmmoFor(PotionGrade grade) => grade switch
        {
            PotionGrade.Perfect => 8,
            PotionGrade.Great => 7,
            PotionGrade.Okay => 5,
            _ => 3,
        };

        public static AdventurerLoadout Build(ActiveOrder order)
        {
            ElementType element = order?.element ?? ElementType.Fire;
            PotionGrade grade = order != null ? order.GetGrade() : PotionGrade.Poor;
            string name = order != null && !string.IsNullOrWhiteSpace(order.potionName)
                ? order.potionName : "Raw Sludge";

            // Base stats are the "spec" of the potion; the 0..1 quality that scales
            // damage/blast/elemental is carried separately via the live ActiveOrder.
            BombData bomb = BombData.Create(
                name, element,
                baseDamage: 22, blastRadius: 2.6f, throwSpeed: 13f,
                idealRange: 6f, minSafeRange: 2f, maxRange: 13f, cooldownSeconds: 1.4f);

            var loadout = ScriptableObject.CreateInstance<AdventurerLoadout>();
            loadout.name = $"Loadout_{name}";
            loadout.SetSlots(AdventurerLoadout.Slot(bomb, AmmoFor(grade)));
            return loadout;
        }
    }
}
