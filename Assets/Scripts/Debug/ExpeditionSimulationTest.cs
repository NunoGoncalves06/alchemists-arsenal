using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using AlchemistsArsenal.Combat;
using AlchemistsArsenal.Combat.Considerations;
using AlchemistsArsenal.Data;

namespace AlchemistsArsenal.DebugTools
{
    /// <summary>
    /// Play-mode integration test for the playable slice: spins up a tiny expedition
    /// (spawner + wave director + one real adventurer) and asserts it reaches a
    /// terminal Won / Lost state for the right reason.
    /// </summary>
    public class ExpeditionSimulationTest : MonoBehaviour
    {
        /// <summary>Set when every scenario has resolved, so a harness can wait on it.</summary>
        public static bool Finished { get; private set; }

        private void Start() { Finished = false; StartCoroutine(Run()); }

        private IEnumerator Run()
        {
            Debug.Log("<color=cyan><b>=== EXPEDITION SLICE SIMULATION ===</b></color>");
            yield return Scenario_Win();
            yield return Scenario_Lose();
            yield return Scenario_OutOfFlasks();
            Finished = true;
            Debug.Log("<color=cyan><b>=== SIMULATION COMPLETE ===</b></color>");
        }

        private IEnumerator Scenario_Win()
        {
            MonsterRegistry.Clear();
            AdventurerRegistry.Clear();

            ElementalMatrix matrix = BuildMatrix();
            SpawnAdventurer(new Vector2(-8f, 0f), 300, matrix);

            MonsterData weakling = MonsterData.Create("Weakling", ElementType.Nature, 8, 1.2f);
            var biome = ScriptableObject.CreateInstance<BiomeData>();
            biome.Configure("Test Grove", ElementType.Nature, Color.gray, 20f,
                new[] { BiomeData.MakeWave(weakling, 3, 0.3f, 0.3f) }, null);

            ExpeditionManager mgr = BuildExpedition(biome, matrix);

            bool finished = false, won = false;
            mgr.OnFinished += w => { finished = true; won = w; };

            float t = 0f;
            while (!finished && t < 30f) { t += Time.deltaTime; yield return null; }

            Report($"Win scenario finished ({t:F1}s)", finished);
            Report("Win scenario outcome = Won", won && mgr.Phase == ExpeditionPhase.Won);

            CleanupExpedition(mgr, biome);
        }

        private IEnumerator Scenario_Lose()
        {
            MonsterRegistry.Clear();
            AdventurerRegistry.Clear();

            ElementalMatrix matrix = BuildMatrix();
            SpawnAdventurer(new Vector2(-3f, 0f), 12, matrix); // fragile, and starts close

            MonsterData brute = MonsterData.Create("Brute", ElementType.Fire, 400, 4.5f);
            var biome = ScriptableObject.CreateInstance<BiomeData>();
            biome.Configure("Test Pit", ElementType.Fire, Color.gray, 16f,
                new[] { BiomeData.MakeWave(brute, 3, 0.2f, 0.2f) }, null);

            ExpeditionManager mgr = BuildExpedition(biome, matrix);

            bool finished = false, won = true;
            mgr.OnFinished += w => { finished = true; won = w; };

            float t = 0f;
            while (!finished && t < 30f) { t += Time.deltaTime; yield return null; }

            Report($"Lose scenario finished ({t:F1}s)", finished);
            Report("Lose scenario outcome = Lost", !won && mgr.Phase == ExpeditionPhase.Lost);

            CleanupExpedition(mgr, biome);
        }

