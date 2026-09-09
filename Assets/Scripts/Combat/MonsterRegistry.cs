using System;
using System.Collections.Generic;
using UnityEngine;

namespace AlchemistsArsenal.Combat
{
    /// <summary>
    /// Central list of live monsters. Monsters register on spawn and unregister on
    /// death, so the utility AI can enumerate targets in O(n) without a
    /// <c>Physics2D.OverlapCircleAll</c> sweep or <c>FindObjectsOfType</c> every
    /// decision tick.
    /// </summary>
    public static class MonsterRegistry
    {
        private static readonly List<ICombatant> _monsters = new List<ICombatant>(32);
        private static readonly ReadOnlyListView _view = new ReadOnlyListView(_monsters);

        /// <summary>Live view — do not cache across frames; contents change as monsters die.</summary>
        public static IReadOnlyList<ICombatant> ActiveMonsters => _view;

        public static int Count => _monsters.Count;

        public static event Action OnChanged;

        public static void Register(ICombatant monster)
        {
            if (monster == null || _monsters.Contains(monster)) return;
            _monsters.Add(monster);
            OnChanged?.Invoke();
        }

        public static void Unregister(ICombatant monster)
        {
            if (monster == null) return;
            if (_monsters.Remove(monster))
                OnChanged?.Invoke();
        }

        /// <summary>Clears everything. Called automatically on play-mode start.</summary>
        public static void Clear()
        {
            if (_monsters.Count == 0) return;
            _monsters.Clear();
            OnChanged?.Invoke();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetOnLoad() => _monsters.Clear();

        // Tiny wrapper so callers get IReadOnlyList without us exposing the backing List.
        private sealed class ReadOnlyListView : IReadOnlyList<ICombatant>
        {
            private readonly List<ICombatant> _source;
            public ReadOnlyListView(List<ICombatant> source) => _source = source;
            public ICombatant this[int index] => _source[index];
            public int Count => _source.Count;
            public IEnumerator<ICombatant> GetEnumerator() => _source.GetEnumerator();
            System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => _source.GetEnumerator();
        }
    }
}
