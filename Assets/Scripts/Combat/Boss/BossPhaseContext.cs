namespace AlchemistsArsenal.Combat
{
    /// <summary>
    /// Immutable snapshot the boss phase scorer hands to each
    /// <see cref="Considerations.BossConsideration"/>. Analogous to
    /// <see cref="UtilityContext"/> but describing the boss's own situation rather
    /// than a bomb throw.
    /// </summary>
    public readonly struct BossPhaseContext
    {
        /// <summary>Boss HP as a fraction of max, 0..1.</summary>
        public readonly float HealthFraction;

        /// <summary>Element the boss has taken the most accumulated pressure from.</summary>
        public readonly ElementType DominantThreat;

        /// <summary>Dominant-threat pressure relative to its hard threshold, 0..1.</summary>
        public readonly float DominantThreatPressure01;

        /// <summary>Seconds since the boss last took any damage.</summary>
        public readonly float TimeSinceLastHit;

        /// <summary>Recent burst of dominant-element damage relative to the soft threshold, 0..1.</summary>
        public readonly float RecentDamageSpike01;

        /// <summary>Seconds the boss has been in its current phase.</summary>
        public readonly float TimeInPhase;

        public BossPhaseContext(
            float healthFraction,
            ElementType dominantThreat,
            float dominantThreatPressure01,
            float timeSinceLastHit,
            float recentDamageSpike01,
            float timeInPhase)
        {
            HealthFraction = healthFraction;
            DominantThreat = dominantThreat;
            DominantThreatPressure01 = dominantThreatPressure01;
            TimeSinceLastHit = timeSinceLastHit;
            RecentDamageSpike01 = recentDamageSpike01;
            TimeInPhase = timeInPhase;
        }
    }
}
