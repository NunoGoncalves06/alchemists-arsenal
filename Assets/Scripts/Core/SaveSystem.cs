using System;
using System.IO;
using UnityEngine;
using AlchemistsArsenal.Combat;

namespace AlchemistsArsenal.Core
{
    /// <summary>
    /// Owns the live <see cref="RunState"/> and persists it as JSON under
    /// <see cref="Application.persistentDataPath"/> (DESIGN.md §9).
    ///
    /// Writes are atomic (temp file + replace) so a crash mid-save can't corrupt a
    /// slot; the loop autosaves at every phase boundary, not only on sleep.
    /// Settings are NOT stored here — those are global (<see cref="SettingsService"/>).
    /// </summary>
    public class SaveSystem : MonoBehaviour
    {
        public static SaveSystem Instance { get; private set; }

        [Tooltip("Slot count exposed on the Save Slots screen. Phase 0 ships with 1; more is a Deepening task.")]
        [SerializeField] private int slotCount = 1;

        public RunState State { get; private set; }
        public int SlotCount => slotCount;

        /// <summary>True when a slot file exists but could not be parsed (reviewer P8).</summary>
        public bool LastSlotCorrupt { get; private set; }

        public event Action<RunState> OnStateLoaded;

        private bool _dirty;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        // --------------------------------------------------------------- slot IO

        private static string PathFor(int slot) =>
            Path.Combine(Application.persistentDataPath, $"slot_{slot}.json");

        public bool SlotExists(int slot) => File.Exists(PathFor(slot));

        /// <summary>Slot file present but unparseable — the menu must not silently overwrite it (P8).</summary>
        public bool SlotCorrupt(int slot) => SlotExists(slot) && Peek(slot) == null;

        /// <summary>Lightweight read for the slot-select screen (never becomes the live state).</summary>
        public RunState Peek(int slot)
        {
            try
            {
                string path = PathFor(slot);
                if (!File.Exists(path)) { LastSlotCorrupt = false; return null; }
                var s = JsonUtility.FromJson<RunState>(File.ReadAllText(path));
                // Every real save is written with saveVersion >= 1; a 0 means the
                // JSON was truncated/garbage and only defaults came back.
                if (s == null || s.saveVersion < 1)
                {
                    LastSlotCorrupt = true;
                    return null;
                }
                LastSlotCorrupt = false;
                return Migrate(s);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[SaveSystem] slot {slot} unreadable: {e.Message}");
                LastSlotCorrupt = true;
                return null;
            }
        }

        public void NewGame(int slot)
        {
            State = RunState.NewGame(slot);
            _dirty = true;
            Save();
            OnStateLoaded?.Invoke(State);
        }

        public bool Load(int slot)
        {
            RunState loaded = Peek(slot);
            if (loaded == null) return false;
            State = loaded;
            State.slot = slot;
            OnStateLoaded?.Invoke(State);
            return true;
        }

        public void Delete(int slot)
        {
            try { if (File.Exists(PathFor(slot))) File.Delete(PathFor(slot)); }
            catch (Exception e) { Debug.LogWarning($"[SaveSystem] delete slot {slot}: {e.Message}"); }
        }

        // --------------------------------------------------------------- save

        public void MarkDirty() => _dirty = true;

        /// <summary>Called by the loop at each phase boundary. No-ops if nothing changed.</summary>
        public void AutoSave()
        {
            if (_dirty) Save();
        }

        public void Save()
        {
            if (State == null) return;

            State.saveVersion = RunState.CurrentVersion;
            State.lastSavedUnixSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            if (GameLoopManager.Instance != null)
                State.phaseAtSave = (int)GameLoopManager.Instance.Phase;

            string path = PathFor(State.slot);
            string tmp = path + ".tmp";
            try
            {
                Directory.CreateDirectory(Application.persistentDataPath);
                File.WriteAllText(tmp, JsonUtility.ToJson(State, prettyPrint: true));

                if (File.Exists(path))
                    File.Replace(tmp, path, path + ".bak");
                else
                    File.Move(tmp, path);

                _dirty = false;
            }
            catch (Exception e)
            {
                Debug.LogError($"[SaveSystem] save failed: {e.Message}");
                try { if (File.Exists(tmp)) File.Delete(tmp); } catch { /* best effort */ }
            }
        }

        // --------------------------------------------------------------- migration

        /// <summary>
        /// Phase 0 has exactly one schema, so this only guards forward-compat: an
        /// unknown (higher) version is clamped and logged. Real per-field migration
        /// arrives with multi-slot saves (Deepening 1.7).
        /// </summary>
        public static RunState Migrate(RunState s)
        {
            if (s == null) return null;
            if (s.saveVersion > RunState.CurrentVersion)
                Debug.LogWarning($"[SaveSystem] save is v{s.saveVersion}, game is v{RunState.CurrentVersion} — loading anyway.");

            int biomes = BiomeLibrary.Count;

            // Clamp / repair every field a truncated or hand-edited slot could have
            // left nonsensical (reviewer P10).
            if (s.bestGrades == null || s.bestGrades.Length != biomes)
            {
                var g = new int[biomes];
                if (s.bestGrades != null) Array.Copy(s.bestGrades, g, Math.Min(s.bestGrades.Length, biomes));
                s.bestGrades = g;
            }
            for (int i = 0; i < s.bestGrades.Length; i++) s.bestGrades[i] = Math.Clamp(s.bestGrades[i], 0, 3);

            s.day = Math.Max(1, s.day);
            s.currentBiomeIndex = Math.Clamp(s.currentBiomeIndex, 0, biomes - 1);
            s.replayBiomeIndex = s.replayBiomeIndex < 0 ? -1 : Math.Clamp(s.replayBiomeIndex, 0, biomes - 1);
            s.gold = Math.Max(0, s.gold);
            s.lastResolvedDay = Math.Clamp(s.lastResolvedDay, 0, s.day);
            s.phaseAtSave = Math.Clamp(s.phaseAtSave, 0, (int)GamePhase.BiomeMap);

            s.ownedHerbs ??= new System.Collections.Generic.List<string>();
            s.ownedUpgrades ??= new System.Collections.Generic.List<string>();
            s.unlockedDiary ??= new System.Collections.Generic.List<string>();
            s.ownedAdventurers ??= new System.Collections.Generic.List<string>();
            if (s.ownedAdventurers.Count == 0) s.ownedAdventurers.Add("Rookie");

            s.saveVersion = RunState.CurrentVersion;
            return s;
        }
    }
}
