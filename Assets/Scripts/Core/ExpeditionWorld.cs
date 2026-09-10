using System;
using System.Collections.Generic;
using UnityEngine;
using AlchemistsArsenal.Combat;
using AlchemistsArsenal.Combat.Considerations;
using AlchemistsArsenal.Data;

namespace AlchemistsArsenal.Core
{
    /// <summary>
    /// Builds one afternoon expedition arena from a biome + the morning's loadout,
    /// wires <see cref="ExpeditionTelemetry"/>, and raises <see cref="OnFinished"/>
    /// with the finished report. The loop-owned root that carries this component is
    /// destroyed at BeginEvening.
    ///
    /// This is the runtime path; <c>ExpeditionBootstrap</c> stays as the editor
    /// drop-in demo (its OnGUI HUD is gone — the real HUD is <c>ExpeditionHudScreen</c>).
    /// </summary>
    public class ExpeditionWorld : MonoBehaviour
    {
        public event Action<ExpeditionReport> OnFinished;

        public ExpeditionManager Expedition { get; private set; }
        public ExpeditionTelemetry Telemetry { get; private set; }
        public Camera ArenaCamera { get; private set; }
        public IReadOnlyList<CombatantBody> Party => _party;

        private readonly List<CombatantBody> _party = new List<CombatantBody>();
        private BiomeData _biome;

        public void Build(BiomeData biome, AdventurerLoadout loadout, int adventurerCount)
        {
            _biome = biome;
            transform.position = Vector3.zero;

            ElementalMatrix matrix = DefaultExpeditionData.Matrix();

            var camGo = new GameObject("ArenaCamera") { tag = "MainCamera" }; // movement code reads Camera.main (reviewer N1)
            ArenaCamera = camGo.AddComponent<Camera>();
            ArenaCamera.transform.SetParent(transform, false);
            ArenaCamera.transform.position = new Vector3(0f, 0f, -10f);
            ArenaCamera.orthographic = true;
            ArenaCamera.orthographicSize = 8.5f;
            ArenaCamera.clearFlags = CameraClearFlags.SolidColor;
            ArenaCamera.backgroundColor = new Color(0.06f, 0.05f, 0.07f);
            ArenaCamera.depth = -1;

            var ground = new GameObject("Ground");
            ground.transform.SetParent(transform, false);
            ground.transform.position = new Vector3(0f, -0.5f, 1f);
            ground.transform.localScale = new Vector3(biome.ArenaWidth + 8f, 14f, 1f);
            PixelArt.AddDisc(ground, biome.GroundTint, -10); // diameter 0 = keep the scale set above

            var considerations = DefaultExpeditionData.AdventurerConsiderations();
            adventurerCount = Mathf.Clamp(adventurerCount, 1, 4);
            for (int i = 0; i < adventurerCount; i++)
                BuildAdventurer(i, adventurerCount, loadout, matrix, considerations);

            var spawner = new GameObject("MonsterSpawner").AddComponent<MonsterSpawner>();
            spawner.transform.SetParent(transform, false);
            spawner.Configure(matrix, biome.ArenaWidth * 0.5f);

            Expedition = new GameObject("ExpeditionManager").AddComponent<ExpeditionManager>();
            Expedition.transform.SetParent(transform, false);

            Telemetry = gameObject.AddComponent<ExpeditionTelemetry>();

            // Configure() starts the run on the same frame — begin telemetry first.
            Telemetry.Begin(Expedition, biome.BiomeName, adventurerCount);
            Expedition.OnFinished += HandleFinished;
            Expedition.Configure(biome, spawner);
        }

        private void HandleFinished(bool won)
        {
            OnFinished?.Invoke(Telemetry != null ? Telemetry.Report : new ExpeditionReport { won = won });
        }

        private void OnDestroy()
        {
            // Belt-and-braces: the static registries must not carry this run's
            // corpses into tomorrow (reviewer P1). Monsters/adventurers are parented
            // under this root and die with it; stray projectiles get swept here.
            MonsterRegistry.Clear();
            AdventurerRegistry.Clear();
            foreach (var p in FindObjectsByType<BombProjectile2D>(FindObjectsSortMode.None))
                if (p != null) Destroy(p.gameObject);
        }

        private void BuildAdventurer(int index, int count, AdventurerLoadout loadout,
            ElementalMatrix matrix, List<UtilityConsideration> considerations)
        {
            float y = (index - (count - 1) * 0.5f) * 2.2f;
            var go = new GameObject($"Adventurer_{index + 1}");
            go.transform.SetParent(transform, false);
            go.SetActive(false);
            go.transform.position = new Vector2(-_biome.ArenaWidth * 0.5f + 2f, y);

            var rb = go.AddComponent<Rigidbody2D>();
            rb.gravityScale = 0f;
            rb.linearDamping = 2.5f;
            rb.freezeRotation = true;
            go.AddComponent<CircleCollider2D>().radius = 0.4f;

            var body = go.AddComponent<CombatantBody>();
            body.Initialise(Team.Adventurer, ElementType.Nature, 120);
            _party.Add(body);

            go.AddComponent<AdventurerMovementController>();

            var ai = go.AddComponent<UtilityAI_CombatController>();
            ai.Configure(body, loadout, matrix, considerations);
            go.AddComponent<BallisticBombLauncher>().Configure(ai, matrix);

            PixelArt.AddDisc(go, new Color(0.85f, 0.80f, 0.55f), 6, 0.8f);
            go.SetActive(true);
        }
    }
}
