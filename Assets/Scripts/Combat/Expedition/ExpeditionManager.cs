using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using AlchemistsArsenal.Data;

namespace AlchemistsArsenal.Combat
{
    public enum ExpeditionPhase { Warmup, Waves, BossFight, Won, Lost }

    /// <summary>
    /// Runs one afternoon expedition: warm-up → monster waves → optional boss fight
    /// → win / lose. Owns the wave schedule and the end conditions; hands the actual
    /// GameObject assembly to <see cref="MonsterSpawner"/>.
    /// </summary>
    public class ExpeditionManager : MonoBehaviour
    {
        [SerializeField] private BiomeData biome;
        [SerializeField] private MonsterSpawner spawner;
        [SerializeField] private float warmupSeconds = 1f;
        [SerializeField] private float gapBetweenWaves = 0.6f;
        [Tooltip("Safety cap — a wave that hasn't cleared by this is force-ended so the run can't hang.")]
        [Min(5f)] [SerializeField] private float maxWaveSeconds = 20f;
        [Tooltip("Safety cap on the boss fight so the run always resolves.")]
        [Min(10f)] [SerializeField] private float maxBossSeconds = 45f;
        [SerializeField] private bool bossEnabled = true;
        [SerializeField] private bool logProgress = true;

        public ExpeditionPhase Phase { get; private set; } = ExpeditionPhase.Warmup;
        public int WaveNumber { get; private set; }
        public int TotalWaves => biome != null ? biome.Waves.Count : 0;
        public GameObject BossInstance { get; private set; }

        private bool _skipWaveRequested;

        /// <summary>HUD "NEXT WAVE" button — clear the field now and move on.</summary>
        public void SkipCurrentWave() => _skipWaveRequested = true;

        public event Action<ExpeditionPhase> OnPhaseChanged;
        public event Action<int> OnWaveStarted;
        public event Action<bool> OnFinished; // true = won

        private readonly List<GameObject> _spawned = new List<GameObject>();
        private bool _running;

        /// <summary>Wire the expedition in code and start it (bootstrap / tests).</summary>
        public void Configure(BiomeData biome, MonsterSpawner spawner, bool enableBoss = true)
        {
            this.biome = biome;
            this.spawner = spawner;
            bossEnabled = enableBoss;
            TryBegin();
        }

        private void Start() => TryBegin();

        private void TryBegin()
        {
            if (_running || biome == null || spawner == null || !isActiveAndEnabled) return;
            StartCoroutine(RunExpedition());
        }

        private IEnumerator RunExpedition()
        {
            _running = true;
            SetPhase(ExpeditionPhase.Warmup);
            yield return WaitOrLose(warmupSeconds);
            if (Phase == ExpeditionPhase.Lost) yield break;

            SetPhase(ExpeditionPhase.Waves);
            IReadOnlyList<BiomeData.Wave> waves = biome.Waves;
            for (int w = 0; w < waves.Count; w++)
            {
                BiomeData.Wave wave = waves[w];
                WaveNumber = w + 1;
                _skipWaveRequested = false; // reset here, not right before the hold loop —
                                             // a click during spawn-in must still register.
                OnWaveStarted?.Invoke(WaveNumber);
                if (logProgress) Debug.Log($"[Expedition] Wave {WaveNumber}/{waves.Count}: {wave.count}x {(wave.monster != null ? wave.monster.DisplayName : "?")}");

                yield return WaitOrLose(wave.delayBeforeWave);
                if (Phase == ExpeditionPhase.Lost) yield break;

                for (int i = 0; i < Mathf.Max(1, wave.count); i++)
                {
                    Track(spawner.SpawnMonster(wave.monster));
                    yield return WaitOrLose(Mathf.Max(0.05f, wave.spawnInterval));
                    if (Phase == ExpeditionPhase.Lost) yield break;
                }

                // Hold until the field is clear before the next wave. The wave
                // auto-advances the instant the last monster dies; a safety cap and
                // the HUD "NEXT WAVE" button both force it early so it can't drag.
                float held = 0f;
                while (LiveMonsters() > 0)
                {
                    if (AllAdventurersDead()) { Lose(); yield break; }
                    held += Time.deltaTime;
                    if (_skipWaveRequested || held >= maxWaveSeconds)
                    {
                        if (logProgress) Debug.Log($"[Expedition] Wave {WaveNumber} ended early ({LiveMonsters()} left).");
                        DespawnLiveMonsters();
                        break;
                    }
                    yield return null;
                }
                _skipWaveRequested = false;
                yield return WaitOrLose(gapBetweenWaves);
                if (Phase == ExpeditionPhase.Lost) yield break;
            }

            if (biome.HasBoss && bossEnabled)
            {
                SetPhase(ExpeditionPhase.BossFight);
                if (logProgress) Debug.Log($"[Expedition] BOSS: {biome.Boss.DisplayName}");
                BossInstance = spawner.SpawnBoss(biome.Boss);
                Track(BossInstance);

                CombatantBody bossBody = BossInstance != null ? BossInstance.GetComponent<CombatantBody>() : null;
                float bossHeld = 0f;
                while (bossBody != null && bossBody.IsAlive)
                {
                    if (AllAdventurersDead()) { Lose(); yield break; }
                    bossHeld += Time.deltaTime;
                    if (bossHeld >= maxBossSeconds)
                    {
                        if (logProgress) Debug.LogWarning("[Expedition] Boss fight timed out — resolving as a clear.");
                        break;
                    }
                    yield return null;
                }
            }

            Win();
        }

        private IEnumerator WaitOrLose(float seconds)
        {
            float t = 0f;
            while (t < seconds)
            {
                if (AllAdventurersDead()) { Lose(); yield break; }
                t += Time.deltaTime;
                yield return null;
            }
        }

        private void Win()
        {
            if (Phase == ExpeditionPhase.Won || Phase == ExpeditionPhase.Lost) return;
            SetPhase(ExpeditionPhase.Won);
            if (logProgress) Debug.Log("<color=green><b>[Expedition] VICTORY</b></color>");
            OnFinished?.Invoke(true);
        }

        private void Lose()
        {
            if (Phase == ExpeditionPhase.Won || Phase == ExpeditionPhase.Lost) return;
            SetPhase(ExpeditionPhase.Lost);
            if (logProgress) Debug.Log("<color=red><b>[Expedition] DEFEAT — all adventurers down</b></color>");
            OnFinished?.Invoke(false);
        }

        private void SetPhase(ExpeditionPhase p)
        {
            if (Phase == p) return;
            Phase = p;
            OnPhaseChanged?.Invoke(p);
        }

        private void Track(GameObject go)
        {
            if (go != null) _spawned.Add(go);
        }

        private static int LiveMonsters()
        {
            int n = 0;
            var list = MonsterRegistry.ActiveMonsters;
            for (int i = 0; i < list.Count; i++)
                if (list[i] != null && list[i].IsAlive) n++;
            return n;
        }

        private static void DespawnLiveMonsters()
        {
            var list = MonsterRegistry.ActiveMonsters;
            for (int i = list.Count - 1; i >= 0; i--)
                if (list[i] is Component c && c != null)
                    Destroy(c.gameObject);
        }

        private static bool AllAdventurersDead()
        {
            var list = AdventurerRegistry.ActiveAdventurers;
            if (list.Count == 0) return false; // none spawned yet — don't lose during setup
            for (int i = 0; i < list.Count; i++)
                if (list[i] != null && list[i].IsAlive) return false;
            return true;
        }
    }
}
