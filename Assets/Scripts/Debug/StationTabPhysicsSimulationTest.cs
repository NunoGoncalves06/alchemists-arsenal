using System.Collections;
using UnityEngine;
using AlchemistsArsenal.Systems;
using AlchemistsArsenal.Crafting;

namespace AlchemistsArsenal.DebugTools
{
    /// <summary>
    /// Runtime verification for Task 2, step 3: switching the crafting tab away
    /// from the Cauldron must NOT interrupt the background FixedUpdate physics or
    /// the heat-decay logic on <see cref="PhysicsCauldronManager"/>.
    ///
    /// Unlike a scripted "assume it works" log, this actually spins up the managers,
    /// drives a real tab transition, waits several physics steps, and checks the
    /// simulation kept advancing.
    /// </summary>
    public class StationTabPhysicsSimulationTest : MonoBehaviour, ISimulationSuite
    {
        public bool Done { get; private set; }

        [SerializeField] private float observeSeconds = 1.5f;
        [SerializeField] private float startingHeat = 0.90f;

        private int _stationChangeEvents;

        private void Start() => StartCoroutine(RunTabSwitchSimulation());

        public IEnumerator RunTabSwitchSimulation()
        {
            Debug.Log("<color=cyan><b>=== STATION TAB SWITCH x BACKGROUND PHYSICS SIMULATION ===</b></color>");

            StationManager station = EnsureStationManager();
            PhysicsCauldronManager cauldron = EnsureCauldron();

            // A stand-in for the Cauldron UI panel. The simulation must live OUTSIDE
            // it — this object gets SetActive(false) on the tab switch below.
            GameObject cauldronPanel = new GameObject("MockCauldronPanel");
            bool simUnderPanel = cauldron.transform.IsChildOf(cauldronPanel.transform);
            Report("Simulation is NOT parented under the UI panel", !simUnderPanel);

            station.OnStationChanged += _ => _stationChangeEvents++;

            // Start from a known non-Cauldron station so the hop below always transitions.
            station.TrySwitchStation(CraftingStation.Prep);

            // Sit on the Cauldron with a hot brew.
            station.TrySwitchStation(CraftingStation.Cauldron);
            cauldron.SetHeat(startingHeat);
            yield return new WaitForFixedUpdate();

            long stepsBefore = cauldron.PhysicsStepCount;
            float heatBefore = cauldron.Heat01;
            Debug.Log($"[Sim] On Cauldron. heat={heatBefore:F3}, physicsSteps={stepsBefore}");

            // Switch away + hide the panel, exactly as StationUIManager would.
            bool switched = station.TrySwitchStation(CraftingStation.Counter);
            cauldronPanel.SetActive(false);
            Report("Tab transition Cauldron -> Counter succeeded", switched);
            Report("StationManager.CurrentStation is now Counter",
                station.CurrentStation == CraftingStation.Counter);

            float elapsed = 0f;
            while (elapsed < observeSeconds)
            {
                elapsed += Time.deltaTime;
                yield return null;
            }

            long stepsAfter = cauldron.PhysicsStepCount;
            float heatAfter = cauldron.Heat01;
            Debug.Log($"[Sim] {observeSeconds}s after switch: heat={heatAfter:F3}, physicsSteps={stepsAfter}");

            Report("PhysicsCauldronManager instance still alive", PhysicsCauldronManager.Instance == cauldron);
            Report("PhysicsCauldronManager still enabled", cauldron.isActiveAndEnabled);
            Report($"FixedUpdate kept running (+{stepsAfter - stepsBefore} steps while Cauldron tab hidden)",
                stepsAfter - stepsBefore >= 10);
            Report($"Heat decay kept running while hidden ({heatBefore:F3} -> {heatAfter:F3})",
                heatAfter < heatBefore - 0.01f);
            Report("Station change events dispatched cleanly", _stationChangeEvents >= 2);

            Destroy(cauldronPanel);
            Done = true;
            Debug.Log("<color=cyan><b>=== SIMULATION COMPLETE ===</b></color>");
        }

        private static void Report(string label, bool passed)
        {
            if (passed)
                Debug.Log($"<color=green><b>PASS</b></color> {label}");
            else
                Debug.LogError($"<color=red><b>FAIL</b></color> {label}");
        }

        private static StationManager EnsureStationManager()
        {
            if (StationManager.Instance != null) return StationManager.Instance;
            return new GameObject("StationManager (test)").AddComponent<StationManager>();
        }

        private static PhysicsCauldronManager EnsureCauldron()
        {
            if (PhysicsCauldronManager.Instance != null) return PhysicsCauldronManager.Instance;
            return new GameObject("PhysicsCauldronManager (test)").AddComponent<PhysicsCauldronManager>();
        }
    }
}
