namespace AlchemistsArsenal.Combat
{
    /// <summary>
    /// Plain-data view of a combatant's elemental ward at one instant. Lets the
    /// pure decision engine reason about wards without touching scene components —
    /// the caller resolves an <see cref="IElementalWardProvider"/> into this.
    /// </summary>
    public readonly struct WardSnapshot
    {
        public static readonly WardSnapshot None = default;

        public readonly bool Active;
        public readonly ElementType Element;

        public WardSnapshot(bool active, ElementType element)
        {
            Active = active;
            Element = element;
        }

        public static WardSnapshot From(IElementalWardProvider provider) =>
            provider != null && provider.HasActiveWard
                ? new WardSnapshot(true, provider.WardElement)
                : None;
    }
}
