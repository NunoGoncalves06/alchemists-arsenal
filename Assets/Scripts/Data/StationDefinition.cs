using UnityEngine;
using AlchemistsArsenal.Systems;

namespace AlchemistsArsenal.Data
{
    /// <summary>
    /// Decoupled ScriptableObject definition for a single crafting station.
    /// Holds presentation + rules metadata so neither the state machine
    /// (<see cref="StationManager"/>) nor the UI (<c>StationUIManager</c>) hard-codes it.
    /// </summary>
    [CreateAssetMenu(fileName = "StationDefinition",
        menuName = "Alchemist's Arsenal/Station Definition", order = 10)]
    public class StationDefinition : ScriptableObject
    {
        [SerializeField] private CraftingStation station = CraftingStation.Counter;
        [SerializeField] private string displayName = "Station";
        [SerializeField] private Sprite tabIcon;
        [SerializeField] [TextArea(2, 4)] private string tutorialHint;

        [Tooltip("If false, the station starts LOCKED (e.g. gated by the Day 1 tutorial) " +
                 "and StationManager will refuse to switch to it until unlocked.")]
        [SerializeField] private bool unlockedByDefault = true;

        public CraftingStation Station => station;

        public string DisplayName =>
            string.IsNullOrWhiteSpace(displayName) ? station.ToString() : displayName;

        public Sprite TabIcon => tabIcon;
        public string TutorialHint => tutorialHint;
        public bool UnlockedByDefault => unlockedByDefault;
    }
}
