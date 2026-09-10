using UnityEngine;
using AlchemistsArsenal.Combat;
using AlchemistsArsenal.Crafting;

namespace AlchemistsArsenal.Core
{
    /// <summary>
    /// The morning/evening shop world: the physics cauldron + a handful of herb
    /// bodies to stir + a framing camera. Lives under a loop-owned root, never
    /// under a UI panel (DESIGN.md §11). Destroying the root tears it all down.
    /// </summary>
    public class ShopWorld : MonoBehaviour
    {
        private const int HerbLayer = 0; // Default — Phase 0 keeps it simple

        public Camera WorldCamera { get; private set; }

        public void Build()
        {
            transform.position = Vector3.zero;

            var camGo = new GameObject("ShopCamera") { tag = "MainCamera" }; // PhysicsCauldronManager reads Camera.main
            WorldCamera = camGo.AddComponent<Camera>();
            WorldCamera.transform.SetParent(transform, false);
            WorldCamera.transform.position = new Vector3(0f, 0.5f, -10f);
            WorldCamera.orthographic = true;
            WorldCamera.orthographicSize = 4.2f;
            WorldCamera.clearFlags = CameraClearFlags.SolidColor;
            WorldCamera.backgroundColor = new Color(0.10f, 0.07f, 0.12f);
            WorldCamera.depth = -1;

            var potGo = new GameObject("Cauldron");
            potGo.transform.SetParent(transform, false);
            var cauldron = potGo.AddComponent<PhysicsCauldronManager>();
            cauldron.Configure(1 << HerbLayer);
            PixelArt.AddDisc(potGo, new Color(0.18f, 0.12f, 0.10f), 0, 3.4f);

            for (int i = 0; i < 6; i++)
            {
                float ang = i / 6f * Mathf.PI * 2f;
                var herb = new GameObject($"Herb_{i}");
                herb.transform.SetParent(transform, false);
                herb.layer = HerbLayer;
                herb.transform.position = new Vector3(Mathf.Cos(ang) * 1.1f, Mathf.Sin(ang) * 1.1f, 0f);

                var rb = herb.AddComponent<Rigidbody2D>();
                rb.gravityScale = 0f;
                rb.linearDamping = 1.2f;
                rb.angularDamping = 0.8f;

                herb.AddComponent<CircleCollider2D>().radius = 0.18f;
                PixelArt.AddDisc(herb, i % 2 == 0 ? new Color(0.37f, 0.65f, 0.22f) : new Color(0.25f, 0.56f, 0.82f), 3, 0.34f);
            }
        }
    }
}
