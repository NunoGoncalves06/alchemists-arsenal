using System;
using UnityEngine;
using AlchemistsArsenal.Data;

namespace AlchemistsArsenal.Combat
{
    /// <summary>
    /// Listens to <see cref="CombatantBody.OnDamaged"/> and builds up per-element
    /// "pressure" that decays over time — so only <em>sustained</em> elemental
    /// damage escalates. Publishes hard-threshold edges (for the boss's forced ward)
    /// and exposes soft signals (pressure 0..1, recent spike, dominant threat) for
    /// utility weighting.
    /// </summary>
    [RequireComponent(typeof(CombatantBody))]
    public class ElementalDamageAccumulator : MonoBehaviour
    {
        [SerializeField] private ElementalThreatProfile profile;
        [Tooltip("Recent-window damage decays this many times faster than the main pool.")]
        [Min(1f)] [SerializeField] private float recentWindowDecayScale = 4f;

        private static readonly int ElementCount = Enum.GetValues(typeof(ElementType)).Length;

        private readonly float[] _pressure = new float[Enum.GetValues(typeof(ElementType)).Length];
        private readonly float[] _recent = new float[Enum.GetValues(typeof(ElementType)).Length];
        private readonly bool[] _overHard = new bool[Enum.GetValues(typeof(ElementType)).Length];

        private CombatantBody _body;
        private float _lastHitTime = -999f;

        public event Action<ElementType> OnHardThresholdCrossed;
        public event Action<ElementType> OnHardThresholdCleared;

        public float TimeSinceLastHit => Time.time - _lastHitTime;

        private void Awake() => _body = GetComponent<CombatantBody>();

        private void OnEnable()
        {
            if (_body != null) _body.OnDamaged += HandleDamage;
        }

        private void OnDisable()
        {
            if (_body != null) _body.OnDamaged -= HandleDamage;
        }

        private void HandleDamage(DamageInfo info)
        {
            int e = (int)info.Element;
            if (e < 0 || e >= ElementCount) return;

            float amount = Mathf.Max(0f, info.Amount);
            _pressure[e] += amount;
            _recent[e] += amount;
            _lastHitTime = Time.time;
        }

        private void Update()
        {
            float dt = Time.deltaTime;

            for (int e = 0; e < ElementCount; e++)
            {
                var element = (ElementType)e;
                float decay = profile != null ? profile.DecayFor(element) : 8f;

                _pressure[e] = Mathf.Max(0f, _pressure[e] - decay * dt);
                _recent[e] = Mathf.Max(0f, _recent[e] - decay * recentWindowDecayScale * dt);

                float hard = profile != null ? profile.HardThreshold(element) : 100f;
                bool over = hard > 0f && _pressure[e] >= hard;

                if (over && !_overHard[e])
                {
                    _overHard[e] = true;
                    OnHardThresholdCrossed?.Invoke(element);
                }
                else if (!over && _overHard[e])
                {
                    _overHard[e] = false;
                    OnHardThresholdCleared?.Invoke(element);
                }
            }
        }

        public float GetPressure01(ElementType element)
        {
            float hard = profile != null ? profile.HardThreshold(element) : 100f;
            return hard <= 0f ? 0f : Mathf.Clamp01(_pressure[(int)element] / hard);
        }

        public float RecentSpike01(ElementType element)
        {
            float soft = profile != null ? profile.SoftThreshold(element) : 45f;
            return soft <= 0f ? 0f : Mathf.Clamp01(_recent[(int)element] / soft);
        }

        public bool IsOverHard(ElementType element) => _overHard[(int)element];

        public ElementType DominantThreat
        {
            get
            {
                int best = 0;
                float bestVal = _pressure[0];
                for (int e = 1; e < ElementCount; e++)
                {
                    if (_pressure[e] > bestVal)
                    {
                        bestVal = _pressure[e];
                        best = e;
                    }
                }
                return (ElementType)best;
            }
        }

        public ElementType CounterWardFor(ElementType incoming) =>
            profile != null ? profile.CounterWard(incoming) : ElementType.Water;

        /// <summary>Test seam.</summary>
        public void SetProfileForTest(ElementalThreatProfile p) => profile = p;
    }
}
