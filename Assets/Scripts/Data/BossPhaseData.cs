using System.Collections.Generic;
using UnityEngine;
using AlchemistsArsenal.Combat;
using AlchemistsArsenal.Combat.Considerations;

namespace AlchemistsArsenal.Data
{
    /// <summary>
    /// Definition of one boss phase: how much it "wants" to be active (its
    /// <see cref="EntryConsiderations"/> scored by <c>BossPhaseScorer</c>), which
    /// attacks it may use, and how it moves / mitigates while active.
    /// </summary>
    [CreateAssetMenu(fileName = "BossPhaseData",
        menuName = "Alchemist's Arsenal/Combat/Boss Phase Data", order = 28)]
    public class BossPhaseData : ScriptableObject
    {
        [SerializeField] private BossPhase phase = BossPhase.Neutral;

        [Tooltip("IAUS axes scoring how much the boss wants to enter/stay in this phase. " +
                 "Empty = never chosen by scoring (e.g. ElementalWard is hard-override only).")]
        [SerializeField] private BossConsideration[] entryConsiderations = new BossConsideration[0];

        [SerializeField] private BossAttackPattern[] attackPatterns = new BossAttackPattern[0];

        [Header("While active")]
        [Min(0f)] [SerializeField] private float minDwellSeconds = 2f;
        [Range(0.1f, 3f)] [SerializeField] private float moveSpeedMultiplier = 1f;

        public BossPhase Phase => phase;
        public IReadOnlyList<BossConsideration> EntryConsiderations => entryConsiderations;
        public IReadOnlyList<BossAttackPattern> AttackPatterns => attackPatterns;
        public float MinDwellSeconds => minDwellSeconds;
        public float MoveSpeedMultiplier => moveSpeedMultiplier;

        public static BossPhaseData Create(BossPhase phase, float dwell,
            BossConsideration[] considerations, BossAttackPattern[] attacks)
        {
            var pd = CreateInstance<BossPhaseData>();
            pd.name = "BossPhase_" + phase;
            pd.phase = phase;
            pd.minDwellSeconds = dwell;
            pd.entryConsiderations = considerations ?? new BossConsideration[0];
            pd.attackPatterns = attacks ?? new BossAttackPattern[0];
            return pd;
        }
    }
}
