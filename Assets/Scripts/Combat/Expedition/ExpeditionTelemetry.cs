using UnityEngine;
using AlchemistsArsenal.Data;

namespace AlchemistsArsenal.Combat
{
    /// <summary>
    /// Watches one expedition and fills an <see cref="ExpeditionReport"/>. Lives in
    /// the Expedition scene; subscribes to the global detonation + death feeds
    /// (reviewer X1/X2) so it needs no per-actor wiring.
    /// </summary>
    public class ExpeditionTelemetry : MonoBehaviour
    {
        public ExpeditionReport Report { get; private set; }

        private ExpeditionManager _expedition;
        private float _startTime;
        private bool _finished;

        public void Begin(ExpeditionManager expedition, string biomeName, int partyCount)
        {
            _expedition = expedition;
            _startTime = Time.time;
            Report = new ExpeditionReport
            {
                biomeName = biomeName,
                partyTotal = Mathf.Max(1, partyCount),
                totalWaves = expedition != null ? expedition.TotalWaves : 0,
            };

            BombProjectile2D.OnDetonatedGlobal += OnDetonated;
            CombatantBody.OnAnyDied += OnDied;
            if (_expedition != null)
            {
                _expedition.OnWaveStarted += OnWaveStarted;
                _expedition.OnFinished += OnFinished;
            }
        }

        private void OnDestroy() => Unsubscribe();

        private void Unsubscribe()
        {
            BombProjectile2D.OnDetonatedGlobal -= OnDetonated;
            CombatantBody.OnAnyDied -= OnDied;
            if (_expedition != null)
            {
                _expedition.OnWaveStarted -= OnWaveStarted;
                _expedition.OnFinished -= OnFinished;
            }
        }

        private void OnWaveStarted(int wave)
        {
            if (Report != null) Report.wavesCleared = Mathf.Max(Report.wavesCleared, wave - 1);
        }

        private void OnDetonated(DetonationInfo d)
        {
            if (Report == null) return;
            var line = Report.LineFor(NameFor(d.Element), d.Element, d.Grade);
            line.throws++;                       // one detonation == one thrown bomb
            line.hits += d.HitCount;
            line.totalDamage += d.TotalDamage;
            line.everHadAdvantage |= d.HadElementalAdvantage;
            Report.craftedGrade = d.Grade;       // Phase 0 has one bomb — grades are uniform
        }

        private void OnDied(CombatantBody body)
        {
            if (Report == null || body == null) return;

            if (body.Team == Team.Adventurer)
            {
                Report.partyDown++;
                return;
            }

            // Monster — roll loot from its archetype.
            var tag = body.GetComponent<MonsterTag>();
            MonsterData data = tag != null ? tag.Data : null;
            if (data == null) return;

            Report.goldFromLoot += Random.Range(data.GoldMin, data.GoldMax + 1);
            if (!string.IsNullOrEmpty(data.HerbDropId) && Random.value <= data.HerbDropChance)
                Report.AddHerb(data.HerbDropId);
        }

        private void OnFinished(bool won)
        {
            if (_finished || Report == null) return;
            _finished = true;

            Report.won = won;
            Report.durationSeconds = Time.time - _startTime;
            Report.wavesCleared = won ? Report.totalWaves : Mathf.Clamp(Report.wavesCleared, 0, Report.totalWaves);

            var boss = _expedition != null ? _expedition.BossInstance : null;
            var bossBody = boss != null ? boss.GetComponent<CombatantBody>() : null;
            Report.bossDefeated = won && bossBody != null && !bossBody.IsAlive;

            Unsubscribe();
        }

        private static string NameFor(ElementType e) => e switch
        {
            ElementType.Fire => "Firebloom Flask",
            ElementType.Water => "Tidevial",
            ElementType.Nature => "Thornburst",
            ElementType.Poison => "Miremist Phial",
            _ => "Arcane Draught",
        };
    }
}
