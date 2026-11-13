using System.Collections.Generic;
using UnityEngine;

namespace AlchemistsArsenal.Combat
{
    /// <summary>
    /// The spots a boss has marked for its next blow, while the mark is on the
    /// ground. A telegraph is only fair if the people standing in it can react, and
    /// the heroes are not driven by the player, so they read this the way a player
    /// reads the decal: after a short reaction delay, anyone standing inside a mark
    /// steps out of it (<see cref="AdventurerMovementController"/>).
    ///
    /// Game-time based, so it behaves the same under the headless harness.
    /// </summary>
    public static class DangerZones
    {
        /// <summary>How long a hero takes to notice a new mark.</summary>
        public const float ReactionSeconds = 0.25f;

        private struct Zone
        {
            public Vector2 Center;
            public float Radius, From, Until;
        }

        private static readonly List<Zone> _zones = new List<Zone>();

        /// <summary>Mark a circle that will be hit <paramref name="seconds"/> from now.</summary>
        public static void Mark(Vector2 center, float radius, float seconds)
        {
            Prune();
            float now = Time.time;
            _zones.Add(new Zone { Center = center, Radius = radius, From = now + ReactionSeconds, Until = now + seconds + 0.05f });
        }

        /// <summary>
        /// The way out of every mark <paramref name="p"/> is standing in (summed, each
        /// weighted by how deep inside it is), or zero when it is standing in none.
        /// </summary>
        public static Vector2 EscapeFrom(Vector2 p, float margin = 0.6f)
        {
            float now = Time.time;
            Vector2 away = Vector2.zero;
            for (int i = 0; i < _zones.Count; i++)
            {
                Zone z = _zones[i];
                if (now < z.From || now > z.Until) continue;
                Vector2 d = p - z.Center;
                float reach = z.Radius + margin;
                float dist = d.magnitude;
                if (dist >= reach) continue;
                // Standing dead centre: any way out is as good as another, so pick a fixed one.
                Vector2 dir = dist > 0.001f ? d / dist : Vector2.down;
                away += dir * (1f - dist / reach + 0.35f);
            }
            return away;
        }

        public static int Count
        {
            get { Prune(); return _zones.Count; }
        }

        public static void Clear() => _zones.Clear();

        private static void Prune()
        {
            float now = Time.time;
            _zones.RemoveAll(z => z.Until < now);
        }
    }
}
