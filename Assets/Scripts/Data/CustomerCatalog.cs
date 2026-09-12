using System.Collections.Generic;
using AlchemistsArsenal.Combat;

namespace AlchemistsArsenal.Data
{
    /// <summary>
    /// A customer who walks into the shop in the morning. Code-authored (same
    /// pattern as <see cref="BiomeLibrary"/> / <see cref="UpgradeCatalog"/>) so the
    /// slice has no SO wiring to hand-write; the art pass swaps
    /// <see cref="PortraitId"/> for an authored sprite without touching this.
    /// </summary>
    public class CustomerDefinition
    {
        public string Id;
        public string DisplayName;
        public string Title;

        /// <summary>Key into <c>PixelSprites.Buyer</c>.</summary>
        public string PortraitId;

        /// <summary>What they'd rather have in the flask, regardless of what's out there.</summary>
        public ElementType Favourite;

        /// <summary>Scales every fee they offer — a rich patron pays over the odds.</summary>
        public float FeeMultiplier = 1f;

        /// <summary>Raises the grade they'll accept before docking the fee (0 = lenient, 1 = fussy).</summary>
        public int Fussiness;

        /// <summary>Counter flavour — one line, spoken in Animalese when they arrive.</summary>
        public string[] Greetings;

        public string Speech = "he lo wi tch";
    }

    public static class CustomerCatalog
    {
        public static readonly CustomerDefinition Rookie = new CustomerDefinition
        {
            Id = "rookie",
            DisplayName = "Rookie",
            Title = "your own adventurer",
            PortraitId = "rookie",
            Favourite = ElementType.Fire,
            FeeMultiplier = 0.9f,
            Fussiness = 0,
            Speech = "hai wi tch gi me a fla she",
            Greetings = new[]
            {
                "Morning! I'm the one carrying whatever you brew, so… make it good?",
                "I'll take the road again today. Whatever's in the flask is what I've got out there.",
            },
        };

        public static readonly IReadOnlyList<CustomerDefinition> All = new List<CustomerDefinition>
        {
            Rookie,
            new CustomerDefinition
            {
                Id = "knight", DisplayName = "Ser Halden", Title = "wallguard captain",
                PortraitId = "knight", Favourite = ElementType.Fire, FeeMultiplier = 1.1f, Fussiness = 1,
                Speech = "wi tch we ne d fla she nau",
                Greetings = new[]
                {
                    "The wall held. Barely. I need something that ends a fight faster than a spear does.",
                    "Guard pay is slow, but it's real. Brew me something worth carrying.",
                },
            },
            new CustomerDefinition
            {
                Id = "herbalist", DisplayName = "Mira Thorn", Title = "hedge-herbalist",
                PortraitId = "herbalist", Favourite = ElementType.Nature, FeeMultiplier = 0.95f, Fussiness = 0,
                Speech = "gu d mor ni ng fren d",
                Greetings = new[]
                {
                    "I'd brew it myself, but my hands shake these days. Yours don't.",
                    "Cut the leaf with the grain, mind. I'll know if you didn't.",
                },
            },
            new CustomerDefinition
            {
                Id = "merchant", DisplayName = "Otho Vance", Title = "caravan factor",
                PortraitId = "merchant", Favourite = ElementType.Water, FeeMultiplier = 1.25f, Fussiness = 2,
                Speech = "ti me is mo ney wi tch",
                Greetings = new[]
                {
                    "My wagons leave at noon whether your flask is ready or not. I pay well for ready.",
                    "I don't buy potions. I buy outcomes. Price reflects that.",
                },
            },
            new CustomerDefinition
            {
                Id = "envoy", DisplayName = "Sister Veil", Title = "coven envoy",
                PortraitId = "envoy", Favourite = ElementType.Arcane, FeeMultiplier = 1.15f, Fussiness = 1,
                Speech = "the co ven is wa tchi ng",
                Greetings = new[]
                {
                    "The Coven noticed your shop. That is not, in itself, good news.",
                    "Brew it clean. What we send it against does not forgive a sloppy seal.",
                },
            },
        };

        /// <summary>
        /// Who's at the counter on <paramref name="day"/>. Day 1 is always Rookie —
        /// the tutorial teaches the loop best when the customer is the same person
        /// who has to carry the flask into the fight.
        /// </summary>
        public static CustomerDefinition ForDay(int day)
        {
            if (day <= 1) return Rookie;
            int i = 1 + ((day - 2) % (All.Count - 1)); // rotate the paying customers
            return All[i];
        }

        public static CustomerDefinition ById(string id)
        {
            foreach (var c in All) if (c.Id == id) return c;
            return Rookie;
        }
    }
}
