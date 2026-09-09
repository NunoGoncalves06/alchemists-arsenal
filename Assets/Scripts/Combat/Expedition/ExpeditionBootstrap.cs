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
    /// and wave director, and (if a <see cref="BossDefinition"/> is supplied or
    /// generated in code) the boss HFSM.
    ///
    /// Every serialized field is optional — leave them null for a self-contained
    /// default run, or drop in authored assets (from the generators) to tune it.
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
        [SerializeField] private bool showHud = true;

        private ExpeditionManager _expedition;

        private void Start()
        {
            if (elementalMatrix == null) elementalMatrix = BuildDefaultMatrix();
            if (loadout == null) loadout = BuildDefaultLoadout();
            if (biome == null) biome = BuildDefaultBiome();

            if (createCamera && Camera.main == null) BuildCamera();
            BuildGround(biome);

            var considerations = BuildConsiderations();
            for (int i = 0; i < adventurerCount; i++)
                BuildAdventurer(i, considerations);

            var spawnerGo = new GameObject("MonsterSpawner");
            var spawner = spawnerGo.AddComponent<MonsterSpawner>();
            spawner.Configure(elementalMatrix, biome.ArenaWidth * 0.5f);

            var expeditionGo = new GameObject("ExpeditionManager");
            _expedition = expeditionGo.AddComponent<ExpeditionManager>();
            _expedition.Configure(biome, spawner);

            Debug.Log($"[ExpeditionBootstrap] '{biome.BiomeName}' — {adventurerCount} adventurers, " +
                      $"{biome.Waves.Count} waves{(biome.HasBoss ? " + boss" : "")}. Press Play is already Playing.");
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

            var launcher = go.AddComponent<BallisticBombLauncher>();
            launcher.Configure(ai, elementalMatrix);

            PlaceholderArt.AddRenderer(go, PlaceholderArt.Shape.Disc, new Color(0.85f, 0.80f, 0.55f), 6);

            go.SetActive(true);
        }

        private static List<UtilityConsideration> BuildConsiderations()
        {
            var elemental = ScriptableObject.CreateInstance<ElementalVulnerabilityConsideration>();
            elemental.Configure(AnimationCurve.Linear(0f, 0f, 1f, 1f), 1.2f, "Favour the elemental matchup.");

            var distance = ScriptableObject.CreateInstance<DistanceConsideration>();
            distance.Configure(new AnimationCurve(
                new Keyframe(0f, 0f), new Keyframe(0.5f, 1f), new Keyframe(1f, 0.15f)), 1.0f, "Prefer the ideal band.");

            var health = ScriptableObject.CreateInstance<SelfHealthConsideration>();
            health.Configure(AnimationCurve.EaseInOut(0f, 0.35f, 1f, 1f), 0.5f, "Slight bias to act while healthy.");

            var ward = ScriptableObject.CreateInstance<WardAvoidanceConsideration>();
            ward.Configure(AnimationCurve.Linear(0f, 0f, 1f, 1f), 1.6f, "Avoid the boss's warded element.");

            return new List<UtilityConsideration> { elemental, distance, health, ward };
        }

        // ------------------------------------------------------------ default data

        private static ElementalMatrix BuildDefaultMatrix()
        {
            var m = ScriptableObject.CreateInstance<ElementalMatrix>();
            m.EnsureInitialised();
            // Wheel: Fire > Nature > Water > Fire, and Poison > Nature, Arcane > Poison.
            m.SetMultiplier(ElementType.Fire, ElementType.Nature, 2f);
            m.SetMultiplier(ElementType.Nature, ElementType.Fire, 0.5f);
            m.SetMultiplier(ElementType.Nature, ElementType.Water, 2f);
            m.SetMultiplier(ElementType.Water, ElementType.Nature, 0.5f);
            m.SetMultiplier(ElementType.Water, ElementType.Fire, 2f);
            m.SetMultiplier(ElementType.Fire, ElementType.Water, 0.5f);
            m.SetMultiplier(ElementType.Poison, ElementType.Nature, 2f);
            m.SetMultiplier(ElementType.Arcane, ElementType.Poison, 2f);
            return m;
        }

        private static AdventurerLoadout BuildDefaultLoadout()
        {
            BombData Fire() => BombData.Create("Firebloom Flask", ElementType.Fire, 24, 2.6f, 13f, 6f, 2f, 13f, 1.4f);
            BombData Water() => BombData.Create("Tidevial", ElementType.Water, 22, 2.4f, 13f, 6f, 2f, 13f, 1.4f);
            BombData Nature() => BombData.Create("Thornburst", ElementType.Nature, 20, 3.0f, 12f, 5f, 2f, 12f, 1.6f);
            BombData Poison() => BombData.Create("Miremist Phial", ElementType.Poison, 16, 3.4f, 14f, 7f, 2f, 15f, 2f);

            return AdventurerLoadout.Create(
                AdventurerLoadout.Slot(Fire(), 6),
                AdventurerLoadout.Slot(Water(), 6),
                AdventurerLoadout.Slot(Nature(), 6),
                AdventurerLoadout.Slot(Poison(), 4));
        }

        private BiomeData BuildDefaultBiome()
        {
            MonsterData treant = MonsterData.Create("Bark Treant", ElementType.Nature, 60, 1.6f);
            MonsterData emberling = MonsterData.Create("Emberling", ElementType.Fire, 40, 2.8f);
            MonsterData frostkin = MonsterData.Create("Frostkin", ElementType.Water, 50, 2.1f);

            var waves = new[]
            {
                BiomeData.MakeWave(treant, 3, 0.7f, 1f),
                BiomeData.MakeWave(emberling, 4, 0.5f, 1.5f),
                BiomeData.MakeWave(frostkin, 3, 0.6f, 1.5f),
            };

            var b = ScriptableObject.CreateInstance<BiomeData>();
            b.Configure("Venom Swamp", ElementType.Nature, new Color(0.12f, 0.17f, 0.13f), 26f,
                waves, bossOverride != null ? bossOverride : BuildDefaultBoss());
            return b;
        }

        private static BossDefinition BuildDefaultBoss()
        {
            var threat = ScriptableObject.CreateInstance<ElementalThreatProfile>();
            threat.SetEntries(new[]
            {
                ThreatEntry(ElementType.Fire,   8f, 45f, 90f,  ElementType.Water),
                ThreatEntry(ElementType.Water,  8f, 45f, 90f,  ElementType.Nature),
                ThreatEntry(ElementType.Nature, 8f, 45f, 90f,  ElementType.Fire),
                ThreatEntry(ElementType.Poison, 7f, 40f, 85f,  ElementType.Arcane),
            });

            var bolt = BossAttackPattern.Create("Arcane Bolt", ElementType.Arcane, 10, 8f, 1.4f, 0.5f, 0.7f, 1.6f, 5f);
            var slam = BossAttackPattern.Create("Coven Slam", ElementType.Nature, 18, 3.5f, 2.4f, 0.8f, 1.0f, 2.6f, 9f);
            var pulse = BossAttackPattern.Create("Ward Pulse", ElementType.Water, 8, 4f, 2.0f, 0.4f, 0.6f, 1.4f, 7f);

            BossConsideration Hp(AnimationCurve c, float w) { var x = ScriptableObject.CreateInstance<BossHealthConsideration>(); x.Configure(c, w); return x; }
            BossConsideration Press(AnimationCurve c, float w) { var x = ScriptableObject.CreateInstance<ElementPressureConsideration>(); x.Configure(c, w); return x; }
            BossConsideration Spike(AnimationCurve c, float w) { var x = ScriptableObject.CreateInstance<RecentDamageConsideration>(); x.Configure(c, w); return x; }
            BossConsideration WardLatch() { var x = ScriptableObject.CreateInstance<ElementThreatOverrideConsideration>(); x.Configure(AnimationCurve.Linear(0f, 0f, 1f, 1f), 3f); return x; }

            AnimationCurve rising = AnimationCurve.EaseInOut(0f, 0.05f, 1f, 1f);
            AnimationCurve falling = AnimationCurve.EaseInOut(0f, 1f, 1f, 0.05f);
            AnimationCurve fallSteep = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(0.35f, 0.15f), new Keyframe(1f, 0f));

            var phases = new[]
            {
                BossPhaseData.Create(BossPhase.Neutral, 2.5f,
                    new[] { Hp(rising, 1f), Press(falling, 0.8f) }, new[] { bolt }),
                BossPhaseData.Create(BossPhase.Enraged, 3f,
                    new[] { Hp(falling, 1.1f), Press(rising, 0.6f) }, new[] { slam, bolt }),
                BossPhaseData.Create(BossPhase.ElementalWard, 1f,
                    new[] { WardLatch() }, new[] { pulse }),
                BossPhaseData.Create(BossPhase.Recovering, 1f,
                    new[] { Spike(rising, 1.2f), Hp(fallSteep, 1f) }, new BossAttackPattern[0]),
            };

            var def = ScriptableObject.CreateInstance<BossDefinition>();
            def.Configure("The Coven Matriarch", ElementType.Arcane, 520, threat, phases);
            return def;
        }

        private static ElementalThreatProfile.Entry ThreatEntry(ElementType e, float decay, float soft, float hard, ElementType ward) =>
            new ElementalThreatProfile.Entry
            {
                element = e, decayPerSecond = decay, softThreshold = soft, hardThreshold = hard, counterWard = ward
            };

        // ------------------------------------------------------------ scene dressing

        private static void BuildCamera()
        {
            var go = new GameObject("Main Camera");
            go.tag = "MainCamera";
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

        // ------------------------------------------------------------ minimal HUD

        private void OnGUI()
        {
            if (!showHud || _expedition == null) return;

            GUI.color = Color.white;
            var style = new GUIStyle(GUI.skin.label) { fontSize = 16, fontStyle = FontStyle.Bold };
            GUI.Label(new Rect(12, 10, 500, 24),
                $"{(biome != null ? biome.BiomeName : "?")}  —  {_expedition.Phase}", style);

            string line2 = _expedition.Phase == ExpeditionPhase.BossFight
                ? BossLine()
                : $"Wave {_expedition.WaveNumber}/{_expedition.TotalWaves}   Monsters: {CountAlive(MonsterRegistry.ActiveMonsters)}";
            GUI.Label(new Rect(12, 34, 600, 22), line2);

            GUI.Label(new Rect(12, 56, 600, 22), $"Adventurers: {AdventurerHpLine()}");

            if (_expedition.Phase == ExpeditionPhase.Won || _expedition.Phase == ExpeditionPhase.Lost)
            {
                var big = new GUIStyle(GUI.skin.label) { fontSize = 40, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
                big.normal.textColor = _expedition.Phase == ExpeditionPhase.Won ? Color.green : new Color(1f, 0.4f, 0.4f);
                GUI.Label(new Rect(0, Screen.height * 0.4f, Screen.width, 60),
                    _expedition.Phase == ExpeditionPhase.Won ? "VICTORY" : "DEFEAT", big);
            }
        }

        private string BossLine()
        {
            var boss = _expedition.BossInstance;
            if (boss == null) return "Boss down.";
            var body = boss.GetComponent<CombatantBody>();
            var phase = boss.GetComponent<BossPhaseManager>();
            return $"BOSS {(body != null ? body.CurrentHP : 0)}/{(body != null ? body.MaxHP : 0)} HP" +
                   $"   phase: {(phase != null ? phase.CurrentPhase.ToString() : "?")}";
        }

        private static int CountAlive(IReadOnlyList<ICombatant> list)
        {
            int n = 0;
            for (int i = 0; i < list.Count; i++) if (list[i] != null && list[i].IsAlive) n++;
            return n;
        }

        private static string AdventurerHpLine()
        {
            var list = AdventurerRegistry.ActiveAdventurers;
            if (list.Count == 0) return "-";
            var parts = new List<string>();
            for (int i = 0; i < list.Count; i++)
                if (list[i] != null) parts.Add($"{list[i].CurrentHP}");
            return string.Join(" / ", parts);
        }
    }
}
