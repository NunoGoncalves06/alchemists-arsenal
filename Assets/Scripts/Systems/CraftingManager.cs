using UnityEngine;
using System;
using System.Collections.Generic;
using AlchemistsArsenal.Combat;

namespace AlchemistsArsenal.Systems
{
    /// <summary>
    /// The morning's orders: one per fighter served at the Counter, each moving
    /// through the benches in <see cref="BrewStage"/> order. Orders are created only
    /// by the Counter (<see cref="StartOrder"/>) — there is no <c>Awake</c>
    /// auto-start (reviewer X3).
    ///
    /// <para><b>How several brews share four benches.</b> Each bench works one order
    /// at a time: the oldest one waiting at its stage (<see cref="Waiting"/>). When
    /// it finishes, it <see cref="Advance"/>s that order to the next stage and takes
    /// up whichever order is waiting next. So while one fighter's grain germinates
    /// or their brew waits for the pot, the player can crush another's leaves or
    /// serve the next fighter at the Counter — concurrency falls out of the benches
    /// being separate, not out of any scheduler.</para>
    /// </summary>
    public class CraftingManager : MonoBehaviour
    {
        public static CraftingManager Instance { get; private set; }

        [Header("Testing")]
        [Tooltip("Headless sims (CauldronSimulationTest etc.) need an order to exist at Awake. " +
                 "The real game leaves this false — Counter creates the order.")]
        [SerializeField] private bool bootstrapOrderForTests = false;
        [SerializeField] private string testPotionName = "Fire Resistance Potion";

        private readonly List<ActiveOrder> _orders = new List<ActiveOrder>();

        /// <summary>Today's orders, in the order the fighters were served.</summary>
        public IReadOnlyList<ActiveOrder> Orders => _orders;

        /// <summary>
        /// The bench a brand-new order goes to first: its grain is malted before anything
        /// else, once the road is far enough along to have the Steep bench (<see cref="Data.Complexity"/>).
        /// </summary>
        public static BrewStage FirstStage => FirstStageFor(Data.Complexity.Level());

        /// <summary>The first bench on road level <paramref name="level"/>: the Steep bench once the road has malting.</summary>
        public static BrewStage FirstStageFor(int level) => Data.Complexity.MaltingOn(level) ? BrewStage.Steep : BrewStage.Prep;

        /// <summary>The bench after <paramref name="stage"/>. The one place the order of the benches is decided.</summary>
        public static BrewStage Next(BrewStage stage) => stage >= BrewStage.Done ? BrewStage.Done : stage + 1;

        /// <summary>The most recently accepted order (for the tutorial, tests and old callers).</summary>
        public ActiveOrder CurrentOrder => _orders.Count > 0 ? _orders[_orders.Count - 1] : null;

        /// <summary>The most recent order's mixture (see <see cref="ActiveOrder.Mixture"/>).</summary>
        public Data.BrewMixture Mixture => CurrentOrder?.Mixture;

        public Action<ActiveOrder> OnOrderStarted;
        public Action<ActiveOrder> OnOrderCompleted;
        public Action<int, float, float> OnTimerChanged; // station index, current, max

        /// <summary>Any order was added, moved on a stage, or the day's orders were cleared.</summary>
        public event Action OrdersChanged;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);

