using System;
using UnityEngine;
using AlchemistsArsenal.Combat;
using AlchemistsArsenal.Data;
using AlchemistsArsenal.Story;
using AlchemistsArsenal.Systems;

namespace AlchemistsArsenal.Core
{
    /// <summary>
    /// The one owner of the day loop (DESIGN.md §2.4 / §2.5). Drives
    /// <see cref="GamePhase"/>, the real-wall-clock morning budget, and the
    /// morning⇄afternoon⇄evening world swap.
    ///
    /// Phase-0 pragmatism: the three "scenes" of §11 are GameObject roots the loop
    /// builds and destroys (`_shopRoot`, `_expeditionRoot`) rather than additive
    /// <c>.unity</c> scenes — same isolation, no YAML to hand-author without the
    /// editor. Sim components never live under a UI root, and every scene-scoped
    /// singleton nulls its Instance on destroy (reviewer X5).
    ///
    /// Legal transition sequence (each method guards its expected <c>from</c> phase):
    /// <code>
    /// Boot ─GoToMainMenu→ MainMenu ─StartNewGame/Continue→ BeginDay
    ///   BeginDay → DayIntro ─BeginMorning→ Morning ─BeginHandoff→ Handoff
    ///   Handoff ─BeginAfternoon→ Afternoon ─BeginEvening→ Evening
    ///   Evening ─BeginBiomeMap→ BiomeMap ─Sleep→ BeginDay (day+1)
    /// </code>
    /// The day's reward is banked once, in <c>BeginEvening</c>, guarded by
    /// <c>RunState.lastResolvedDay</c>; the biome advances there too on a clear.
    /// </summary>
    public class GameLoopManager : MonoBehaviour
    {
        public static GameLoopManager Instance { get; private set; }

        [Header("Morning")]
        [Min(30f)] [SerializeField] private float morningBudgetSeconds = 150f;

        public GamePhase Phase { get; private set; } = GamePhase.Boot;
        public float MorningRemaining01 { get; private set; } = 1f;
        public float BudgetRateMultiplier { get; set; } = 1f; // tutorial sets 0.35

        public int Day => SaveSystem.Instance != null && SaveSystem.Instance.State != null
            ? SaveSystem.Instance.State.day : 1;

        public int TargetBiomeIndex => SaveSystem.Instance != null && SaveSystem.Instance.State != null
            ? SaveSystem.Instance.State.TargetBiomeIndex : 0;

        public ExpeditionReport LatestReport { get; private set; }
        public AdventurerLoadout PendingLoadout { get; private set; }
        public ExpeditionWorld CurrentExpedition => _expeditionWorld;

        public event Action<GamePhase> OnPhaseChanged;
        public event Action<float> OnMorningTimeChanged;

        private GameObject _shopRoot;
        private GameObject _expeditionRoot;
        private ExpeditionWorld _expeditionWorld;
        private float _morningRemaining;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private void Update()
        {
            if (Phase != GamePhase.Morning) return;
            if (UI.TutorialManager.Active) return; // budget is frozen while Day-1 is being taught (reviewer P3)

            _morningRemaining -= Time.unscaledDeltaTime * Mathf.Max(0f, BudgetRateMultiplier);
            MorningRemaining01 = Mathf.Clamp01(_morningRemaining / morningBudgetSeconds);
            OnMorningTimeChanged?.Invoke(MorningRemaining01);

            if (_morningRemaining <= 0f)
                BeginHandoff(); // time's up — ship whatever the order has reached
        }

        // ---------------------------------------------------------------- phases

        private void SetPhase(GamePhase p)
        {
            if (Phase == p) return;
            Phase = p;
            OnPhaseChanged?.Invoke(p);
        }

        public void GoToMainMenu()
        {
            DestroyWorld(ref _shopRoot);
            DestroyWorld(ref _expeditionRoot);
            _expeditionWorld = null;
            SetPhase(GamePhase.MainMenu);
        }

        public void StartNewGame()
        {
            SaveSystem.Instance.NewGame(0);
            DiaryManager.UnlockOpening(SaveSystem.Instance.State);
            SaveSystem.Instance.MarkDirty();
            BeginDay();
        }

        public bool Continue()
        {
            if (!SaveSystem.Instance.Load(0)) return false;

            // No special case: BeginEvening already advanced the day when it banked
            // the reward (reviewer P13), so a save is always on a day that hasn't
            // started yet. Continue is just "resume the current day".
            BeginDay();
            return true;
        }

        public void BeginDay()
        {
            // Fresh shop every day — no stale heat / herb positions (reviewer P9).
            DestroyWorld(ref _shopRoot);
            BuildShopWorld();
            DestroyWorld(ref _expeditionRoot);
            _expeditionWorld = null;
            SetPhase(GamePhase.DayIntro);
        }

        /// <summary>Called by the Day-Intro card when it finishes / is skipped.</summary>
        public void BeginMorning()
        {
            if (Phase != GamePhase.DayIntro) { Debug.LogWarning($"[Loop] BeginMorning from {Phase} ignored"); return; }
            _morningRemaining = morningBudgetSeconds;
            MorningRemaining01 = 1f;
            SetPhase(GamePhase.Morning);
        }

