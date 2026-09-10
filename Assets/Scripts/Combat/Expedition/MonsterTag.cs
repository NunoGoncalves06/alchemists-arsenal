using UnityEngine;
using AlchemistsArsenal.Data;

namespace AlchemistsArsenal.Combat
{
    /// <summary>
    /// Maps a spawned monster GameObject back to the <see cref="MonsterData"/>
    /// archetype it came from — <see cref="CombatantBody"/> doesn't retain it
    /// (reviewer X1). Loot resolution and expedition telemetry read this on death.
    /// </summary>
    public class MonsterTag : MonoBehaviour
    {
        public MonsterData Data;
    }
}
