using UnityEngine;

namespace AlchemistsArsenal.PhysicsKit
{
    /// <summary>
    /// Contact filters for overlap queries. This project has "queries hit triggers"
    /// on, so without <c>useTriggers = false</c> every sensor and effector trigger
    /// would fill a detonation's hit buffer. Callers keep their own preallocated
    /// <c>Collider2D[]</c> (no shared static buffer: a detonation can kill something
    /// whose death handler runs another query).
    /// </summary>
    public static class PhysicsQuery
    {
        /// <summary>Solid colliders on <paramref name="layerMask"/> only.</summary>
        public static ContactFilter2D Solid(int layerMask)
        {
            var f = new ContactFilter2D { useTriggers = false };
            f.SetLayerMask(layerMask);
            return f;
        }

        /// <summary>Everything on <paramref name="layerMask"/>, triggers included.</summary>
        public static ContactFilter2D Any(int layerMask)
        {
            var f = new ContactFilter2D { useTriggers = true };
            f.SetLayerMask(layerMask);
            return f;
        }
    }
}
