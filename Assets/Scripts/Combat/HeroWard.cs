using UnityEngine;
using AlchemistsArsenal.Data;

namespace AlchemistsArsenal.Combat
{
    /// <summary>
    /// The defensive half of a hero's perk: a permanent resistance to their own
    /// element.
    ///
    /// <para>This deliberately adds no combat code at all. <see cref="CombatantBody"/>
    /// already resolves a sibling <see cref="IElementalWardProvider"/> in Awake and
    /// already applies it in <c>ApplyDamage</c> — the same seam the boss's temporary
    /// Elemental Ward phase uses. Dropping this component on an adventurer is the
    /// whole implementation.</para>
    ///
    /// <para>It covers two channels rather than one: bomb/boss-pattern damage, which
    /// is element-tagged and goes through the matrix, and monster contact damage,
    /// which bypasses the matrix but is still tagged with the monster's element and
    /// so is still warded.</para>
    ///
    /// <para><b>Ordering matters:</b> add this <i>before</i> the GameObject is
    /// activated. <c>CombatantBody.Awake</c> caches the provider once, and a ward
    /// attached after activation is never seen.</para>
    /// </summary>
    [DisallowMultipleComponent]
    public class HeroWard : MonoBehaviour, IElementalWardProvider
    {
        [SerializeField] private ElementType element = ElementType.Nature;
        [SerializeField] [Range(0f, 1f)] private float multiplier = HeroPerks.WardMultiplier;

        public bool HasActiveWard => true;
        public ElementType WardElement => element;
        public float WardMultiplier => multiplier;

        public void Configure(ElementType wardElement)
        {
            element = wardElement;
            multiplier = HeroPerks.WardMultiplier;
        }
    }
}
