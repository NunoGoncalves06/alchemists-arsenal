using System;
using UnityEngine;
using AlchemistsArsenal.Data;

namespace AlchemistsArsenal.Combat
{
    /// <summary>
    /// The boss HFSM.
    ///
    /// Top level (phase): re-scored on a cadence with <see cref="BossPhaseScorer"/>
    /// over each phase's IAUS considerations, with hysteresis (min dwell + switch
    /// margin). <see cref="ElementalDamageAccumulator"/> can hard-override this into
    /// <see cref="BossPhase.ElementalWard"/> for a fixed duration, after which the
    /// boss drops into <see cref="BossPhase.Recovering"/>.
    ///
    /// Bottom level (behaviour): a per-phase Idle → Telegraph → Attack → Recover
    /// loop driven by attack-pattern timings. This class holds no physics — strikes
    /// go through <see cref="BossAttackExecutor"/>.
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

        // --- IElementalWardProvider ---
        public bool HasActiveWard => CurrentPhase == BossPhase.ElementalWard;
        public ElementType WardElement => _wardElement;
        public float WardMultiplier => definition != null ? definition.WardDamageMultiplier : 0.3f;

        private CombatantBody _body;
        private ElementalDamageAccumulator _accumulator;

        private float _phaseEnteredAt;
        private float _nextEvalTime;
        private float _forcedUntil;          // Time.time until which the phase is locked
        private ElementType _wardElement = ElementType.Water;

        private enum Behaviour { Idle, Telegraph, Attack, Recover }
        private Behaviour _behaviour;
        private float _behaviourTimer;
        private int _attackCursor;
        private float _attackReadyAt;
        private BossAttackPattern _pendingAttack;

        private void Awake()
        {
            _body = GetComponent<CombatantBody>();
            _accumulator = GetComponent<ElementalDamageAccumulator>();
        }

        private void OnEnable()
        {
            _accumulator.OnHardThresholdCrossed += HandleHardThreshold;
            EnterPhase(BossPhase.Neutral, 0f);
        }

        private void OnDisable()
        {
            _accumulator.OnHardThresholdCrossed -= HandleHardThreshold;
        }

        private void Update()
        {
            float now = Time.time;
            bool locked = now < _forcedUntil;

            if (CurrentPhase == BossPhase.ElementalWard && !locked)
            {
                // Ward expired → forced recovery window.
                EnterPhase(BossPhase.Recovering, RecoveryDuration());
            }
            else if (!locked && now >= _nextEvalTime)
            {
                _nextEvalTime = now + EvalInterval();
                EvaluatePhase();
            }

            TickBehaviour(Time.deltaTime);
        }

        // ---------------------------------------------------------- phase logic

        private void HandleHardThreshold(ElementType incoming)
        {
            _wardElement = _accumulator.CounterWardFor(incoming);
            EnterPhase(BossPhase.ElementalWard, WardDuration());
            if (logTransitions)
                Debug.Log($"[Boss] HARD OVERRIDE — sustained {incoming} → ward {_wardElement} for {WardDuration():F1}s");
        }

        private void EvaluatePhase()
        {
            if (definition == null) return;

            BossPhaseContext ctx = BuildContext();
            BossPhase winner = CurrentPhase;
            float winnerScore = float.NegativeInfinity;
            float currentScore = 0f;

            foreach (BossPhaseData pd in definition.Phases)
            {
                if (pd == null || pd.Phase == BossPhase.ElementalWard) continue; // ward: override only
                if (pd.EntryConsiderations == null || pd.EntryConsiderations.Count == 0) continue;

                float s = BossPhaseScorer.Score(pd.EntryConsiderations, in ctx);
                if (pd.Phase == CurrentPhase) currentScore = s;
                if (s > winnerScore)
                {
                    winnerScore = s;
                    winner = pd.Phase;
                }
            }

            if (winner == CurrentPhase) return;

            bool dwellOk = Time.time - _phaseEnteredAt >= CurrentDwell();
            if (dwellOk && winnerScore - currentScore > definition.PhaseSwitchMargin)
                EnterPhase(winner, 0f);
        }

