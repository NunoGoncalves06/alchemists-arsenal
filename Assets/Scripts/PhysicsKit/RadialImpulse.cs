using UnityEngine;

namespace AlchemistsArsenal.PhysicsKit
{
    /// <summary>
    /// The one radial-impulse rule, shared by bomb blasts, boss slams and the
    /// cauldron's pops. Falloff is inverse-distance shaped and reaches exactly 0 at
    /// the radius edge: <c>(1 - t) / (1 + 2t)</c> for <c>t = d / r</c>, so the core of
    /// a blast shoves far harder than its rim (the old rule was a straight line,
    /// which made a glancing hit push half as hard as a direct one).
    ///
    /// Impulses are divided by nothing: a heavy body simply moves less, because
    /// that is what an impulse on a heavy body does.
    /// </summary>
    public static class RadialImpulse
    {
        /// <summary>0..1 strength at <paramref name="distance"/> from the epicentre.</summary>
        public static float Falloff(float distance, float radius)
        {
            if (radius <= 0.0001f || distance >= radius) return 0f;
            float t = Mathf.Clamp01(distance / radius);
            return (1f - t) / (1f + 2f * t);
        }

        /// <summary>
        /// Push <paramref name="body"/> away from <paramref name="epicenter"/>. A body
        /// sitting exactly on the epicentre is pushed along <paramref name="fallbackDir"/>
        /// (callers pass something deterministic, never a random direction). Returns
        /// the impulse applied.
        /// </summary>
        public static Vector2 Apply(Rigidbody2D body, Vector2 epicenter, float radius, float strength,
            Vector2 fallbackDir)
        {
            if (body == null || body.bodyType != RigidbodyType2D.Dynamic) return Vector2.zero;
            Vector2 to = body.position - epicenter;
            float d = to.magnitude;
            float k = Falloff(d, radius);
            if (k <= 0f) return Vector2.zero;
            Vector2 dir = d > 0.001f ? to / d
                : (fallbackDir.sqrMagnitude > 0.0001f ? fallbackDir.normalized : Vector2.up);
            Vector2 impulse = dir * (strength * k);
            body.AddForceAtPosition(impulse, epicenter, ForceMode2D.Impulse);
            return impulse;
        }
    }
}
