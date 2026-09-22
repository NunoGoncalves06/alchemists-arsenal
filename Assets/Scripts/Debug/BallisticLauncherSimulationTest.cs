using System.Collections;
using System.Reflection;
using UnityEngine;
using AlchemistsArsenal.Combat;
using AlchemistsArsenal.Data;

namespace AlchemistsArsenal.DebugTools
{
    /// <summary>
    /// Play-mode verification for Task 4: the ballistic launcher + detonation.
    /// Confirms the projectile flies under physics (no transform writes), detonates
    /// in FixedUpdate, applies radial knockback and rubric-scaled damage.
    /// </summary>
    public class BallisticLauncherSimulationTest : MonoBehaviour, ISimulationSuite
    {
        public bool Done { get; private set; }

        // --- balance assumptions under test --------------------------------
        private const int BombBaseDamage = 20;
        private const float BombBlastRadius = 2.5f;
        private const float BombThrowSpeed = 13f;
        private const float PerfectMult = 1.20f;   // quality >= 95%
        private const float FireVsNature = 2.0f;
        private const float ObserveSeconds = 4f;
        // -----------------------------------------------------------------

        private void Start() => StartCoroutine(Run());

        private IEnumerator Run()
        {
            Debug.Log("<color=cyan><b>=== BALLISTIC LAUNCHER + DETONATION SIMULATION ===</b></color>");

            // ---- 1. Pure ballistic maths --------------------------------------
            Vector2 origin = new Vector2(0f, 0f);
            Vector2 target = new Vector2(6f, 0.5f);
            Vector2 gravity = Physics2D.gravity; // gravityScale 1

            bool solved = BallisticSolver.TrySolveArc(origin, target, BombThrowSpeed, gravity, false, out Vector2 v0);
            Report("Solver found an arc to the target", solved);

            if (solved)
            {
                // Integrate the closed-form path and find where it re-crosses target height.
                Vector2 landing = SimulatePath(origin, v0, gravity, target.y);
                Report($"Closed-form arc lands near target (err {Vector2.Distance(landing, target):F2}m)",
                    Vector2.Distance(landing, target) < 0.6f);
            }

            // ---- 2. Physics integration -------------------------------------
            ElementalMatrix matrix = ScriptableObject.CreateInstance<ElementalMatrix>();
            matrix.EnsureInitialised();
            matrix.SetMultiplier(ElementType.Fire, ElementType.Nature, FireVsNature);

            BombData fireBomb = MakeBomb("Firebloom", ElementType.Fire);

            var targetGo = new GameObject("TestMonster");
            var targetRb = targetGo.AddComponent<Rigidbody2D>();
            targetRb.gravityScale = 0f;
            targetRb.position = target;
            var targetCol = targetGo.AddComponent<CircleCollider2D>();
            targetCol.radius = 0.5f;
            var monster = targetGo.AddComponent<CombatantBody>();
            SetField(monster, "team", Team.Monster);
            SetField(monster, "element", ElementType.Nature);
            SetField(monster, "maxHP", 200);
            SetField(monster, "currentHP", 200);

            yield return null; // let Awake/OnEnable run

            var projGo = new GameObject("TestBomb");
            var projRb = projGo.AddComponent<Rigidbody2D>();
            projRb.position = origin;
            var projCol = projGo.AddComponent<CircleCollider2D>();
            projCol.radius = 0.15f;
            projCol.isTrigger = true;
            var projectile = projGo.AddComponent<BombProjectile2D>();

            var request = new BombThrowRequest(
                thrower: null, target: monster, bomb: fireBomb,
                origin: origin, targetPosition: target,
                utilityScore: 1f, potionQuality01: 0.98f);

            int hpBefore = monster.CurrentHP;
            projectile.Configure(in request, matrix, ~0);

            yield return new WaitForFixedUpdate();
            Vector2 posAfterOneStep = projRb.position;
            Report("Projectile is moving under physics (velocity set, not teleported)",
                projRb.linearVelocity.sqrMagnitude > 0.1f && posAfterOneStep != origin);

            float t = 0f;
            while (t < ObserveSeconds && projectile != null && !projectile.HasDetonated)
            {
                t += Time.deltaTime;
                yield return null;
            }

            Report("Bomb detonated within observation window", projectile == null || projectile.HasDetonated);

            yield return new WaitForFixedUpdate();

            int expected = Mathf.RoundToInt(BombBaseDamage * PerfectMult * FireVsNature); // 20 * 1.2 * 2 = 48
            int dealt = hpBefore - monster.CurrentHP;
            Report($"Rubric-scaled damage applied (expected ~{expected}, dealt {dealt})",
                Mathf.Abs(dealt - expected) <= 1);

            Report($"Radial knockback impulse moved the target (v={targetRb.linearVelocity.magnitude:F2})",
                targetRb.linearVelocity.sqrMagnitude > 0.01f);

            if (projGo != null) Destroy(projGo);
            Destroy(targetGo);
            Done = true;
            Debug.Log("<color=cyan><b>=== SIMULATION COMPLETE ===</b></color>");
        }

        // ------------------------------------------------------------- helpers

        private static Vector2 SimulatePath(Vector2 origin, Vector2 v0, Vector2 gravity, float targetHeight)
        {
            Vector2 prev = origin;
            for (int step = 1; step < 1440; step++)
            {
                float time = step / 120f;
                Vector2 p = BallisticSolver.SamplePath(origin, v0, gravity, time);
                if (time > 0.1f && prev.y > targetHeight && p.y <= targetHeight)
                    return p;
                prev = p;
            }
            return prev;
        }

        private static void Report(string label, bool pass)
        {
            if (pass) Debug.Log($"<color=green><b>PASS</b></color> {label}");
            else Debug.LogError($"<color=red><b>FAIL</b></color> {label}");
        }

        private static BombData MakeBomb(string name, ElementType element)
        {
            var bomb = ScriptableObject.CreateInstance<BombData>();
            bomb.name = name;
            SetField(bomb, "displayName", name);
            SetField(bomb, "element", element);
            SetField(bomb, "baseDamage", BombBaseDamage);
            SetField(bomb, "blastRadius", BombBlastRadius);
            SetField(bomb, "throwSpeed", BombThrowSpeed);
            SetField(bomb, "gravityScale", 1f);
            SetField(bomb, "idealRange", 6f);
            SetField(bomb, "minSafeRange", 2f);
            SetField(bomb, "maxRange", 14f);
            SetField(bomb, "cooldownSeconds", 1f);
            return bomb;
        }

        private static void SetField(object target, string field, object value)
        {
            FieldInfo fi = target.GetType().GetField(field, BindingFlags.NonPublic | BindingFlags.Instance);
            if (fi == null) { Debug.LogError($"[SimTest] field '{field}' missing on {target.GetType().Name}"); return; }
            fi.SetValue(target, value);
        }
    }
}
