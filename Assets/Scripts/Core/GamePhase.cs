namespace AlchemistsArsenal.Core
{
    /// <summary>
    /// The single day-loop state (DESIGN.md §2.4). One authoritative owner:
    /// <see cref="GameLoopManager"/>.
    /// </summary>
    public enum GamePhase
    {
        Boot = 0,
        MainMenu = 1,
        DayIntro = 2,
        Morning = 3,      // the 4-station shop (Phase 0: Counter + Cauldron)
        Handoff = 4,      // confirm the party before the fight
        Afternoon = 5,    // the expedition auto-battler
        Evening = 6,      // report + diary
        BiomeMap = 7      // choose to advance / replay, then sleep = save
    }
}
