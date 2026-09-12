using UnityEngine;
using System;
using AlchemistsArsenal.Combat;

namespace AlchemistsArsenal.Systems
{
    /// <summary>
    /// Holds the day's single <see cref="ActiveOrder"/>. It is created only by the
    /// Counter confirmation (<see cref="StartNewOrder(string, ElementType)"/>) —
    /// there is no <c>Awake</c> auto-start and <see cref="CompleteActiveOrder"/>
    /// does not start the next one (reviewer X3).
    /// </summary>
    public class CraftingManager : MonoBehaviour
    {
        public static CraftingManager Instance { get; private set; }

        [Header("Testing")]
        [Tooltip("Headless sims (CauldronSimulationTest etc.) need an order to exist at Awake. " +
                 "The real game leaves this false — Counter creates the order.")]
        [SerializeField] private bool bootstrapOrderForTests = false;
        [SerializeField] private string testPotionName = "Fire Resistance Potion";

        public ActiveOrder CurrentOrder { get; private set; }

        /// <summary>
        /// The day's working mixture — the recipe, the leaves actually crushed into
        /// it, and whether the mortar work is done. Born with the order, because the
        /// recipe is decided by what the customer asked for. The Cauldron reads
        /// <see cref="AlchemistsArsenal.Data.BrewMixture.Ready"/> as its gate.
        /// </summary>
        public Data.BrewMixture Mixture { get; private set; }

        public Action<ActiveOrder> OnOrderStarted;
        public Action<ActiveOrder> OnOrderCompleted;
        public Action<int, float, float> OnTimerChanged; // station index, current, max

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);

            if (bootstrapOrderForTests)
                StartNewOrder(testPotionName, ElementType.Fire);
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        /// <summary>Counter confirmation: begin the day's order at its element, born Poor.</summary>
        public void StartNewOrder(string potionName, ElementType element)
        {
            string orderId = "ORD-" + UnityEngine.Random.Range(1000, 9999);
            CurrentOrder = new ActiveOrder(orderId, potionName, element);
            Mixture = new Data.BrewMixture(element);
            OnOrderStarted?.Invoke(CurrentOrder);
        }

        /// <summary>Bottling seal: finalise. Does NOT auto-start a new order.</summary>
        public void CompleteActiveOrder()
        {
            if (CurrentOrder == null) return;
            Debug.Log($"[Crafting] Sealed {CurrentOrder.potionName} — quality {CurrentOrder.qualityScore} ({CurrentOrder.GetGrade()})");
            OnOrderCompleted?.Invoke(CurrentOrder);
        }

        /// <summary>Clear the order at the end of the day (called by the loop on AdvanceDay).</summary>
        public void ClearOrder()
        {
            CurrentOrder = null;
            Mixture = null;
        }

        public void UpdateTimer(int stationIndex, float current, float max) =>
            OnTimerChanged?.Invoke(stationIndex, current, max);
    }
}
