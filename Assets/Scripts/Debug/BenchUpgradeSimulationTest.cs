using UnityEngine;
using AlchemistsArsenal.Combat;
using AlchemistsArsenal.Crafting;
using AlchemistsArsenal.Data;

namespace AlchemistsArsenal.DebugTools
{
    /// <summary>
    /// The bench machines' arithmetic: the doser measures to the middle of the band,
    /// the crusher's drop lands a clean strike, and the drying rack turns a bench of
    /// wilted, thin leaves into sound, potent ones. Pure functions; no bench is built.
    /// </summary>
    public class BenchUpgradeSimulationTest : MonoBehaviour, ISimulationSuite
    {
        public bool Done { get; private set; }

        private int _pass, _fail;

        private void Start()
        {
            Debug.Log("<color=cyan><b>=== BENCH UPGRADE SIMULATION ===</b></color>");
            TestDoser();
            TestCrusher();
            TestDryingRack();
            Done = true;
            Debug.Log($"<color=cyan><b>=== DONE — {_pass} pass, {_fail} fail ===</b></color>");
        }

        private void Check(bool ok, string what)
        {
            if (ok) { _pass++; Debug.Log($"<color=green>✔</color> {what}"); }
            else { _fail++; Debug.LogError($"❌ {what}"); }
        }

        private void TestDoser()
        {
            bool ok = true;
            foreach (int cap in new[] { 60, 98, 140 })
            {
                float fill = BottlingBench.DoseFor(cap) / (float)cap;
                ok &= fill >= BottlingBench.TargetLow && fill <= BottlingBench.TargetHigh;
            }
            Check(ok, "The doser measures every flask size to inside the gold lines");
        }

        private void TestCrusher()
        {
            float v = Mathf.Sqrt(2f * Mathf.Abs(Physics2D.gravity.y) * PrepBench.CrusherDrop);
            Check(Mathf.Abs(v - PrepBench.IdealStrike) < 0.05f, $"The crusher's drop lands at the ideal strike ({v:0.00} m/s)");
            Check(PrepBench.CrusherDrop < PrepBench.MaxLift, "The crusher's drop is within the frame's lift");
        }

        private void TestDryingRack()
        {
            var rng = new System.Random(7);
            int wiltBare = 0, wiltRack = 0, minRack = 9;
            float potBare = 0f, potRack = 0f;
            const int N = 400;
            for (int i = 0; i < N; i++)
            {
                HerbData bare = PrepBench.Roll(ElementType.Nature, rng, rack: false);
                HerbData dried = PrepBench.Roll(ElementType.Nature, rng, rack: true);
                if (bare.Wilted) wiltBare++;
                if (dried.Wilted) wiltRack++;
                potBare += bare.Potency;
                potRack += dried.Potency;
                minRack = Mathf.Min(minRack, dried.Potency);
                Object.Destroy(bare);
                Object.Destroy(dried);
            }
            Check(wiltRack == 0 && minRack >= 2, $"On the rack no leaf wilts and none is thin (lowest potency {minRack})");
            Check(wiltBare >= N * 0.3f, $"Without the rack many leaves come wilted ({wiltBare} of {N})");
            Check(potRack / N >= potBare / N + 0.8f, $"The rack makes the leaves clearly more potent ({potBare / N:0.00} → {potRack / N:0.00})");
        }
    }
}
