using System;
using UnityEngine;
using AlchemistsArsenal.Combat;

namespace AlchemistsArsenal.Data
{
    /// <summary>
    /// Attacker-element × defender-element damage multiplier table (e.g. Fire on
    /// Nature = ×2, Fire on Water = ×0.5). Stored as a flat row-major grid indexed
    /// by <see cref="ElementType"/> integer values.
    ///
    /// Per-biome variants are supported — a boss biome can ship a harsher matrix.
    /// </summary>
    [CreateAssetMenu(fileName = "ElementalMatrix",
        menuName = "Alchemist's Arsenal/Combat/Elemental Matrix", order = 22)]
    public class ElementalMatrix : ScriptableObject
    {
        public const float NeutralMultiplier = 1f;

        private static readonly int ElementCount = Enum.GetValues(typeof(ElementType)).Length;

        [Tooltip("Row-major grid: multipliers[attacker * ElementCount + defender].")]
        [SerializeField] private float[] multipliers;

        [Range(0.05f, 1f)]
        [SerializeField] private float weakMultiplier = 0.5f;

        [Range(1f, 5f)]
        [SerializeField] private float strongMultiplier = 2f;

        public float WeakMultiplier => weakMultiplier;
        public float StrongMultiplier => strongMultiplier;

        public float GetMultiplier(ElementType attacker, ElementType defender)
        {
            int a = (int)attacker;
            int d = (int)defender;
            int index = a * ElementCount + d;

            if (multipliers == null || index < 0 || index >= multipliers.Length)
                return NeutralMultiplier;

            float value = multipliers[index];
            return value <= 0f ? NeutralMultiplier : value;
        }

        /// <summary>Ensure the grid is allocated and every unset cell reads as neutral.</summary>
        public void EnsureInitialised()
        {
            int size = ElementCount * ElementCount;
            if (multipliers != null && multipliers.Length == size) return;

            var resized = new float[size];
            for (int i = 0; i < size; i++)
                resized[i] = (multipliers != null && i < multipliers.Length && multipliers[i] > 0f)
                    ? multipliers[i]
                    : NeutralMultiplier;
            multipliers = resized;
        }

        /// <summary>Editor / generator helper — set a single directed matchup.</summary>
        public void SetMultiplier(ElementType attacker, ElementType defender, float value)
        {
            EnsureInitialised();
            multipliers[(int)attacker * ElementCount + (int)defender] = Mathf.Max(0.01f, value);
        }

        private void OnValidate() => EnsureInitialised();
        private void Reset() => EnsureInitialised();
    }
}
