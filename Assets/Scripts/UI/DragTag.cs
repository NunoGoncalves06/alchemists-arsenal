using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace AlchemistsArsenal.UI
{
    /// <summary>
    /// A piece of paper on the UI that the hand can pick up: drag it and it follows
    /// the pointer above everything else; let go over <see cref="Target"/> and
    /// <see cref="OnDropped"/> fires, anywhere else and it slides home. A plain click
    /// fires <see cref="OnClicked"/> (a shortcut, never the only way).
    ///
    /// <para>What moves is <see cref="Paper"/>, a child of this element, so the
    /// layout group the tag sits in never notices the drag. While carried, the tag
    /// gets its own sorting canvas so it draws over its neighbours.</para>
    /// </summary>
    public class DragTag : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler, IPointerClickHandler
    {
        public RectTransform Paper;
        public RectTransform Target;
        public Action OnDropped, OnClicked;
        /// <summary>Tells the target whether a tag is hovering over it (true) or has left (false).</summary>
        public Action<bool> OnHoverTarget;

        public static bool AnyCarried { get; private set; }

        private Canvas _lift;
        private Vector3 _grabOffset;
        private bool _carried, _overTarget;
        private float _homeT = 1f;
        private Vector2 _homeFrom;   // anchored position, so any anchoring slides home right

        public void OnBeginDrag(PointerEventData e)
        {
            if (Paper == null) return;
            _carried = true;
            AnyCarried = true;
            _homeT = 1f;
            Lift(true);
            _grabOffset = Paper.position - PointerWorld(e);
            Paper.localRotation = Quaternion.Euler(0f, 0f, 4f);   // held, not laid
        }

        public void OnDrag(PointerEventData e)
        {
            if (!_carried) return;
            Paper.position = PointerWorld(e) + _grabOffset;
            bool over = OverTarget(e);
            if (over != _overTarget) { _overTarget = over; OnHoverTarget?.Invoke(over); }
        }

        public void OnEndDrag(PointerEventData e)
        {
            if (!_carried) return;
            _carried = false;
            AnyCarried = false;
            if (_overTarget) { _overTarget = false; OnHoverTarget?.Invoke(false); }
            Paper.localRotation = Quaternion.identity;
            if (OverTarget(e))
            {
                Lift(false);
                Paper.anchoredPosition = Vector2.zero;
                OnDropped?.Invoke();
                return;
            }
            _homeFrom = Paper.anchoredPosition;
            _homeT = 0f;   // slide back on the rack
        }

        public void OnPointerClick(PointerEventData e)
        {
            if (e.dragging || _carried) return;
            OnClicked?.Invoke();
        }

        private void Update()
        {
            if (_homeT >= 1f || Paper == null) return;
            _homeT = Mathf.Min(1f, _homeT + Time.unscaledDeltaTime / 0.18f);
            float k = 1f - (1f - _homeT) * (1f - _homeT);
            Paper.anchoredPosition = Vector2.Lerp(_homeFrom, Vector2.zero, k);
            if (_homeT >= 1f) { Paper.anchoredPosition = Vector2.zero; Lift(false); }
        }

        private void OnDisable()
        {
            if (_carried) AnyCarried = false;
            _carried = false;
            _homeT = 1f;
            if (Paper != null) { Paper.anchoredPosition = Vector2.zero; Paper.localRotation = Quaternion.identity; }
            Lift(false);
        }

        private void Lift(bool on)
        {
            if (Paper == null) return;
            // Made once and kept: a GraphicRaycaster depends on its Canvas, so the
            // pair cannot be torn down cleanly mid-frame. Only the sorting toggles.
            if (on && _lift == null)
            {
                _lift = Paper.gameObject.AddComponent<Canvas>();
                Paper.gameObject.AddComponent<GraphicRaycaster>();
            }
            if (_lift == null) return;
            _lift.overrideSorting = on;
            _lift.sortingOrder = on ? 500 : 0;
        }

        private Vector3 PointerWorld(PointerEventData e)
        {
            RectTransformUtility.ScreenPointToWorldPointInRectangle((RectTransform)transform, e.position,
                e.pressEventCamera, out Vector3 world);
            return world;
        }

        private bool OverTarget(PointerEventData e) =>
            Target != null && Target.gameObject.activeInHierarchy &&
            RectTransformUtility.RectangleContainsScreenPoint(Target, e.position, e.pressEventCamera);
    }
}
