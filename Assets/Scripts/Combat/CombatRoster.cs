using System;
using System.Collections;
using System.Collections.Generic;

namespace AlchemistsArsenal.Combat
{
    /// <summary>
    /// Reusable live roster of combatants for one side. A single implementation
    /// behind <see cref="MonsterRegistry"/> and <see cref="AdventurerRegistry"/> so
    /// the two stay identical.
    ///
    /// The exposed <see cref="View"/> is a zero-alloc window onto the backing list —
    /// do not cache it across frames; its contents change as combatants spawn / die.
    /// </summary>
    public sealed class CombatRoster : IReadOnlyList<ICombatant>
    {
        private readonly List<ICombatant> _items = new List<ICombatant>(32);

        public event Action OnChanged;

        public IReadOnlyList<ICombatant> View => this;
        public int Count => _items.Count;
        public ICombatant this[int index] => _items[index];

        public void Register(ICombatant combatant)
        {
            if (combatant == null || _items.Contains(combatant)) return;
            _items.Add(combatant);
            OnChanged?.Invoke();
        }

        public void Unregister(ICombatant combatant)
        {
            if (combatant != null && _items.Remove(combatant))
                OnChanged?.Invoke();
        }

        public void Clear()
        {
            if (_items.Count == 0) return;
            _items.Clear();
            OnChanged?.Invoke();
        }

        /// <summary>Silent reset — no event — for play-mode / domain-reload hooks.</summary>
        public void HardReset() => _items.Clear();

        public List<ICombatant>.Enumerator GetEnumerator() => _items.GetEnumerator();
        IEnumerator<ICombatant> IEnumerable<ICombatant>.GetEnumerator() => _items.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => _items.GetEnumerator();
    }
}
