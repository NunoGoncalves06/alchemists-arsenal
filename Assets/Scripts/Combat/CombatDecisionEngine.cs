using System.Collections.Generic;
using UnityEngine;
using AlchemistsArsenal.Data;
using AlchemistsArsenal.Combat.Considerations;

namespace AlchemistsArsenal.Combat
{
    /// <summary>
    /// Pure decision logic for the adventurer utility AI — no MonoBehaviour, no
    /// scene access, no side effects. Given the world state it enumerates every
    /// (ready bomb × live monster) pair, scores each with <see cref="UtilityScorer"/>,
    /// and returns the best throw above the threshold.
    ///
    /// Kept separate from the controller so it is trivially unit-testable and so the
    /// "scoring is separate from action execution" rule is structural, not a promise.
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
            List<ScoredCandidate> breakdown = null)
        {
            request = default;
            best = default;
            bool found = false;
            float bestScore = float.NegativeInfinity;

            if (self == null || !self.IsAlive || monsters == null || readyBombs == null)
                return false;

            for (int b = 0; b < readyBombs.Count; b++)
            {
                BombData bomb = readyBombs[b];
                if (bomb == null) continue;

                for (int m = 0; m < monsters.Count; m++)
                {
                    ICombatant target = monsters[m];
                    if (target == null || !target.IsAlive || target.Team != Team.Monster)
                        continue;

                    float distance = Vector2.Distance(self.Position, target.Position);
                    float closingSpeed = ClosingSpeed(self, target);
                    int clusterCount = CountCluster(monsters, target.Position, bomb.BlastRadius);

                    var ctx = new UtilityContext(
                        self, target, bomb, matrix,
                        potionQuality01, distance, closingSpeed, clusterCount);

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
