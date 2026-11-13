using UnityEngine;

namespace AlchemistsArsenal.Combat
{
    /// <summary>
    /// The flask at a fighter's belt, shown while they have one to throw. After the
    /// last throw the hand is empty: a fighter walking off the field dry carries nothing.
    /// The ammo has already gone down when <see cref="UtilityAI_CombatController.OnBombThrowRequested"/>
    /// fires, so the count read there is what is left after this throw.
    /// </summary>
    public sealed class BeltFlaskView : MonoBehaviour
    {
        private UtilityAI_CombatController _ai;
        private SpriteRenderer[] _renderers;

        /// <summary>True while the belt flask is drawn.</summary>
        public bool Visible { get; private set; } = true;

        public void Bind(UtilityAI_CombatController ai)
        {
            if (_ai != null) _ai.OnBombThrowRequested -= OnThrow;
            _ai = ai;
            _renderers = GetComponentsInChildren<SpriteRenderer>(true);
            if (_ai != null) _ai.OnBombThrowRequested += OnThrow;
            Show(true);   // built only for a fighter handed a flask; the ammo may load after this
        }

        private void OnThrow(BombThrowRequest r)
        {
            if (_ai != null && _ai.FlasksLeft <= 0) Show(false);
        }

        private void Show(bool on)
        {
            Visible = on;
            if (_renderers == null) return;
            foreach (var r in _renderers) if (r != null) r.enabled = on;
        }

        private void OnDestroy()
        {
            if (_ai != null) _ai.OnBombThrowRequested -= OnThrow;
        }
    }
}
