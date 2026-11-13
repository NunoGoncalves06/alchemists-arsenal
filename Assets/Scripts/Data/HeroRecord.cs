using System;
using AlchemistsArsenal.Combat;

namespace AlchemistsArsenal.Data
{
    /// <summary>
    /// One hired adventurer, as persisted in <see cref="Core.RunState.roster"/>.
    ///
    /// Deliberately a plain <c>[Serializable]</c> class of primitives: JsonUtility
    /// (which <see cref="Core.SaveSystem"/> uses) round-trips public fields on a
    /// nested serializable class and a <c>List&lt;T&gt;</c> of them, but not
    /// dictionaries, properties, readonly fields or anything polymorphic. The
    /// shipped precedent for this exact shape is <see cref="ContractRecord"/>.
    ///
    /// Everything derived — HP, damage, spacing, the perk's display name — is
    /// computed from these fields by <see cref="HeroCatalog"/> and
    /// <see cref="HeroPerks"/> rather than stored, so a balance change applies to
    /// heroes already in a save.
    /// </summary>
    [Serializable]
    public class HeroRecord
    {
        /// <summary>Stable key. Never reused, even after a hero leaves the roster.</summary>
        public string id = "";

        public string displayName = "Rookie";

        /// <summary>Key into <c>PixelSprites.Buyer</c> / <c>PixelSprites.Fighter</c>.</summary>
        public string portraitId = "rookie";

        /// <summary>1..<see cref="HeroCatalog.MaxLevel"/>.</summary>
        public int level = 1;

        /// <summary>
        /// The element this hero is attuned to. Drives both halves of the perk:
        /// extra damage when carrying a flask of this element, and reduced damage
        /// taken from it. See <see cref="HeroPerks"/>.
        /// </summary>
        public ElementType affinity = ElementType.Nature;

        /// <summary>Key into <see cref="HeroPerks.Archetypes"/> — how they fight, not how hard.</summary>
        public string archetypeId = HeroPerks.DefaultArchetype;

        /// <summary>
        /// The first day this hero is fit again. A hero downed on day D is set to
        /// D+2 as the day rolls over, so they miss exactly one expedition.
        /// 0 means "never hurt".
        /// </summary>
        public int restUntilDay = 0;

        /// <summary>Chosen at Evening, consumed by the next afternoon's expedition.</summary>
        public bool deployed = true;

        public bool IsFit(int day) => day >= restUntilDay;

        /// <summary>"Lv 3 · Ember Hand" — the one-line identity used across the UI.</summary>
        public string Subtitle => $"Lv {level} · {HeroPerks.PerkName(affinity, archetypeId)}";
    }
}
