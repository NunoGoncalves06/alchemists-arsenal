namespace AlchemistsArsenal.Combat
{
    /// <summary>Top level of the boss HFSM — the "phase" a boss is currently in.</summary>
    public enum BossPhase
    {
        /// <summary>Baseline: measured attacks, standard defence.</summary>
        Neutral = 0,

        /// <summary>Low health / under pressure: faster, more aggressive patterns.</summary>
        Enraged = 1,

        /// <summary>Hard-override state: resists the element it has been hammered with.</summary>
        ElementalWard = 2,

        /// <summary>Brief vulnerable cooldown after a ward drops.</summary>
        Recovering = 3
    }
}
