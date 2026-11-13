using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using AlchemistsArsenal.Combat;
using AlchemistsArsenal.Combat.Considerations;
using AlchemistsArsenal.Data;

namespace AlchemistsArsenal.DebugTools
{
    /// <summary>
    /// Headless checks for the utility-AI bomb selection. Builds synthetic combatants
    /// and data, runs <see cref="CombatDecisionEngine"/>, prints the full score table,
    /// and asserts the expected bomb wins. Fails loudly.
    /// </summary>
    public class UtilityAiSimulationTest : MonoBehaviour, ISimulationSuite
    {
        public bool Done { get; private set; }

        // --- balance assumptions under test ---------------------------------
        private const float StrongMult = 2f;
        private const float WeakMult = 0.5f;
        private const float PoorQualityCutoff = 0.60f;
        private const float ScoreThreshold = 0.15f;
        // -------------------------------------------------------------------

        private void Start() => RunSuite();

        public void RunSuite()
        {
            Debug.Log("<color=cyan><b>=== UTILITY AI — BOMB SELECTION SIMULATION ===</b></color>");

            ElementalMatrix matrix = ScriptableObject.CreateInstance<ElementalMatrix>();
            matrix.EnsureInitialised();
            matrix.SetMultiplier(ElementType.Fire, ElementType.Nature, StrongMult);
            matrix.SetMultiplier(ElementType.Fire, ElementType.Water, WeakMult);
            matrix.SetMultiplier(ElementType.Water, ElementType.Fire, StrongMult);

            BombData fire = MakeBomb("Firebloom", ElementType.Fire, blast: 2.5f, ideal: 6f);
            BombData water = MakeBomb("Tidevial", ElementType.Water, blast: 2.5f, ideal: 6f);
            var bombs = new List<BombData> { fire, water };

            var axes = new List<UtilityConsideration>
            {
                ScriptableObject.CreateInstance<ElementalVulnerabilityConsideration>(),
                ScriptableObject.CreateInstance<DistanceConsideration>(),
                ScriptableObject.CreateInstance<SelfHealthConsideration>(),
            };

            // Scenario 1 — Nature monster at ideal range, healthy hero, good potion.
            {
                var self = new FakeCombatant(Team.Adventurer, ElementType.Nature, 100, 100, new Vector2(0, 0));
                var natureMob = new FakeCombatant(Team.Monster, ElementType.Nature, 80, 80, new Vector2(6, 0));
                var monsters = new List<ICombatant> { natureMob };

                bool ok = CombatDecisionEngine.TrySelectThrow(self, monsters, bombs, matrix, axes,
                    potionQuality01: 0.95f, ScoreThreshold,
                    out BombThrowRequest req, out _, DumpBreakdown("S1 Nature target"));

                Report("S1: a throw was chosen", ok);
                Report("S1: FIRE bomb chosen vs Nature (x2)", ok && req.Bomb == fire);
            }

            // Scenario 2 — same monster, but the potion is Poor (<60%): elemental bonus is stripped,
            // so Fire no longer beats Water on the elemental axis.
            {
                var self = new FakeCombatant(Team.Adventurer, ElementType.Nature, 100, 100, new Vector2(0, 0));
                var natureMob = new FakeCombatant(Team.Monster, ElementType.Nature, 80, 80, new Vector2(6, 0));
                var monsters = new List<ICombatant> { natureMob };

                CombatDecisionEngine.TrySelectThrow(self, monsters, bombs, matrix, axes,
                    potionQuality01: 0.45f, ScoreThreshold,
                    out _, out CombatDecisionEngine.ScoredCandidate best, DumpBreakdown("S2 Poor potion"));

                float fireScore = ScoreOf(best, "S2", fire);
                float waterScore = ScoreOf(best, "S2", water);
                Report("S2: Poor potion neutralises the elemental edge (fire≈water)",
                    Mathf.Abs(fireScore - waterScore) < 0.02f);
            }


            // Scenario 4 - the badly-matched target. A x0.5 matchup used to score a
            // raw 0 on the elemental axis, which ApplyAxis turns into
            // `1 - weight * (1 - 0)` = 0 at any weight >= 1 (the default is 1.2).
            // The whole action scored 0, fell under the threshold, and the
            // adventurer simply stopped throwing - which in game looked exactly
            // like having run out of flasks while standing in a monster's face.
            // A weak flask must always beat no flask.
            {
                var self = new FakeCombatant(Team.Adventurer, ElementType.Nature, 100, 100, new Vector2(0, 0));
                // Water resists Fire (x0.5), and it is the ONLY target available.
                var waterMob = new FakeCombatant(Team.Monster, ElementType.Water, 80, 80, new Vector2(6, 0));
                var monsters = new List<ICombatant> { waterMob };
                var onlyFire = new List<BombData> { fire };

                bool ok = CombatDecisionEngine.TrySelectThrow(self, monsters, onlyFire, matrix, axes,
                    potionQuality01: 0.95f, ScoreThreshold,
                    out BombThrowRequest req, out CombatDecisionEngine.ScoredCandidate best,
                    DumpBreakdown("S4 unfavourable matchup"));

                Report("S4: still throws at a x0.5 target rather than standing idle", ok);
                Report("S4: the throw uses the flask it actually has", ok && req.Bomb == fire);
                Report($"S4: a bad matchup still scores above threshold ({best.Score:F3} >= {ScoreThreshold})",
                    best.Score >= ScoreThreshold);
            }

            // Scenario 5 - preference is preserved: given the choice, a x2 target
            // must still outrank a x0.5 one by a wide margin. The floor must not
            // have flattened the axis into irrelevance.
            {
                var self = new FakeCombatant(Team.Adventurer, ElementType.Nature, 100, 100, new Vector2(0, 0));
                var natureMob = new FakeCombatant(Team.Monster, ElementType.Nature, 80, 80, new Vector2(6, 0));
                var waterMob = new FakeCombatant(Team.Monster, ElementType.Water, 80, 80, new Vector2(6, 1f));
                var monsters = new List<ICombatant> { waterMob, natureMob };

                bool ok = CombatDecisionEngine.TrySelectThrow(self, monsters, new List<BombData> { fire },
                    matrix, axes, potionQuality01: 0.95f, ScoreThreshold,
                    out BombThrowRequest req, out _, DumpBreakdown("S5 target preference"));

                Report("S5: with both on the field, Fire still picks the Nature target (x2)",
                    ok && req.Target == (ICombatant)natureMob);
            }

            // Scenario 3 — the cornered hero: a monster hugging them, inside min-safe
            // range. This used to be a veto (and this check asserted it), which is the
            // stall that lost Cinder Peaks: the last monster hugged the hero, nothing
            // scored, and the wave ran out its cap with the belt still full. Bombs
            // cannot hurt the party, so a point-blank throw is poor but legal.
            {
                var self = new FakeCombatant(Team.Adventurer, ElementType.Nature, 100, 100, new Vector2(0, 0));
                var pointBlank = new FakeCombatant(Team.Monster, ElementType.Nature, 80, 80, new Vector2(1f, 0));
                var monsters = new List<ICombatant> { pointBlank };

                bool ok = CombatDecisionEngine.TrySelectThrow(self, monsters, bombs, matrix, axes,
                    potionQuality01: 0.95f, ScoreThreshold,
                    out _, out CombatDecisionEngine.ScoredCandidate best, DumpBreakdown("S3 point-blank"));

                Report($"S3: a monster hugging the hero still draws a throw ({best.Score:F3} >= {ScoreThreshold})", ok);

                // ...but the band is still a preference: given a second target at the
                // ideal range, that one wins.
                var ideal = new FakeCombatant(Team.Monster, ElementType.Nature, 80, 80, new Vector2(6f, 0));
                bool ok2 = CombatDecisionEngine.TrySelectThrow(self, new List<ICombatant> { pointBlank, ideal },
                    bombs, matrix, axes, potionQuality01: 0.95f, ScoreThreshold,
                    out BombThrowRequest req2, out _, DumpBreakdown("S3b point-blank vs ideal"));
                Report("S3: with a target at the ideal range too, that one is preferred",
                    ok2 && req2.Target == (ICombatant)ideal);
            }

            Done = true;
            Debug.Log("<color=cyan><b>=== SIMULATION COMPLETE ===</b></color>");
        }

