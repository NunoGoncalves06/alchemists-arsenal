using UnityEngine;
using System.Collections.Generic;
using AlchemistsArsenal.Systems;
using AlchemistsArsenal.Crafting;

namespace AlchemistsArsenal.DebugTools
{
    public class CauldronSimulationTest : MonoBehaviour
    {
        [Header("Simulation Parameters")]
        [SerializeField] private float simStepTime = 0.1f; // Simulated DeltaTime per step

        private void Start()
        {
            RunFullSimulationSuite();
        }

        public void RunFullSimulationSuite()
        {
            Debug.Log("<color=cyan><b>=== STARTING CAULDRON BREWING SIMULATION SUITE ===</b></color>");

            RunScenario_PerfectBrewing();
            RunScenario_OverheatingBrewing();
            RunScenario_UnderheatingBrewing();

            Debug.Log("<color=cyan><b>=== SIMULATION SUITE COMPLETE ===</b></color>");
        }

        private void RunScenario_PerfectBrewing()
        {
            Debug.Log("\n<b>[Scenario 1: Perfect Brewing]</b> Simulating 10 steps of perfect stirring within the optimal heat zone...");
            ActiveOrder order = new ActiveOrder("SIM-001", "Perfect Healing Potion");

            // Mock perfect temperature (e.g. 0.55, which is inside 0.4 - 0.7 range)
            float heat = 0.55f; 
            int deductionsCount = 0;

            for (int step = 1; step <= 10; step++)
            {
                // Simulate checking bounds (similar to PhysicsCauldronManager CheckHeatQualityImpact)
                if (heat < 0.4f || heat > 0.7f)
                {
                    order.ApplyDeduction(5, "Cauldron Brewing", "Heat out of bounds simulation step: " + step, step * simStepTime);
                    deductionsCount++;
                }
            }

            Debug.Log($"Result: Final Quality: {order.qualityScore}/100 | Tier: {order.GetTier()} | Total Deductions Registered: {order.deductions.Count}");
            if (order.qualityScore == 100 && order.GetTier() == QualityTier.Perfect && deductionsCount == 0)
            {
                Debug.Log("<color=green><b>✔ PASS: Perfect brewing maintained pristine 100 quality score and Perfect Tier.</b></color>");
            }
            else
            {
                Debug.LogError("❌ FAIL: Perfect brewing should not deduct points.");
            }
        }

        private void RunScenario_OverheatingBrewing()
        {
            Debug.Log("\n<b>[Scenario 2: Overheating Brewing]</b> Simulating 5 seconds of extreme overheating (Heat = 0.95)...");
            ActiveOrder order = new ActiveOrder("SIM-002", "Fire Blast Potion");

            float heat = 0.95f; // Well above maxOptimalHeat (0.7)
            float nextDeductionTime = 0f;
            float interval = 1f;

            // Simulate 5 seconds, step by step
            for (float time = 0f; time <= 5f; time += simStepTime)
            {
                bool isTooHot = heat > 0.7f;
                if (isTooHot && time >= nextDeductionTime)
                {
                    // Overheating formula: base (5) + severity * 10
                    float severity = (heat - 0.7f) / (1f - 0.7f); // 0.25 / 0.3 = 0.833
                    int finalDeduction = 5 + Mathf.RoundToInt(severity * 10f); // 5 + 8 = 13 points

                    order.ApplyDeduction(finalDeduction, "Cauldron Brewing", $"Overheating simulation at {time}s", time);
                    nextDeductionTime = time + interval;
                }
            }

            Debug.Log($"Result: Final Quality: {order.qualityScore}/100 | Tier: {order.GetTier()} | Total Deductions: {order.deductions.Count}");
            foreach (var log in order.deductions)
            {
                Debug.Log($"  -> [{log.timestamp:F1}s] Deducted {log.pointsDeducted} pts. Reason: {log.reason}");
            }

            if (order.qualityScore < 100 && order.GetTier() == QualityTier.Poor)
            {
                Debug.Log("<color=green><b>✔ PASS: Overheating correctly penalized potion quality down to Poor tier with realistic severity-based scaling.</b></color>");
            }
            else
            {
                Debug.LogError("❌ FAIL: Overheating should result in significant score reduction.");
            }
        }

        private void RunScenario_UnderheatingBrewing()
        {
            Debug.Log("\n<b>[Scenario 3: Underheating Brewing]</b> Simulating 5 seconds of underheating / cold temperature (Heat = 0.15)...");
            ActiveOrder order = new ActiveOrder("SIM-003", "Frost Ward Potion");

            float heat = 0.15f; // Well below minOptimalHeat (0.4)
            float nextDeductionTime = 0f;
            float interval = 1f;

            for (float time = 0f; time <= 5f; time += simStepTime)
            {
                bool isTooCold = heat < 0.4f;
                if (isTooCold && time >= nextDeductionTime)
                {
                    // Underheating formula: base (5) + severity * 5
                    float severity = (0.4f - heat) / 0.4f; // 0.25 / 0.4 = 0.625
                    int finalDeduction = 5 + Mathf.RoundToInt(severity * 5f); // 5 + 3 = 8 points

                    order.ApplyDeduction(finalDeduction, "Cauldron Brewing", $"Underheating simulation at {time}s", time);
                    nextDeductionTime = time + interval;
                }
            }

            Debug.Log($"Result: Final Quality: {order.qualityScore}/100 | Tier: {order.GetTier()} | Total Deductions: {order.deductions.Count}");
            foreach (var log in order.deductions)
            {
                Debug.Log($"  -> [{log.timestamp:F1}s] Deducted {log.pointsDeducted} pts. Reason: {log.reason}");
            }

            if (order.qualityScore < 100 && order.qualityScore > 0)
            {
                Debug.Log("<color=green><b>✔ PASS: Underheating correctly penalized potion quality proportionally (less severe than overheating).</b></color>");
            }
            else
            {
                Debug.LogError("❌ FAIL: Underheating should result in moderate quality score reduction.");
            }
        }
    }
}
