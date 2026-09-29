using System.Collections.Generic;
using UnityEngine;
using AlchemistsArsenal.Combat;

namespace AlchemistsArsenal.Data
{
    /// <summary>How well the leaves that went into the pot match the recipe.</summary>
    public enum MixOutcome { Ruined = 0, Weak = 1, Close = 2, Perfect = 3 }

    /// <summary>
    /// What a given potion is actually made of. Each element has its own leaves in
    /// its own proportions, so Prep is a recipe to follow rather than "pick three of
    /// the matching colour": every brew wants a base doubled up and a binder from a
    /// different element, and putting the wrong leaf in has a named consequence
    /// rather than just a smaller number.
    /// </summary>
    public class PotionRecipe
    {
        public ElementType Result;
        public string Name;
        public string Method;

        /// <summary>The leaves it takes, in order. Duplicates are deliberate.</summary>
        public ElementType[] Steps;

        public int StepCount => Steps != null ? Steps.Length : 0;

        /// <summary>Reads as "2 x Fire  +  1 x Nature".</summary>
        public string Shorthand
        {
            get
            {
                var counts = new List<KeyValuePair<ElementType, int>>();
                foreach (ElementType e in Steps)
                {
                    int found = -1;
                    for (int i = 0; i < counts.Count; i++) if (counts[i].Key == e) found = i;
                    if (found >= 0) counts[found] = new KeyValuePair<ElementType, int>(e, counts[found].Value + 1);
                    else counts.Add(new KeyValuePair<ElementType, int>(e, 1));
                }

                var sb = new System.Text.StringBuilder();
                foreach (var kv in counts)
                {
                    if (sb.Length > 0) sb.Append("  +  ");
                    sb.Append(kv.Value).Append(" x ").Append(kv.Key);
                }
                return sb.ToString();
            }
        }
    }

