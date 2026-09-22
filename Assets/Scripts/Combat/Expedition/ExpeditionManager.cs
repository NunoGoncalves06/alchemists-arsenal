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
        // Was 20s. That was set back when a timed-out wave was silently counted
        // as a CLEAR, so a cap that was far too short never looked wrong - it just
        // handed out free wins. It is genuinely too short: biome 0's third wave is
        // 138 HP of Water monsters, and the Counter correctly steers you to a Fire
        // flask for the biome's dominant Nature threat, which Water halves. That
        // is 13 flasks at a 1.4s cooldown = 17.6s of flawless uptime against a 20s
        // clock - unreachable once you add travel time or a single miss.
        //
        // Now that an empty belt is detected directly (PartyOutOfFlasks), this is
        // a pure anti-hang backstop rather than a balance knob, so it can be
        // generous.
        [Tooltip("Safety cap — a wave that hasn't cleared by this is force-ended so the run can't hang.")]
        [Min(5f)] [SerializeField] private float maxWaveSeconds = 45f;
        [Tooltip("Safety cap on the boss fight so the run always resolves.")]
        [Min(10f)] [SerializeField] private float maxBossSeconds = 60f;
        [Tooltip("Grace after the last flask is thrown, so a bomb still in the air can finish the job.")]
        [Min(0f)] [SerializeField] private float outOfFlasksGraceSeconds = 2.5f;
        [SerializeField] private bool bossEnabled = true;
        [SerializeField] private bool logProgress = true;

        public ExpeditionPhase Phase { get; private set; } = ExpeditionPhase.Warmup;
        public int WaveNumber { get; private set; }

        /// <summary>
        /// Waves the party actually killed their way through. A wave that hit the
        /// safety cap is NOT counted: the old code despawned the survivors and
        /// broke out of the hold loop, which fell through to Win() and reported a
        /// full clear. A player who ran out of flasks therefore stood still taking
        /// contact damage for maxWaveSeconds per wave and was then told they had
        /// won, with the monsters vanishing in front of them.
        /// </summary>
        public int WavesCleared { get; private set; }

        /// <summary>Why the expedition ended, for the Evening report.</summary>
        public string OutcomeReason { get; private set; } = "";
        public int TotalWaves => biome != null ? biome.Waves.Count : 0;
        public GameObject BossInstance { get; private set; }

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
                // auto-advances the instant the last monster dies.
                float held = 0f, dry = 0f;
                while (LiveMonsters() > 0)
                {
                    if (AllAdventurersDead()) { Lose("The party was wiped out."); yield break; }

                    // Out of flasks with monsters still standing is not a stall to
                    // wait out - it is unwinnable, and waiting it out is exactly
                    // the "stand there and take damage" the player sees. Pull them
                    // off the road immediately.
                    if (PartyOutOfFlasks())
                    {
                        dry += Time.deltaTime;
                        if (dry >= outOfFlasksGraceSeconds)
                        {
                            DespawnLiveMonsters();
                            Lose("Out of flasks - the party retreated.");
                            yield break;
                        }
                    }
                    else dry = 0f;

                    // The safety cap ends the ROAD, not just the wave. Only a clear
                    // wins, so an uncleared wave already meant a loss - but the old
                    // code despawned the survivors and carried on, making the party
                    // fight every remaining wave and the guardian for nothing, and a
                    // HUD "NEXT WAVE" button that did the same was a silent forfeit.
                    held += Time.deltaTime;
                    if (held >= maxWaveSeconds)
                    {
                        int left = LiveMonsters();
                        if (logProgress) Debug.Log($"[Expedition] Wave {WaveNumber} hit its cap ({left} left).");
                        DespawnLiveMonsters();
                        Lose($"Wave {WaveNumber} held out - {left} still standing when the party fell back.");
                        yield break;
                    }
                    yield return null;
                }
                WavesCleared++;
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
                float bossHeld = 0f, bossDry = 0f;
                while (bossBody != null && bossBody.IsAlive)
                {
                    if (AllAdventurersDead()) { Lose("The party was wiped out."); yield break; }
                    if (PartyOutOfFlasks())
                    {
                        bossDry += Time.deltaTime;
                        if (bossDry >= outOfFlasksGraceSeconds)
                        {
                            Lose("Out of flasks before the boss fell.");
                            yield break;
                        }
                    }
                    else bossDry = 0f;

                    bossHeld += Time.deltaTime;
                    if (bossHeld >= maxBossSeconds)
                    {
                        // Was "resolving as a clear", which handed out a win for
                        // failing to kill the boss. It is a safety cap, not a
                        // victory condition.
                        if (logProgress) Debug.LogWarning("[Expedition] Boss fight timed out.");
                        Lose("The boss drove the party off.");
                        yield break;
                    }
                    yield return null;
                }
            }

            // Only a genuine clear wins. Anything short of it is a failed road.
            if (WavesCleared >= TotalWaves) Win();
            else Lose($"Only {WavesCleared} of {TotalWaves} waves were cleared.");
        }

        private IEnumerator WaitOrLose(float seconds)
        {
            float t = 0f;
            while (t < seconds)
            {
                if (AllAdventurersDead()) { Lose("The party was wiped out."); yield break; }
                t += Time.deltaTime;
                yield return null;
            }
        }

        private void Win()
        {
            if (Phase == ExpeditionPhase.Won || Phase == ExpeditionPhase.Lost) return;
            SetPhase(ExpeditionPhase.Won);
            OutcomeReason = "The road is clear.";
            if (logProgress) Debug.Log("<color=green><b>[Expedition] VICTORY</b></color>");
            OnFinished?.Invoke(true);
        }

        private void Lose(string reason)
        {
            if (Phase == ExpeditionPhase.Won || Phase == ExpeditionPhase.Lost) return;
            SetPhase(ExpeditionPhase.Lost);
            OutcomeReason = reason;
            if (logProgress) Debug.Log($"<color=red><b>[Expedition] DEFEAT — {reason}</b></color>");
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

        /// <summary>
        /// True when every living adventurer has thrown their last flask. They
        /// cannot damage anything after this, so the fight is over whatever the
        /// clock says.
        /// </summary>
        private static bool PartyOutOfFlasks()
        {
            var list = AdventurerRegistry.ActiveAdventurers;
            if (list.Count == 0) return false;

            bool sawLiveAdventurer = false;
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i] == null || !list[i].IsAlive) continue;
                sawLiveAdventurer = true;

                if (list[i] is Component c && c != null)
                {
                    var ai = c.GetComponent<UtilityAI_CombatController>();
                    // No controller means we cannot tell - assume they can fight,
                    // so this never ends a fight it does not understand.
                    if (ai == null || ai.FlasksLeft > 0) return false;
                }
                else return false;
            }
            return sawLiveAdventurer;
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
