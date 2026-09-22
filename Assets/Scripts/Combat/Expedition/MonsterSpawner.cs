using UnityEngine;
using AlchemistsArsenal.Art;
using AlchemistsArsenal.Data;

namespace AlchemistsArsenal.Combat
{
    /// <summary>
    /// Factory for monster and boss GameObjects — assembles the Rigidbody2D +
    /// collider + <see cref="CombatantBody"/> + walker (+ boss HFSM) and gives each a
    /// placeholder sprite. Builds each GameObject inactive, configures it, then
    /// activates it so registry routing is correct. The wave schedule lives in
    /// <see cref="ExpeditionManager"/>.
    /// </summary>
    public class MonsterSpawner : MonoBehaviour
    {
        [SerializeField] private ElementalMatrix elementalMatrix;
        [SerializeField] private float spawnEdgeX = 13f;
        [SerializeField] private float spawnBandY = 3f;

        /// <summary>
        /// Spawn placement runs off this spawner's OWN stream, not UnityEngine.Random.
        ///
        /// Sharing the global stream made the fight silently depend on how much
        /// randomness the rest of the game happened to consume first: adding the Prep
        /// bench's leaf-drop (which jitters each herb) shifted every monster's spawn
        /// position, so an unrelated crafting feature flipped both expeditions from
        /// won to lost. Same seed in, same fight out, regardless of what else drew.
        /// </summary>
        private System.Random _rng = new System.Random(12345);

        public void Configure(ElementalMatrix matrix, float edgeX, int seed = 12345)
        {
            elementalMatrix = matrix;
            spawnEdgeX = edgeX;
            _rng = new System.Random(seed);
        }

        private float SpawnY() => (float)(_rng.NextDouble() * 2.0 - 1.0) * spawnBandY;

        public GameObject SpawnMonster(MonsterData data)
        {
            if (data == null) return null;

            Vector2 pos = new Vector2(spawnEdgeX, SpawnY());
            var go = NewBody($"Monster_{data.DisplayName}", pos, Team.Monster, data.Element, data.MaxHealth, 0.45f);
            go.transform.SetParent(transform, worldPositionStays: true); // under ExpeditionWorld — torn down with it
            // Tougher monsters are heavier: a blast shoves a Warded Effigy less than a
            // Thornling. Steering scales by mass, so their walking speed is unchanged.
            go.GetComponent<Rigidbody2D>().mass = MassFor(data.MaxHealth);

            var walker = go.AddComponent<MonsterWalker>();
            walker.Configure(data.MoveSpeed);

            go.AddComponent<MonsterTag>().Data = data; // instance -> archetype, for loot / telemetry

            var art = new GameObject("Art");
            art.transform.SetParent(go.transform, false);
            AddSprite(art, data.Sprite != null ? data.Sprite : PixelSprites.Monster(data.DisplayName, data.Element), 5);
            Vfx.BodyVisuals.Attach(go, art.transform);
            go.SetActive(true);
            HealthBar2D.Attach(go.GetComponent<CombatantBody>(), width: 0.9f, lift: 0.62f);
            return go;
        }

        public GameObject SpawnBoss(BossDefinition boss)
        {
            if (boss == null) return null;

            var go = NewBody("Boss_" + boss.DisplayName, new Vector2(spawnEdgeX - 1f, 0f),
                Team.Monster, boss.CoreElement, boss.MaxHealth, 1.1f);
            go.transform.SetParent(transform, worldPositionStays: true);

            go.AddComponent<MonsterWalker>().Configure(1.4f);

            go.AddComponent<ElementalDamageAccumulator>();
            var executor = go.AddComponent<BossAttackExecutor>();
            executor.Configure(elementalMatrix);

            var phase = go.AddComponent<BossPhaseManager>();
            phase.Configure(boss, executor);

            AddSprite(go, PixelSprites.Boss(), 5);
            go.transform.localScale = Vector3.one * 2.2f;
            go.SetActive(true);
            HealthBar2D.Attach(go.GetComponent<CombatantBody>(), width: 2.6f, lift: 1.5f);
            return go;
        }

        // ---------------------------------------------------------------- helpers

        /// <summary>0.8 for a 20 HP pest up to 2.4 for a 170 HP brute.</summary>
        public static float MassFor(int maxHp) => Mathf.Clamp(0.65f + maxHp / 100f, 0.8f, 2.4f);

        private static GameObject NewBody(string name, Vector2 pos, Team team, ElementType element,
            int hp, float radius)
        {
            var go = new GameObject(name);
            go.SetActive(false);            // configure before Awake/OnEnable
            go.transform.position = pos;    // one-time spawn placement (never moved by transform again)

            var rb = go.AddComponent<Rigidbody2D>();
            rb.gravityScale = 0f;
            rb.linearDamping = 1.5f;
            rb.freezeRotation = true;

            go.AddComponent<CircleCollider2D>().radius = radius;
            go.AddComponent<CombatantBody>().Initialise(team, element, hp);
            return go;
        }

        private static void AddSprite(GameObject go, Sprite sprite, int sortingOrder)
        {
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.sortingOrder = sortingOrder;
            // Per-texture material — a single shared one makes the whole batch draw
            // with one texture (see PixelSprites.MaterialFor).
            Material mat = PixelSprites.MaterialFor(sprite);
            if (mat != null) sr.sharedMaterial = mat;
        }
    }
}
