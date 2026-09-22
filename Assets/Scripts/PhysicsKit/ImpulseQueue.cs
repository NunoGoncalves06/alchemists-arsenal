using System.Collections.Generic;
using UnityEngine;

namespace AlchemistsArsenal.PhysicsKit
{
    /// <summary>
    /// Pushes that are decided outside the physics step (a click, a button, a
    /// collision callback) and applied on it. An impulse added straight from
    /// <c>Update</c> lands between steps, at whatever point in the frame input was
    /// read, so the same click could throw a leaf a little differently depending on
    /// frame timing. Queue it here and <see cref="Flush"/> first thing in
    /// <c>FixedUpdate</c>: every push happens at the start of a step.
    /// </summary>
    public sealed class ImpulseQueue
    {
        private struct Push
        {
            public Rigidbody2D Body;
            public Vector2 Impulse;
            public float Torque;
            public bool ResetVelocity;
        }

        private readonly List<Push> _pending = new List<Push>();

        public int Count => _pending.Count;

        /// <summary>Queue an impulse (already scaled by mass if it should be) and a spin.</summary>
        public void Add(Rigidbody2D body, Vector2 impulse, float torque = 0f, bool resetVelocity = false)
        {
            if (body == null) return;
            _pending.Add(new Push { Body = body, Impulse = impulse, Torque = torque, ResetVelocity = resetVelocity });
        }

        /// <summary>Apply everything queued. Call at the top of <c>FixedUpdate</c>.</summary>
        public void Flush()
        {
            for (int i = 0; i < _pending.Count; i++)
            {
                Push p = _pending[i];
                if (p.Body == null) continue;
                if (p.ResetVelocity) { p.Body.linearVelocity = Vector2.zero; p.Body.angularVelocity = 0f; }
                p.Body.AddForce(p.Impulse, ForceMode2D.Impulse);
                if (p.Torque != 0f) p.Body.AddTorque(p.Torque, ForceMode2D.Impulse);
            }
            _pending.Clear();
        }
    }
}
