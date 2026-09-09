using System;
using UnityEngine;
using AlchemistsArsenal.Combat;

namespace AlchemistsArsenal.Data
{
    /// <summary>
    /// Per-element tuning for <c>ElementalDamageAccumulator</c>: how fast pressure
    /// decays, the soft (weight-bias) and hard (forced ward) thresholds, and which
    /// element to raise a ward with when hammered by this one.
    /// </summary>
    [CreateAssetMenu(fileName = "ElementalThreatProfile",
        menuName = "Alchemist's Arsenal/Combat/Elemental Threat Profile", order = 26)]
    public class ElementalThreatProfile : ScriptableObject
    {
        [Serializable]
        public struct Entry
        {
            public ElementType element;
            [Min(0f)] public float decayPerSecond;
            [Min(0f)] public float softThreshold;
            [Min(0f)] public float hardThreshold;
            [Tooltip("Ward element the boss raises when this element crosses its hard threshold.")]
            public ElementType counterWard;
        }

        [SerializeField] private Entry[] entries = Array.Empty<Entry>();

        [Header("Fallbacks for elements without an explicit entry")]
        [Min(0f)] [SerializeField] private float defaultDecayPerSecond = 8f;
        [Min(0f)] [SerializeField] private float defaultSoftThreshold = 45f;
        [Min(0f)] [SerializeField] private float defaultHardThreshold = 100f;
        [SerializeField] private ElementType defaultCounterWard = ElementType.Water;

        public bool TryGet(ElementType element, out Entry entry)
        {
            if (entries != null)
            {
                for (int i = 0; i < entries.Length; i++)
                {
                    if (entries[i].element == element)
                    {
                        entry = entries[i];
                        return true;
                    }
                }
            }
            entry = default;
            return false;
        }

        public float DecayFor(ElementType e) =>
            TryGet(e, out Entry entry) && entry.decayPerSecond > 0f ? entry.decayPerSecond : defaultDecayPerSecond;

        public float SoftThreshold(ElementType e) =>
            TryGet(e, out Entry entry) && entry.softThreshold > 0f ? entry.softThreshold : defaultSoftThreshold;

        public float HardThreshold(ElementType e) =>
            TryGet(e, out Entry entry) && entry.hardThreshold > 0f ? entry.hardThreshold : defaultHardThreshold;

        public ElementType CounterWard(ElementType e) =>
            TryGet(e, out Entry entry) ? entry.counterWard : defaultCounterWard;

        /// <summary>Editor / generator helper.</summary>
        public void SetEntries(Entry[] newEntries) => entries = newEntries ?? Array.Empty<Entry>();
    }
}
