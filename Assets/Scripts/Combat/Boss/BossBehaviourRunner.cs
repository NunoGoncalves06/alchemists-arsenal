using System.Collections.Generic;
using UnityEngine;
using AlchemistsArsenal.Data;

namespace AlchemistsArsenal.Combat
{
    /// <summary>
    /// The bottom level of the boss HFSM: a per-phase Idle → Telegraph → Attack →
    /// Recover loop driven by <see cref="BossAttackPattern"/> timings. Plain C# so
    /// it is testable in isolation and so <see cref="BossPhaseManager"/> stays about
    /// phase selection only. Holds no physics — strikes go through
    /// <see cref="BossAttackExecutor"/>.
    /// </summary>
    public sealed class BossBehaviourRunner
    {
        public enum State { Idle, Telegraph, Attack, Recover }

        public State Current { get; private set; } = State.Idle;

        private readonly CombatantBody _body;
        private readonly BossAttackExecutor _executor;

        private float _timer = 0.2f;
        private int _cursor;
        private float _readyAt;
        private BossAttackPattern _pending;

        public BossBehaviourRunner(CombatantBody body, BossAttackExecutor executor)
        {
            _body = body;
            _executor = executor;
        }

        /// <summary>Called on every phase change.</summary>
        public void Reset()
        {
            Current = State.Idle;
            _timer = 0.2f;
            _pending = null;
        }

        public void Tick(float dt, IReadOnlyList<BossAttackPattern> patterns)
        {
            _timer -= dt;

            switch (Current)
            {
                case State.Idle:
                    if (_timer <= 0f && CanAttack(patterns)) StartTelegraph(patterns);
                    break;

                case State.Telegraph:
                    if (_timer <= 0f) Fire();
                    break;

                case State.Attack:
                    if (_timer <= 0f)
                    {
                        Current = State.Recover;
                        _timer = _pending != null ? _pending.RecoverySeconds : 0.6f;
                    }
                    break;

                case State.Recover:
                    if (_timer <= 0f)
                    {
                        Current = State.Idle;
                        _timer = 0.25f;
                    }
                    break;
            }
        }

        private bool CanAttack(IReadOnlyList<BossAttackPattern> patterns) =>
            patterns != null && patterns.Count > 0 &&
            Time.time >= _readyAt && _body != null && _body.IsAlive;

        private void StartTelegraph(IReadOnlyList<BossAttackPattern> patterns)
        {
            _pending = patterns[_cursor++ % patterns.Count];
            Current = State.Telegraph;
            _timer = _pending.WindupSeconds;
        }

        private void Fire()
        {
            Current = State.Attack;
            _timer = 0.1f;
            _readyAt = Time.time + (_pending != null ? _pending.CooldownSeconds : 2f);

            if (_pending == null || _executor == null || _body == null) return;

            ICombatant target = NearestAdventurer(_body.Position);
            if (target != null)
                _executor.Execute(_pending, _body.Position, target.Position, _body);
        }

        private static ICombatant NearestAdventurer(Vector2 from)
        {
            var list = AdventurerRegistry.ActiveAdventurers;
            ICombatant best = null;
            float bestSqr = float.MaxValue;
            for (int i = 0; i < list.Count; i++)
            {
                ICombatant c = list[i];
                if (c == null || !c.IsAlive) continue;
                float sq = ((Vector2)c.Position - from).sqrMagnitude;
                if (sq < bestSqr) { bestSqr = sq; best = c; }
            }
            return best;
        }
    }
}
