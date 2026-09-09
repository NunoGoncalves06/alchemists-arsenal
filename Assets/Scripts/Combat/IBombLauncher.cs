namespace AlchemistsArsenal.Combat
{
    /// <summary>
    /// Downstream receiver of a <see cref="BombThrowRequest"/>. Implementations own
    /// all instantiation and physics — the utility AI only produces the request.
    /// </summary>
    public interface IBombLauncher
    {
        // Plain by-value param so it can bind directly to Action&lt;BombThrowRequest&gt;
        // (the controller's event). BombThrowRequest is a small readonly struct.
        void Launch(BombThrowRequest request);
    }
}
