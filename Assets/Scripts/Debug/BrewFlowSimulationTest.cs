using UnityEngine;
using AlchemistsArsenal.Combat;
using AlchemistsArsenal.Data;
using AlchemistsArsenal.Systems;

namespace AlchemistsArsenal.DebugTools
{
    /// <summary>
    /// The order's way through the shop, one bench at a time: Steep, Kiln, Prep,
    /// Cauldron, Bottling, Delivery, Done. Checks the stage chain, where a fresh
    /// order starts, that an order waits at exactly one bench, that the morning is
    /// only done once every flask is handed over, and who a flask may be handed to.
    /// Runs on the live <see cref="CraftingManager"/> before day one (no orders yet)
    /// and clears it after.
    /// </summary>
    public class BrewFlowSimulationTest : MonoBehaviour, ISimulationSuite
    {
        public bool Done { get; private set; }

        private int _pass, _fail;

        private void Start()
        {
            Debug.Log("<color=cyan><b>=== BREW FLOW SIMULATION ===</b></color>");
            TestStageChain();
            TestFirstStage();
            TestOneBenchAtATime();
            TestHandOver();
            Done = true;
            Debug.Log($"<color=cyan><b>=== DONE — {_pass} pass, {_fail} fail ===</b></color>");
        }

        private void Check(bool ok, string what)
        {
            if (ok) { _pass++; Debug.Log($"<color=green>✔</color> {what}"); }
            else { _fail++; Debug.LogError($"❌ {what}"); }
        }

        private void TestStageChain()
        {
            BrewStage[] chain = { BrewStage.Steep, BrewStage.Kiln, BrewStage.Prep, BrewStage.Cauldron,
                                  BrewStage.Bottling, BrewStage.Delivery, BrewStage.Done };
            bool ok = true;
            for (int i = 0; i < chain.Length - 1; i++)
                ok &= CraftingManager.Next(chain[i]) == chain[i + 1];
            Check(ok, "Each bench hands on to the next: Steep, Kiln, Prep, Cauldron, Bottling, Delivery, Done");
            Check(CraftingManager.Next(BrewStage.Done) == BrewStage.Done, "A delivered order stays done");
        }

        private void TestFirstStage()
        {
            Check(CraftingManager.FirstStageFor(0) == BrewStage.Prep, "On the first road the grain comes malted: an order starts at Prep");
            Check(CraftingManager.FirstStageFor(1) == BrewStage.Steep, "From the second road an order starts at the Steep bench");
        }

        private void TestOneBenchAtATime()
        {
            CraftingManager cm = CraftingManager.Instance;
            if (cm == null) { Check(false, "A CraftingManager to test with"); return; }
            // StartOrder draws an order id: keep the harness's seeded random stream untouched.
            Random.State random = Random.state;
            cm.ClearOrder();
            ActiveOrder a = cm.StartOrder(new ContractRecord { accepted = true, element = ElementType.Fire, heroId = "h1", heroName = "One" });
            ActiveOrder b = cm.StartOrder(new ContractRecord { accepted = true, element = ElementType.Water, heroId = "h2", heroName = "Two" });
            a.stage = b.stage = BrewStage.Steep;

            Check(cm.Waiting(BrewStage.Steep) == a, "A bench takes the oldest order waiting at it");
            cm.Advance(a);
            Check(a.stage == BrewStage.Kiln && cm.Waiting(BrewStage.Kiln) == a && cm.Waiting(BrewStage.Steep) == b,
                  "Advanced, the order leaves the Steep bench for the Kiln and the next one is up at Steep");
            Check(cm.CountAt(BrewStage.Steep) == 1 && cm.CountAt(BrewStage.Kiln) == 1, "Each order waits at exactly one bench");

            while (a.stage != BrewStage.Delivery) cm.Advance(a);
            Check(!a.Finished && !cm.AllDone, "A sealed flask is not done until it is handed over");
            while (!b.Finished) cm.Advance(b);
            cm.Advance(a);
            Check(a.Finished && cm.AllDone, "Handed over, every order is done and the morning can be sent");
            cm.ClearOrder();
            Random.state = random;
        }

        private void TestHandOver()
        {
            var o = new ActiveOrder("T", "Test Flask", ElementType.Nature) { heroId = "h1", stage = BrewStage.Bottling };
            Check(!o.CanHandTo("h1"), "A flask still at Bottling cannot be handed over");
            o.stage = BrewStage.Delivery;
            Check(o.CanHandTo("h1"), "At Delivery it goes to the fighter who ordered it");
            Check(!o.CanHandTo("h2") && !o.CanHandTo(""), "And to nobody else");
        }
    }
}
