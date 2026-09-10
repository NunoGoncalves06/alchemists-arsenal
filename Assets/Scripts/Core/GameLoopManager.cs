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
            BeginDay();
            return true;
        }

        public void BeginDay()
        {
            EnsureShopWorld();
            DestroyWorld(ref _expeditionRoot);
            _expeditionWorld = null;
            SetPhase(GamePhase.DayIntro);
        }

        /// <summary>Called by the Day-Intro card when it finishes / is skipped.</summary>
        public void BeginMorning()
        {
            _morningRemaining = morningBudgetSeconds;
            MorningRemaining01 = 1f;
            EnsureShopWorld();
            SetPhase(GamePhase.Morning);
        }

        /// <summary>Counter confirmation — the sole creator of the day's order.</summary>
        public void ConfirmOrder(string potionName, ElementType element)
        {
            if (CraftingManager.Instance != null)
                CraftingManager.Instance.StartNewOrder(potionName, element);
        }

        public void BeginHandoff() => SetPhase(GamePhase.Handoff);

        public void BeginAfternoon()
        {
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
            SaveSystem.Instance.AutoSave();
        }

        // The HUD shows the result slab and advances on CONTINUE — the loop only
        // stashes the report here.
        private void HandleExpeditionFinished(ExpeditionReport report)
        {
            LatestReport = report;
        }

        public void BeginEvening()
        {
            RunState s = SaveSystem.Instance.State;
            ExpeditionReport r = LatestReport;

            if (r != null)
            {
                var (paid, tip) = Economy.Payout(r.craftedGrade, replay: s.IsReplayDay);
                r.goldPaidByGrade = paid;
                r.perfectTip = tip;
                s.AddGold(r.TotalGold);

                if (r.won)
                    s.RecordGrade(TargetBiomeIndex, r.Stars);

                foreach (var kv in r.herbDrops)
                    if (!s.ownedHerbs.Contains(kv.Key)) s.ownedHerbs.Add(kv.Key);

                DiaryManager.EvaluateAfterExpedition(s, TargetBiomeIndex, r);
                SaveSystem.Instance.MarkDirty();
            }

            EnsureShopWorld(); // night chrome reuses the shop world
            DestroyWorld(ref _expeditionRoot);
            _expeditionWorld = null;
            SetPhase(GamePhase.Evening);
            SaveSystem.Instance.AutoSave();
        }

        public void BeginBiomeMap() => SetPhase(GamePhase.BiomeMap);

        /// <summary>From the Biome Map: sleep = advance the day (+ maybe the biome) and save.</summary>
        public void Sleep(bool advanceBiome, int replayIndex)
        {
            RunState s = SaveSystem.Instance.State;

            if (replayIndex >= 0)
                s.replayBiomeIndex = replayIndex;
            else
            {
                s.replayBiomeIndex = -1;
                if (advanceBiome && s.currentBiomeIndex < BiomeLibrary.Count - 1)
                    s.currentBiomeIndex++;
            }

            s.day++;
            if (CraftingManager.Instance != null) CraftingManager.Instance.ClearOrder();
            SaveSystem.Instance.Save();
            BeginDay();
        }

        // ---------------------------------------------------------------- worlds

        private void EnsureShopWorld()
        {
            if (_shopRoot != null) return;
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