    public static class RecipeBook
    {
        /// <summary>
        /// Four recipes per element, one per tier (<see cref="Complexity.RecipeTier"/>):
        /// two leaves on the first road, three on the next two, then four and five.
        /// The base is always doubled (tripled in the longest) and bound with a
        /// neighbouring element; the binder is never the counter-element, so a player
        /// who knows the combat matrix still has to learn the kitchen.
        /// </summary>
        private static readonly PotionRecipe[][] Tiers =
        {
            new[]   // Fire
            {
                R(ElementType.Fire, "Ember Tonic", "Two ember roots, crushed together while they're still warm.", ElementType.Fire, ElementType.Fire),
                R(ElementType.Fire, "Fireblood", "Crush ember root twice over, then fold bark through it to hold the heat.", ElementType.Fire, ElementType.Fire, ElementType.Nature),
                R(ElementType.Fire, "Cinderheart", "Fireblood with a pinch of star anise, so it burns longer.", ElementType.Fire, ElementType.Fire, ElementType.Nature, ElementType.Arcane),
                R(ElementType.Fire, "Wildfire", "Three roots, bark to bind, anise to wake it. Stand back.", ElementType.Fire, ElementType.Fire, ElementType.Fire, ElementType.Nature, ElementType.Arcane),
            },
            new[]   // Water
            {
                R(ElementType.Water, "Dewdrop", "Two frost lilies and nothing else.", ElementType.Water, ElementType.Water),
                R(ElementType.Water, "Tidevial", "Two measures of frost lily, bound with green so it does not separate.", ElementType.Water, ElementType.Water, ElementType.Nature),
                R(ElementType.Water, "Rimewater", "Tidevial sharpened with anise until it frosts the glass.", ElementType.Water, ElementType.Water, ElementType.Nature, ElementType.Arcane),
                R(ElementType.Water, "Deep Tide", "Three lilies, green to bind, a drop of venom to make it bite.", ElementType.Water, ElementType.Water, ElementType.Water, ElementType.Nature, ElementType.Poison),
            },
            new[]   // Nature
            {
                R(ElementType.Nature, "Sap Tonic", "Bark and moss, crushed wet.", ElementType.Nature, ElementType.Nature),
                R(ElementType.Nature, "Greenblood", "Bark and moss doubled, cut with water so it pours.", ElementType.Nature, ElementType.Nature, ElementType.Water),
                R(ElementType.Nature, "Thornbrew", "Greenblood with anise, so the thorns grow in the flask.", ElementType.Nature, ElementType.Nature, ElementType.Water, ElementType.Arcane),
                R(ElementType.Nature, "Old Growth", "Three barks, water to carry them, anise to wake the roots.", ElementType.Nature, ElementType.Nature, ElementType.Nature, ElementType.Water, ElementType.Arcane),
            },
            new[]   // Poison
            {
                R(ElementType.Poison, "Bogwater", "Two bog spores, left to sour.", ElementType.Poison, ElementType.Poison),
                R(ElementType.Poison, "Blackdraught", "Bog spore twice, woken with a pinch of star anise.", ElementType.Poison, ElementType.Poison, ElementType.Arcane),
                R(ElementType.Poison, "Nightshade", "Blackdraught thinned with frost lily so it spreads.", ElementType.Poison, ElementType.Poison, ElementType.Arcane, ElementType.Water),
                R(ElementType.Poison, "Plaguebloom", "Three spores, anise to wake them, lily to carry the cloud.", ElementType.Poison, ElementType.Poison, ElementType.Poison, ElementType.Arcane, ElementType.Water),
            },
            new[]   // Arcane
            {
                R(ElementType.Arcane, "Glimmer", "Two hexblooms, stirred until they hum.", ElementType.Arcane, ElementType.Arcane),
                R(ElementType.Arcane, "Hexdraught", "Hexbloom doubled, soured with venom so the sigil takes.", ElementType.Arcane, ElementType.Arcane, ElementType.Poison),
                R(ElementType.Arcane, "Sigilwine", "Hexdraught with an ember root to set the sigil alight.", ElementType.Arcane, ElementType.Arcane, ElementType.Poison, ElementType.Fire),
                R(ElementType.Arcane, "Starfall", "Three hexblooms, venom and ember. Pour it outside.", ElementType.Arcane, ElementType.Arcane, ElementType.Arcane, ElementType.Poison, ElementType.Fire),
            },
        };

        private static PotionRecipe R(ElementType result, string name, string method, params ElementType[] steps) =>
            new PotionRecipe { Result = result, Name = name, Method = method, Steps = steps };

        private static int Row(ElementType e) => e switch
        {
            ElementType.Fire => 0, ElementType.Water => 1, ElementType.Nature => 2, ElementType.Poison => 3, _ => 4,
        };

        /// <summary>The recipe for <paramref name="result"/> at <paramref name="tier"/> (0-3; tier 1 is the classic three-leaf one).</summary>
        public static PotionRecipe For(ElementType result, int tier = 1)
        {
            PotionRecipe[] row = Tiers[Row(result)];
            PotionRecipe r = row[Mathf.Clamp(tier, 0, row.Length - 1)];
            return new PotionRecipe { Result = r.Result, Name = r.Name, Method = r.Method, Steps = (ElementType[])r.Steps.Clone() };
        }

        /// <summary>Today's recipe for <paramref name="result"/>: the tier today's road asks for.</summary>
        public static PotionRecipe Today(ElementType result) => For(result, Complexity.RecipeTier(Complexity.Level()));

