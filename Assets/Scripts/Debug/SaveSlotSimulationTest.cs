using System.IO;
using UnityEngine;
using AlchemistsArsenal.Core;
using AlchemistsArsenal.UI;

namespace AlchemistsArsenal.DebugTools
{
    /// <summary>
    /// The three save slots, on real files in a scratch folder: each slot keeps its
    /// own run, the menu's choices (anything to continue, the first empty slot, a
    /// corrupt file), the migration of a save written before slots and rosters, the
    /// atomic write, the slot card's summary, and when the Evening offers MENU.
    /// Puts the save folder and the live run back as they were.
    /// </summary>
    public class SaveSlotSimulationTest : MonoBehaviour, ISimulationSuite
    {
        public bool Done { get; private set; }

        private int _pass, _fail;

        private void Start()
        {
            Debug.Log("<color=cyan><b>=== SAVE SLOT SIMULATION ===</b></color>");
            SaveSystem save = SaveSystem.Instance;
            if (save == null) { Check(false, "slots: a SaveSystem to test with"); Done = true; return; }

            string dirBefore = SaveSystem.DirectoryOverride;
            RunState stateBefore = save.State;
            string dir = Path.Combine(Application.persistentDataPath, "slot-test");
            try
            {
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
                Directory.CreateDirectory(dir);
                SaveSystem.DirectoryOverride = dir;

                TestThreeSlots(save, dir);
                TestChoice(save, dir);
                TestMigration(save, dir);
                TestAtomicSave(save, dir);
                TestSummary();
                TestEveningMenu();
            }
            finally
            {
                SaveSystem.DirectoryOverride = dirBefore;
                if (stateBefore != null) save.Load(stateBefore.slot);
                try { Directory.Delete(dir, true); } catch { /* best effort */ }
            }
            Done = true;
            Debug.Log($"<color=cyan><b>=== DONE — {_pass} pass, {_fail} fail ===</b></color>");
        }

        private void Check(bool ok, string what)
        {
            if (ok) { _pass++; Debug.Log($"<color=green>✔</color> {what}"); }
            else { _fail++; Debug.LogError($"❌ {what}"); }
        }

        private void TestThreeSlots(SaveSystem save, string dir)
        {
            Check(SaveSystem.Slots == 3, "slots: there are three");
            for (int i = 0; i < SaveSystem.Slots; i++)
            {
                save.NewGame(i);
                save.State.gold = 100 + i;
                save.State.day = 2 + i;
                save.Save();
            }
            bool files = true;
            for (int i = 0; i < SaveSystem.Slots; i++) files &= File.Exists(Path.Combine(dir, $"slot_{i}.json"));
            Check(files, "slots: a new game in each slot writes its own file");

            bool own = true;
            for (int i = SaveSystem.Slots - 1; i >= 0; i--)
                own &= save.Load(i) && save.State.slot == i && save.State.gold == 100 + i && save.State.day == 2 + i;
            Check(own, "slots: each slot resumes its own run (gold, day, slot)");

            save.Load(1);
            save.State.gold = 999;
            save.Save();
            Check(save.Peek(0).gold == 100 && save.Peek(2).gold == 102 && save.Peek(1).gold == 999,
                "slots: saving one slot leaves the others alone");
        }

        private void TestChoice(SaveSystem save, string dir)
        {
            foreach (string f in Directory.GetFiles(dir)) File.Delete(f);
            Check(!save.AnyContinuable(), "slots: with no save, there is nothing to continue");
            Check(SaveSlots.FirstEmpty(save) == 0, "slots: a new game is offered the first slot when all are empty");

            save.NewGame(0);
            Check(save.AnyContinuable(), "slots: one save is enough to continue");
            Check(SaveSlots.FirstEmpty(save) == 1, "slots: the first empty slot skips the used one");

            File.WriteAllText(Path.Combine(dir, "slot_1.json"), "{ this is not a save");
            save.NewGame(2);
            Check(save.SlotCorrupt(1) && !save.SlotCorrupt(0) && !save.SlotCorrupt(2),
                "slots: a corrupt file is reported for its slot only");
            Check(save.Peek(1) == null && save.Peek(0) != null && save.Peek(2) != null && save.AnyContinuable(),
                "slots: a corrupt slot cannot be continued, the others still can");
            Check(SaveSlots.FirstEmpty(save) == -1, "slots: a corrupt file is not an empty slot");

            foreach (string f in Directory.GetFiles(dir)) File.Delete(f);
            File.WriteAllText(Path.Combine(dir, "slot_0.json"), "{ broken");
            Check(!save.AnyContinuable(), "slots: only corrupt files means nothing to continue");
        }