        private void EnterPhase(BossPhase phase, float lockDuration)
        {
            BossPhase prev = CurrentPhase;
            CurrentPhase = phase;
            _phaseEnteredAt = Time.time;
            _forcedUntil = lockDuration > 0f ? Time.time + lockDuration : 0f;
            _nextEvalTime = Time.time + EvalInterval();

            _behaviour = Behaviour.Idle;
            _behaviourTimer = 0.2f;
            _pendingAttack = null;

            if (prev != phase)
            {
                OnPhaseChanged?.Invoke(prev, phase);
                if (logTransitions) Debug.Log($"[Boss] phase {prev} → {phase}");
            }
        }

        private BossPhaseContext BuildContext()
        {
            float hp = _body.MaxHP > 0 ? (float)_body.CurrentHP / _body.MaxHP : 1f;
            ElementType threat = _accumulator.DominantThreat;
            return new BossPhaseContext(
                hp,
                threat,
                _accumulator.GetPressure01(threat),
                _accumulator.TimeSinceLastHit,
                _accumulator.RecentSpike01(threat),
                Time.time - _phaseEnteredAt);
        }

        // ------------------------------------------------------- behaviour FSM

        private void TickBehaviour(float dt)
        {
            _behaviourTimer -= dt;
            BossPhaseData pd = definition != null ? definition.ForPhase(CurrentPhase) : null;

            switch (_behaviour)
            {
                case Behaviour.Idle:
                    if (_behaviourTimer <= 0f && CanAttack(pd))
                        StartTelegraph(pd);
                    break;

                case Behaviour.Telegraph:
                    if (_behaviourTimer <= 0f)
                        FireAttack();
                    break;

                case Behaviour.Attack:
                    if (_behaviourTimer <= 0f)
                    {
                        _behaviour = Behaviour.Recover;
                        _behaviourTimer = _pendingAttack != null ? _pendingAttack.RecoverySeconds : 0.6f;
                    }
                    break;

                case Behaviour.Recover:
                    if (_behaviourTimer <= 0f)
                    {
                        _behaviour = Behaviour.Idle;
                        _behaviourTimer = 0.25f;
                    }
                    break;
            }
        }

        private bool CanAttack(BossPhaseData pd) =>
            pd != null && pd.AttackPatterns.Count > 0 && Time.time >= _attackReadyAt && _body.IsAlive;

        private void StartTelegraph(BossPhaseData pd)
        {
            _pendingAttack = pd.AttackPatterns[_attackCursor++ % pd.AttackPatterns.Count];
            _behaviour = Behaviour.Telegraph;
            _behaviourTimer = _pendingAttack.WindupSeconds;
        }

        private void FireAttack()
        {
            _behaviour = Behaviour.Attack;
            _behaviourTimer = 0.1f;
            _attackReadyAt = Time.time + (_pendingAttack != null ? _pendingAttack.CooldownSeconds : 2f);

            if (_pendingAttack == null || attackExecutor == null) return;

            ICombatant target = NearestAdventurer();
            if (target != null)
                attackExecutor.Execute(_pendingAttack, _body.Position, target.Position, _body);
        }

        private ICombatant NearestAdventurer()
        {
            var list = AdventurerRegistry.ActiveAdventurers;
            ICombatant best = null;
            float bestSqr = float.MaxValue;
            Vector2 from = _body.Position;
            for (int i = 0; i < list.Count; i++)
            {
                ICombatant c = list[i];
                if (c == null || !c.IsAlive) continue;
                float sq = ((Vector2)c.Position - from).sqrMagnitude;
                if (sq < bestSqr) { bestSqr = sq; best = c; }
            }
            return best;
        }

        // --------------------------------------------------------- small helpers

        private float EvalInterval() => definition != null ? definition.PhaseEvalInterval : 0.75f;
        private float WardDuration() => definition != null ? definition.WardDurationSeconds : 6f;
        private float RecoveryDuration() => definition != null ? definition.RecoveryDurationSeconds : 3f;

        private float CurrentDwell()
        {
            BossPhaseData pd = definition != null ? definition.ForPhase(CurrentPhase) : null;
            return pd != null ? pd.MinDwellSeconds : 1.5f;
        }

        /// <summary>Test seam.</summary>
        public void ConfigureForTest(BossDefinition def, BossAttackExecutor executor = null)
        {
            definition = def;
            attackExecutor = executor;
        }
    }
}
