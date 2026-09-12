using System;
using System.Collections.Generic;
using UnityEngine;
using AlchemistsArsenal.Data;
using AlchemistsArsenal.Systems;
using AlchemistsArsenal.Combat.Considerations;

namespace AlchemistsArsenal.Combat
{
    /// <summary>
    /// Drives one adventurer's bomb-selection AI. Every <see cref="decisionInterval"/>
    /// seconds it asks <see cref="CombatDecisionEngine"/> for the best (bomb × monster)
    /// throw and raises <see cref="OnBombThrowRequested"/>.
    ///
    /// It does NOT instantiate anything, touch a Rigidbody, or apply damage — the
    /// physics/launch layer subscribes to the event and owns all of that.
    /// </summary>
    [DisallowMultipleComponent]
    public class UtilityAI_CombatController : MonoBehaviour
    {
        [Header("Data")]
        [SerializeField] private AdventurerLoadout loadout;
        [SerializeField] private ElementalMatrix elementalMatrix;

        [Tooltip("Ordered utility axes. Reorder / swap freely — scoring is count-agnostic.")]
        [SerializeField] private List<UtilityConsideration> considerations = new List<UtilityConsideration>();

        [Header("Decision Tuning")]
        [Min(0.02f)] [SerializeField] private float decisionInterval = 0.25f;
        // Lowered from 0.15 — at 0.15 the adventurer would often just stand there
        // not throwing while every candidate scored a little under the bar, which
        // reads as "nothing is happening" / "combat feels broken" far more than a
        // slightly-suboptimal throw does (playtest: "combat is all fucked").
        [Range(0f, 1f)] [SerializeField] private float scoreThreshold = 0.08f;

        [Header("Debug")]
        [SerializeField] private bool logDecisions;

        /// <summary>Raised when the AI commits to a throw. The launch layer listens here.</summary>
        public event Action<BombThrowRequest> OnBombThrowRequested;

        public IReadOnlyList<CombatDecisionEngine.ScoredCandidate> LastBreakdown => _breakdown;

        /// <summary>
        /// Flasks still on the belt, across every bomb in the loadout. The HUD shows
        /// this because running dry mid-wave is invisible otherwise — the adventurer
        /// simply stops throwing and it reads as the AI having broken.
        /// </summary>
        public int FlasksLeft
        {
            get
            {
                int total = 0;
                foreach (var kv in _ammo) total += Mathf.Max(0, kv.Value);
                return total;
            }
        }

        /// <summary>
        /// Seconds until the next throw is possible: the shortest cooldown among the
        /// bombs that still have ammo. 0 when something is ready right now.
        /// </summary>
        public float CooldownRemaining
        {
            get
            {
                if (loadout == null) return 0f;
                float now = Time.time;
                float best = float.MaxValue;
                foreach (AdventurerLoadout.BombSlot slot in loadout.Slots)
                {
                    BombData bomb = slot.bomb;
                    if (bomb == null) continue;
                    if (_ammo.TryGetValue(bomb, out int left) && left <= 0) continue;
                    float readyAt = _readyAt.TryGetValue(bomb, out float t) ? t : 0f;
                    best = Mathf.Min(best, Mathf.Max(0f, readyAt - now));
                    if (best <= 0f) return 0f;
                }
                return best == float.MaxValue ? 0f : best;
            }
        }

        /// <summary>0..1 of the way through the current cooldown (1 = ready).</summary>
        public float CooldownFraction
        {
            get
            {
                float remaining = CooldownRemaining;
                if (remaining <= 0f) return 1f;
                float longest = 0.01f;
                foreach (AdventurerLoadout.BombSlot slot in loadout.Slots)
                    if (slot.bomb != null) longest = Mathf.Max(longest, slot.bomb.CooldownSeconds);
                return Mathf.Clamp01(1f - remaining / longest);
            }
        }

        private ICombatant _self;
        private float _nextDecisionTime;

