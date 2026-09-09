namespace AlchemistsArsenal.Combat
{
    /// <summary>
    /// Something (a boss in an Elemental Ward phase) that currently resists one
    /// element. Read by <see cref="CombatantBody"/> to mitigate incoming damage and
    /// by the adventurer's utility AI (via a ward consideration) to deprioritise
    /// bombs of the warded element.
    /// </summary>
    public interface IElementalWardProvider
    {
        bool HasActiveWard { get; }
        ElementType WardElement { get; }

        /// <summary>Damage multiplier applied to hits of <see cref="WardElement"/> while active (0..1).</summary>
        float WardMultiplier { get; }
    }
}
