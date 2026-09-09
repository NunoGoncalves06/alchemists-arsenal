using UnityEngine;
using UnityEngine.UI;
using TMPro;
using AlchemistsArsenal.Systems;
using AlchemistsArsenal.Crafting;

namespace AlchemistsArsenal.UI
{
    public class CauldronUI : MonoBehaviour
    {
        [Header("Heat UI References")]
        [SerializeField] private Slider heatSlider;
        [SerializeField] private Image heatSliderFill;
        [SerializeField] private RectTransform optimalRangeIndicator; // Visual overlay for optimal heat zone
        [SerializeField] private TextMeshProUGUI temperatureStatusText;

        [Header("Order Info References")]
        [SerializeField] private TextMeshProUGUI orderNameText;
        [SerializeField] private TextMeshProUGUI qualityScoreText;
        [SerializeField] private TextMeshProUGUI tierText;
        [SerializeField] private Image qualityPanelBg;

        [Header("Visual Colors")]
        [SerializeField] private Color coldColor = new Color(0.2f, 0.6f, 1f);
        [SerializeField] private Color optimalColor = new Color(0.2f, 0.8f, 0.2f);
        [SerializeField] private Color hotColor = new Color(1f, 0.2f, 0.2f);
        [SerializeField] private Color perfectQualityColor = new Color(0f, 0.8f, 0.4f);
        [SerializeField] private Color goodQualityColor = new Color(0.9f, 0.7f, 0f);
        [SerializeField] private Color poorQualityColor = new Color(0.8f, 0.1f, 0.1f);

        private ActiveOrder currentTrackedOrder;

        private void OnEnable()
        {
            // Subscribe to the PhysicsCauldronManager changes
            if (PhysicsCauldronManager.Instance != null)
            {
                PhysicsCauldronManager.Instance.OnHeatChanged += HandleHeatChanged;
                SetupOptimalVisualRange();
            }

            // Subscribe to CraftingManager events
            if (CraftingManager.Instance != null)
            {
                CraftingManager.Instance.OnOrderStarted += RegisterOrder;
                CraftingManager.Instance.OnOrderCompleted += UnregisterOrder;

                if (CraftingManager.Instance.CurrentOrder != null)
                {
                    RegisterOrder(CraftingManager.Instance.CurrentOrder);
                }
            }
        }

        private void OnDisable()
        {
            if (PhysicsCauldronManager.Instance != null)
            {
                PhysicsCauldronManager.Instance.OnHeatChanged -= HandleHeatChanged;
            }

            if (CraftingManager.Instance != null)
            {
                CraftingManager.Instance.OnOrderStarted -= RegisterOrder;
                CraftingManager.Instance.OnOrderCompleted -= UnregisterOrder;
            }

            UnregisterOrder(null);
        }

        private void Start()
        {
            // Initial call to set up the optimal bounds visualizer overlay if not done
            SetupOptimalVisualRange();
        }

        private void RegisterOrder(ActiveOrder order)
        {
            UnregisterOrder(null);

            currentTrackedOrder = order;
            if (currentTrackedOrder != null)
            {
                currentTrackedOrder.OnQualityChanged += HandleQualityChanged;
                UpdateOrderUI();
            }
        }

        private void UnregisterOrder(ActiveOrder order)
        {
            if (currentTrackedOrder != null)
            {
                currentTrackedOrder.OnQualityChanged -= HandleQualityChanged;
            }
            currentTrackedOrder = null;
        }

        private void SetupOptimalVisualRange()
        {
            if (PhysicsCauldronManager.Instance == null || optimalRangeIndicator == null || heatSlider == null) return;

            float min = PhysicsCauldronManager.Instance.MinOptimalHeat;
            float max = PhysicsCauldronManager.Instance.MaxOptimalHeat;

            // Anchor/Size adjustment of optimal indicator relative to slider width
            RectTransform sliderRect = heatSlider.GetComponent<RectTransform>();
            if (sliderRect != null)
            {
                float sliderWidth = sliderRect.rect.width;
                float startX = min * sliderWidth;
                float endX = max * sliderWidth;

                optimalRangeIndicator.anchorMin = new Vector2(min, 0f);
                optimalRangeIndicator.anchorMax = new Vector2(max, 1f);
                optimalRangeIndicator.offsetMin = Vector2.zero;
                optimalRangeIndicator.offsetMax = Vector2.zero;
            }
        }

        private void HandleHeatChanged(float currentHeat)
        {
            if (heatSlider != null)
            {
                heatSlider.value = currentHeat;
            }

            if (PhysicsCauldronManager.Instance == null) return;

            float minOpt = PhysicsCauldronManager.Instance.MinOptimalHeat;
            float maxOpt = PhysicsCauldronManager.Instance.MaxOptimalHeat;

            if (currentHeat < minOpt)
            {
                // Underheated (Too cold)
                if (heatSliderFill != null) heatSliderFill.color = coldColor;
                if (temperatureStatusText != null)
                {
                    temperatureStatusText.text = "TOO COLD - STIR FASTER!";
                    temperatureStatusText.color = coldColor;
                }
            }
            else if (currentHeat > maxOpt)
            {
                // Overheated (Too hot)
                if (heatSliderFill != null) heatSliderFill.color = hotColor;
                if (temperatureStatusText != null)
                {
                    temperatureStatusText.text = "OVERHEATING - STOP STIRRING!";
                    temperatureStatusText.color = hotColor;
                }
            }
            else
            {
                // Optimal range (Brewing perfectly)
                if (heatSliderFill != null) heatSliderFill.color = optimalColor;
                if (temperatureStatusText != null)
                {
                    temperatureStatusText.text = "BREWING PERFECTLY";
                    temperatureStatusText.color = optimalColor;
                }
            }
        }

        private void HandleQualityChanged(int newQuality)
        {
            UpdateOrderUI();
        }

        private void UpdateOrderUI()
        {
            if (currentTrackedOrder == null)
            {
                if (orderNameText != null) orderNameText.text = "No Active Order";
                if (qualityScoreText != null) qualityScoreText.text = "Q: --";
                if (tierText != null) tierText.text = "--";
                return;
            }

            if (orderNameText != null)
            {
                orderNameText.text = $"Order: {currentTrackedOrder.potionName}";
            }

            if (qualityScoreText != null)
            {
                qualityScoreText.text = $"Quality: {currentTrackedOrder.qualityScore}/100";
            }

            if (tierText != null)
            {
                QualityTier tier = currentTrackedOrder.GetTier();
                tierText.text = tier.ToString().ToUpper();

                // Style based on Quality Tier
                switch (tier)
                {
                    case QualityTier.Perfect:
                        tierText.color = perfectQualityColor;
                        if (qualityPanelBg != null) qualityPanelBg.color = new Color(0f, 0.8f, 0.4f, 0.15f);
                        break;
                    case QualityTier.Good:
                        tierText.color = goodQualityColor;
                        if (qualityPanelBg != null) qualityPanelBg.color = new Color(0.9f, 0.7f, 0f, 0.15f);
                        break;
                    case QualityTier.Poor:
                        tierText.color = poorQualityColor;
                        if (qualityPanelBg != null) qualityPanelBg.color = new Color(0.8f, 0.1f, 0.1f, 0.15f);
                        break;
                }
            }
        }
    }
}