        // ----------------------------------------------------------- helpers

        private readonly Dictionary<string, List<CombatDecisionEngine.ScoredCandidate>> _tables = new();

        private List<CombatDecisionEngine.ScoredCandidate> DumpBreakdown(string label)
        {
            var list = new List<CombatDecisionEngine.ScoredCandidate>();
            _tables[label] = list;
            return list;
        }

        private float ScoreOf(CombatDecisionEngine.ScoredCandidate best, string tableLabelPrefix, BombData bomb)
        {
            foreach (var kv in _tables)
            {
                if (!kv.Key.StartsWith(tableLabelPrefix)) continue;
                foreach (var c in kv.Value)
                    if (c.Bomb == bomb) return c.Score;
            }
            return best.Bomb == bomb ? best.Score : 0f;
        }

        private static void Report(string label, bool pass)
        {
            if (pass) Debug.Log($"<color=green><b>PASS</b></color> {label}");
            else Debug.LogError($"<color=red><b>FAIL</b></color> {label}");
        }

        private static BombData MakeBomb(string name, ElementType element, float blast, float ideal)
        {
            var bomb = ScriptableObject.CreateInstance<BombData>();
            bomb.name = name;
            SetField(bomb, "displayName", name);
            SetField(bomb, "element", element);
            SetField(bomb, "baseDamage", 20);
            SetField(bomb, "blastRadius", blast);
            SetField(bomb, "idealRange", ideal);
            SetField(bomb, "minSafeRange", 2f);
            SetField(bomb, "maxRange", ideal + 6f);
            SetField(bomb, "cooldownSeconds", 1f);
            return bomb;
        }

        private static void SetField(object target, string field, object value)
        {
            FieldInfo fi = target.GetType().GetField(field, BindingFlags.NonPublic | BindingFlags.Instance);
            if (fi == null) { Debug.LogError($"[SimTest] field '{field}' not found on {target.GetType().Name}"); return; }
            fi.SetValue(target, value);
        }

        /// <summary>Minimal in-memory combatant for scoring tests.</summary>
        private sealed class FakeCombatant : ICombatant
        {
            public FakeCombatant(Team team, ElementType element, int hp, int maxHp, Vector2 pos)
            {
                Team = team; Element = element; CurrentHP = hp; MaxHP = maxHp; Position = pos; Velocity = Vector2.zero;
            }

            public int CurrentHP { get; }
            public int MaxHP { get; }
            public ElementType Element { get; }
            public Team Team { get; }
            public Vector2 Position { get; }
            public Vector2 Velocity { get; }
            public bool IsAlive => CurrentHP > 0;
        }
    }
}
