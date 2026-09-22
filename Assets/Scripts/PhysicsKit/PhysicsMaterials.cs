using System.Collections.Generic;
using UnityEngine;

namespace AlchemistsArsenal.PhysicsKit
{
    /// <summary>
    /// The shared <see cref="PhysicsMaterial2D"/> set, built once in code. A leaf
    /// should skid and settle, glass should barely bounce, a coin should ring off the
    /// counter, ice should slide. Before this every body in the game used the
    /// default material.
    /// </summary>
    public static class PhysicsMaterials
    {
        private static readonly Dictionary<string, PhysicsMaterial2D> _cache = new Dictionary<string, PhysicsMaterial2D>();

        public static PhysicsMaterial2D Herb => Get("Herb", friction: 0.8f, bounce: 0.04f);   // a leaf thuds, it does not bounce
        public static PhysicsMaterial2D Wood => Get("Wood", friction: 0.6f, bounce: 0.1f);
        public static PhysicsMaterial2D Stone => Get("Stone", friction: 0.7f, bounce: 0.04f);
        public static PhysicsMaterial2D Glass => Get("Glass", friction: 0.12f, bounce: 0.08f);
        public static PhysicsMaterial2D Cork => Get("Cork", friction: 0.9f, bounce: 0.02f);
        public static PhysicsMaterial2D Coin => Get("Coin", friction: 0.35f, bounce: 0.42f);
        public static PhysicsMaterial2D Droplet => Get("Droplet", friction: 0f, bounce: 0f);
        public static PhysicsMaterial2D Iron => Get("Iron", friction: 0.3f, bounce: 0.05f);
        public static PhysicsMaterial2D Ice => Get("Ice", friction: 0.01f, bounce: 0.02f);
        public static PhysicsMaterial2D Body => Get("Body", friction: 0.2f, bounce: 0f);

        private static PhysicsMaterial2D Get(string name, float friction, float bounce)
        {
            if (_cache.TryGetValue(name, out var m) && m != null) return m;
            m = new PhysicsMaterial2D(name) { friction = friction, bounciness = bounce };
            _cache[name] = m;
            return m;
        }
    }
}
