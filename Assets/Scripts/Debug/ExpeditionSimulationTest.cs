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
        private void Start() => StartCoroutine(Run());

        private IEnumerator Run()
        {
            Debug.Log("<color=cyan><b>=== EXPEDITION SLICE SIMULATION ===</b></color>");
            yield return Scenario_Win();
            yield return Scenario_Lose();
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

        // ------------------------------------------------------------- helpers

        private static ElementalMatrix BuildMatrix()
        {
            var m = ScriptableObject.CreateInstance<ElementalMatrix>();
            m.SetMultiplier(ElementType.Fire, ElementType.Nature, 2f);
            m.SetMultiplier(ElementType.Nature, ElementType.Fire, 0.5f);
            return m;
        }

        private static void SpawnAdventurer(Vector2 pos, int hp, ElementalMatrix matrix)
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
                AdventurerLoadout.Slot(BombData.Create("Fire", ElementType.Fire, 40, 2.6f, 13f, 6f, 2f, 13f, 0.8f), 99));
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
            foreach (var b in Object.FindObjectsByType<CombatantBody>(FindObjectsSortMode.None))
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
