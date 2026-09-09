using System.Collections.Generic;
using UnityEngine;
using AlchemistsArsenal.Combat;

namespace AlchemistsArsenal.Data
{
    /// <summary>
    /// Top-level boss config (Biome 5 — The Coven's Peak). Bundles the phase set,
    /// the elemental threat profile, and the HFSM timing knobs.
    /// </summary>
    [CreateAssetMenu(fileName = "BossDefinition",
        menuName = "Alchemist's Arsenal/Combat/Boss Definition", order = 29)]
    public class BossDefinition : ScriptableObject
    {
        [SerializeField] private string displayName = "The Coven Matriarch";
        [SerializeField] private ElementType coreElement = ElementType.Arcane;
        [Min(1)] [SerializeField] private int maxHealth = 600;

        [SerializeField] private ElementalThreatProfile threatProfile;
        [SerializeField] private BossPhaseData[] phases = new BossPhaseData[0];

        [Header("HFSM timing")]
        [Min(0.1f)] [SerializeField] private float phaseEvalInterval = 0.75f;
        [Range(0f, 1f)] [SerializeField] private float phaseSwitchMargin = 0.05f;
        [Min(0f)] [SerializeField] private float wardDurationSeconds = 6f;
        [Min(0f)] [SerializeField] private float recoveryDurationSeconds = 3f;

        [Header("Ward effect")]
        [Range(0f, 1f)] [SerializeField] private float wardDamageMultiplier = 0.3f;

        public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? name : displayName;
        public ElementType CoreElement => coreElement;
        public int MaxHealth => maxHealth;
        public ElementalThreatProfile ThreatProfile => threatProfile;
        public IReadOnlyList<BossPhaseData> Phases => phases;
        public float PhaseEvalInterval => phaseEvalInterval;
        public float PhaseSwitchMargin => phaseSwitchMargin;
        public float WardDurationSeconds => wardDurationSeconds;
        public float RecoveryDurationSeconds => recoveryDurationSeconds;
        public float WardDamageMultiplier => wardDamageMultiplier;

        public BossPhaseData ForPhase(BossPhase phase)
        {
            if (phases == null) return null;
            for (int i = 0; i < phases.Length; i++)
                if (phases[i] != null && phases[i].Phase == phase) return phases[i];
            return null;
        }
    }
}
