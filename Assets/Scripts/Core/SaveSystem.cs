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

        /// <summary>
        /// When >= 0, every slot maps to this file instead. The headless playtest
        /// sets it: a batchmode run of a copy of this project shares
        /// <see cref="Application.persistentDataPath"/> with the developer's own
        /// Editor (same company + product name), so without it every test run
        /// overwrote the real <c>slot_0.json</c>.
        /// </summary>
        public static int SlotOverride = -1;

        private static string PathFor(int slot) =>
            Path.Combine(Application.persistentDataPath, $"slot_{(SlotOverride >= 0 ? SlotOverride : slot)}.json");

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
                // `saveVersion` can't be the signal — RunState's field initializer
                // sets it to 1 even on a partial parse. `lastSavedUnixSeconds` is
                // only ever written by Save(), so a 0 means "never a real save"
                // (garbage that JsonUtility populated with defaults) — reviewer R7.
                if (s == null || s.lastSavedUnixSeconds <= 0)
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
            State = Migrate(RunState.NewGame(slot));
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

            s.ownedHerbs ??= new System.Collections.Generic.List<string>();
            s.ownedUpgrades ??= new System.Collections.Generic.List<string>();
            s.unlockedDiary ??= new System.Collections.Generic.List<string>();
            s.ownedAdventurers ??= new System.Collections.Generic.List<string>();
            if (s.ownedAdventurers.Count == 0) s.ownedAdventurers.Add("Rookie");

            MigrateRoster(s);

            s.saveVersion = RunState.CurrentVersion;
            return s;
        }

        /// <summary>
        /// Seed the roster from the legacy string list on first load, then keep
        /// every record inside the ranges the rest of the game assumes. Split out
        /// of <see cref="Migrate"/> only for readability; it is part of the same
        /// contract and is covered by the same headless test.
        ///
        /// No <c>CurrentVersion</c> bump is needed for any of this: JsonUtility
        /// runs field initialisers before populating, so a save written before the
        /// roster existed arrives here with an empty list and gets seeded.
        /// </summary>
        private static void MigrateRoster(RunState s)
        {
            s.roster ??= new System.Collections.Generic.List<Data.HeroRecord>();

            if (s.roster.Count == 0)
                foreach (string name in s.ownedAdventurers)
                    s.roster.Add(Data.HeroCatalog.NewHire(name, s.roster.Count));
            if (s.roster.Count == 0)
                s.roster.Add(Data.HeroCatalog.NewHire("Rookie", 0));

            for (int i = s.roster.Count - 1; i >= 0; i--)
            {
                Data.HeroRecord h = s.roster[i];
                if (h == null) { s.roster.RemoveAt(i); continue; }

                if (string.IsNullOrWhiteSpace(h.id)) h.id = "h" + i;
                if (string.IsNullOrWhiteSpace(h.displayName)) h.displayName = "Rookie";
                if (string.IsNullOrWhiteSpace(h.portraitId)) h.portraitId = "rookie";

                h.level = Math.Clamp(h.level, 1, Data.HeroCatalog.MaxLevel);
                h.affinity = (Combat.ElementType)Math.Clamp((int)h.affinity, 0, 4);
                if (!Data.HeroPerks.ArchetypeExists(h.archetypeId))
                    h.archetypeId = Data.HeroPerks.DefaultArchetype;

                // A hand-edited save must never be able to bench someone forever.
                h.restUntilDay = Math.Clamp(h.restUntilDay, 0, s.day + 1);
            }

            if (s.roster.Count > Data.HeroCatalog.MaxRoster)
                s.roster.RemoveRange(Data.HeroCatalog.MaxRoster,
                    s.roster.Count - Data.HeroCatalog.MaxRoster);

            // Trim deployment to the cap, dropping anyone unfit, then guarantee at
            // least one hero is going out (see RunState.EnsureDeployment).
            s.EnsureDeployment();
        }
    }
}
