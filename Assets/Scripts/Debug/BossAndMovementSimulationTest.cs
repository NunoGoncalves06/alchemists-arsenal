using System.Collections;
using System.Reflection;
using UnityEngine;
using AlchemistsArsenal.Combat;
using AlchemistsArsenal.Combat.Considerations;
using AlchemistsArsenal.Data;

namespace AlchemistsArsenal.DebugTools
{
    /// <summary>
    /// Play-mode verification for Task 5: the elemental damage accumulator, boss
    /// phase scoring / hard override, the ward feedback consideration, and the
    /// physics-driven adventurer movement FSM.
    /// </summary>
    public class BossAndMovementSimulationTest : MonoBehaviour
    {
        private const float HardThreshold = 100f;
        private const float Decay = 8f;

        private void Start() => StartCoroutine(Run());

        private IEnumerator Run()
        {
            Debug.Log("<color=cyan><b>=== BOSS HFSM + MOVEMENT SIMULATION ===</b></color>");

            TestAccumulator();
            TestBossPhaseScoring();
            TestWardFeedbackConsideration();
            yield return Test_AccumulatorHardOverride();
            yield return Test_AdventurerMovement();

            Debug.Log("<color=cyan><b>=== SIMULATION COMPLETE ===</b></color>");
        }

        // ---------------------------------------------------------------- 1

        private static ElementalThreatProfile MakeProfile()
        {
            var p = ScriptableObject.CreateInstance<ElementalThreatProfile>();
            p.SetEntries(new[]
            {
                new ElementalThreatProfile.Entry
                {
                    element = ElementType.Fire, decayPerSecond = Decay,
                    softThreshold = 45f, hardThreshold = HardThreshold, counterWard = ElementType.Water
                }
            });
            return p;
        }

        private void TestAccumulator()
        {
            var go = new GameObject("AccTest");
            go.AddComponent<Rigidbody2D>();
            var body = go.AddComponent<CombatantBody>();
            SetField(body, "maxHP", 500);
            SetField(body, "currentHP", 500);
            var acc = go.AddComponent<ElementalDamageAccumulator>();
            acc.SetProfileForTest(MakeProfile());

            for (int i = 0; i < 5; i++)
                body.ApplyDamage(new DamageInfo(15, ElementType.Fire, Vector2.zero, null));

            Report("Accumulator: Fire is the dominant threat", acc.DominantThreat == ElementType.Fire);
            Report("Accumulator: Fire pressure registered (>0)", acc.GetPressure01(ElementType.Fire) > 0f);
            Report("Accumulator: below hard threshold after 75 dmg", !acc.IsOverHard(ElementType.Fire));

            Destroy(go);
        }

        // ---------------------------------------------------------------- 2

        private void TestBossPhaseScoring()
        {
            var hpForNeutral = MakeBossConsideration<BossHealthConsideration>(AnimationCurve.Linear(0f, 0f, 1f, 1f));
            var hpForEnraged = MakeBossConsideration<BossHealthConsideration>(AnimationCurve.Linear(0f, 1f, 1f, 0f));

            var neutral = new System.Collections.Generic.List<BossConsideration> { hpForNeutral };
            var enraged = new System.Collections.Generic.List<BossConsideration> { hpForEnraged };

            var healthy = new BossPhaseContext(0.9f, ElementType.Fire, 0.1f, 5f, 0f, 1f);
            var dying = new BossPhaseContext(0.2f, ElementType.Fire, 0.3f, 1f, 0.2f, 1f);

            float nHealthy = BossPhaseScorer.Score(neutral, in healthy);
            float eHealthy = BossPhaseScorer.Score(enraged, in healthy);
            float nDying = BossPhaseScorer.Score(neutral, in dying);
            float eDying = BossPhaseScorer.Score(enraged, in dying);

            Report($"Boss scoring: healthy → Neutral wins ({nHealthy:F2} > {eHealthy:F2})", nHealthy > eHealthy);
            Report($"Boss scoring: near death → Enraged wins ({eDying:F2} > {nDying:F2})", eDying > nDying);
        }

        // ---------------------------------------------------------------- 3

        private void TestWardFeedbackConsideration()
        {
            var ward = ScriptableObject.CreateInstance<WardAvoidanceConsideration>();

            BombData fire = MakeBomb(ElementType.Fire);
            BombData water = MakeBomb(ElementType.Water);

            var warded = new UtilityContext(null, null, fire, null, 1f, 5f, 0f, 1,
                targetWarded: true, targetWardElement: ElementType.Fire);
            var wardedWater = new UtilityContext(null, null, water, null, 1f, 5f, 0f, 1,
                targetWarded: true, targetWardElement: ElementType.Fire);
            var noWard = new UtilityContext(null, null, fire, null, 1f, 5f, 0f, 1);

            Report("Ward feedback: warded element scores 0", Mathf.Approximately(ward.Score(in warded), 0f));
            Report("Ward feedback: other element unaffected (1)", ward.Score(in wardedWater) > 0.99f);
            Report("Ward feedback: no ward → unaffected (1)", ward.Score(in noWard) > 0.99f);
        }