        // Runtime state — the loadout asset itself is never mutated.
        private readonly Dictionary<BombData, int> _ammo = new Dictionary<BombData, int>();
        private readonly Dictionary<BombData, float> _readyAt = new Dictionary<BombData, float>();
        private readonly List<BombData> _readyBombs = new List<BombData>();
        private readonly List<CombatDecisionEngine.ScoredCandidate> _breakdown = new List<CombatDecisionEngine.ScoredCandidate>();

        private void Awake()
        {
            _self = GetComponent<ICombatant>();
            if (_self == null)
                Debug.LogError($"[{nameof(UtilityAI_CombatController)}] No ICombatant on '{name}'. AI disabled.", this);

            RebuildAmmo();
        }

        private void OnEnable() => _nextDecisionTime = Time.time;

        private void Update()
        {
            if (_self == null || !_self.IsAlive) return;
            if (Time.time < _nextDecisionTime) return;
            _nextDecisionTime = Time.time + decisionInterval;

            RunDecision();
        }

        private void RunDecision()
        {
            IReadOnlyList<ICombatant> monsters = MonsterRegistry.ActiveMonsters;
            if (monsters.Count == 0) return;

            CollectReadyBombs();
            if (_readyBombs.Count == 0) return;

            _breakdown.Clear();

            bool decided = CombatDecisionEngine.TrySelectThrow(
                _self,
                monsters,
                _readyBombs,
                elementalMatrix,
                considerations,
                GetPotionQuality01(),
                scoreThreshold,
                out BombThrowRequest request,
                out CombatDecisionEngine.ScoredCandidate best,
                _breakdown,
                ResolveWard);

            if (!decided) return;

            _readyAt[request.Bomb] = Time.time + request.Bomb.CooldownSeconds;
            if (_ammo.TryGetValue(request.Bomb, out int count))
                _ammo[request.Bomb] = Mathf.Max(0, count - 1);

            if (logDecisions)
                Debug.Log($"[UtilityAI] {name} → throw {request.Bomb.DisplayName} " +
                          $"at {DescribeTarget(request.Target)} (score {best.Score:F3}, cluster {best.ClusterCount}).");

            OnBombThrowRequested?.Invoke(request);
        }

        private void CollectReadyBombs()
        {
            _readyBombs.Clear();
            if (loadout == null) return;

            float now = Time.time;
            foreach (AdventurerLoadout.BombSlot slot in loadout.Slots)
            {
                BombData bomb = slot.bomb;
                if (bomb == null) continue;
                if (_ammo.TryGetValue(bomb, out int left) && left <= 0) continue;
                if (_readyAt.TryGetValue(bomb, out float readyAt) && now < readyAt) continue;
                if (!_readyBombs.Contains(bomb)) _readyBombs.Add(bomb);
            }
        }

        private void RebuildAmmo()
        {
            _ammo.Clear();
            _readyAt.Clear();
            if (loadout == null) return;

            foreach (AdventurerLoadout.BombSlot slot in loadout.Slots)
            {
                if (slot.bomb == null) continue;
                _ammo.TryGetValue(slot.bomb, out int existing);
                _ammo[slot.bomb] = existing + Mathf.Max(0, slot.count);
            }
        }

        private static float GetPotionQuality01()
        {
            ActiveOrder order = CraftingManager.Instance != null ? CraftingManager.Instance.CurrentOrder : null;
            return order != null ? Mathf.Clamp01(order.qualityScore / 100f) : 1f;
        }

        private static string DescribeTarget(ICombatant target) =>
            target is Component c ? c.name : "target";

        // Feedback loop: resolve the target's ward into plain data for the pure engine.
        private static WardSnapshot ResolveWard(ICombatant target) =>
            target is Component comp
                ? WardSnapshot.From(comp.GetComponent<IElementalWardProvider>())
                : WardSnapshot.None;

        /// <summary>Wire the controller in code (bootstrap / tests).</summary>
        public void Configure(
            ICombatant self, AdventurerLoadout loadout,
            ElementalMatrix matrix, List<UtilityConsideration> axes)
        {
            _self = self;
            this.loadout = loadout;
            elementalMatrix = matrix;
            considerations = axes;
            RebuildAmmo();
        }
    }
}
