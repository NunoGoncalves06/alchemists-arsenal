using System.Collections;
using UnityEngine;
using AlchemistsArsenal.Crafting;
using AlchemistsArsenal.PhysicsKit;

namespace AlchemistsArsenal.DebugTools
{
    /// <summary>
    /// The pestle's strike speed has one source. <see cref="PrepBench.FallSpeed"/> is what
    /// the dial predicts; a real body of the pestle's own mass, drag and shape is dropped
    /// onto a plank and its measured impact (<see cref="Impact.Vertical"/>, what the ledger
    /// is written from) must agree within a tenth of a metre a second, from rest and from
    /// a release that is still rising.
    /// </summary>
    public class PrepStrikeSimulationTest : MonoBehaviour, ISimulationSuite
    {
        public bool Done { get; private set; }

        private int _pass, _fail;

        private const float Damping = 0.05f;   // the pestle's linearDamping in PrepBench.BuildPestle

        private void Start()
        {
            Debug.Log("<color=cyan><b>=== PREP STRIKE SIMULATION ===</b></color>");
            StartCoroutine(Run());
        }

        private void Check(bool ok, string what)
        {
            if (ok) { _pass++; Debug.Log($"<color=green>✔</color> {what}"); }
            else { _fail++; Debug.LogError($"❌ {what}"); }
        }

        private IEnumerator Run()
        {
            var plank = new GameObject("StrikeTestPlank");
            plank.transform.position = new Vector3(500f, 0f, 0f);
            var box = plank.AddComponent<BoxCollider2D>();
            box.size = new Vector2(4f, 0.4f);
            box.offset = new Vector2(0f, -0.2f);   // the top face is at y = 0

            foreach (float vyUp in new[] { 0f, 1.6f })
            {
                foreach (float drop in new[] { 0.8f, 1.5f, 2.5f, 3.5f })
                {
                    float measured = -1f;
                    var body = new GameObject("StrikeTestPestle");
                    body.transform.position = new Vector3(500f, drop + 1.3f, 0f);   // the capsule's foot is 1.3 below its middle
                    var rb = body.AddComponent<Rigidbody2D>();
                    rb.mass = 2f;
                    rb.linearDamping = Damping;
                    rb.freezeRotation = true;
                    rb.gravityScale = 0f;   // held in place until it is let go
                    var cap = body.AddComponent<CapsuleCollider2D>();
                    cap.size = new Vector2(0.5f, 2.6f);
                    var sensor = body.AddComponent<ImpactSensor2D>();
                    sensor.Configure(~0, 0.1f);
                    sensor.OnImpact += (_, hit) => { if (measured < 0f) measured = hit.Vertical; };
                    yield return new WaitForFixedUpdate();   // placed; now let go, rising or not
                    rb.linearVelocity = new Vector2(0f, vyUp);
                    rb.gravityScale = 1f;

                    for (int i = 0; i < 300 && measured < 0f; i++) yield return new WaitForFixedUpdate();
                    float predicted = PrepBench.FallSpeed(drop, vyUp, Damping, Time.fixedDeltaTime);
                    Check(measured >= 0f && Mathf.Abs(measured - predicted) <= 0.1f,
                        $"strike: dropped {drop:0.0} m, rising at {vyUp:0.0} m/s: predicted {predicted:0.00}, hit at {measured:0.00} m/s");
                    Destroy(body);
                    yield return null;
                }
            }
            Destroy(plank);
            Done = true;
            Debug.Log($"<color=cyan><b>=== DONE — {_pass} pass, {_fail} fail ===</b></color>");
        }
    }
}
