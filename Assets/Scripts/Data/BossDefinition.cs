using System.Collections.Generic;
using UnityEngine;
using AlchemistsArsenal.Combat;

namespace AlchemistsArsenal.Data
{
    /// <summary>
    /// Top-level boss config. Bundles who it is (name, epithet, which rig draws it),
    /// the phase set, the elemental threat profile, and the HFSM timing knobs.
    /// </summary>
    [CreateAssetMenu(fileName = "BossDefinition",
        menuName = "Alchemist's Arsenal/Combat/Boss Definition", order = 29)]
    public class BossDefinition : ScriptableObject
    {
        [SerializeField] private string displayName = "The Coven Matriarch";
        [Tooltip("The line under its name on the title card.")]
        [SerializeField] private string epithet = "";
        [Tooltip("Which rig draws it (see Vfx.BossVisual): \"woodwose\" or \"matriarch\".")]
        [SerializeField] private string visualId = "matriarch";
        [Tooltip("Seconds it spends arriving before it moves or attacks.")]
        [Min(0f)] [SerializeField] private float entranceSeconds = 1.6f;
        [Tooltip("Where volleys leave from, relative to its feet (world units): the heart-knot, the eye ring.")]
        [SerializeField] private Vector2 projectileOrigin = new Vector2(0f, 2.2f);
        [SerializeField] private ElementType coreElement = ElementType.Arcane;
        [Min(1)] [SerializeField] private int maxHealth = 600;

        [Tooltip("Health against a party of 1, 2, 3, 4 as a fraction of Max Health (authored for three). Empty = the shared PartyHealthScale.")]
        [SerializeField] private float[] partyHealthScale = new float[0];

        [SerializeField] private ElementalThreatProfile threatProfile;
        [SerializeField] private BossPhaseData[] phases = new BossPhaseData[0];

        [Header("HFSM timing")]
        [Min(0.1f)] [SerializeField] private float phaseEvalInterval = 0.75f;
        [Range(0f, 1f)] [SerializeField] private float phaseSwitchMargin = 0.05f;

        [Header("Ward effect (latch duration lives on ElementalDamageAccumulator)")]
        [Range(0f, 1f)] [SerializeField] private float wardDamageMultiplier = 0.3f;

        public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? name : displayName;
        public string Epithet => epithet ?? "";
        public string VisualId => string.IsNullOrWhiteSpace(visualId) ? "matriarch" : visualId;
        public float EntranceSeconds => entranceSeconds;
        public Vector2 ProjectileOrigin => projectileOrigin;
        public ElementType CoreElement => coreElement;
        public int MaxHealth => maxHealth;

        /// <summary>
        /// <see cref="MaxHealth"/> is authored for a full party of three. A party
        /// starts as one hero (the second and third are bought), and a guardian
        /// tuned for three is a wall for one: each hero brings their own belt of
        /// flasks, so a smaller party has proportionally less to throw.
        /// </summary>
        // Measured, not guessed (the harness's grade sweep): with these a Great
        // flask beats the Matriarch at any party size and an Okay one does not.
        public static readonly float[] PartyHealthScale = { 0.32f, 0.66f, 1f, 1.3f };

        /// <summary>The health this guardian has against a party of <paramref name="partySize"/>.</summary>
        public int HealthFor(int partySize)
        {
            float[] scale = partyHealthScale != null && partyHealthScale.Length > 0 ? partyHealthScale : PartyHealthScale;
            return Mathf.Max(1, Mathf.RoundToInt(maxHealth * scale[Mathf.Clamp(partySize, 1, scale.Length) - 1]));
        }

        /// <summary>
        /// This guardian's own answer to party size, instead of the shared one. The
        /// Matriarch is the last road's guardian: she is not meant to be carried by
        /// one fighter however well kitted, so she shrinks far less for a small party.
        /// </summary>
        public void ConfigurePartyScale(params float[] scale) => partyHealthScale = scale ?? new float[0];
        public ElementalThreatProfile ThreatProfile => threatProfile;
        public IReadOnlyList<BossPhaseData> Phases => phases;
        public float PhaseEvalInterval => phaseEvalInterval;
        public float PhaseSwitchMargin => phaseSwitchMargin;
        public float WardDamageMultiplier => wardDamageMultiplier;

        public BossPhaseData ForPhase(BossPhase phase)
        {
            if (phases == null) return null;
            for (int i = 0; i < phases.Length; i++)
                if (phases[i] != null && phases[i].Phase == phase) return phases[i];
            return null;
        }

        public void Configure(string displayName, ElementType coreElement, int maxHealth,
            ElementalThreatProfile threatProfile, BossPhaseData[] phases)
        {
            this.displayName = displayName;
            this.coreElement = coreElement;
            this.maxHealth = maxHealth;
            this.threatProfile = threatProfile;
            this.phases = phases ?? new BossPhaseData[0];
        }

        public void ConfigureIdentity(string epithet, string visualId, float entranceSeconds, Vector2 projectileOrigin)
        {
            this.epithet = epithet;
            this.visualId = visualId;
            this.entranceSeconds = Mathf.Max(0f, entranceSeconds);
            this.projectileOrigin = projectileOrigin;
        }
    }
}
