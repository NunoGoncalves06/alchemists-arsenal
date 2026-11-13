using System;
using System.Collections.Generic;
using UnityEngine;
using AlchemistsArsenal.Data;

namespace AlchemistsArsenal.Systems
{
    /// <summary>
    /// The four morning-crafting stations, in flow order.
    /// Integer values double as the tab index.
    /// </summary>
    public enum CraftingStation
    {
        Counter  = 0, // take the order
        Prep     = 1, // chop / grind herbs
        Cauldron = 2, // physics brewing
        Bottling = 3  // seal & hand off to the afternoon auto-battler
    }

    /// <summary>Immutable description of a single station transition.</summary>
    public readonly struct StationTransition
    {
        public readonly CraftingStation From;
        public readonly CraftingStation To;

        public StationTransition(CraftingStation from, CraftingStation to)
        {
            From = from;
            To = to;
        }

        public override string ToString() => $"{From} -> {To}";
    }

    /// <summary>
    /// Finite state machine that owns the active crafting station and broadcasts
    /// clean enter/exit/transition events. It is pure navigation state: it never
    /// reads or writes UI, and never mutates <see cref="ActiveOrder"/> progress
    /// (visiting a tab is not the same as completing a crafting step).
    ///
    /// Presentation layers subscribe to the events below; nothing calls back into
    /// the UI from here.
    /// </summary>
    public class StationManager : MonoBehaviour
    {
        public static StationManager Instance { get; private set; }

        [Header("Data (decoupled ScriptableObject definitions)")]
        [Tooltip("Optional. Drives display names, icons and default lock state. " +
                 "The FSM works without it — every station is then unlocked.")]
        [SerializeField] private StationSet stationSet;

        [SerializeField] private CraftingStation initialStation = CraftingStation.Counter;

        [Header("Debug")]
        [SerializeField] private bool verboseLogging = true;

        public CraftingStation CurrentStation { get; private set; }
        public CraftingStation PreviousStation { get; private set; }
        public StationSet StationSet => stationSet;

        /// <summary>Raised with the station being left, before <see cref="OnStationEntered"/>.</summary>
        public event Action<CraftingStation> OnStationExited;

        /// <summary>Raised with the station being entered, after <see cref="OnStationExited"/>.</summary>
        public event Action<CraftingStation> OnStationEntered;

        /// <summary>Raised once per successful transition with the full from/to pair.</summary>
        public event Action<StationTransition> OnStationChanged;

        /// <summary>Raised when a station is locked (true) or unlocked (false).</summary>
        public event Action<CraftingStation, bool> OnLockStateChanged;

        private readonly HashSet<CraftingStation> _locked = new HashSet<CraftingStation>();

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);

            CurrentStation = initialStation;
            PreviousStation = initialStation;
            ApplyDefaultLocks();
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        // ---------------------------------------------------------------- locks

        private void ApplyDefaultLocks()
        {
            _locked.Clear();
            if (stationSet == null) return;

            foreach (StationDefinition def in stationSet.Stations)
            {
                if (def != null && !def.UnlockedByDefault && def.Station != CurrentStation)
                    _locked.Add(def.Station);
            }
        }

        public bool IsStationUnlocked(CraftingStation station) => !_locked.Contains(station);

        /// <summary>
        /// Lock or unlock a station. Used by the Day 1 tutorial to gate stations
        /// until each step is taught. The currently-active station can never be locked.
        /// </summary>
        public void SetStationLocked(CraftingStation station, bool locked)
        {
            if (locked && station == CurrentStation) return;

            bool changed = locked ? _locked.Add(station) : _locked.Remove(station);
            if (!changed) return;

            if (verboseLogging)
                Debug.Log($"[StationManager] {station} {(locked ? "LOCKED" : "unlocked")}.");
            OnLockStateChanged?.Invoke(station, locked);
        }

        public void UnlockAllStations()
        {
            if (_locked.Count == 0) return;

            var wasLocked = new List<CraftingStation>(_locked);
            _locked.Clear();
            foreach (CraftingStation s in wasLocked)
                OnLockStateChanged?.Invoke(s, false);
        }

        // ----------------------------------------------------------- transitions

        /// <summary>
        /// Attempt a transition. Returns false (no events raised) if the target is
        /// the current station or is locked.
        /// </summary>
        public bool TrySwitchStation(CraftingStation next)
        {
            if (next == CurrentStation) return false;

            if (!IsStationUnlocked(next))
            {
                if (verboseLogging)
                    Debug.Log($"[StationManager] Switch to {next} denied — station is locked.");
                return false;
            }

            CraftingStation from = CurrentStation;

            OnStationExited?.Invoke(from);

            PreviousStation = from;
            CurrentStation = next;

            // TODO: Hook AudioManager cross-fade here (Category 7 — Sound & Music):
            // cross-fade shop ambience <-> station track on this transition.
            OnStationEntered?.Invoke(next);
            OnStationChanged?.Invoke(new StationTransition(from, next));

            if (verboseLogging)
                Debug.Log($"[StationManager] Transition {from} -> {next} (index {(int)next})");

            return true;
        }

        public bool TrySwitchStation(int stationIndex)
        {
            if (!Enum.IsDefined(typeof(CraftingStation), stationIndex)) return false;
            return TrySwitchStation((CraftingStation)stationIndex);
        }

        // void wrappers so UnityEvents / Button.onClick can bind in the inspector
        public void SwitchStation(CraftingStation next) => TrySwitchStation(next);
        public void SwitchStation(int stationIndex) => TrySwitchStation(stationIndex);

        /// <summary>
        /// Re-emit an "entered" event for the current station so a view that bound
        /// late (after the last real transition) can sync itself.
        /// </summary>
        public void ForceResync()
        {
            OnStationEntered?.Invoke(CurrentStation);
            OnStationChanged?.Invoke(new StationTransition(CurrentStation, CurrentStation));
        }
    }
}
