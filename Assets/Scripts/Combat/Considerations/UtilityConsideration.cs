using UnityEngine;

namespace AlchemistsArsenal.Combat.Considerations
{
    /// <summary>
    /// One normalised axis of a utility decision. A consideration reads the
    /// <see cref="UtilityContext"/>, produces a raw 0..1 input, and shapes it
    /// through a designer-authored <see cref="AnimationCurve"/> response curve.
    ///
    /// Considerations are ScriptableObjects so an enemy archetype can be handed a
    /// different, reorderable list of them in the inspector without any code change.
    /// </summary>
    public abstract class UtilityConsideration : ScriptableObject
    {
        [SerializeField, TextArea(1, 3)]
        private string description;

        [Tooltip("Maps the raw 0..1 input (X) to the scored 0..1 output (Y). " +
                 "Draw the shape you want: rising, falling, bell, step, ease.")]
        [SerializeField]
        private AnimationCurve responseCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f);

        [Tooltip("How strongly a poor score on this axis drags the whole decision down. " +
                 "0 = ignored, 1 = normal, >1 = amplified veto.")]
        [Range(0f, 3f)]
        [SerializeField]
        private float weight = 1f;

        public float Weight => weight;
        public string Description => description;

        /// <summary>
        /// Raw, un-curved input for this axis. Implementations must return a value
        /// in [0, 1] (it is clamped anyway).
        /// </summary>
        protected abstract float GetRawScore(in UtilityContext context);

        /// <summary>Final 0..1 score: raw input clamped, then run through the response curve.</summary>
        public float Score(in UtilityContext context)
        {
            float raw = Mathf.Clamp01(GetRawScore(in context));
            float shaped = responseCurve != null && responseCurve.length > 0
                ? responseCurve.Evaluate(raw)
                : raw;
            return Mathf.Clamp01(shaped);
        }
    }
}
