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
    ///
    /// The target is chosen, and the spot locked, when the telegraph <b>starts</b>.
    /// It used to be picked at the moment of the blow, so the mark on the ground
    /// (there was none) could never have told anyone where the hit would land, and
    /// stepping away during the windup did nothing: the blow followed you. Now the
    /// windup is a promise the executor keeps — the decal shows the spot, the
    /// heroes can leave it (<see cref="DangerZones"/>), and the blow lands there.
    /// </summary>
    public sealed class BossBehaviourRunner
    {
        public enum State { Idle, Telegraph, Attack, Recover }

        public State Current { get; private set; } = State.Idle;

        /// <summary>The attack being wound up or delivered, if any.</summary>
        public BossAttackPattern Pending => _pending;

        /// <summary>0 at the start of the windup, 1 at the blow.</summary>
        public float Windup01 => Current == State.Telegraph && _windup > 0f ? Mathf.Clamp01(1f - _timer / _windup) : 0f;

        private readonly CombatantBody _body;
        private readonly BossAttackExecutor _executor;

        private float _timer = 0.2f;
        private float _windup;
        private int _cursor;
        private float _readyAt;
        private BossAttackPattern _pending;
        private Vector2 _origin, _aim;

        public BossBehaviourRunner(CombatantBody body, BossAttackExecutor executor)
        {
            _body = body;
            _executor = executor;
        }

        /// <summary>Called on every phase change. A windup in progress is abandoned.</summary>
        public void Reset()
        {
            if (Current == State.Telegraph && _executor != null) _executor.CancelTelegraph();
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
            ICombatant target = NearestAdventurer(_body.Position);
            if (target == null)
            {
                _timer = 0.25f;   // nobody to swing at yet; look again shortly
                return;
            }

            _pending = patterns[_cursor++ % patterns.Count];
            _origin = _body.Position;
            _aim = BossAttackExecutor.ImpactPoint(_pending, _origin, target.Position);
            _windup = Mathf.Max(0.05f, _pending.WindupSeconds);
            Current = State.Telegraph;
            _timer = _windup;

            if (_executor != null) _executor.Telegraph(_pending, _origin, _aim, _windup);
        }

        private void Fire()
        {
            Current = State.Attack;
            _timer = 0.1f;
            _readyAt = Time.time + (_pending != null ? _pending.CooldownSeconds : 2f);

            if (_pending == null || _executor == null || _body == null) return;
            _executor.Execute(_pending, _origin, _aim, _body);
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
