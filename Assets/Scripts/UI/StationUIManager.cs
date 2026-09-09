using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using AlchemistsArsenal.Systems;
using AlchemistsArsenal.Data;
using AlchemistsArsenal.Crafting;

namespace AlchemistsArsenal.UI
{
    /// <summary>
    /// Tab navigation for the four crafting stations. Purely a view of
    /// <see cref="StationManager"/>: clicks are forwarded to the FSM, and panel
    /// visibility is driven back from the FSM's events.
    ///
    /// Panels are shown / hidden with <c>SetActive</c> only — never re-parented,
    /// destroyed, or pooled — so background systems that live outside these panels
    /// (the cauldron simulation, crafting timers) keep running while hidden.
    /// </summary>
    public class StationUIManager : MonoBehaviour
    {
        [Serializable]
        public class StationTab
        {
            public CraftingStation station;

            [Tooltip("Root object of this station's panel. Toggled with SetActive only. " +
                     "Must NOT contain gameplay simulation components (see OnValidate).")]
            public GameObject panel;

            public Button tabButton;

            [Tooltip("Optional — the tab's background Image for active/locked tinting. " +
                     "Auto-filled from tabButton if left empty.")]
            public Image tabBackground;

            [Tooltip("Optional — shown when the station is locked and not the active tab.")]
            public GameObject lockedOverlay;
        }

        [Header("Tabs")]
        [SerializeField] private List<StationTab> tabs = new List<StationTab>();

        [Header("Tab Tinting")]
        [SerializeField] private Color activeTabColor = new Color(0.90f, 0.80f, 0.40f);
        [SerializeField] private Color inactiveTabColor = new Color(0.70f, 0.70f, 0.70f);
        [SerializeField] private Color lockedTabColor = new Color(0.40f, 0.40f, 0.40f);

        private readonly Dictionary<Button, CraftingStation> _buttonToStation =
            new Dictionary<Button, CraftingStation>();

        private bool _buttonsBound;
        private bool _managerHooked;

        // ------------------------------------------------------------- lifecycle

        private void Awake()
        {
            foreach (StationTab tab in tabs)
            {
                if (tab?.tabButton != null && tab.tabBackground == null)
                    tab.tabBackground = tab.tabButton.GetComponent<Image>();
            }
        }

        private void OnEnable()
        {
            BindButtons();
            HookManager();
        }

        private void OnDisable()
        {
            UnbindButtons();
            UnhookManager();
        }

        private void Update()
        {
            // StationManager (a DontDestroyOnLoad singleton) may finish Awake after
            // this component's OnEnable on the very first frame. Hook it as soon
            // as it appears, then this branch is skipped for the rest of the run.
            if (!_managerHooked && StationManager.Instance != null)
                HookManager();
        }

        // ------------------------------------------------------------- buttons

        private void BindButtons()
        {
            if (_buttonsBound) return;

            _buttonToStation.Clear();
            foreach (StationTab tab in tabs)
            {
                if (tab?.tabButton == null) continue;

                CraftingStation captured = tab.station;
                _buttonToStation[tab.tabButton] = captured;
                tab.tabButton.onClick.AddListener(() => OnTabClicked(captured));
            }
            _buttonsBound = true;
        }

        private void UnbindButtons()
        {
            if (!_buttonsBound) return;

            // Listeners were added as captured lambdas, which RemoveListener can't
            // match, so clear the click event on each button we bound.
            foreach (Button button in _buttonToStation.Keys)
            {
                if (button != null) button.onClick.RemoveAllListeners();
            }
            _buttonToStation.Clear();
            _buttonsBound = false;
        }

        // ------------------------------------------------------------- manager

        private void HookManager()
        {
            if (_managerHooked) return;

            StationManager mgr = StationManager.Instance;
            if (mgr == null)
            {
                RefreshAll(CraftingStation.Counter); // safe default until the FSM exists
                return;
            }

            mgr.OnStationChanged += HandleStationChanged;
            mgr.OnLockStateChanged += HandleLockChanged;
            _managerHooked = true;

            RefreshAll(mgr.CurrentStation);
        }

        private void UnhookManager()
        {
            if (!_managerHooked) return;

            StationManager mgr = StationManager.Instance;
            if (mgr != null)
            {
                mgr.OnStationChanged -= HandleStationChanged;
                mgr.OnLockStateChanged -= HandleLockChanged;
            }
            _managerHooked = false;
        }

        // ------------------------------------------------------------- handlers

        private void OnTabClicked(CraftingStation station)
        {
            StationManager mgr = StationManager.Instance;
            if (mgr != null)
                mgr.TrySwitchStation(station); // FSM decides; RefreshAll runs from its event
            else
                RefreshAll(station);           // standalone preview fallback
        }

        private void HandleStationChanged(StationTransition transition) => RefreshAll(transition.To);

        private void HandleLockChanged(CraftingStation station, bool locked)
        {
            StationManager mgr = StationManager.Instance;
            RefreshAll(mgr != null ? mgr.CurrentStation : CraftingStation.Counter);
        }

        // ------------------------------------------------------------- view

        private void RefreshAll(CraftingStation active)
        {
            StationManager mgr = StationManager.Instance;
            StationSet set = mgr != null ? mgr.StationSet : null;

            foreach (StationTab tab in tabs)
            {
                if (tab == null) continue;

                bool isActive = tab.station == active;
                bool isLocked = mgr != null && !mgr.IsStationUnlocked(tab.station);

                // Panel visibility — SetActive only, and only when it actually changes.
                if (tab.panel != null && tab.panel.activeSelf != isActive)
                    tab.panel.SetActive(isActive);

                if (tab.tabButton != null)
                    tab.tabButton.interactable = isActive || !isLocked;

                if (tab.lockedOverlay != null)
                    tab.lockedOverlay.SetActive(isLocked && !isActive);

                if (tab.tabBackground != null)
                    tab.tabBackground.color =
                        isActive ? activeTabColor : (isLocked ? lockedTabColor : inactiveTabColor);

                // Optional label/icon from the decoupled SO definition.
                if (set != null && set.TryGet(tab.station, out StationDefinition def))
                    ApplyDefinition(tab, def);
            }
        }

        private static void ApplyDefinition(StationTab tab, StationDefinition def)
        {
            if (tab.tabButton == null) return;

            var label = tab.tabButton.GetComponentInChildren<Text>(true);
            if (label != null) label.text = def.DisplayName;

            if (def.TabIcon != null)
            {
                var icon = tab.tabButton.transform.Find("Icon");
                if (icon != null && icon.TryGetComponent(out Image iconImage))
                    iconImage.sprite = def.TabIcon;
            }
        }

        // ------------------------------------------------------------- editor guard

#if UNITY_EDITOR
        private void OnValidate()
        {
            foreach (StationTab tab in tabs)
            {
                if (tab?.panel == null) continue;

                if (tab.panel.GetComponentInChildren<PhysicsCauldronManager>(true) != null)
                {
                    Debug.LogWarning(
                        $"[StationUIManager] Panel '{tab.panel.name}' contains a PhysicsCauldronManager. " +
                        "Gameplay simulation must live on a persistent object OUTSIDE the tab panels, " +
                        "otherwise SetActive(false) on tab switch will freeze the cauldron physics and heat decay.",
                        this);
                }
            }
        }
#endif
    }
}
