using UnityEngine;
using AlchemistsArsenal.Core;

namespace AlchemistsArsenal.Data
{
    /// <summary>
    /// A fighter's numbers at a level, with the road upgrades owned: the HP they take
    /// into the fight, a flask's base damage (before the brew's quality, the hero's
    /// attunement and the first day's luck), and the flasks they carry beyond what the
    /// brew fills. The roster shows these and the arena builds the fighter from them,
    /// so what a level-up promises is what the next fight uses.
    /// </summary>
    public readonly struct HeroStats
    {
        // 120 -> 150 HP: a single adventurer facing 3+ monsters at once was dying before
        // the AI/movement fixes had a real chance to work (playtest: near-instant losses).
        public const int BaseHp = 150, BaseDamage = 22;

        public readonly int Hp, Damage, FlaskBonus;
        public readonly float DamageScale;

        private HeroStats(int hp, int damage, int flaskBonus, float damageScale)
        {
            Hp = hp; Damage = damage; FlaskBonus = flaskBonus; DamageScale = damageScale;
        }

        /// <summary>HP before the level: the base, Thick Boots and Hard Leathers.</summary>
        public static int HpBase(RunState s) => BaseHp
            + (s != null && s.HasUpgrade(UpgradeCatalog.ThickBoots) ? 30 : 0)
            + (s != null && s.HasUpgrade(UpgradeCatalog.HardLeathers) ? 50 : 0);

        /// <summary>The flask's damage multiplier from the road kit (Heavier Flasks, Tempered Glass).</summary>
        public static float KitDamage(RunState s) =>
            (s != null && s.HasUpgrade(UpgradeCatalog.HeavierFlasks) ? 1.25f : 1f)
            * (s != null && s.HasUpgrade(UpgradeCatalog.TemperedGlass) ? 1.25f : 1f);

        /// <summary>Flasks from the kit (Spare Vials, Bandolier), before the level's.</summary>
        public static int KitFlasks(RunState s) =>
            (s != null && s.HasUpgrade(UpgradeCatalog.SpareVials) ? 5 : 0)
            + (s != null && s.HasUpgrade(UpgradeCatalog.Bandolier) ? 8 : 0);

        public static HeroStats For(int level, RunState s) => new HeroStats(
            Mathf.RoundToInt(HpBase(s) * HeroCatalog.HpScale(level)),
            Mathf.RoundToInt(BaseDamage * KitDamage(s) * HeroCatalog.DamageScale(level)),
            KitFlasks(s) + HeroCatalog.AmmoBonus(level),
            HeroCatalog.DamageScale(level));
    }
}
