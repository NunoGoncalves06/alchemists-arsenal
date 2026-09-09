using System;
using System.Collections.Generic;
using UnityEngine;
using AlchemistsArsenal.Data;
using AlchemistsArsenal.Combat.Considerations;

namespace AlchemistsArsenal.Combat
{
    /// <summary>
    /// Decision logic for the adventurer utility AI. Deterministic given its
    /// arguments — no MonoBehaviour, no singletons, no <c>GetComponent</c>, no side
    /// effects. It enumerates every (ready bomb × live monster) pair, scores each
    /// with <see cref="UtilityScorer"/>, and returns the best throw above the
    /// threshold.
    ///
    /// Ward state is passed in as plain data: the optional <c>wardResolver</c>
    /// delegate maps a target to a <see cref="WardSnapshot"/>. The engine never
    /// touches a scene component itself; the caller (a MonoBehaviour) does the
    /// resolving.
    /// </summary>
    public static class CombatDecisionEngine
    {
        public readonly struct ScoredCandidate
        {
            public readonly BombData Bomb;
            public readonly ICombatant Target;
            public readonly float Score;
            public readonly int ClusterCount;

            public ScoredCandidate(BombData bomb, ICombatant target, float score, int clusterCount)
            {
                Bomb = bomb;
                Target = target;
                Score = score;
                ClusterCount = clusterCount;
            }
        }

        /// <summary>
        /// Evaluate the field. Returns true and fills <paramref name="request"/> when a
        /// throw scores at or above <paramref name="scoreThreshold"/>. Optionally appends
        /// every scored pair to <paramref name="breakdown"/> for debugging.
        /// </summary>
        public static bool TrySelectThrow(
            ICombatant self,
            IReadOnlyList<ICombatant> monsters,
            IReadOnlyList<BombData> readyBombs,
            ElementalMatrix matrix,
            IReadOnlyList<UtilityConsideration> considerations,
            float potionQuality01,
            float scoreThreshold,
            out BombThrowRequest request,
            out ScoredCandidate best,
            List<ScoredCandidate> breakdown = null,
            Func<ICombatant, WardSnapshot> wardResolver = null)
        {
            request = default;
            best = default;
            bool found = false;
            float bestScore = float.NegativeInfinity;

            if (self == null || !self.IsAlive || monsters == null || readyBombs == null)
                return false;

            for (int m = 0; m < monsters.Count; m++)
            {
                ICombatant target = monsters[m];
                if (target == null || !target.IsAlive || target.Team != Team.Monster)
                    continue;

                // Resolve the target's ward once, not per bomb.
                WardSnapshot ward = wardResolver != null ? wardResolver(target) : WardSnapshot.None;
                bool warded = ward.Active;
                ElementType wardElement = ward.Element;

                float distance = Vector2.Distance(self.Position, target.Position);
                float closingSpeed = ClosingSpeed(self, target);

                for (int b = 0; b < readyBombs.Count; b++)
                {
                    BombData bomb = readyBombs[b];
                    if (bomb == null) continue;

                    int clusterCount = CountCluster(monsters, target.Position, bomb.BlastRadius);

                    var ctx = new UtilityContext(
                        self, target, bomb, matrix,
                        potionQuality01, distance, closingSpeed, clusterCount,
                        warded, wardElement);

                    float score = UtilityScorer.ScoreAction(considerations, in ctx);

                    breakdown?.Add(new ScoredCandidate(bomb, target, score, clusterCount));

                    if (score > bestScore)
                    {
                        bestScore = score;
                        best = new ScoredCandidate(bomb, target, score, clusterCount);
                    }
                }
            }

            if (bestScore >= scoreThreshold && best.Bomb != null && best.Target != null)
            {
                request = new BombThrowRequest(
                    self, best.Target, best.Bomb,
                    self.Position, best.Target.Position,
                    best.Score, potionQuality01);
                found = true;
            }

            return found;
        }

        private static float ClosingSpeed(ICombatant self, ICombatant target)
        {
            Vector2 toSelf = self.Position - target.Position;
            if (toSelf.sqrMagnitude < 0.0001f) return 0f;
            return Vector2.Dot(target.Velocity, toSelf.normalized);
        }

        private static int CountCluster(IReadOnlyList<ICombatant> monsters, Vector2 centre, float radius)
        {
            float sqr = radius * radius;
            int count = 0;
            for (int i = 0; i < monsters.Count; i++)
            {
                ICombatant m = monsters[i];
                if (m == null || !m.IsAlive) continue;
                if (((Vector2)m.Position - centre).sqrMagnitude <= sqr) count++;
            }
            return count;
        }
    }
}
