using System.Collections.Generic;
using UnityEngine;
using AlchemistsArsenal.Systems;

namespace AlchemistsArsenal.Data
{
    /// <summary>
    /// Ordered collection of <see cref="StationDefinition"/> assets that make up
    /// the morning crafting flow. A single shared asset is the one source of truth
    /// for both <see cref="StationManager"/> (rules) and the UI (tab labels/icons).
    /// </summary>
    [CreateAssetMenu(fileName = "StationSet",
        menuName = "Alchemist's Arsenal/Station Set", order = 11)]
    public class StationSet : ScriptableObject
    {
        [SerializeField] private StationDefinition[] stations = new StationDefinition[0];

        public IReadOnlyList<StationDefinition> Stations => stations;

        public StationDefinition Get(CraftingStation station)
        {
            if (stations == null) return null;
            for (int i = 0; i < stations.Length; i++)
            {
                if (stations[i] != null && stations[i].Station == station)
                    return stations[i];
            }
            return null;
        }

        public bool TryGet(CraftingStation station, out StationDefinition definition)
        {
            definition = Get(station);
            return definition != null;
        }
    }
}
