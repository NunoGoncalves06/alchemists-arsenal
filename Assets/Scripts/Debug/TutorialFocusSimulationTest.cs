using UnityEngine;
using AlchemistsArsenal.Crafting;
using AlchemistsArsenal.Data;
using AlchemistsArsenal.Systems;

namespace AlchemistsArsenal.DebugTools
{
    /// <summary>
    /// The Steep lessons' rules: the lid lesson is for the line (all the grain in), never
    /// before it; and nothing at the jar while it sprouts sends the player away from it.
    /// </summary>
    public class TutorialFocusSimulationTest : MonoBehaviour, ISimulationSuite
    {
        public bool Done { get; private set; }

        private int _pass, _fail;

        private void Start()
        {
            Debug.Log("<color=cyan><b>=== TUTORIAL FOCUS SIMULATION ===</b></color>");
            int line = QualityBudget.GrainTarget;
            Check(!SteepBench.AtTheLine(SteepStep.Filling, line - 1), "steep: one grain short of the line is not the line");
            Check(SteepBench.AtTheLine(SteepStep.Filling, line), "steep: the target number of grains is the line");
            Check(SteepBench.AtTheLine(SteepStep.Filling, line + 3), "steep: past the target is still at the line");
            Check(!SteepBench.AtTheLine(SteepStep.Soaking, line) && !SteepBench.AtTheLine(SteepStep.Waiting, line),
                "steep: the line only means something while filling");

            bool kept = true, said = true;
            foreach (bool vat in new[] { false, true })
                foreach (bool turning in new[] { false, true })
                {
                    string h = SteepBench.SproutingHint(vat, turning);
                    kept &= !string.IsNullOrEmpty(h) && !h.Contains("another bench");
                    said &= vat ? h.Contains("vat") : turning ? h.Contains("turning") : h.StartsWith("Sprouting");
                }
            Check(kept, "steep: no sprouting hint sends the player to another bench");
            Check(said, "steep: the sprouting hint still says what the bench is doing");

            Done = true;
            Debug.Log($"<color=cyan><b>=== DONE — {_pass} pass, {_fail} fail ===</b></color>");
        }

        private void Check(bool ok, string what)
        {
            if (ok) { _pass++; Debug.Log($"<color=green>✔</color> {what}"); }
            else { _fail++; Debug.LogError($"❌ {what}"); }
        }
    }
}
