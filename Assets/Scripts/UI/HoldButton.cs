using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace AlchemistsArsenal.UI
{
    /// <summary>
    /// Press-and-hold control. uGUI's <c>Button</c> only reports a completed click,
    /// and some crafting steps — pouring a flask to the line, working a bellows —
    /// are about how long you hold and when you let go, so they need the down/up
    /// edges rather than the click.
    ///
    /// Added alongside a normal Button on the same object: the EventSystem
    /// dispatches pointer events to every handler on the GameObject, so the Button
    /// still gives the visual press state while this reports the edges. It does NOT
    /// see the Button's <c>interactable</c> flag, so the owner still has to guard
    /// its own state in <see cref="OnPress"/>.
    /// </summary>
    public class HoldButton : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
    {
        public Action OnPress, OnRelease;

        /// <summary>True while the pointer is held down on this control.</summary>
        public bool Held { get; private set; }

        public void OnPointerDown(PointerEventData _)
        {
            if (Held) return;
            Held = true;
            OnPress?.Invoke();
        }

        public void OnPointerUp(PointerEventData _) => Release();

        // Dragging off the control counts as letting go — otherwise the flask keeps
        // filling while the cursor sits somewhere else entirely.
        public void OnPointerExit(PointerEventData _) => Release();

        private void OnDisable() => Release();

        private void Release()
        {
            if (!Held) return;
            Held = false;
            OnRelease?.Invoke();
        }
    }
}
