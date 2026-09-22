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
        /// One recipe per element. The base is doubled and bound with a neighbouring
        /// element — the binder is never the counter-element, so a player who knows
        /// the combat matrix still has to learn the kitchen.
        /// </summary>
        public static PotionRecipe For(ElementType result) => result switch
        {
            ElementType.Fire => new PotionRecipe
            {
                Result = ElementType.Fire,
                Name = "Fireblood",
                Method = "Crush ember root twice over, then fold bark through it to hold the heat.",
                Steps = new[] { ElementType.Fire, ElementType.Fire, ElementType.Nature },
            },
            ElementType.Water => new PotionRecipe
            {
                Result = ElementType.Water,
                Name = "Tidevial",
                Method = "Two measures of frost lily, bound with green so it does not separate.",
                Steps = new[] { ElementType.Water, ElementType.Water, ElementType.Nature },
            },
            ElementType.Nature => new PotionRecipe
            {
                Result = ElementType.Nature,
                Name = "Greenblood",
                Method = "Bark and moss doubled, cut with water so it pours.",
                Steps = new[] { ElementType.Nature, ElementType.Nature, ElementType.Water },
            },
            ElementType.Poison => new PotionRecipe
            {
                Result = ElementType.Poison,
                Name = "Blackdraught",
                Method = "Bog spore twice, woken with a pinch of star anise.",
                Steps = new[] { ElementType.Poison, ElementType.Poison, ElementType.Arcane },
            },
            _ => new PotionRecipe
            {
                Result = ElementType.Arcane,
                Name = "Hexdraught",
                Method = "Hexbloom doubled, soured with venom so the sigil takes.",
                Steps = new[] { ElementType.Arcane, ElementType.Arcane, ElementType.Poison },
            },
        };

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

        public BrewMixture(ElementType result) { Recipe = RecipeBook.For(result); }

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
