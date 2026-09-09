using UnityEngine;
using System;

namespace AlchemistsArsenal.Systems
{
    public class CraftingManager : MonoBehaviour
    {
        public static CraftingManager Instance { get; private set; }

        [Header("Active Order Config")]
        [SerializeField] private string defaultPotionName = "Fire Resistance Potion";

        public ActiveOrder CurrentOrder { get; private set; }

        // Event for when active order changes
        public Action<ActiveOrder> OnOrderStarted;
        public Action<ActiveOrder> OnOrderCompleted;

        // Timer events for the stations
        public Action<int, float, float> OnTimerChanged; // Station index, current timer, max timer

        private void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
                DontDestroyOnLoad(gameObject);
                StartNewOrder();
            }
            else
            {
                Destroy(gameObject);
            }
        }

        public void StartNewOrder()
        {
            string orderId = "ORD-" + UnityEngine.Random.Range(1000, 9999);
            CurrentOrder = new ActiveOrder(orderId, defaultPotionName);
            OnOrderStarted?.Invoke(CurrentOrder);
        }

        public void CompleteActiveOrder()
        {
            if (CurrentOrder != null)
            {
                OnOrderCompleted?.Invoke(CurrentOrder);
                Debug.Log($"Completed Order: {CurrentOrder.potionName} with Quality Score: {CurrentOrder.qualityScore} ({CurrentOrder.GetTier()})");
                StartNewOrder();
            }
        }

        /// <summary>
        /// Simulated method to trigger timer updates for UI.
        /// </summary>
        public void UpdateTimer(int stationIndex, float current, float max)
        {
            OnTimerChanged?.Invoke(stationIndex, current, max);
        }
    }
}
