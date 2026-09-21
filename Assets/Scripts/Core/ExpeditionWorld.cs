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

        /// <summary>
        /// Who is actually fighting this afternoon. The potion is carried by the
        /// person who ordered it — a wallguard captain who commissioned a Fire flask
        /// walks the road himself — so the name and the sprite both come from the
        /// morning's contract rather than always being Rookie.
        /// </summary>
        public string FighterName { get; private set; } = "Rookie";

        /// <summary>Portrait/sprite key for <see cref="FighterName"/>.</summary>
        public string FighterId { get; private set; } = "rookie";

        private readonly List<CombatantBody> _party = new List<CombatantBody>();
        private BiomeData _biome;

        /// <summary>Who went out today, in party order. Parallel to <see cref="Party"/>.</summary>
        public IReadOnlyList<HeroRecord> PartyRecords => _partyRecords;

        private readonly List<HeroRecord> _partyRecords = new List<HeroRecord>();

        public void Build(BiomeData biome, IReadOnlyList<AdventurerLoadout> loadouts,
            IReadOnlyList<HeroRecord> party, bool enableBoss = true)
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

            // Invisible arena bounds so nobody (adventurer especially) walks off camera.
            float halfW = biome.ArenaWidth * 0.5f + 1.5f;
            const float halfH = 7f;
            BuildWall(new Vector2(-halfW, 0f), new Vector2(1f, halfH * 2f));
            BuildWall(new Vector2(halfW, 0f), new Vector2(1f, halfH * 2f));
            BuildWall(new Vector2(0f, halfH), new Vector2(halfW * 2f, 1f));
            BuildWall(new Vector2(0f, -halfH), new Vector2(halfW * 2f, 1f));

            ResolveFighter();

            int adventurerCount = Mathf.Clamp(party != null ? party.Count : 1, 1, 4);
            for (int i = 0; i < adventurerCount; i++)
            {
                HeroRecord hero = party != null && i < party.Count ? party[i] : null;
                AdventurerLoadout lo = loadouts != null && loadouts.Count > 0
                    ? loadouts[Mathf.Min(i, loadouts.Count - 1)]
                    : null;
                BuildAdventurer(i, adventurerCount, lo, hero, matrix);
            }

            var spawner = new GameObject("MonsterSpawner").AddComponent<MonsterSpawner>();
            spawner.transform.SetParent(transform, false);
            // Deterministic per (day, biome) so a fight is reproducible, and isolated
            // from the global random stream so unrelated features cannot shift it.
            RunState run = SaveSystem.Instance != null ? SaveSystem.Instance.State : null;
            int day = run != null ? run.day : 1;
            int biomeIndex = run != null ? run.TargetBiomeIndex : 0;
            spawner.Configure(matrix, biome.ArenaWidth * 0.5f, 7919 * day + 31 * biomeIndex);

            Expedition = new GameObject("ExpeditionManager").AddComponent<ExpeditionManager>();
            Expedition.transform.SetParent(transform, false);

            Telemetry = gameObject.AddComponent<ExpeditionTelemetry>();

            // Configure() starts the run on the same frame — begin telemetry first.
            Telemetry.Begin(Expedition, biome.BiomeName, adventurerCount, biome.Waves.Count);
            Expedition.OnFinished += HandleFinished;
            Expedition.Configure(biome, spawner, enableBoss);
        }

        /// <summary>Read the day's customer off the contract; fall back to Rookie.</summary>
        private void ResolveFighter()
        {
            RunState s = SaveSystem.Instance != null ? SaveSystem.Instance.State : null;
            ContractRecord job = s != null ? s.contract : null;
            CustomerDefinition buyer = job != null && job.accepted
                ? CustomerCatalog.ById(job.buyerId)
                : CustomerCatalog.Rookie;

            FighterName = buyer.DisplayName;
            FighterId = buyer.PortraitId;
        }

        private void BuildWall(Vector2 pos, Vector2 size)
        {
            var wall = new GameObject("ArenaWall");
            wall.transform.SetParent(transform, false);
            wall.transform.position = pos;
            wall.AddComponent<BoxCollider2D>().size = size;
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
            foreach (var p in FindObjectsByType<BombProjectile2D>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (p != null) Destroy(p.gameObject);
        }

        /// <summary>
        /// Re-weight the IAUS axes for an archetype. Only the three axes the
        /// archetypes differ on are touched; ward avoidance is left alone because
        /// dodging a warded element is never a stylistic choice.
        /// </summary>
        private static void ApplyArchetypeWeights(List<UtilityConsideration> axes, HeroArchetype archetype)
        {
            if (axes == null) return;
            foreach (UtilityConsideration axis in axes)
            {
                if (axis == null) continue;
                switch (axis)
                {
                    case ElementalVulnerabilityConsideration: axis.SetWeight(archetype.ElementalWeight); break;
                    case DistanceConsideration: axis.SetWeight(archetype.DistanceWeight); break;
                    case SelfHealthConsideration: axis.SetWeight(archetype.SelfHealthWeight); break;
                }
            }
        }

        private void BuildAdventurer(int index, int count, AdventurerLoadout loadout,
            HeroRecord hero, ElementalMatrix matrix)
        {
            float y = (index - (count - 1) * 0.5f) * 2.2f;

            // Identity comes from the roster now. The contract's buyer stays a
            // Counter-side narrative element (who ordered the flask), and is only
            // the fallback for the bootstrap/demo path that has no roster.
            string fighterName = hero != null ? hero.displayName : (index == 0 ? FighterName : "Rookie");
            string fighterId = hero != null ? hero.portraitId : (index == 0 ? FighterId : "rookie");
            _partyRecords.Add(hero);

            var go = new GameObject($"Adventurer_{fighterName}");
            go.transform.SetParent(transform, false);
            go.SetActive(false);
            // Was 2 units from the left edge — with the arena walls (ExpeditionWorld
            // now builds them) that's almost no room to Retreat into: fleeing straight
            // away from an approaching monster pins the adventurer against its own
            // spawn wall almost immediately. Starting a quarter of the way into the
            // arena gives real maneuvering room in both directions.
            go.transform.position = new Vector2(-_biome.ArenaWidth * 0.25f, y);

            var rb = go.AddComponent<Rigidbody2D>();
            rb.gravityScale = 0f;
            rb.linearDamping = 2.5f;
            rb.freezeRotation = true;
            go.AddComponent<CircleCollider2D>().radius = 0.4f;

            RunState s = SaveSystem.Instance != null ? SaveSystem.Instance.State : null;
            // 120 -> 150 base: a single adventurer facing 3+ monsters at once was
            // dying before the AI/movement fixes had a real chance to work
            // (playtest: near-instant losses). Thick Boots still stacks on top.
            int maxHp = 150 + (s != null && s.HasUpgrade(UpgradeCatalog.ThickBoots) ? 30 : 0);

            int level = hero != null ? hero.level : 1;
            ElementType affinity = hero != null ? hero.affinity : ElementType.Nature;
            HeroArchetype archetype = HeroPerks.Archetype(hero != null ? hero.archetypeId : null);
            maxHp = Mathf.RoundToInt(maxHp * HeroCatalog.HpScale(level));

            var body = go.AddComponent<CombatantBody>();
            // The element was hardcoded Nature for everyone, which also made every
            // hero maximally vulnerable to Fire and Poison. It is the hero's own
            // affinity now.
            body.Initialise(Team.Adventurer, affinity, maxHp);
            _party.Add(body);

            // The defensive half of the perk. Must go on before the GameObject is
            // activated: CombatantBody.Awake caches the provider exactly once.
            go.AddComponent<HeroWard>().Configure(affinity);

            if (hero != null) go.AddComponent<HeroTag>().heroId = hero.id;

            var move = go.AddComponent<AdventurerMovementController>();
            // Deliberately tighter than the arena walls (half-height 7): the band the
            // fighter circles inside keeps clear of the HUD slabs at the top and
            // bottom of the screen, so they never end up standing behind a button.
            move.ConfigureArena(new Vector2(_biome.ArenaWidth * 0.5f + 1f, 5.2f));
            move.Configure(archetype);

            // Fresh axes per hero. These used to be built once and the same
            // ScriptableObject instances handed to every party member, so any
            // per-hero weight would have been written into all of them.
            var considerations = DefaultExpeditionData.AdventurerConsiderations();
            ApplyArchetypeWeights(considerations, archetype);

            var ai = go.AddComponent<UtilityAI_CombatController>();
            ai.Configure(body, loadout, matrix, considerations);
            ai.ConfigureHero(affinity, HeroCatalog.DamageScale(level));
            if (loadout != null) ai.ConfigurePotionQuality(loadout.PotionQuality01);
            go.AddComponent<BallisticBombLauncher>().Configure(ai, matrix);

            // A player-marker ring under the adventurer's feet — no monster has one —
            // plus a larger sprite than before. Exhaustive code search found no
            // mechanism for the sprite/colour to actually change at runtime (every
            // SpriteRenderer in the project is created once and never touched
            // again), so this isn't chasing a confirmed swap bug; it's making the
            // adventurer impossible to mistake for a monster regardless of cause —
            // a fast-moving 16px sprite at typical arena zoom is genuinely hard to
            // track by eye once several monsters close in around it (playtest).
            var marker = new GameObject("PlayerMarker");
            marker.transform.SetParent(go.transform, false);
            PixelArt.AddDisc(marker, new Color(0.35f, 0.95f, 1f, 0.6f), 4, 1.15f);

            var art = new GameObject("Art");
            art.transform.SetParent(go.transform, false);
            PixelArt.AddSprite(art, Art.PixelSprites.Fighter(fighterId), 6, 1.9f);
            go.SetActive(true);

            // Everyone in the arena carries a health bar now, the party included —
            // the HUD card is easy to miss while you are watching the fight itself.
            HealthBar2D.Attach(body, width: 1.2f, lift: 0.85f);
        }
    }
}
