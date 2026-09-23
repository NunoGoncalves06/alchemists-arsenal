using System;
using System.Collections.Generic;
using UnityEngine;
using AlchemistsArsenal.Combat;

namespace AlchemistsArsenal.Data
{
    /// <summary>
    /// The job the player took at the Counter: who ordered it, what element, what
    /// grade they'll accept, and what it pays. Plain serializable so it rides along
    /// in <see cref="Core.RunState"/> and survives a mid-day save.
    ///
    /// Grade note: <see cref="PotionGrade"/> counts DOWN (Perfect = 0 … Poor = 3),
    /// so "met the contract" is <c>(int)delivered &lt;= requiredGrade</c>. Use
    /// <see cref="Meets"/> rather than writing that comparison again anywhere.
    /// </summary>
    [Serializable]
    public class ContractRecord
    {
        public bool accepted;
        /// <summary>Portrait key and name of whoever stood at the Counter — the fighter, since fighters order their own flasks.</summary>
        public string buyerId = "";
        public string buyerName = "";

        /// <summary>The fighter this flask is for (a roster id), and their name.</summary>
        public string heroId = "";
        public string heroName = "";

        /// <summary>Who is backing a guild commission, if anyone (the day's visitor).</summary>
        public string sponsor = "";
        public string title = "";
        public string note = "";
        public ElementType element = ElementType.Fire;

        /// <summary>The worst grade that still counts as delivered — <c>(int)PotionGrade</c>.</summary>
        public int requiredGrade = (int)PotionGrade.Okay;

        public int fee = 50;
        public int bonus;

        public string PotionName => $"{element} Flask";
        public PotionGrade RequiredGrade => (PotionGrade)Mathf.Clamp(requiredGrade, 0, 3);
        public bool Meets(PotionGrade delivered) => (int)delivered <= requiredGrade;

        public ContractRecord Clone() => (ContractRecord)MemberwiseClone();

        public static ContractRecord None => new ContractRecord { accepted = false };
    }

    /// <summary>
    /// Builds the morning's offers. Deterministic per day so the Counter, a reload
    /// and the headless playtest all see the same board.
    /// </summary>
    public static class ContractBoard
    {
        /// <summary>What beats <paramref name="attacker"/> on the elemental matrix.</summary>
        public static ElementType Counter(ElementType attacker) => attacker switch
        {
            ElementType.Fire => ElementType.Water,
            ElementType.Nature => ElementType.Fire,
            ElementType.Water => ElementType.Nature,
            ElementType.Poison => ElementType.Arcane,
            _ => ElementType.Fire,
        };

        /// <summary>Head-count per element for a biome's waves (index = ElementType).</summary>
        public static int[] ThreatCounts(BiomeData biome)
        {
            var counts = new int[5];
            if (biome == null) return counts;
            foreach (var w in biome.Waves)
            {
                if (w.monster == null) continue;
                int i = (int)w.monster.Element;
                if (i >= 0 && i < counts.Length) counts[i] += Mathf.Max(1, w.count);
            }
            return counts;
        }

        public static ElementType Dominant(int[] counts)
        {
            int best = 0;
            for (int i = 1; i < counts.Length; i++) if (counts[i] > counts[best]) best = i;
            return (ElementType)best;
        }

        /// <summary>
        /// The three jobs on the board for <paramref name="hero"/> this day, best
        /// matchup first. The fighter at the Counter is the one who carries the
        /// flask, so the jobs are theirs to choose between:
        /// <list type="bullet">
        /// <item><b>Standing order</b> — the element that counters today's road. Safe.</item>
        /// <item><b>Guild commission</b> — the same element, nearly double the pay,
        /// backed by the day's visitor (<paramref name="patron"/>), who refuses a
        /// sloppy flask.</item>
        /// <item><b>Their own element</b> — the fighter's attunement: they hit 20%
        /// harder with it, whatever the road holds. Their gold and their perk
        /// against the matchup is the actual decision.</item>
        /// </list>
        /// </summary>
        public static List<ContractRecord> Offers(int day, int biomeIndex, HeroRecord hero, CustomerDefinition patron = null)
        {
            BiomeData biome = BiomeLibrary.Get(biomeIndex);
            int[] counts = ThreatCounts(biome);
            ElementType dominant = Dominant(counts);
            ElementType best = Counter(dominant);

            string heroId = hero != null ? hero.id : "";
            string heroName = hero != null ? hero.displayName : CustomerCatalog.Rookie.DisplayName;
            string portrait = hero != null ? hero.portraitId : CustomerCatalog.Rookie.PortraitId;
            ElementType own = hero != null ? hero.affinity : CustomerCatalog.Rookie.Favourite;

            float patronMult = patron != null ? Mathf.Max(0.5f, patron.FeeMultiplier) : 1f;
            int Fee(int b, int perDay, float mult) => Mathf.RoundToInt((b + perDay * Mathf.Min(day, 12)) * mult);

            // A fussy patron demands one band better before they'll pay in full.
            int Req(PotionGrade g, int fussiness) => Mathf.Clamp((int)g - fussiness, 0, 3);

            ContractRecord Job(string title, string note, ElementType element, int required, int fee, int bonus,
                string sponsor = "") => new ContractRecord
            {
                buyerId = portrait, buyerName = heroName, heroId = heroId, heroName = heroName, sponsor = sponsor,
                title = title, note = note, element = element, requiredGrade = required, fee = fee, bonus = bonus,
            };

            var offers = new List<ContractRecord>
            {
                Job("Standing order", $"Counters the {dominant} out there today. The safe job.",
                    best, Req(PotionGrade.Okay, 0), Fee(42, 4, 1f), 12),
                Job("Guild commission",
                    patron != null
                        ? $"Backed by {patron.DisplayName}. Nearly double the pay — a sloppy flask pays half."
                        : "Nearly double the pay — but a sloppy flask pays half.",
                    best, Req(PotionGrade.Great, patron != null ? patron.Fussiness : 0), Fee(66, 6, patronMult), 34,
                    patron != null ? patron.DisplayName : ""),
                Job("Their own element",
                    own == best
                        ? $"{heroName}'s own element — and it matches the road as well."
                        : $"{heroName} hits 20% harder with {own}, whatever is out there.",
                    own, Req(PotionGrade.Okay, 0), Fee(58, 5, 1f), 22),
            };
            return offers;
        }
    }
}
