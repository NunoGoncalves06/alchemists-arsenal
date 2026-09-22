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
        public string buyerId = "";
        public string buyerName = "";
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

        /// <summary>The three jobs on the board for this day, best-matchup first.</summary>
        public static List<ContractRecord> Offers(int day, int biomeIndex)
        {
            CustomerDefinition buyer = CustomerCatalog.ForDay(day);
            BiomeData biome = BiomeLibrary.Get(biomeIndex);
            int[] counts = ThreatCounts(biome);
            ElementType dominant = Dominant(counts);
            ElementType best = Counter(dominant);

            float mult = Mathf.Max(0.5f, buyer.FeeMultiplier);
            int Fee(int b, int perDay) => Mathf.RoundToInt((b + perDay * Mathf.Min(day, 12)) * mult);

            // Fussy buyers demand one band better before they'll pay in full.
            int Req(PotionGrade g) => Mathf.Clamp((int)g - buyer.Fussiness, 0, 3);

            var offers = new List<ContractRecord>
            {
                new ContractRecord
                {
                    buyerId = buyer.Id, buyerName = buyer.DisplayName,
                    title = "Standing order",
                    note = $"Counters the {dominant} out there today. The safe job.",
                    element = best,
                    requiredGrade = Req(PotionGrade.Okay),
                    fee = Fee(42, 4), bonus = 12,
                },
                new ContractRecord
                {
                    buyerId = buyer.Id, buyerName = buyer.DisplayName,
                    title = "Guild commission",
                    note = "Nearly double the pay — but a sloppy flask pays half.",
                    element = best,
                    requiredGrade = Req(PotionGrade.Great),
                    fee = Fee(66, 6), bonus = 34,
                },
            };

            // Something they want for themselves — often the wrong call for today's
            // road, which is the actual decision: their gold against your party's odds.
            ElementType personal = buyer.Favourite != best ? buyer.Favourite : Counter(SecondThreat(counts, dominant));
            if (personal == best) personal = ElementType.Arcane;
            offers.Add(new ContractRecord
            {
                buyerId = buyer.Id, buyerName = buyer.DisplayName,
                title = "Personal request",
                note = personal == best
                    ? "Happens to match the road as well."
                    : $"They want {personal}, whatever's out there — and it's what your party will carry.",
                element = personal,
                requiredGrade = Req(PotionGrade.Okay),
                fee = Fee(58, 5), bonus = 22,
            });

            return offers;
        }

        private static ElementType SecondThreat(int[] counts, ElementType dominant)
        {
            int best = -1;
            for (int i = 0; i < counts.Length; i++)
            {
                if ((ElementType)i == dominant || counts[i] <= 0) continue;
                if (best < 0 || counts[i] > counts[best]) best = i;
            }
            return best < 0 ? dominant : (ElementType)best;
        }
    }
}
