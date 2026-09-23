using System;
using System.Collections.Generic;
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
        [Tooltip("Extra morning for each fighter beyond the first: every fighter orders their own flask.")]
        [Min(0f)] [SerializeField] private float extraSecondsPerFighter = 110f;

        public GamePhase Phase { get; private set; } = GamePhase.Boot;
        public float MorningRemaining01 { get; private set; } = 1f;
        public float BudgetRateMultiplier { get; set; } = 1f; // tutorial sets 0.35

        public int Day => SaveSystem.Instance != null && SaveSystem.Instance.State != null
            ? SaveSystem.Instance.State.day : 1;

        public int TargetBiomeIndex => SaveSystem.Instance != null && SaveSystem.Instance.State != null
            ? SaveSystem.Instance.State.TargetBiomeIndex : 0;

        public ExpeditionReport LatestReport { get; private set; }
        /// <summary>One loadout per hero going out, built at BeginAfternoon.</summary>
        public IReadOnlyList<AdventurerLoadout> PendingLoadouts { get; private set; }

        /// <summary>The heroes going out today, resolved at BeginAfternoon.</summary>
        public IReadOnlyList<HeroRecord> PendingParty { get; private set; }

        /// <summary>The lead hero's loadout. Kept for the Handoff screen and tests.</summary>
        public AdventurerLoadout PendingLoadout =>
            PendingLoadouts != null && PendingLoadouts.Count > 0 ? PendingLoadouts[0] : null;
        public ExpeditionWorld CurrentExpedition => _expeditionWorld;

        public event Action<GamePhase> OnPhaseChanged;
        public event Action<float> OnMorningTimeChanged;

        private GameObject _shopRoot;
        private GameObject _expeditionRoot;
        private ExpeditionWorld _expeditionWorld;
        private float _morningRemaining;
        private float _morningBudget = 150f;

        /// <summary>This morning's whole clock, in seconds (it grows with the party).</summary>
        public float MorningBudgetSeconds => _morningBudget;

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
            MorningRemaining01 = Mathf.Clamp01(_morningRemaining / Mathf.Max(1f, _morningBudget));
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
            // Fresh shop every day — no stale brew / herb positions (reviewer P9).
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
            // Every fighter orders their own flask, so every fighter adds to the morning.
            RunState st = SaveSystem.Instance != null ? SaveSystem.Instance.State : null;
            int fighters = st != null ? Mathf.Max(1, st.DeployedParty().Count) : 1;
            _morningBudget = morningBudgetSeconds + extraSecondsPerFighter * (fighters - 1);
            _morningRemaining = _morningBudget;
            MorningRemaining01 = 1f;
            SetPhase(GamePhase.Morning);
        }

        /// <summary>Counter confirmation — the sole creator of the day's order.</summary>
        public void ConfirmOrder(string potionName, ElementType element)
        {
            if (CraftingManager.Instance != null)
                CraftingManager.Instance.StartNewOrder(potionName, element);
        }

        /// <summary>
        /// Counter: a fighter takes a job. Records the contract on the run state (so
        /// Evening knows what was promised to whom) and opens that fighter's order —
        /// the one path from "a fighter asked for something" to "there is a potion to
        /// brew". A fighter has one job a day; taking another replaces it.
        /// </summary>
        public ActiveOrder AcceptContract(ContractRecord contract)
        {
            if (contract == null) return null;
            contract.accepted = true;
            RunState s = SaveSystem.Instance != null ? SaveSystem.Instance.State : null;
            if (s != null)
            {
                s.contracts ??= new List<ContractRecord>();
                if (!string.IsNullOrEmpty(contract.heroId))
                    s.contracts.RemoveAll(c => c != null && c.heroId == contract.heroId);
                s.contracts.Add(contract);
                SaveSystem.Instance.MarkDirty();
            }
            return CraftingManager.Instance != null ? CraftingManager.Instance.StartOrder(contract) : null;
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

            // Once, here — finished or not. Each fighter carries their OWN order's
            // flasks: the one they asked for at the Counter, at whatever grade it
            // reached. A fighter nobody served goes out with the dregs (a null order
            // is a Poor "Raw Sludge"). Each gets their own loadout instance, because
            // the AI keeps a private ammo pool per controller.
            RunState party = SaveSystem.Instance != null ? SaveSystem.Instance.State : null;
            CraftingManager cm = CraftingManager.Instance;
            PendingParty = party != null ? party.DeployedParty() : null;
            var carried = new List<ActiveOrder>();
            if (PendingParty != null)
                foreach (HeroRecord h in PendingParty) carried.Add(cm != null ? cm.OrderFor(h.id) : null);
            // An order with no fighter behind it (the bootstrap demo, the sims) is the lead's.
            if (cm != null && cm.Orders.Count > 0 && string.IsNullOrEmpty(cm.Orders[0].heroId))
            {
                if (carried.Count == 0) carried.Add(cm.Orders[0]);
                else if (carried[0] == null) carried[0] = cm.Orders[0];
            }
            PendingLoadouts = LoadoutBuilder.BuildAll(carried, PendingParty);

            BiomeData biome = BiomeLibrary.Get(TargetBiomeIndex);

            // The shop unloads for the fight. Destroy() only lands at the end of the
            // frame and both worlds sit around the origin, so the shop's cauldron was
            // drawing into the arena's first frame (and its camera was a second
            // MainCamera). Deactivating takes effect immediately.
            DestroyWorld(ref _shopRoot);
            _expeditionRoot = new GameObject("~ExpeditionWorld");
            _expeditionWorld = _expeditionRoot.AddComponent<ExpeditionWorld>();
            _expeditionWorld.OnFinished += HandleExpeditionFinished;

            // Day 1 is the teaching run — no boss, just the waves. And the Woods'
            // guardian waits until the Woods have been cleared once: a player who
            // lost the teaching run walks the same road again on day 2, and must
            // not find a guardian at the end of it on the retry.
            RunState run = SaveSystem.Instance != null ? SaveSystem.Instance.State : null;
            bool enableBoss = run == null
                || (run.day > 1 && !(TargetBiomeIndex == 0 && run.bestGrades != null && run.bestGrades[0] == 0));
            _expeditionWorld.Build(biome, PendingLoadouts, PendingParty, enableBoss: enableBoss);

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

            // The road that was actually walked today. Captured before anything below
            // runs, because clearing the current node advances currentBiomeIndex, and
            // TargetBiomeIndex reads it: the diary used to be evaluated against the
            // NEXT biome, so a first clear never unlocked its entry.
            int played = TargetBiomeIndex;

            // Resolve the day exactly once — a mid-Evening quit + Continue must not
            // run this twice (reviewer P7). The day always advances here (even if
            // the report is somehow missing) so the loop can never stall on a day
            // (reviewer R6).
            if (s.lastResolvedDay != s.day)
            {
                if (r != null)
                {
                    r.replayDay = s.IsReplayDay;
                    SettleContracts(s, r);
                    s.AddGold(r.TotalGold); // loot + fees

                    if (r.won)
                    {
                        s.RecordGrade(played, r.Stars);
                        // Clearing your current node advances the road; a replay does not.
                        if (!s.IsReplayDay && s.currentBiomeIndex == played
                            && s.currentBiomeIndex < BiomeLibrary.Count - 1)
                            s.currentBiomeIndex++;
                    }

                    foreach (var kv in r.herbDrops)
                        if (!s.ownedHerbs.Contains(kv.Key)) s.ownedHerbs.Add(kv.Key);

                    DiaryManager.EvaluateAfterExpedition(s, played, r);
                    // The story due tonight is flagged here, inside the resolve-once
                    // block, and saved by the AutoSave below: quit mid-cutscene and it
                    // still plays (at the next Day Intro).
                    StoryDirector.OnDayResolved(s, played, r);

                    // Whoever went down sits tomorrow out. HeroTag/restUntilDay existed
                    // for this and nothing ever set it, so a hero could fall every day.
                    HeroCatalog.ApplyInjuries(s, r.downedHeroIds, s.day);
                }

                s.lastResolvedDay = s.day;
                s.day++;
                s.EnsureDeployment();
                s.replayBiomeIndex = -1;
                s.contracts?.Clear();              // tomorrow every fighter orders afresh
                s.contract = ContractRecord.None;
                if (CraftingManager.Instance != null) CraftingManager.Instance.ClearOrder();
                SaveSystem.Instance.MarkDirty();
            }

            DestroyWorld(ref _expeditionRoot);
            _expeditionWorld = null;
            SetPhase(GamePhase.Evening);
            SaveSystem.Instance.AutoSave();
        }

        /// <summary>
        /// Pay every fighter's job on the grade of the flask THEY carried, and write
        /// it into the report line by line. No fee at all for a lost road (P11) — the
        /// fighters came back with nothing done. A day with no job at all (a test, an
        /// old save) pays the base fee on the telemetry's grade, as it always did.
        /// The report's single-job fields and its <c>craftedGrade</c> (which the star
        /// for a fine flask reads) take the first job and the worst flask.
        /// </summary>
        private static void SettleContracts(RunState s, ExpeditionReport r)
        {
            CraftingManager cm = CraftingManager.Instance;
            r.contracts.Clear();
            int paidTotal = 0;
            bool anyTip = false, allMet = true, anyJob = false;
            PotionGrade worst = PotionGrade.Perfect;
            ContractRecord first = null;

            if (s.contracts != null)
                foreach (ContractRecord job in s.contracts)
                {
                    if (job == null || !job.accepted) continue;
                    anyJob = true;
                    first ??= job;
                    ActiveOrder o = cm != null ? cm.OrderFor(job.heroId) : null;
                    if (o == null && cm != null && string.IsNullOrEmpty(job.heroId)) o = cm.CurrentOrder;
                    PotionGrade g = o != null ? o.GetGrade() : PotionGrade.Poor;
                    if ((int)g > (int)worst) worst = g;   // PotionGrade counts down: Poor is 3

                    int fee = 0; bool tip = false, met = false;
                    if (r.won) (fee, tip, met) = Economy.ContractPayout(g, job, s.IsReplayDay);
                    paidTotal += fee;
                    anyTip |= tip;
                    allMet &= met;
                    r.contracts.Add(new ExpeditionReport.ContractLine
                    {
                        heroName = string.IsNullOrEmpty(job.heroName) ? job.buyerName : job.heroName,
                        portraitId = string.IsNullOrEmpty(job.buyerId) ? "rookie" : job.buyerId,
                        title = job.title, sponsor = job.sponsor ?? "", element = job.element,
                        required = job.RequiredGrade, delivered = g,
                        fee = job.fee, bonus = job.bonus, met = r.won && met, paid = fee,
                    });
                }

            if (!anyJob)
            {
                if (r.won) (paidTotal, anyTip, _) = Economy.ContractPayout(r.craftedGrade, null, s.IsReplayDay);
                allMet = true;
            }
            else r.craftedGrade = worst;

            r.contractBuyer = first != null ? (string.IsNullOrEmpty(first.heroName) ? first.buyerName : first.heroName) : "";
            r.contractTitle = first != null ? first.title : "";
            r.contractFee = first != null ? first.fee : Economy.BaseFee;
            r.contractBonus = first != null ? first.bonus : 0;
            r.contractRequired = first != null ? first.RequiredGrade : PotionGrade.Poor;
            r.goldPaidByGrade = paidTotal;
            r.perfectTip = anyTip;
            r.contractMet = r.won && allMet;
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

        /// <summary>
        /// Deactivate, then destroy. Destroy() only lands at the end of the frame, and
        /// every world is rebuilt in the same frame it is torn down: an old world left
        /// active drew into the new one's first frame and its singletons (the pot, the
        /// arena camera) were still live while the new ones woke up.
        /// </summary>
        private static void DestroyWorld(ref GameObject root)
        {
            if (root != null)
            {
                root.SetActive(false);
                Destroy(root);
            }
            root = null;
        }
    }
}