        /// <summary>
        /// Regression test for the reported bug: the player ran out of flasks, the
        /// adventurer stood there taking contact damage, each wave hit its safety
        /// cap, the monsters were despawned, and the run reported a VICTORY with a
        /// full wave clear.
        ///
        /// Two separate faults: the timeout despawned the survivors and fell
        /// through to Win(), and nothing ever noticed the party could no longer
        /// damage anything. This asserts both are fixed - an unarmed party must
        /// LOSE, promptly, and must not be credited with clearing anything.
        /// </summary>
        private IEnumerator Scenario_OutOfFlasks()
        {
            MonsterRegistry.Clear();
            AdventurerRegistry.Clear();

            ElementalMatrix matrix = BuildMatrix();
            // Tough enough to survive the whole wave, but with a single flask -
            // far too little to kill anything here.
            SpawnAdventurer(new Vector2(-8f, 0f), 4000, matrix, ammo: 1);

            MonsterData tank = MonsterData.Create("Tank", ElementType.Nature, 5000, 0.6f);
            var biome = ScriptableObject.CreateInstance<BiomeData>();
            biome.Configure("Test Impasse", ElementType.Nature, Color.gray, 20f,
                new[] { BiomeData.MakeWave(tank, 2, 0.3f, 0.3f) }, null);

            ExpeditionManager mgr = BuildExpedition(biome, matrix);

            bool finished = false, won = true;
            mgr.OnFinished += w => { finished = true; won = w; };

            float t = 0f;
            while (!finished && t < 40f) { t += Time.deltaTime; yield return null; }

            Report($"Out-of-flasks scenario finished ({t:F1}s)", finished);
            Report("Out-of-flasks outcome = Lost (NOT a phantom victory)",
                finished && !won && mgr.Phase == ExpeditionPhase.Lost);
            Report($"Out-of-flasks credits 0 waves cleared (got {mgr.WavesCleared})",
                mgr.WavesCleared == 0);
            Report($"Out-of-flasks says why ('{mgr.OutcomeReason}')",
                !string.IsNullOrEmpty(mgr.OutcomeReason));

            // It must give up rather than let the player be chewed on for the full
            // safety cap of every wave in the biome.
            Report($"Out-of-flasks ends promptly, not after the wave timeout ({t:F1}s)", t < 15f);

            CleanupExpedition(mgr, biome);
        }

        // ------------------------------------------------------------- helpers

        private static ElementalMatrix BuildMatrix()
        {
            var m = ScriptableObject.CreateInstance<ElementalMatrix>();
            m.SetMultiplier(ElementType.Fire, ElementType.Nature, 2f);
            m.SetMultiplier(ElementType.Nature, ElementType.Fire, 0.5f);
            return m;
        }

        private static void SpawnAdventurer(Vector2 pos, int hp, ElementalMatrix matrix, int ammo = 99)
        {
            var go = new GameObject("TestAdventurer");
            go.SetActive(false);
            go.transform.position = pos;

            var rb = go.AddComponent<Rigidbody2D>();
            rb.gravityScale = 0f;
            rb.linearDamping = 2.5f;
            rb.freezeRotation = true;
            go.AddComponent<CircleCollider2D>().radius = 0.4f;

            var body = go.AddComponent<CombatantBody>();
            body.Initialise(Team.Adventurer, ElementType.Nature, hp);

            go.AddComponent<AdventurerMovementController>();

            var ai = go.AddComponent<UtilityAI_CombatController>();
            var elemental = ScriptableObject.CreateInstance<ElementalVulnerabilityConsideration>();
            elemental.Configure(AnimationCurve.Linear(0, 0, 1, 1), 1.2f);
            var distance = ScriptableObject.CreateInstance<DistanceConsideration>();
            distance.Configure(new AnimationCurve(new Keyframe(0, 0), new Keyframe(0.5f, 1), new Keyframe(1, 0.2f)), 1f);
            var loadout = AdventurerLoadout.Create(
                AdventurerLoadout.Slot(BombData.Create("Fire", ElementType.Fire, 40, 2.6f, 13f, 6f, 2f, 13f, 0.8f), ammo));
            ai.Configure(body, loadout, matrix, new List<UtilityConsideration> { elemental, distance });

            go.AddComponent<BallisticBombLauncher>().Configure(ai, matrix);

            go.SetActive(true);
        }

        private static ExpeditionManager BuildExpedition(BiomeData biome, ElementalMatrix matrix)
        {
            var spawnerGo = new GameObject("TestSpawner");
            var spawner = spawnerGo.AddComponent<MonsterSpawner>();
            spawner.Configure(matrix, biome.ArenaWidth * 0.5f);

            var mgrGo = new GameObject("TestExpedition");
            var mgr = mgrGo.AddComponent<ExpeditionManager>();
            mgr.Configure(biome, spawner); // starts the run
            return mgr;
        }

        private static void CleanupExpedition(ExpeditionManager mgr, Object biome)
        {
            foreach (var b in Object.FindObjectsByType<CombatantBody>(FindObjectsInactive.Include))
                if (b != null) Destroy(b.gameObject);
            if (mgr != null) Destroy(mgr.gameObject);
            MonsterRegistry.Clear();
            AdventurerRegistry.Clear();
        }

        private static void Report(string label, bool pass)
        {
            if (pass) Debug.Log($"<color=green><b>PASS</b></color> {label}");
            else Debug.LogError($"<color=red><b>FAIL</b></color> {label}");
        }
    }
}
