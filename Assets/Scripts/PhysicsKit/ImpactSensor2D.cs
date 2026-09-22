using System;
using UnityEngine;

namespace AlchemistsArsenal.PhysicsKit
{
    /// <summary>One collision, measured.</summary>
    public readonly struct Impact
    {
        /// <summary>Closing speed along the contact normal (m/s). What a strike "feels" like.</summary>
        public readonly float Speed;
        /// <summary>Sum of the solver's normal impulses at the contacts (N·s).</summary>
        public readonly float Impulse;
        public readonly Vector2 Point;
        public readonly Collider2D Other;

        public Impact(float speed, float impulse, Vector2 point, Collider2D other)
        {
            Speed = speed; Impulse = impulse; Point = point; Other = other;
        }
    }

    /// <summary>
    /// Raises <see cref="OnImpact"/> for every collision this body starts, with how
    /// hard it hit. The mortar reads pestle strikes from it; the flask reads the cork
    /// seating. Measuring the real contact (rather than a timing bar) is what makes
    /// "strike cleanly" a physical skill.
    /// </summary>
    [DisallowMultipleComponent]
    public class ImpactSensor2D : MonoBehaviour
    {
        [Tooltip("Collisions slower than this (m/s) are resting contact, not impacts.")]
        [SerializeField] private float minSpeed = 0.4f;
        [SerializeField] private int layerMask = ~0;

        private readonly ContactPoint2D[] _contacts = new ContactPoint2D[8];

        public event Action<ImpactSensor2D, Impact> OnImpact;

        public void Configure(int acceptLayers, float minimumSpeed)
        {
            layerMask = acceptLayers;
            minSpeed = minimumSpeed;
        }

        private void OnCollisionEnter2D(Collision2D c)
        {
            if (c.collider == null || ((1 << c.collider.gameObject.layer) & layerMask) == 0) return;

            int n = c.GetContacts(_contacts);
            float impulse = 0f, speed = 0f;
            Vector2 point = n > 0 ? _contacts[0].point : (Vector2)transform.position;
            for (int i = 0; i < n; i++)
            {
                impulse += _contacts[i].normalImpulse;
                speed = Mathf.Max(speed, Mathf.Abs(Vector2.Dot(c.relativeVelocity, _contacts[i].normal)));
            }
            if (n == 0) speed = c.relativeVelocity.magnitude;
            if (speed < minSpeed) return;

            OnImpact?.Invoke(this, new Impact(speed, impulse, point, c.collider));
        }
    }
}
