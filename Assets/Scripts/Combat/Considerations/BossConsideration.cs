using UnityEngine;

namespace AlchemistsArsenal.Combat.Considerations
{
    /// <summary>
    /// One normalised axis of the boss phase decision. Mirrors
    /// <see cref="UtilityConsideration"/> (AnimationCurve response + weight) but
    /// reads a <see cref="BossPhaseContext"/>. ScriptableObject so each phase gets
    /// its own reorderable list in the inspector.
    /// </summary>
    public abstract class BossConsideration : ScriptableObject
    {
        [SerializeField, TextArea(1, 3)] private string description;

        [Tooltip("Maps the raw 0..1 input (X) to the scored 0..1 output (Y).")]
        [SerializeField] private AnimationCurve responseCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f);

        [Range(0f, 3f)] [SerializeField] private float weight = 1f;

        public float Weight => weight;
        public string Description => description;

        protected abstract float GetRawScore(in BossPhaseContext context);

        public float Score(in BossPhaseContext context)
        {
            float raw = Mathf.Clamp01(GetRawScore(in context));
            float shaped = responseCurve != null && responseCurve.length > 0
                ? responseCurve.Evaluate(raw)
                : raw;
            return Mathf.Clamp01(shaped);
        }
    }
}
