using UnityEngine;
using UnityEngine.UI;
using TMPro;
using AlchemistsArsenal.Systems;
using AlchemistsArsenal.Crafting;
using AlchemistsArsenal.Combat;

namespace AlchemistsArsenal.UI
{
    public class CauldronUI : MonoBehaviour
    {
        [Header("Stir UI References")]
        [SerializeField] private Slider stirSlider;
        [SerializeField] private Image stirSliderFill;
        [SerializeField] private RectTransform optimalRangeIndicator; // Visual overlay for the optimal stir band
        [SerializeField] private TextMeshProUGUI stirStatusText;

        [Header("Order Info References")]
        [SerializeField] private TextMeshProUGUI orderNameText;
        [SerializeField] private TextMeshProUGUI qualityScoreText;
        [SerializeField] private TextMeshProUGUI tierText;
        [SerializeField] private Image qualityPanelBg;

        [Header("Visual Colors")]
        [SerializeField] private Color spillColor = new Color(0.2f, 0.6f, 1f);
        [SerializeField] private Color optimalColor = new Color(0.2f, 0.8f, 0.2f);
        [SerializeField] private Color scorchColor = new Color(1f, 0.2f, 0.2f);
        [SerializeField] private Color perfectQualityColor = new Color(0f, 0.8f, 0.4f);
        [SerializeField] private Color goodQualityColor = new Color(0.9f, 0.7f, 0f);
        [SerializeField] private Color poorQualityColor = new Color(0.8f, 0.1f, 0.1f);

        private ActiveOrder currentTrackedOrder;

        private void OnEnable()
        {
            // Subscribe to the PhysicsCauldronManager changes
            if (PhysicsCauldronManager.Instance != null)
            {
                PhysicsCauldronManager.Instance.OnStirChanged += HandleStirChanged;
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
                PhysicsCauldronManager.Instance.OnStirChanged -= HandleStirChanged;
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
            if (PhysicsCauldronManager.Instance == null || optimalRangeIndicator == null || stirSlider == null) return;

            float min = PhysicsCauldronManager.Instance.MinOptimalStir;
            float max = PhysicsCauldronManager.Instance.MaxOptimalStir;

            // Anchor/Size adjustment of optimal indicator relative to slider width
            RectTransform sliderRect = stirSlider.GetComponent<RectTransform>();
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

        private void HandleStirChanged(float stir)
        {
            if (stirSlider != null)
            {
                stirSlider.value = stir;
            }

            if (PhysicsCauldronManager.Instance == null) return;

            float minOpt = PhysicsCauldronManager.Instance.MinOptimalStir;
            float maxOpt = PhysicsCauldronManager.Instance.MaxOptimalStir;

            if (stir < minOpt)
            {
                // Too slow: the brew catches on the bottom
                if (stirSliderFill != null) stirSliderFill.color = scorchColor;
                if (stirStatusText != null)
                {
                    stirStatusText.text = "STICKING TO THE BOTTOM - STIR FASTER!";
                    stirStatusText.color = scorchColor;
                }
            }
            else if (stir > maxOpt)
            {
                // Too fast: the surface goes over the rim
                if (stirSliderFill != null) stirSliderFill.color = spillColor;
                if (stirStatusText != null)
                {
                    stirStatusText.text = "TOO FAST - IT WILL SLOP OUT!";
                    stirStatusText.color = spillColor;
                }
            }
            else
            {
                // Optimal range (Brewing perfectly)
                if (stirSliderFill != null) stirSliderFill.color = optimalColor;
                if (stirStatusText != null)
                {
                    stirStatusText.text = "BREWING PERFECTLY";
                    stirStatusText.color = optimalColor;
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
                PotionGrade grade = currentTrackedOrder.GetGrade();
                tierText.text = grade.ToString().ToUpper();

                // Style based on the 4-band combat grade (the only grade the player sees).
                switch (grade)
                {
                    case PotionGrade.Perfect:
                        tierText.color = perfectQualityColor;
                        if (qualityPanelBg != null) qualityPanelBg.color = new Color(0f, 0.8f, 0.4f, 0.15f);
                        break;
                    case PotionGrade.Great:
                        tierText.color = goodQualityColor;
                        if (qualityPanelBg != null) qualityPanelBg.color = new Color(0.9f, 0.7f, 0f, 0.15f);
                        break;
                    case PotionGrade.Okay:
                        tierText.color = goodQualityColor;
                        if (qualityPanelBg != null) qualityPanelBg.color = new Color(0.9f, 0.55f, 0f, 0.15f);
                        break;
                    default: // Poor
                        tierText.color = poorQualityColor;
                        if (qualityPanelBg != null) qualityPanelBg.color = new Color(0.8f, 0.1f, 0.1f, 0.15f);
                        break;
                }
            }
        }
    }
}
