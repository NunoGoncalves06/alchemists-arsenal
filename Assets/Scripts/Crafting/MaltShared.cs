using System.Collections.Generic;
using UnityEngine;
using AlchemistsArsenal.Art;
using AlchemistsArsenal.Audio;
using AlchemistsArsenal.Core;
using AlchemistsArsenal.PhysicsKit;
using AlchemistsArsenal.Systems;

namespace AlchemistsArsenal.Crafting
{
    /// <summary>One grain of barley (or an empty husk) as a physics body, on the Steep or the Kiln bench.</summary>
    public sealed class Grain
    {
        public Rigidbody2D Body;
        public CircleCollider2D Collider;
        public SpriteRenderer Art;
        public bool Husk, Skimmed;
    }

    /// <summary>
    /// What the two malting benches share: the grain bodies, their art, and how they
    /// score. Both write the order's <see cref="ActiveOrder.maltPoints"/>, which is what
    /// <see cref="ActiveOrder.MaltQuality01"/> is worked out from when the kiln is done,
    /// so the budget (<see cref="Data.QualityBudget.MaltMax"/>) is the same as when they
    /// were one bench; only the station label on each line says which bench it was.
    /// </summary>
    public static class MaltShared
    {
        public const float GrainRadius = 0.1f;
        public const int OrderGrain = 11;

        public static SpriteRenderer AddArt(Transform parent, Sprite s, int order, float scale)
        {
            var go = new GameObject("Art");
            go.transform.SetParent(parent, false);
            go.transform.localScale = Vector3.one * scale;
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = s;
            sr.sharedMaterial = SpriteMaterials.For(s);
            sr.sortingOrder = order;
            return sr;
        }

        /// <summary>The bounds of the first sprite drawn under <paramref name="root"/>: what a spotlight on that object centres on.</summary>
        public static Bounds FirstArtBounds(Transform root)
        {
            if (root != null)
                foreach (var r in root.GetComponentsInChildren<SpriteRenderer>())
                    if (r.enabled && r.sprite != null) return r.bounds;
            return new Bounds(root != null ? root.position : Vector3.zero, Vector3.zero);
        }

        public static Vector2[] ToLocal(Transform t, List<Vector2> world)
        {
            var local = new Vector2[world.Count];
            for (int i = 0; i < world.Count; i++) local[i] = t.InverseTransformPoint(world[i]);
            return local;
        }

        /// <summary>The launch velocity that carries a body from <paramref name="from"/> to <paramref name="to"/> in <paramref name="t"/> seconds.</summary>
        public static Vector2 Launch(Vector2 from, Vector2 to, float t)
        {
            float g = -Physics2D.gravity.y;
            return new Vector2((to.x - from.x) / t, (to.y - from.y + 0.5f * g * t * t) / t);
        }

        public static Grain SpawnGrain(Transform parent, Vector2 at, bool husk)
        {
            var go = new GameObject(husk ? "Husk" : "Grain");
            go.transform.SetParent(parent, false);
            go.transform.position = at;
            var rb = go.AddComponent<Rigidbody2D>();
            rb.useAutoMass = true;
            rb.linearDamping = 0.1f;
            rb.angularDamping = 0.6f;
            rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            var col = go.AddComponent<CircleCollider2D>();
            col.radius = GrainRadius;
            // Sound barley is dense and sinks; an empty husk floats. The buoyancy does the sorting.
            col.density = husk ? 0.35f : 2.2f;
            col.sharedMaterial = PhysicsMaterials.Herb;
            var art = AddArt(go.transform, MaltArt.Grain(husk, 0), OrderGrain, 1f);
            GameLayers.Assign(go, GameLayers.ShopProp);
            return new Grain { Body = rb, Collider = col, Art = art, Husk = husk };
        }

        public static void SetGrainArt(Grain g, int sprout)
        {
            if (g.Art == null || g.Husk) return;
            Sprite s = MaltArt.Grain(false, sprout);
            if (g.Art.sprite == s) return;
            g.Art.sprite = s;
            g.Art.sharedMaterial = SpriteMaterials.For(s);
        }

        public static void Pay(ActiveOrder o, int points, string station, string reason)
        {
            if (o == null || points <= 0) return;
            o.ApplyBonus(points, station, reason);
            o.maltPoints += points;
        }

        public static void Charge(ActiveOrder o, int points, string station, string reason)
        {
            if (o == null || points <= 0) return;
            o.ApplyDeduction(points, station, reason);
            o.maltPoints -= points;
            AudioManager.Play(Sfx.Deny);
        }

        public static bool HasUpgrade(string id) =>
            SaveSystem.Instance != null && SaveSystem.Instance.State != null && SaveSystem.Instance.State.HasUpgrade(id);
    }
}
