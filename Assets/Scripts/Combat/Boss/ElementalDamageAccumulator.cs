using System;
using UnityEngine;
using AlchemistsArsenal.Data;

namespace AlchemistsArsenal.Combat
{
    /// <summary>
    /// Listens to <see cref="CombatantBody.OnDamaged"/> and builds up per-element
    /// "pressure" that decays over time — so only <em>sustained</em> elemental
    /// damage escalates.
    ///
    /// Soft signals (<see cref="GetPressure01"/>, <see cref="RecentSpike01"/>,
    /// <see cref="DominantThreat"/>) feed the boss phase scorer. When an element
    /// crosses its hard threshold a <see cref="WardLatchActive"/> latch is armed for
    /// <see cref="wardLatchSeconds"/>; the boss's ElementalWard phase is scored on
    /// that latch, so there is no separate override code path.
    /// </summary>
    [RequireComponent(typeof(CombatantBody))]
    public class ElementalDamageAccumulator : MonoBehaviour
    {
        [SerializeField] private ElementalThreatProfile profile;

        [Tooltip("Recent-window damage decays this many times faster than the main pool.")]
        [Min(1f)] [SerializeField] private float recentWindowDecayScale = 4f;

        [Tooltip("How long a ward stays latched after its element crosses the hard threshold.")]
        [Min(0f)] [SerializeField] private float wardLatchSeconds = 6f;

        private static readonly int ElementCount = Enum.GetValues(typeof(ElementType)).Length;

        private readonly float[] _pressure = new float[ElementCount];
        private readonly float[] _recent = new float[ElementCount];
        private readonly bool[] _overHard = new bool[ElementCount];

        private CombatantBody _body;
        private float _lastHitTime = -999f;

        private float _wardLatchUntil = -1f;
        private ElementType _wardLatchElement = ElementType.Water;

        public event Action<ElementType> OnHardThresholdCrossed;
        public event Action<ElementType> OnHardThresholdCleared;

        public float TimeSinceLastHit => Time.time - _lastHitTime;

        /// <summary>True while a ward is latched from a recent hard-threshold crossing.</summary>
        public bool WardLatchActive => Time.time < _wardLatchUntil;

        /// <summary>The element the boss should ward with (only meaningful while latched).</summary>
        public ElementType WardLatchElement => _wardLatchElement;

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
                    _wardLatchElement = CounterWardFor(element);
                    _wardLatchUntil = Time.time + wardLatchSeconds;
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

        /// <summary>Highest current per-element pressure. Also reports the raw amount.</summary>
        public ElementType GetDominantThreat(out float pressure)
        {
            int best = 0;
            pressure = _pressure[0];
            for (int e = 1; e < ElementCount; e++)
            {
                if (_pressure[e] > pressure)
                {
                    pressure = _pressure[e];
                    best = e;
                }
            }
            return (ElementType)best;
        }

        /// <summary>
        /// Element with the most accumulated pressure. NOTE: with zero pressure this
        /// still returns the first enum value — callers that branch on the identity
        /// (not just the 0..1 magnitude) should check <see cref="GetDominantThreat"/>'s
        /// out-value first.
        /// </summary>
        public ElementType DominantThreat => GetDominantThreat(out _);

        public ElementType CounterWardFor(ElementType incoming) =>
            profile != null ? profile.CounterWard(incoming) : ElementType.Water;

        /// <summary>Assign the threat profile in code (spawner / tests).</summary>
        public void SetProfile(ElementalThreatProfile p) => profile = p;
    }
}