            if (bootstrapOrderForTests)
                StartNewOrder(testPotionName, ElementType.Fire);
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        /// <summary>
        /// The Counter took <paramref name="job"/> for the fighter it names: open its
        /// order, born Poor, at the first bench.
        /// </summary>
        public ActiveOrder StartOrder(Data.ContractRecord job)
        {
            if (job == null) return null;
            string orderId = "ORD-" + UnityEngine.Random.Range(1000, 9999);
            int level = Data.Complexity.Level();
            var order = new ActiveOrder(orderId, job.PotionName, job.element)
            {
                heroId = job.heroId ?? "",
                heroName = string.IsNullOrEmpty(job.heroName) ? job.buyerName : job.heroName,
                contract = job,
                queueIndex = _orders.Count,
                stage = FirstStage,
                // No two fighters brew the same recipe today: each order of an element already open moves this one to the next take.
                Mixture = new Data.BrewMixture(job.element, Data.Complexity.RecipeTier(level),
                    Data.RecipeBook.VariantFor(job.element, _orders.FindAll(o => string.IsNullOrEmpty(job.heroId) || o.heroId != job.heroId).ConvertAll(o => o.element))),
            };
            order.potionName = order.Mixture.Recipe.Name;   // each fighter's own take, never three "Fire Flask"s
            // Before the road has a Malting bench, the grain comes from the miller
            // already malted: full enzymes, and the malt's share of the score, so a
            // first-road flask can still reach Perfect.
            if (!Data.Complexity.MaltingOn(level))
            {
                order.MaltQuality01 = 1f;
                order.maltPoints = Data.QualityBudget.MaltMax;
                order.ApplyBonus(Data.QualityBudget.MaltMax, "Malting", "Malted grain from the miller");
            }
            _orders.Add(order);
            OnOrderStarted?.Invoke(order);
            OrdersChanged?.Invoke();
            return order;
        }

        /// <summary>An order with no job behind it (headless sims, the bootstrap demo).</summary>
        public void StartNewOrder(string potionName, ElementType element)
        {
            StartOrder(new Data.ContractRecord { accepted = false, element = element, title = potionName });
        }

        /// <summary>The oldest order waiting at <paramref name="stage"/>, or null.</summary>
        public ActiveOrder Waiting(BrewStage stage)
        {
            foreach (ActiveOrder o in _orders)
                if (o.stage == stage) return o;
            return null;
        }

        /// <summary>How many orders are at <paramref name="stage"/> right now.</summary>
        public int CountAt(BrewStage stage)
        {
            int n = 0;
            foreach (ActiveOrder o in _orders) if (o.stage == stage) n++;
            return n;
        }

        /// <summary>How many orders have got past <paramref name="stage"/>.</summary>
        public int CountPast(BrewStage stage)
        {
            int n = 0;
            foreach (ActiveOrder o in _orders) if (o.stage > stage) n++;
            return n;
        }

        public ActiveOrder OrderFor(string heroId)
        {
            if (string.IsNullOrEmpty(heroId)) return null;
            foreach (ActiveOrder o in _orders) if (o.heroId == heroId) return o;
            return null;
        }

        /// <summary>
        /// The flask <paramref name="heroId"/> goes out with: the one handed to them at
        /// Delivery, or else their own order as far as it got (the clock ran out).
        /// </summary>
        public ActiveOrder CarriedBy(string heroId)
        {
            if (string.IsNullOrEmpty(heroId)) return null;
            foreach (ActiveOrder o in _orders) if (o.deliveredTo == heroId) return o;
            return OrderFor(heroId);
        }

        /// <summary>Every order taken today is sealed, labelled and handed over.</summary>
        public bool AllDone
        {
            get
            {
                if (_orders.Count == 0) return false;
                foreach (ActiveOrder o in _orders) if (!o.Finished) return false;
                return true;
            }
        }

        /// <summary>A bench finished its work on <paramref name="order"/>: on to the next stage.</summary>
        public void Advance(ActiveOrder order)
        {
            if (order == null || order.Finished) return;
            order.stage = Next(order.stage);
            if (order.Finished)
            {
                Debug.Log($"[Crafting] Sealed {order.heroName}'s {order.potionName} — quality {order.qualityScore} ({order.GetGrade()})");
                OnOrderCompleted?.Invoke(order);
            }
            OrdersChanged?.Invoke();
        }

        /// <summary>Kept for old callers: finish the most recent order outright.</summary>
        public void CompleteActiveOrder()
        {
            ActiveOrder o = CurrentOrder;
            while (o != null && !o.Finished) Advance(o);
        }

        /// <summary>Clear the day's orders (called by the loop as the day resolves).</summary>
        public void ClearOrder()
        {
            _orders.Clear();
            OrdersChanged?.Invoke();
        }

        public void UpdateTimer(int stationIndex, float current, float max) =>
            OnTimerChanged?.Invoke(stationIndex, current, max);
    }
}
