using System;
using UnityEngine;

namespace AlchemistsArsenal.PhysicsKit
{
    /// <summary>
    /// A body the player can pick up and throw. Holding it is a
    /// <see cref="TargetJoint2D"/> spring toward the pointer, so it is still a
    /// physics body the whole time: it collides with the bench while carried, is
    /// heavier to swing if it is heavy, and keeps its velocity when let go (so
    /// flicking a leaf toward the mortar throws it).
    /// </summary>
    [RequireComponent(typeof(Rigidbody2D))]
    [DisallowMultipleComponent]
    public class Grabbable2D : MonoBehaviour
    {
        [Tooltip("Spring stiffness of the hold (Hz). Higher follows the pointer more tightly.")]
        [SerializeField] private float frequency = 7f;
        [SerializeField] [Range(0f, 1f)] private float dampingRatio = 0.9f;
        [Tooltip("Maximum pull, per unit of mass, so light and heavy things feel alike to hold.")]
        [SerializeField] private float maxForcePerMass = 90f;

        private Rigidbody2D _rb;
        private TargetJoint2D _joint;

        public bool IsHeld => _joint != null;
        public Rigidbody2D Body => _rb != null ? _rb : (_rb = GetComponent<Rigidbody2D>());

        /// <summary>False blocks new grabs (a leaf already settled in the mortar, say).</summary>
        public bool CanGrab { get; set; } = true;

        public event Action<Grabbable2D> OnGrabbed;
        public event Action<Grabbable2D> OnReleased;

        private void Awake() => _rb = GetComponent<Rigidbody2D>();

        public void BeginGrab(Vector2 worldPoint)
        {
            if (IsHeld || !CanGrab) return;
            _joint = gameObject.AddComponent<TargetJoint2D>();
            _joint.autoConfigureTarget = false;
            _joint.anchor = transform.InverseTransformPoint(worldPoint);
            _joint.target = worldPoint;
            _joint.frequency = frequency;
            _joint.dampingRatio = dampingRatio;
            _joint.maxForce = maxForcePerMass * Mathf.Max(0.05f, Body.mass);
            Body.WakeUp();
            OnGrabbed?.Invoke(this);
        }

        /// <summary>Move the hold point. The joint pulls toward it on the next physics step.</summary>
        public void DragTo(Vector2 worldPoint)
        {
            if (_joint != null) _joint.target = worldPoint;
        }

        public void Release()
        {
            if (_joint == null) return;
            Destroy(_joint);
            _joint = null;
            OnReleased?.Invoke(this);
        }

        private void OnDisable() => Release();
    }
}
