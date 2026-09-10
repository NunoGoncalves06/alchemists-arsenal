using System.Collections.Generic;
using UnityEngine;
using AlchemistsArsenal.Combat.Considerations;
using AlchemistsArsenal.Data;

namespace AlchemistsArsenal.Combat
{
    /// <summary>
    /// Drop this on ONE empty GameObject in an empty scene and press Play — it
    /// assembles a full afternoon expedition in code: camera, arena, adventurers
    /// (movement FSM + IAUS bomb selection + ballistic launcher), a monster spawner
    /// and wave director, and the boss HFSM.
    ///
    /// Every serialized field is optional — leave them null for a self-contained
    /// default run (see <see cref="DefaultExpeditionData"/>), or drop in authored
    /// assets from the generators to tune it. This file is pure assembly + HUD.
    /// </summary>
    public class ExpeditionBootstrap : MonoBehaviour
    {
        [Header("Optional authored assets (built in code if left null)")]
        [SerializeField] private BiomeData biome;
        [SerializeField] private ElementalMatrix elementalMatrix;
        [SerializeField] private AdventurerLoadout loadout;
        [SerializeField] private BossDefinition bossOverride;

        [Header("Party")]
        [Range(1, 4)] [SerializeField] private int adventurerCount = 2;
        [SerializeField] private int adventurerHealth = 120;

        [Header("Scene")]
        [SerializeField] private bool createCamera = true;

        private ExpeditionManager _expedition;

        private void Start()
        {
            if (elementalMatrix == null) elementalMatrix = DefaultExpeditionData.Matrix();
            if (loadout == null) loadout = DefaultExpeditionData.Loadout();
            if (biome == null) biome = DefaultExpeditionData.Biome(bossOverride);

            if (createCamera && Camera.main == null) BuildCamera();
            BuildGround(biome);

            var considerations = DefaultExpeditionData.AdventurerConsiderations();
            for (int i = 0; i < adventurerCount; i++)
                BuildAdventurer(i, considerations);

            var spawner = new GameObject("MonsterSpawner").AddComponent<MonsterSpawner>();
            spawner.Configure(elementalMatrix, biome.ArenaWidth * 0.5f);

            _expedition = new GameObject("ExpeditionManager").AddComponent<ExpeditionManager>();
            _expedition.Configure(biome, spawner);

            Debug.Log($"[ExpeditionBootstrap] '{biome.BiomeName}' — {adventurerCount} adventurers, " +
                      $"{biome.Waves.Count} waves{(biome.HasBoss ? " + boss" : "")}.");
        }

        // ------------------------------------------------------------ adventurers

        private void BuildAdventurer(int index, List<UtilityConsideration> considerations)
        {
            float y = (index - (adventurerCount - 1) * 0.5f) * 2.2f;
            Vector2 pos = new Vector2(-biome.ArenaWidth * 0.5f + 2f, y);

            var go = new GameObject($"Adventurer_{index + 1}");
            go.SetActive(false);
            go.transform.position = pos;

            var rb = go.AddComponent<Rigidbody2D>();
            rb.gravityScale = 0f;
            rb.linearDamping = 2.5f;
            rb.freezeRotation = true;

            go.AddComponent<CircleCollider2D>().radius = 0.4f;

            var body = go.AddComponent<CombatantBody>();
            body.Initialise(Team.Adventurer, ElementType.Nature, adventurerHealth);

            go.AddComponent<AdventurerMovementController>();

            var ai = go.AddComponent<UtilityAI_CombatController>();
            ai.Configure(body, loadout, elementalMatrix, considerations);

            go.AddComponent<BallisticBombLauncher>().Configure(ai, elementalMatrix);

            PlaceholderArt.AddRenderer(go, PlaceholderArt.Shape.Disc, new Color(0.85f, 0.80f, 0.55f), 6);

            go.SetActive(true);
        }

        // ------------------------------------------------------------ scene dressing

        private static void BuildCamera()
        {
            var go = new GameObject("Main Camera") { tag = "MainCamera" };
            go.transform.position = new Vector3(0f, 0f, -10f);
            var cam = go.AddComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = 9f;
            cam.backgroundColor = new Color(0.06f, 0.07f, 0.09f);
            cam.clearFlags = CameraClearFlags.SolidColor;
        }

        private static void BuildGround(BiomeData biome)
        {
            var go = new GameObject("Ground");
            go.transform.position = new Vector3(0f, -0.5f, 1f);
            go.transform.localScale = new Vector3(biome.ArenaWidth + 8f, 14f, 1f);
            PlaceholderArt.AddRenderer(go, PlaceholderArt.Shape.Disc, biome.GroundTint, -10);
        }

        // The runtime HUD is ExpeditionHudScreen (DESIGN.md §7.8). This component is
        // only the editor drop-in demo now — no OnGUI.
    }
}
