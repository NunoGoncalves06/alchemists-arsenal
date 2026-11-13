namespace AlchemistsArsenal.Combat
{
    /// <summary>
    /// Elemental affinities shared by potions/bombs and monsters. Order is fixed —
    /// <see cref="AlchemistsArsenal.Data.ElementalMatrix"/> indexes into a flat
    /// grid using the integer values, so new elements must be appended, never inserted.
    /// </summary>
    public enum ElementType
    {
        Nature = 0,
        Fire   = 1,
        Water  = 2,
        Poison = 3,
        Arcane = 4
    }

    /// <summary>Which side of the afternoon auto-battle a combatant belongs to.</summary>
    public enum Team
    {
        Adventurer = 0,
        Monster    = 1
    }
}
