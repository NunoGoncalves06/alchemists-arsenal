using System;
using UnityEngine;

namespace AlchemistsArsenal.PhysicsKit
{
    /// <summary>
    /// Turns pointer presses into grabs of <see cref="Grabbable2D"/> bodies under the
    /// pointer, for one bench. Reads input in <c>Update</c> and only moves joint
    /// targets; every force is the joint's, inside the physics step.
    /// </summary>
    public class PointerGrabber : MonoBehaviour
    {
        [SerializeField] private Camera cam;
        [SerializeField] private float pickRadius = 0.22f;

        private int _mask = GameLayers.ShopPropMask;
        private Grabbable2D _held;
        private readonly Collider2D[] _hits = new Collider2D[8];

        /// <summary>Only grab while this returns true (the bench is the open tab).</summary>
        public Func<bool> Enabled;

        public Grabbable2D Held => _held;
        public event Action<Grabbable2D> OnPicked;

        public void Configure(Camera camera, int layerMask, float radius = 0.22f)
        {
            cam = camera;
            _mask = layerMask;
            pickRadius = radius;
        }

        private void Update()
        {
            bool on = Enabled == null || Enabled();
            if (!on)
            {
                Drop();
                return;
            }

            Vector2 p = Pointer.World(cam);
            if (_held != null)
            {
                if (!Pointer.Held) Drop();
                else _held.DragTo(p);
                return;
            }

            if (Pointer.PressedThisFrame && !Pointer.OverUI)
            {
                Grabbable2D g = Pick(p);
                if (g != null)
                {
                    g.BeginGrab(p);
                    if (g.IsHeld) { _held = g; OnPicked?.Invoke(g); }
                }
            }
        }

        /// <summary>The nearest grabbable within the pick radius of <paramref name="p"/>.</summary>
        public Grabbable2D Pick(Vector2 p)
        {
            int n = Physics2D.OverlapCircle(p, pickRadius, PhysicsQuery.Any(_mask), _hits);
            Grabbable2D best = null;
            float bestSqr = float.MaxValue;
            for (int i = 0; i < n; i++)
            {
                var rb = _hits[i].attachedRigidbody;
                if (rb == null) continue;
                var g = rb.GetComponent<Grabbable2D>();
                if (g == null || !g.CanGrab) continue;
                float sq = (rb.position - p).sqrMagnitude;
                if (sq < bestSqr) { bestSqr = sq; best = g; }
            }
            return best;
        }

        public void Drop()
        {
            if (_held == null) return;
            _held.Release();
            _held = null;
        }

        private void OnDisable() => Drop();
    }
}
