using System.Collections.Generic;
using AlchemistsArsenal.Data;

namespace AlchemistsArsenal.Combat
{
    /// <summary>
    /// Two or three things to try after a lost road, read off what actually happened:
    /// the party against the road's need, the road upgrades, the flask's element and
    /// grade, and who ran out of flasks. Pure, so the HUD, the evening report and the
    /// tests all see the same tips.
    /// </summary>
    public static class DefeatTips
    {
        public enum Tip { Hire, RoadUpgrades, Element, Flasks, Ammo, Levels, Replay }

        public const int Max = 3;

        public readonly struct Facts
        {
            public readonly PotionGrade Grade;
            public readonly int Fighters, FightersNeeded, RoadTier, RoadTierNeeded;
            public readonly ElementType Flask, Counter;
            public readonly int Withdrew;
            public readonly bool SpareVials, Bandolier;
            public readonly int LowestLevel;

            public Facts(PotionGrade grade, int fighters, int fightersNeeded, int roadTier, int roadTierNeeded,
                ElementType flask, ElementType counter, int withdrew, bool spareVials, bool bandolier, int lowestLevel)
            {
                Grade = grade; Fighters = fighters; FightersNeeded = fightersNeeded;
                RoadTier = roadTier; RoadTierNeeded = roadTierNeeded;
                Flask = flask; Counter = counter; Withdrew = withdrew;
                SpareVials = spareVials; Bandolier = bandolier; LowestLevel = lowestLevel;
            }

            /// <summary>The facts of the road just lost: <paramref name="biomeIndex"/> is the road played.</summary>
            public static Facts From(ExpeditionReport r, Core.RunState s, int biomeIndex)
            {
                int i = UnityEngine.Mathf.Clamp(biomeIndex, 0, BiomeLibrary.Count - 1);
                ElementType threat = ContractBoard.Dominant(ContractBoard.ThreatCounts(BiomeLibrary.Get(i)));
                // Every fighter's brew counts: the element tip names one that was wrong, the grade is the weakest carried.
                ElementType counter = ContractBoard.Counter(threat), flask = counter;
                PotionGrade grade = r.bombs.Count > 0 ? PotionGrade.Perfect : r.craftedGrade;
                foreach (var b in r.bombs)
                {
                    if (b.element != counter) flask = b.element;
                    if (b.grade > grade) grade = b.grade;
                }
                int lowest = HeroCatalog.MaxLevel;
                if (s != null)
                    foreach (HeroRecord h in s.DeployedParty()) if (h != null && h.level < lowest) lowest = h.level;
                return new Facts(grade, r.partyTotal, BiomeLibrary.FightersNeeded[i],
                    s != null ? UpgradeCatalog.RoadTier(s) : 0, BiomeLibrary.RoadTierNeeded[i],
                    flask, counter, r.partyWithdrew,
                    s != null && s.HasUpgrade(UpgradeCatalog.SpareVials), s != null && s.HasUpgrade(UpgradeCatalog.Bandolier), lowest);
            }
        }

        /// <summary>The tips, most decisive first: what the road needs and the party lacked, then what would help anyway.</summary>
        public static List<Tip> For(in Facts f)
        {
            var tips = new List<Tip>();
            if (f.Fighters < f.FightersNeeded) tips.Add(Tip.Hire);
            if (f.RoadTier < f.RoadTierNeeded) tips.Add(Tip.RoadUpgrades);
            if (f.Flask != f.Counter) tips.Add(Tip.Element);
            if (f.Grade > PotionGrade.Great) tips.Add(Tip.Flasks);
            if (f.Withdrew > 0) tips.Add(Tip.Ammo);
            // A second tip that is still true of this run, so one reason never stands alone.
            if (tips.Count < 2 && f.Grade > PotionGrade.Perfect && !tips.Contains(Tip.Flasks)) tips.Add(Tip.Flasks);
            if (tips.Count < 2 && f.LowestLevel < HeroCatalog.MaxLevel) tips.Add(Tip.Levels);
            if (tips.Count < 2) tips.Add(Tip.Replay);
            if (tips.Count > Max) tips.RemoveRange(Max, tips.Count - Max);
            return tips;
        }

        public static string Text(Tip tip, in Facts f) => tip switch
        {
            Tip.Hire => $"This road needs {f.FightersNeeded} fighters; you sent {f.Fighters}. Hire at the Guild.",
            Tip.RoadUpgrades => "Buy the road upgrades in the shop: tougher flasks, boots and leathers.",
            Tip.Element => $"Brew {f.Counter} for this road: {f.Flask} flasks do little here.",
            Tip.Flasks => "Brew cleaner flasks: a better grade hits harder and fills more vials.",
            Tip.Ammo => !f.SpareVials ? "A fighter ran out of flasks. Spare Vials carry more."
                      : !f.Bandolier ? "A fighter ran out of flasks. The Bandolier carries more."
                      : "A fighter ran out of flasks. Level your heroes: each level carries more.",
            Tip.Levels => "Level your heroes: each level carries more flasks.",
            _ => "Replay a cleared road for gold and levels, then come back.",
        };
    }
}