        /// <summary>
        /// What happens when <paramref name="intruder"/> meets a brew based on
        /// <paramref name="baseElement"/> that it does not belong in. Named, because
        /// "-7 quality" tells the player nothing about what they did wrong.
        /// </summary>
        public static string Reaction(ElementType baseElement, ElementType intruder)
        {
            if (baseElement == intruder) return "";
            if ((baseElement == ElementType.Fire && intruder == ElementType.Water) ||
                (baseElement == ElementType.Water && intruder == ElementType.Fire))
                return "quenched — it spits steam and goes flat";
            if ((baseElement == ElementType.Nature && intruder == ElementType.Poison) ||
                (baseElement == ElementType.Poison && intruder == ElementType.Nature))
                return "rotted — the green turns black in the pot";
            if (baseElement == ElementType.Arcane || intruder == ElementType.Arcane)
                return "unstable — the surface will not stop moving";
            if (intruder == ElementType.Fire) return "scorched — it cooks before you stir it";
            return "muddied — the colour goes nowhere";
        }

        /// <summary>Quality swing for a finished mix.</summary>
        public static int QualityDelta(MixOutcome outcome) => outcome switch
        {
            MixOutcome.Perfect => 12,
            MixOutcome.Close => 5,
            MixOutcome.Weak => 0,
            _ => -14,
        };

        /// <summary>
        /// How wide the cauldron's band of stir speeds is, given the mix. This is the
        /// dependency made mechanical rather than merely sequential: prep it properly
        /// and the stirring is genuinely easier; botch it and you are chasing a narrow
        /// band around a pot that catches the moment you ease off.
        /// </summary>
        public static float BandScale(MixOutcome outcome) => outcome switch
        {
            MixOutcome.Perfect => 1.35f,
            MixOutcome.Close => 1f,
            MixOutcome.Weak => 0.85f,
            _ => 0.7f,
        };

        public static string Describe(MixOutcome outcome) => outcome switch
        {
            MixOutcome.Perfect => "PERFECT MIX — the recipe exactly. The pot will forgive a ragged stir.",
            MixOutcome.Close => "CLOSE — one leaf off. It will brew, but watch the band.",
            MixOutcome.Weak => "WEAK — mostly wrong. The stirring band is narrow now.",
            _ => "RUINED — none of this belongs together. Good luck.",
        };
    }

    /// <summary>
    /// The day's working mixture: what the recipe asks for, what actually went in,
    /// and whether the mortar work is done. The Cauldron will not brew until this
    /// says it is ready — you crush and add the leaves before you stir.
    /// </summary>
    public class BrewMixture
    {
        public readonly PotionRecipe Recipe;
        public readonly List<ElementType> Added = new List<ElementType>();

        /// <summary>Ground in the mortar — the second half of prep.</summary>
        public bool Ground;

        public BrewMixture(ElementType result) : this(result, 1) { }

        public BrewMixture(ElementType result, int tier) { Recipe = RecipeBook.For(result, tier); }

        public int Remaining => Mathf.Max(0, Recipe.StepCount - Added.Count);
        public bool AllLeavesIn => Added.Count >= Recipe.StepCount;

        /// <summary>The Cauldron's gate: leaves in AND ground.</summary>
        public bool Ready => AllLeavesIn && Ground;

        /// <summary>The leaf the recipe is asking for next, in order.</summary>
        public ElementType NextStep =>
            Added.Count < Recipe.StepCount ? Recipe.Steps[Added.Count] : Recipe.Result;

        /// <summary>Is <paramref name="element"/> what the recipe wants right now?</summary>
        public bool IsNextStep(ElementType element) => !AllLeavesIn && NextStep == element;

        /// <summary>
        /// How the pile compares to the recipe, as a multiset — order is a bonus, not
        /// a requirement, so a player who adds the right three in the wrong sequence
        /// still gets a good mix.
        /// </summary>
        public MixOutcome Evaluate()
        {
            if (Added.Count == 0) return MixOutcome.Ruined;

            var need = new List<ElementType>(Recipe.Steps);
            int matched = 0;
            foreach (ElementType e in Added)
                if (need.Remove(e)) matched++;

            if (matched >= Recipe.StepCount) return MixOutcome.Perfect;
            if (matched == Recipe.StepCount - 1) return MixOutcome.Close;
            if (matched > 0) return MixOutcome.Weak;
            return MixOutcome.Ruined;
        }
    }
}
