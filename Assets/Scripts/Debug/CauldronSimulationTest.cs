using UnityEngine;
using AlchemistsArsenal.Systems;
using AlchemistsArsenal.Combat;

namespace AlchemistsArsenal.DebugTools
{
    /// <summary>
    /// Headless checks for the reworked <see cref="ActiveOrder"/> quality model:
    /// born at 25 (Poor), raised by clean brewing, lowered by heat penalties, and
    /// graded on the 4-band <see cref="PotionGrade"/> (reviewer X4).
    /// </summary>
    public class CauldronSimulationTest : MonoBehaviour
    {
        [SerializeField] private float simStepTime = 0.1f;

        private void Start() => RunFullSimulationSuite();

        public void RunFullSimulationSuite()
        {
            Debug.Log("<color=cyan><b>=== CAULDRON QUALITY MODEL SIMULATION ===</b></color>");
            Scenario_CleanBrewClimbs();
            Scenario_OverheatingPenalises();
            Scenario_UntouchedStaysPoor();
            Debug.Log("<color=cyan><b>=== SIMULATION SUITE COMPLETE ===</b></color>");
        }

        private void Scenario_CleanBrewClimbs()
        {
            Debug.Log("\n<b>[1: Clean brew]</b> 10 steps in the green zone should raise quality toward Great/Perfect.");
            var order = new ActiveOrder("SIM-001", "Perfect Healing Potion", ElementType.Nature);
            int raises = 0;
            order.OnQualityChanged += _ => raises++;

            for (int step = 1; step <= 10; step++)
                order.ApplyBonus(9, "Cauldron Brewing", "held green zone step " + step, step * simStepTime);

            Debug.Log($"Result: quality {order.qualityScore}/100 | grade {order.GetGrade()} | events {raises}");
            bool ok = order.qualityScore > ActiveOrder.StartingQuality
                      && order.GetGrade() is PotionGrade.Great or PotionGrade.Perfect
                      && raises == 10;
            Report(ok, "Clean brewing raised quality and fired OnQualityChanged upward.");
        }

        private void Scenario_OverheatingPenalises()
        {
            Debug.Log("\n<b>[2: Overheating]</b> Heat 0.95 for 5 s should drive a mid brew back down to Poor.");
            var order = new ActiveOrder("SIM-002", "Fire Blast Potion", ElementType.Fire);
            order.ApplyBonus(55, "Cauldron Brewing", "decent brew so far", 0f); // ~80, Great

            float heat = 0.95f, nextDeduction = 0f, interval = 1f;
            for (float t = 0f; t <= 5f; t += simStepTime)
            {
                if (heat > 0.7f && t >= nextDeduction)
                {
                    float severity = (heat - 0.7f) / (1f - 0.7f);
                    int pts = 5 + Mathf.RoundToInt(severity * 10f);
                    order.ApplyDeduction(pts, "Cauldron Brewing", $"overheating at {t:F1}s", t);
                    nextDeduction = t + interval;
                }
            }

            Debug.Log($"Result: quality {order.qualityScore}/100 | grade {order.GetGrade()} | logs {order.deductions.Count}");
            Report(order.qualityScore < 80 && order.GetGrade() == PotionGrade.Poor,
                   "Sustained overheating collapsed a Great brew to Poor.");
        }

        private void Scenario_UntouchedStaysPoor()
        {
            Debug.Log("\n<b>[3: Untouched]</b> A never-brewed order ships at the starting floor — Poor, non-zero.");
            var order = new ActiveOrder("SIM-003", "Frost Ward Potion", ElementType.Water);
            Debug.Log($"Result: quality {order.qualityScore}/100 | grade {order.GetGrade()}");
            Report(order.qualityScore == ActiveOrder.StartingQuality && order.GetGrade() == PotionGrade.Poor,
                   "Untouched order is a fixed-floor Poor potion, never empty.");
        }

        private static void Report(bool ok, string what)
        {
            if (ok) Debug.Log($"<color=green><b>✔ PASS:</b></color> {what}");
            else Debug.LogError($"❌ FAIL: {what}");
        }
    }
}