        private void TestMigration(SaveSystem save, string dir)
        {
            foreach (string f in Directory.GetFiles(dir)) File.Delete(f);
            // A save from before slots, rosters, the story and per-fighter jobs: one
            // accepted job, an adventurer name list, written at version 1.
            const string legacy = "{\"saveVersion\":1,\"slot\":0,\"day\":3,\"currentBiomeIndex\":2,\"gold\":140," +
                                  "\"bestGrades\":[2,1,0,0,0],\"ownedAdventurers\":[\"Rookie\"]," +
                                  "\"contract\":{\"accepted\":true,\"title\":\"Standing order\",\"fee\":50,\"bonus\":12}," +
                                  "\"lastSavedUnixSeconds\":1700000000}";
            File.WriteAllText(Path.Combine(dir, "slot_0.json"), legacy);
            RunState s = save.Peek(0);
            Check(s != null && s.slot == 0 && s.day == 3 && s.gold == 140,
                "migration: an old slot_0 save still loads as slot 0, run intact");
            Check(s != null && s.roster != null && s.roster.Count == 1 && s.roster[0].displayName == "Rookie",
                "migration: the roster is seeded from the old adventurer list");
            Check(s != null && s.contracts.Count == 1 && s.contracts[0].title == "Standing order" && s.contracts[0].heroId == s.roster[0].id,
                "migration: the single old job moves into the per-fighter jobs");
            Check(s != null && s.saveVersion == RunState.CurrentVersion && s.storyFlags != null,
                "migration: the save comes up to the current version");
            Check(save.Load(0) && save.State.slot == 0, "migration: Continue on slot 1 resumes it");
        }

        private void TestAtomicSave(SaveSystem save, string dir)
        {
            foreach (string f in Directory.GetFiles(dir)) File.Delete(f);
            save.NewGame(2);
            save.State.gold = 7;
            save.Save();
            string path = Path.Combine(dir, "slot_2.json");
            Check(!File.Exists(path + ".tmp"), "atomic: no half-written .tmp is left behind");
            Check(File.Exists(path + ".bak"), "atomic: a save over an existing slot keeps the previous file as .bak");
            Check(save.Peek(2).gold == 7, "atomic: the slot holds the new save");
        }

        private void TestSummary()
        {
            var s = RunState.NewGame(1);
            s.day = 4;
            s.currentBiomeIndex = 2;
            s.lastSavedUnixSeconds = 1_700_000_000;
            SaveSlots.SlotSummary sum = SaveSlots.Describe(s, 1_700_000_000 + 2 * 3600);
            Check(sum.Road == Combat.BiomeLibrary.Name(2), $"summary: the card names the road ({sum.Road})");
            Check(sum.Night == "Night 3", $"summary: the card counts the nights ({sum.Night})");
            Check(sum.LastPlayed.Contains("2 hours ago"), $"summary: the card says when it was last played ({sum.LastPlayed})");
            s.day = 1;
            Check(SaveSlots.Describe(s, 1_700_000_000).Night == "First day", "summary: a fresh run has had no nights yet");
        }

        private void TestEveningMenu()
        {
            var s = RunState.NewGame(0);
            Check(!EveningScreen.ShowsMenu(s), "evening: no MENU beside SLEEP before the last road is cleared");
            s.bestGrades[Combat.BiomeLibrary.Count - 1] = 1;
            Check(EveningScreen.ShowsMenu(s), "evening: MENU beside SLEEP once the last road is cleared");
        }
    }
}