        /// <summary>Counter confirmation — the sole creator of the day's order.</summary>
        public void ConfirmOrder(string potionName, ElementType element)
        {
            if (CraftingManager.Instance != null)
                CraftingManager.Instance.StartNewOrder(potionName, element);
        }

        public void BeginHandoff()
        {
            if (Phase != GamePhase.Morning) { Debug.LogWarning($"[Loop] BeginHandoff from {Phase} ignored"); return; }
            SetPhase(GamePhase.Handoff);
        }

        public void BeginAfternoon()
        {
            if (Phase != GamePhase.Handoff) { Debug.LogWarning($"[Loop] BeginAfternoon from {Phase} ignored"); return; }
            if (_expeditionRoot != null) return; // re-entrancy guard (reviewer P6)

            ActiveOrder order = CraftingManager.Instance != null ? CraftingManager.Instance.CurrentOrder : null;
            PendingLoadout = LoadoutBuilder.Build(order); // once, here — finished or not

            BiomeData biome = BiomeLibrary.Get(TargetBiomeIndex);

            DestroyWorld(ref _shopRoot); // shop unloads for the fight
            _expeditionRoot = new GameObject("~ExpeditionWorld");
            _expeditionWorld = _expeditionRoot.AddComponent<ExpeditionWorld>();
            _expeditionWorld.OnFinished += HandleExpeditionFinished;
            _expeditionWorld.Build(biome, PendingLoadout, adventurerCount: 1);

            SetPhase(GamePhase.Afternoon);
        }

        // The HUD shows the result slab and advances on CONTINUE — the loop only
        // stashes the report here.
        private void HandleExpeditionFinished(ExpeditionReport report)
        {
            LatestReport = report;
        }

        public void BeginEvening()
        {
            if (Phase != GamePhase.Afternoon) { Debug.LogWarning($"[Loop] BeginEvening from {Phase} ignored"); return; }

            RunState s = SaveSystem.Instance.State;
            ExpeditionReport r = LatestReport;

            // Bank the day's reward exactly once — a mid-Evening quit + Continue
            // must not run this twice (reviewer P7).
            if (r != null && s.lastResolvedDay != s.day)
            {
                int fee = 0; bool tip = false;
                if (r.won) (fee, tip) = Economy.Payout(r.craftedGrade, replay: s.IsReplayDay); // no fee for a lost job (P11)
                r.goldPaidByGrade = fee;
                r.perfectTip = tip;
                s.AddGold(r.TotalGold); // loot + fee

                if (r.won)
                {
                    s.RecordGrade(TargetBiomeIndex, r.Stars);
                    // Clearing your current node advances the road; a replay does not.
                    if (!s.IsReplayDay && s.currentBiomeIndex == TargetBiomeIndex
                        && s.currentBiomeIndex < BiomeLibrary.Count - 1)
                        s.currentBiomeIndex++;
                }

                foreach (var kv in r.herbDrops)
                    if (!s.ownedHerbs.Contains(kv.Key)) s.ownedHerbs.Add(kv.Key);

                DiaryManager.EvaluateAfterExpedition(s, TargetBiomeIndex, r);

                // The day is over the moment you're paid: advance it here so a
                // mid-Evening quit resumes on the next day with no Continue-side
                // fix-up (reviewer P13). A replay day consumes its replay target.
                s.lastResolvedDay = s.day;
                s.day++;
                s.replayBiomeIndex = -1;
                if (CraftingManager.Instance != null) CraftingManager.Instance.ClearOrder();
                SaveSystem.Instance.MarkDirty();
            }

            DestroyWorld(ref _expeditionRoot);
            _expeditionWorld = null;
            SetPhase(GamePhase.Evening);
            SaveSystem.Instance.AutoSave();
        }

        public void BeginBiomeMap()
        {
            if (Phase != GamePhase.Evening) { Debug.LogWarning($"[Loop] BeginBiomeMap from {Phase} ignored"); return; }
            SetPhase(GamePhase.BiomeMap);
        }

        /// <summary>
        /// Sleep just passes the night — <see cref="BeginEvening"/> already advanced
        /// the day (reviewer P13). <paramref name="replayNextDay"/> ≥ 0 points
        /// tomorrow's expedition at a cleared biome instead of the current node.
        /// </summary>
        public void Sleep(int replayNextDay)
        {
            RunState s = SaveSystem.Instance.State;
            s.replayBiomeIndex = replayNextDay < 0 ? -1 : replayNextDay;
            SaveSystem.Instance.Save();
            BeginDay();
        }

        // ---------------------------------------------------------------- worlds

        private void BuildShopWorld()
        {
            _shopRoot = new GameObject("~ShopWorld");
            _shopRoot.AddComponent<ShopWorld>().Build();
        }

        private static void DestroyWorld(ref GameObject root)
        {
            if (root != null) Destroy(root);
            root = null;
        }
    }
}
