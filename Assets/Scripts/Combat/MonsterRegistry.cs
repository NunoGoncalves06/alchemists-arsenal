using System;
using System.Collections.Generic;
using UnityEngine;

namespace AlchemistsArsenal.Combat
{
    /// <summary>
    /// Live list of monsters. Monsters register on spawn and unregister on death,
    /// so the adventurer AI can enumerate targets in O(n) without a
    /// <c>Physics2D.OverlapCircleAll</c> sweep or <c>FindObjectsByType</c> every tick.
    /// Thin facade over a shared <see cref="CombatRoster"/>.
    /// </summary>
    public static class MonsterRegistry
    {
        private static readonly CombatRoster _roster = new CombatRoster();

        public static IReadOnlyList<ICombatant> ActiveMonsters => _roster.View;
        public static int Count => _roster.Count;

        public static event Action OnChanged
        {
            add => _roster.OnChanged += value;
            remove => _roster.OnChanged -= value;
        }

        public static void Register(ICombatant monster) => _roster.Register(monster);
        public static void Unregister(ICombatant monster) => _roster.Unregister(monster);
        public static void Clear() => _roster.Clear();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetOnLoad() => _roster.HardReset();
    }
}
