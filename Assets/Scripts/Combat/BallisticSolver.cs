using UnityEngine;

namespace AlchemistsArsenal.Combat
{
    /// <summary>
    /// Closed-form 2D ballistic maths. Pure and deterministic — no MonoBehaviour,
    /// no scene access — so it can be unit-tested and reused by VFX/preview code.
    /// </summary>
    public static class BallisticSolver
    {
        /// <summary>
        /// Launch velocity to hit <paramref name="target"/> from <paramref name="origin"/>
        /// at a fixed <paramref name="speed"/> under constant <paramref name="gravity"/>
        /// (expected (0, negative)). Standard projectile range equation solved for the
        /// launch angle:
        /// <code>
        /// tan(theta) = ( v^2 +/- sqrt( v^4 - g*(g*x^2 + 2*y*v^2) ) ) / (g*x)
        /// </code>
        /// Returns false when the discriminant is negative (target out of range at
        /// that speed) — callers should fall back to <see cref="SolveLob"/>.
        /// </summary>
        public static bool TrySolveArc(Vector2 origin, Vector2 target, float speed, Vector2 gravity,
            bool preferHighArc, out Vector2 launchVelocity)
        {
            launchVelocity = Vector2.zero;
            Vector2 delta = target - origin;

            float g = -gravity.y; // positive magnitude of downward pull
            if (g <= 0.0001f)
            {
                launchVelocity = delta.sqrMagnitude > 0.0001f ? delta.normalized * speed : Vector2.zero;
                return true;
            }

            float x = Mathf.Abs(delta.x);
            float y = delta.y;
            float v2 = speed * speed;

            if (x < 0.0001f)
            {
                // Straight up / down.
                launchVelocity = new Vector2(0f, Mathf.Sign(Mathf.Approximately(y, 0f) ? 1f : y) * speed);
                return true;
            }

            float discriminant = v2 * v2 - g * (g * x * x + 2f * y * v2);
            if (discriminant < 0f) return false;

            float sqrtDisc = Mathf.Sqrt(discriminant);
            float tanTheta = preferHighArc
                ? (v2 + sqrtDisc) / (g * x)
                : (v2 - sqrtDisc) / (g * x);

            float theta = Mathf.Atan(tanTheta);
            float dirX = Mathf.Sign(delta.x);

            launchVelocity = new Vector2(
                dirX * speed * Mathf.Cos(theta),
                speed * Mathf.Sin(theta));
            return true;
        }

        /// <summary>
        /// Guaranteed-solvable lob: choose a time-of-flight from the horizontal
        /// distance and nominal speed, then solve v0 from
        /// <c>p = p0 + v0·t + ½·g·t²</c>.
        /// </summary>
        public static Vector2 SolveLob(Vector2 origin, Vector2 target, float nominalSpeed, Vector2 gravity,
            float minFlightTime = 0.35f)
        {
            Vector2 delta = target - origin;
            float horizontal = Mathf.Max(Mathf.Abs(delta.x), 0.01f);
            float t = Mathf.Max(minFlightTime, horizontal / Mathf.Max(nominalSpeed, 0.01f));

            return new Vector2(
                delta.x / t,
                (delta.y - 0.5f * gravity.y * t * t) / t);
        }

        /// <summary>Position of a projectile at time <paramref name="t"/> — for tests / previews.</summary>
        public static Vector2 SamplePath(Vector2 origin, Vector2 launchVelocity, Vector2 gravity, float t) =>
            origin + launchVelocity * t + 0.5f * gravity * (t * t);
    }
}
