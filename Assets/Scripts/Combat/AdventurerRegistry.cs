using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using UnityEngine;

namespace AlchemistsArsenal.Combat
{
    /// <summary>
    /// Live list of adventurers, mirroring <see cref="MonsterRegistry"/>. Lets the
    /// boss AI find targets without a physics sweep or <c>FindObjectsByType</c>.
    /// </summary>
    public static class AdventurerRegistry
    {
        private static readonly List<ICombatant> _adventurers = new List<ICombatant>(8);
        private static readonly ReadOnlyCollection<ICombatant> _view = _adventurers.AsReadOnly();

        public static IReadOnlyList<ICombatant> ActiveAdventurers => _view;
        public static int Count => _adventurers.Count;
        public static event Action OnChanged;

        public static void Register(ICombatant adventurer)
        {
            if (adventurer == null || _adventurers.Contains(adventurer)) return;
            _adventurers.Add(adventurer);
            OnChanged?.Invoke();
        }

        public static void Unregister(ICombatant adventurer)
        {
            if (adventurer != null && _adventurers.Remove(adventurer))
                OnChanged?.Invoke();
        }

        public static void Clear()
        {
            if (_adventurers.Count == 0) return;
            _adventurers.Clear();
            OnChanged?.Invoke();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetOnLoad() => _adventurers.Clear();
    }
}
