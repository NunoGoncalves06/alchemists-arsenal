using System;
using System.Collections.Generic;
using AlchemistsArsenal.Combat;

namespace AlchemistsArsenal.Systems
{
    [Serializable]
    public class DeductionLog
    {
        public int pointsDelta;      // negative = penalty, positive = clean-step bonus
        public string station;
        public string reason;
        public float timestamp;

        public DeductionLog(int delta, string stationName, string changeReason, float time)
        {
            pointsDelta = delta;
            station = stationName;
            reason = changeReason;
            timestamp = time;
        }
    }

    /// <summary>
    /// Where an order is in the shop: the bench it is at. Benches take orders in
    /// exactly this sequence and no other (<see cref="CraftingManager.Next"/>), so a
    /// flask can never skip a bench or be at two at once.
    /// </summary>
    public enum BrewStage { Steep = 0, Kiln = 1, Prep = 2, Cauldron = 3, Bottling = 4, Delivery = 5, Done = 6 }

    /// <summary>
    /// Where an order's grain is at the Steep bench: waiting for the jar, being
    /// poured, soaking, germinating, then green (sprouted, ready to scoop for the kiln).
    /// </summary>
    public enum SteepStep { Waiting = 0, Filling = 1, Soaking = 2, Germinating = 3, Green = 4 }

    /// <summary>Where an order's green malt is at the Kiln bench: waiting in its basket, drying on the tray, dried.</summary>
    public enum KilnStep { Waiting = 0, Drying = 1, Dried = 2 }

    /// <summary>
    /// One fighter's potion order. Created only by the Counter (DESIGN.md §7.6.1),
    /// one per fighter who steps up to it, and modified — up and down — by the
    /// benches it passes through, in <see cref="BrewStage"/> order.
    ///
    /// It is <b>born at <see cref="StartingQuality"/> (Poor)</b>, not 100: an
    /// untouched order is a bad potion, not a free Great one (reviewer C-R2b).
    /// The rubric's "decay" framing still holds — clean brewing is how you climb
    /// from 25 toward 100 and then hold it.
    /// </summary>
    [Serializable]
    public class ActiveOrder
    {
        public const int StartingQuality = 25;

        public string orderId;
        public string potionName;
        public ElementType element = ElementType.Fire;
        public int qualityScore = StartingQuality;

        /// <summary>The fighter who ordered it, and who will carry it out of the shop.</summary>
        public string heroId = "";
        public string heroName = "";

        /// <summary>The bench this order is at.</summary>
        public BrewStage stage = BrewStage.Prep;

        /// <summary>0 for the first fighter served today, 1 for the next…</summary>
        public int queueIndex;

        /// <summary>The job taken at the Counter for it (null for a test order).</summary>
        [NonSerialized] public Data.ContractRecord contract;

        /// <summary>
        /// Its own working mixture — the recipe the job decided, the leaves actually
        /// crushed into it, whether the mortar work is done. One per order, because
        /// two fighters' potions are two different recipes in flight at once.
        /// </summary>
        [NonSerialized] public Data.BrewMixture Mixture;

        public bool Finished => stage == BrewStage.Done;

        /// <summary>Where this order's grain is at the Steep bench.</summary>
        public SteepStep steepStep = SteepStep.Waiting;

        /// <summary>Where this order's green malt is at the Kiln bench.</summary>
        public KilnStep kilnStep = KilnStep.Waiting;

        /// <summary>The sound grain scooped out of the jar: what the kiln dries.</summary>
        public int steepedGrains;

        /// <summary>The fighter this flask was handed to at the Delivery bench, or empty.</summary>
        public string deliveredTo = "";

        /// <summary>The sealed flask is at Delivery and <paramref name="toHeroId"/> is the fighter who ordered it.</summary>
        public bool CanHandTo(string toHeroId) =>
            stage == BrewStage.Delivery && !string.IsNullOrEmpty(toHeroId) && toHeroId == heroId;

        /// <summary>Points the malting has earned (bonuses less charges), for <see cref="MaltQuality01"/>.</summary>
        public int maltPoints;

        /// <summary>
        /// How good the malt came out, 0..1. Its enzymes are what break the herbs down
        /// in the pot: the Cauldron dissolves them faster on good malt.
        /// </summary>
        public float MaltQuality01 = 0.5f;

        public List<DeductionLog> deductions = new List<DeductionLog>();

        /// <summary>Raised on every quality change — <b>raise and lower</b> (reviewer X4).</summary>
        public Action<int> OnQualityChanged;

        public ActiveOrder(string id, string name, ElementType element = ElementType.Fire)
        {
            orderId = id;
            potionName = name;
            this.element = element;
            qualityScore = StartingQuality;
            deductions = new List<DeductionLog>();
        }

        /// <summary>
        /// Move quality by <paramref name="delta"/> (either sign), clamp to 0..100,
        /// log it, and fire <see cref="OnQualityChanged"/>. The one mutation path.
        /// </summary>
        public void AdjustQuality(int delta, string stationName, string reason, float timeStamp = 0f)
        {
            if (delta == 0) return;

            int before = qualityScore;
            qualityScore = Math.Clamp(qualityScore + delta, 0, 100);
            int applied = qualityScore - before;
            if (applied == 0) return;

            deductions.Add(new DeductionLog(applied, stationName, reason, timeStamp));
            OnQualityChanged?.Invoke(qualityScore);
        }

        /// <summary>Back-compat wrapper for penalty call sites (points is a positive magnitude).</summary>
        public void ApplyDeduction(int points, string stationName, string reason, float timeStamp = 0f)
        {
            if (points <= 0) return;
            AdjustQuality(-points, stationName, reason, timeStamp);
        }

        /// <summary>Clean-step reward (points is a positive magnitude).</summary>
        public void ApplyBonus(int points, string stationName, string reason, float timeStamp = 0f)
        {
            if (points <= 0) return;
            AdjustQuality(points, stationName, reason, timeStamp);
        }


        /// <summary>0..1 quality, the form the combat layer consumes.</summary>
        public float Quality01 => qualityScore / 100f;

        /// <summary>The 4-band combat grade — the single grade the player ever sees.</summary>
        public PotionGrade GetGrade() => CombatQuality.GradeFor(qualityScore);
    }
}
