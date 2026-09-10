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

        public void Configure(ElementalMatrix matrix, float edgeX)
        {
            elementalMatrix = matrix;
            spawnEdgeX = edgeX;
        }

        public GameObject SpawnMonster(MonsterData data)
        {
            if (data == null) return null;

            Vector2 pos = new Vector2(spawnEdgeX, Random.Range(-spawnBandY, spawnBandY));
            var go = NewBody($"Monster_{data.DisplayName}", pos, Team.Monster, data.Element, data.MaxHealth, 0.45f);
            go.transform.SetParent(transform, worldPositionStays: true); // under ExpeditionWorld — torn down with it

            var walker = go.AddComponent<MonsterWalker>();
            walker.Configure(data.MoveSpeed);

            go.AddComponent<MonsterTag>().Data = data; // instance -> archetype, for loot / telemetry

            AddSprite(go, data.Sprite != null ? data.Sprite : PixelSprites.Monster(data.DisplayName), 5, 1f);
            go.SetActive(true);
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

            AddSprite(go, PixelSprites.Boss(), 5, 1f);
            go.transform.localScale = Vector3.one * 2.2f;
            go.SetActive(true);
            return go;
        }

        // ---------------------------------------------------------------- helpers

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

        private static void AddSprite(GameObject go, Sprite sprite, int sortingOrder, float worldSize)
        {
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.sortingOrder = sortingOrder;
            if (PixelSprites.Unlit != null) sr.sharedMaterial = PixelSprites.Unlit;
        }
    }
}
