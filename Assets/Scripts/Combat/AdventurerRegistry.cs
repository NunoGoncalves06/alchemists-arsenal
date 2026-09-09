using System;
using System.Collections.Generic;
using UnityEngine;

namespace AlchemistsArsenal.Combat
{
    /// <summary>
    /// Live list of adventurers, mirroring <see cref="MonsterRegistry"/>. Lets the
    /// boss AI find targets without a physics sweep. Thin facade over a shared
    /// <see cref="CombatRoster"/>.
    /// </summary>
    public static class AdventurerRegistry
    {
        private static readonly CombatRoster _roster = new CombatRoster();

        public static IReadOnlyList<ICombatant> ActiveAdventurers => _roster.View;
        public static int Count => _roster.Count;

        public static event Action OnChanged
        {
            add => _roster.OnChanged += value;
            remove => _roster.OnChanged -= value;
        }

        public static void Register(ICombatant adventurer) => _roster.Register(adventurer);
        public static void Unregister(ICombatant adventurer) => _roster.Unregister(adventurer);
        public static void Clear() => _roster.Clear();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetOnLoad() => _roster.HardReset();
    }
}