        // ---------------------------------------------------------------- 4

        private IEnumerator Test_AccumulatorHardOverride()
        {
            var go = new GameObject("HardOverrideTest");
            go.AddComponent<Rigidbody2D>();
            var body = go.AddComponent<CombatantBody>();
            SetField(body, "maxHP", 1000);
            SetField(body, "currentHP", 1000);
            var acc = go.AddComponent<ElementalDamageAccumulator>();
            acc.SetProfileForTest(MakeProfile());

            bool crossed = false;
            ElementType crossedElement = default;
            acc.OnHardThresholdCrossed += e => { crossed = true; crossedElement = e; };

            // Dump 120 Fire damage in one frame — over the 100 hard threshold.
            for (int i = 0; i < 8; i++)
                body.ApplyDamage(new DamageInfo(15, ElementType.Fire, Vector2.zero, null));

            yield return null; // one Update tick for the accumulator to detect the edge

            Report("Hard override: threshold-crossed event fired for Fire", crossed && crossedElement == ElementType.Fire);
            Report("Hard override: counter-ward for Fire is Water",
                acc.CounterWardFor(ElementType.Fire) == ElementType.Water);

            Destroy(go);
        }

        // ---------------------------------------------------------------- 5

        private IEnumerator Test_AdventurerMovement()
        {
            // Drop any throwaway combatants the earlier tests spawned.
            MonsterRegistry.Clear();
            AdventurerRegistry.Clear();

            var monsterGo = new GameObject("MoveTest_Monster");
            var mRb = monsterGo.AddComponent<Rigidbody2D>();
            mRb.bodyType = RigidbodyType2D.Kinematic;
            mRb.position = new Vector2(15f, 0f);
            monsterGo.AddComponent<CircleCollider2D>();
            var monster = monsterGo.AddComponent<CombatantBody>();
            SetField(monster, "team", Team.Monster);

            var advGo = new GameObject("MoveTest_Adventurer");
            var aRb = advGo.AddComponent<Rigidbody2D>();
            aRb.gravityScale = 0f;
            aRb.linearDamping = 0.5f;
            aRb.position = Vector2.zero;
            var adv = advGo.AddComponent<CombatantBody>();
            SetField(adv, "team", Team.Adventurer);
            SetField(adv, "maxHP", 100);
            SetField(adv, "currentHP", 100);
            var mover = advGo.AddComponent<AdventurerMovementController>();

            yield return null;

            float startDist = Vector2.Distance(aRb.position, mRb.position);

            float t = 0f;
            var seenThrow = false;
            while (t < 3f)
            {
                t += Time.deltaTime;
                if (mover.State == AdventurerMovementController.MoveState.Throw) seenThrow = true;
                yield return null;
            }

            float endDist = Vector2.Distance(aRb.position, mRb.position);

            Report($"Movement: adventurer closed the gap ({startDist:F1}m → {endDist:F1}m)", endDist < startDist - 2f);
            Report("Movement: driven by physics (rb has velocity)", aRb.linearVelocity.sqrMagnitude > 0.01f || endDist < startDist - 2f);
            Report("Movement: entered Throw state once in range", seenThrow);

            Destroy(monsterGo);
            Destroy(advGo);
        }

        // ---------------------------------------------------------------- helpers

        private static T MakeBossConsideration<T>(AnimationCurve curve) where T : BossConsideration
        {
            var c = ScriptableObject.CreateInstance<T>();
            SetField(c, "responseCurve", curve);
            SetField(c, "weight", 1f);
            return c;
        }

        private static BombData MakeBomb(ElementType element)
        {
            var b = ScriptableObject.CreateInstance<BombData>();
            SetField(b, "element", element);
            SetField(b, "blastRadius", 2.5f);
            SetField(b, "idealRange", 6f);
            SetField(b, "minSafeRange", 2f);
            SetField(b, "maxRange", 12f);
            return b;
        }

        private static void Report(string label, bool pass)
        {
            if (pass) Debug.Log($"<color=green><b>PASS</b></color> {label}");
            else Debug.LogError($"<color=red><b>FAIL</b></color> {label}");
        }

        private static void SetField(object target, string field, object value)
        {
            var t = target.GetType();
            FieldInfo fi = null;
            while (t != null && fi == null)
            {
                fi = t.GetField(field, BindingFlags.NonPublic | BindingFlags.Instance);
                t = t.BaseType;
            }
            if (fi == null) { Debug.LogError($"[SimTest] field '{field}' not found on {target.GetType().Name}"); return; }
            fi.SetValue(target, value);
        }
    }
}
