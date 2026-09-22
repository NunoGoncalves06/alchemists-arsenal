using UnityEngine;

namespace AlchemistsArsenal.PhysicsKit
{
    /// <summary>
    /// A draught through the shop: every so often it nudges a hanging body (a herb
    /// bundle on a <see cref="HingeJoint2D"/>) with a small torque, and gravity and
    /// the joint do the rest, so the bundles swing and settle like real ones rather
    /// than playing a canned sway. Applied on the physics step; its randomness is
    /// its own seeded stream.
    /// </summary>
    [RequireComponent(typeof(Rigidbody2D))]
    public class Draft2D : MonoBehaviour
    {
        private Rigidbody2D _rb;
        private System.Random _rng;
        private float _next;

        public static Draft2D Attach(GameObject go, int seed)
        {
            var d = go.AddComponent<Draft2D>();
            d._rng = new System.Random(seed);
            return d;
        }

        private void Awake() => _rb = GetComponent<Rigidbody2D>();

        private void FixedUpdate()
        {
            if (_rb == null || _rng == null) return;
            _next -= Time.fixedDeltaTime;
            if (_next > 0f) return;
            _next = 1.5f + (float)_rng.NextDouble() * 3f;
            float push = ((float)_rng.NextDouble() - 0.5f) * 0.02f;
            _rb.AddTorque(push, ForceMode2D.Impulse);
        }
    }
}
