using System;
using System.Collections.Generic;
using UnityEngine;
using AlchemistsArsenal.Data;

namespace AlchemistsArsenal.Combat
{
    /// <summary>
    /// The top level of the boss HFSM: which <see cref="BossPhase"/> is active.
    ///
    /// Every phase — including <see cref="BossPhase.ElementalWard"/> — is chosen the
    /// same way: <see cref="BossPhaseScorer"/> scores each phase's IAUS
    /// considerations against a <see cref="BossPhaseContext"/>, with min-dwell +
    /// switch-margin hysteresis. ElementalWard simply carries a ward-latch override
    /// consideration at a dominant weight, so a hard elemental beating scores it
    /// into control without any separate code path.
    ///
    /// The moment-to-moment behaviour (telegraph / attack / recover) lives in
    /// <see cref="BossBehaviourRunner"/>. This class holds no physics. It does own
    /// how fast the boss walks: the phase's <see cref="BossPhaseData.MoveSpeedMultiplier"/>
    /// (which nothing read before), nothing at all while it is still arriving, and
    /// barely a shuffle while it winds up, so a shockwave bursts from where its
    /// ring was drawn.
    /// </summary>
    [RequireComponent(typeof(CombatantBody))]
    [RequireComponent(typeof(ElementalDamageAccumulator))]
    public class BossPhaseManager : MonoBehaviour, IElementalWardProvider
    {
        [SerializeField] private BossDefinition definition;
        [SerializeField] private BossAttackExecutor attackExecutor;
        [SerializeField] private bool logTransitions;

        public BossPhase CurrentPhase { get; private set; } = BossPhase.Neutral;
        public event Action<BossPhase, BossPhase> OnPhaseChanged;

        public BossDefinition Definition => definition;
        public BossAttackExecutor Executor => attackExecutor;

        /// <summary>Still arriving: it neither moves nor attacks yet.</summary>
        public bool IsEntering => Time.time < _entranceUntil;
        public float Entrance01 => definition != null && definition.EntranceSeconds > 0f
            ? Mathf.Clamp01(1f - (_entranceUntil - Time.time) / definition.EntranceSeconds) : 1f;

        public BossBehaviourRunner.State ActionState => _behaviour != null ? _behaviour.Current : BossBehaviourRunner.State.Idle;
        public BossAttackPattern PendingAttack => _behaviour != null ? _behaviour.Pending : null;
        public float Windup01 => _behaviour != null ? _behaviour.Windup01 : 0f;

        // --- IElementalWardProvider ---
        public bool HasActiveWard => CurrentPhase == BossPhase.ElementalWard;
        public ElementType WardElement =>
            _accumulator != null ? _accumulator.WardLatchElement : ElementType.Water;
        public float WardMultiplier => definition != null ? definition.WardDamageMultiplier : 0.3f;

        private CombatantBody _body;
        private ElementalDamageAccumulator _accumulator;
        private BossBehaviourRunner _behaviour;
        private MonsterWalker _walker;
        private float _entranceUntil;

        private float _phaseEnteredAt;
        private float _nextEvalTime;

        private void Awake()
        {
            _body = GetComponent<CombatantBody>();
            _accumulator = GetComponent<ElementalDamageAccumulator>();
            _walker = GetComponent<MonsterWalker>();
            _behaviour = new BossBehaviourRunner(_body, attackExecutor);
        }

        private void OnEnable()
        {
            EnterPhase(BossPhase.Neutral);
            _nextEvalTime = Time.time;
            _entranceUntil = Time.time + (definition != null ? definition.EntranceSeconds : 0f);
        }

        private void Update()
        {
            if (_walker != null) _walker.SpeedMultiplier = WalkMultiplier();
            if (IsEntering) return;

            if (Time.time >= _nextEvalTime)
            {
                _nextEvalTime = Time.time + EvalInterval();
                EvaluatePhase();
            }

            _behaviour?.Tick(Time.deltaTime, CurrentPatterns());
        }

        private void EvaluatePhase()
        {
            if (definition == null || definition.Phases.Count == 0) return;

            BossPhaseContext ctx = BuildContext();
            BossPhase winner = CurrentPhase;
            float winnerScore = float.NegativeInfinity;
            float currentScore = 0f;

            foreach (BossPhaseData pd in definition.Phases)
            {
                if (pd == null || pd.EntryConsiderations == null || pd.EntryConsiderations.Count == 0)
                    continue;

                float s = BossPhaseScorer.Score(pd.EntryConsiderations, in ctx);
                if (pd.Phase == CurrentPhase) currentScore = s;
                if (s > winnerScore)
                {
                    winnerScore = s;
                    winner = pd.Phase;
                }
            }

            if (winner == CurrentPhase) return;

            // Dwell hysteresis stops marginal flip-flopping — but a *decisive* lead
            // (e.g. a latched ward scoring ~1, or an HP collapse into Recovering) is
            // meant to act immediately, so it bypasses dwell. This one rule covers
            // both without naming either phase.
            float lead = winnerScore - currentScore;
            bool decisive = winnerScore > 0.85f && lead > 0.35f;
            bool dwellOk = Time.time - _phaseEnteredAt >= CurrentDwell();

            if ((decisive || dwellOk) && lead > definition.PhaseSwitchMargin)
                EnterPhase(winner);
        }

        private void EnterPhase(BossPhase phase)
        {
            BossPhase prev = CurrentPhase;
            CurrentPhase = phase;
            _phaseEnteredAt = Time.time;
            _behaviour?.Reset();

            if (prev != phase)
            {
                OnPhaseChanged?.Invoke(prev, phase);
                if (logTransitions) Debug.Log($"[Boss] phase {prev} → {phase}");
            }
        }

        private BossPhaseContext BuildContext()
        {
            float hp = _body.MaxHP > 0 ? (float)_body.CurrentHP / _body.MaxHP : 1f;
            ElementType threat = _accumulator.GetDominantThreat(out _);

            return new BossPhaseContext(
                hp,
                threat,
                _accumulator.GetPressure01(threat),
                _accumulator.TimeSinceLastHit,
                _accumulator.RecentSpike01(threat),
                Time.time - _phaseEnteredAt,
                _accumulator.WardLatchActive,
                _accumulator.WardLatchElement);
        }

        private IReadOnlyList<BossAttackPattern> CurrentPatterns()
        {
            BossPhaseData pd = definition != null ? definition.ForPhase(CurrentPhase) : null;
            return pd != null ? pd.AttackPatterns : Array.Empty<BossAttackPattern>();
        }

        private float WalkMultiplier()
        {
            if (IsEntering || _body == null || !_body.IsAlive) return 0f;
            var state = ActionState;
            if (state == BossBehaviourRunner.State.Telegraph || state == BossBehaviourRunner.State.Attack) return 0.1f;
            BossPhaseData pd = definition != null ? definition.ForPhase(CurrentPhase) : null;
            return pd != null ? pd.MoveSpeedMultiplier : 1f;
        }

        private float EvalInterval() => definition != null ? definition.PhaseEvalInterval : 0.75f;

        private float CurrentDwell()
        {
            BossPhaseData pd = definition != null ? definition.ForPhase(CurrentPhase) : null;
            return pd != null ? pd.MinDwellSeconds : 1.5f;
        }

        /// <summary>Wire the boss in code (spawner / tests).</summary>
        public void Configure(BossDefinition def, BossAttackExecutor executor = null)
        {
            definition = def;
            attackExecutor = executor;
            _behaviour = new BossBehaviourRunner(GetComponent<CombatantBody>(), executor);
        }
    }
}
