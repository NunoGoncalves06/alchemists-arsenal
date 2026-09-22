namespace AlchemistsArsenal.DebugTools
{
    /// <summary>
    /// A self-checking simulation suite the headless playtest can run and wait on.
    /// <see cref="Done"/> flips once every check has reported; failures are logged
    /// as errors, which the playtest turns into a failed run.
    /// </summary>
    public interface ISimulationSuite
    {
        bool Done { get; }
    }
}
