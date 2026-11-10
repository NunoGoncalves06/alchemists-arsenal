using System;
using System.Collections.Generic;
using UnityEngine;
using AlchemistsArsenal.Combat;
using AlchemistsArsenal.Core;
using AlchemistsArsenal.Crafting;
using AlchemistsArsenal.Data;
using AlchemistsArsenal.Systems;
using AlchemistsArsenal.UI.Stations;

namespace AlchemistsArsenal.UI
{
    /// <summary>What a lesson's spotlight is on: a point in the shop, or a piece of UI.</summary>
    public struct LessonTarget
    {
        public Vector2 World;
        public float WorldRadius;
        public RectTransform Rect;
        public float Pad;
        public bool IsWorld;
        /// <summary>For a big piece of UI: an ellipse inside the rect, not round its corners.</summary>
        public bool Inside;

        public static LessonTarget At(Vector2 world, float radius) =>
            new LessonTarget { World = world, WorldRadius = radius, IsWorld = true };
        public static LessonTarget On(RectTransform rect, float pad = 10f) =>
            new LessonTarget { Rect = rect, Pad = pad };
        public static LessonTarget Over(RectTransform rect) =>
            new LessonTarget { Rect = rect, Inside = true };
    }

    /// <summary>
    /// One thing the tutorial teaches: the bench it happens at, when it applies,
    /// where to look, one short line, and when it has been learnt.
    /// </summary>
    public sealed class Lesson
    {
        public string Id;
        /// <summary>The first level (road) that uses this mechanic; see <see cref="Complexity"/>.</summary>
        public int MinLevel;
        /// <summary>The bench's rail index (0 Counter .. 6 Delivery), or -1 for none.</summary>
        public int Station = -1;
        public Func<bool> Ready, Done;
        public Func<LessonTarget?> Where;
        public Func<string> Say;
        /// <summary>A world area to keep lit besides the target (and every bench gauge), or null.</summary>
        public Func<Rect?> Reveal;
        /// <summary>The screen the lesson happens on (the benches are all on the Morning).</summary>
        public ScreenId OnScreen = ScreenId.Morning;
        /// <summary>A briefing, not a task: it is done when the player clicks to go on.</summary>
        public bool ClickToGo;
        /// <summary>
        /// A focus lesson: it only starts with the player already at its bench (it
        /// never sends them anywhere), keeps them there until it is learnt, and lights
        /// nothing but its target and its own <see cref="Reveal"/>.
        /// </summary>
        public bool Focus;
    }

    /// <summary>
    /// The tutorial, as data. Each lesson plays once, the first time its mechanic
    /// turns up: day one teaches the basic loop, day two malting and labels, day
    /// three skimming, turning the bed and the stir that changes direction. The
    /// order here is the order they are offered in when several are ready at once.
    /// </summary>
    public static class TutorialScript
    {
        /// <summary>The day-one lessons (a save that finished the old tutorial has seen these).</summary>
        public static readonly string[] BasicLessonIds = { "job", "leaves", "pestle", "stir", "pour", "cork", "send" };

        private static CraftingManager Crafting => CraftingManager.Instance;
        private static SteepBench Malt => SteepBench.Instance;
        private static KilnBench Kiln => KilnBench.Instance;
        private static DeliveryBench Delivery => DeliveryBench.Instance;
        private static PrepBench Prep => PrepBench.Instance;
        private static PhysicsCauldronManager Pot => PhysicsCauldronManager.Instance;
        private static BottlingBench Bottle => BottlingBench.Instance;
        private static MorningScreen Screen =>
            UIManager.Instance != null ? UIManager.Instance.ScreenOf(ScreenId.Morning) as MorningScreen : null;

        private static SteepStep TubStep => Malt != null && Malt.TubOrder != null ? Malt.TubOrder.steepStep : SteepStep.Waiting;
        private static BrewMixture PrepMix => Prep != null && Prep.Working != null ? Prep.Working.Mixture : null;

        public static readonly List<Lesson> Lessons = new List<Lesson>
        {
            // ---------------------------------------------------------- counter
            new Lesson
            {
                Id = "job", MinLevel = 0, Station = 0,
                Ready = () => CounterStation.NextFighter != null && Screen != null && Screen.Counter.FirstOfferTag != null,
                Done = () => Crafting != null && Crafting.Orders.Count > 0,
                Where = () => DragTag.AnyCarried ? LessonTarget.On(Screen.Counter.OrderBook, 8f)
                                                 : LessonTarget.On(Screen.Counter.FirstOfferTag, 6f),
                Say = () => DragTag.AnyCarried ? "Drop it on the order book." : "Drag a job onto the order book.",
            },

            // ---------------------------------------------------------- malting
            new Lesson
            {
                Id = "sack", MinLevel = 1, Station = 1,
                Ready = () => TubStep == SteepStep.Filling && !Malt.LineReached,
                Done = () => Malt == null || Malt.LineReached || TubStep != SteepStep.Filling,
                Where = () => LessonTarget.At(Malt.SackBounds.center, 1.3f),
                Say = () => "Hold the sack to pour grain up to the green band.",
            },
            new Lesson
            {
                Id = "lid", MinLevel = 1, Station = 1,
                Ready = () => Malt != null && Malt.LineReached && !Malt.Vat,
                Done = () => TubStep != SteepStep.Filling,
                Where = () => Malt.Carrying ? LessonTarget.At(Malt.JarMouthWorld, 1.3f) : LessonTarget.At(Malt.LidBounds.center, 0.9f),
                Say = () => Malt.Carrying ? "Set the lid on the jar." : "Pick up the lid.",
            },
            new Lesson
            {
                Id = "skim", MinLevel = 2, Station = 1,
                Ready = () => FloatingHusk().HasValue && !Malt.Vat,
                Done = () => !FloatingHusk().HasValue,
                Where = () => FloatingHusk() is Vector2 h ? LessonTarget.At(h, 0.7f) : (LessonTarget?)null,
                Say = () => "Husks float. Click them out before they spoil the steep.",
            },
            new Lesson
            {
                // From the moment it starts to sprout until it is turned: a warning
                // first, then the turn, with the player kept at the jar the whole time.
                Id = "turn", MinLevel = 2, Station = 1, Focus = true,
                Ready = () => Malt != null && Malt.TurnPending && !Malt.Vat,
                Done = () => Malt == null || !Malt.TurnPending,
                Where = () => LessonTarget.At(Malt.JarBounds.center, 1.4f),
                Reveal = () => Malt.JarArea,
                Say = () => Malt.TurnDue ? "Now: click the jar to turn the bed."
                                         : "As it sprouts the bed packs down. Stay, and turn it while the needle is in the green.",
            },
            new Lesson
            {
                Id = "scoop", MinLevel = 1, Station = 1,
                Ready = () => Malt != null && Malt.CanScoop,
                Done = () => Malt == null || !Malt.CanScoop,
                Where = () => Malt.Carrying ? LessonTarget.At(Malt.KilnSackBounds.center, 1.1f) : LessonTarget.At(Malt.JarBounds.center, 1.4f),
                Say = () => Malt.Carrying ? (Malt.Vat ? "Into the basket for the kiln." : "Into the sack for the kiln.")
                                          : "Sprouted. Scoop the green malt from the jar.",
            },
            new Lesson
            {
                Id = "kiln", MinLevel = 1, Station = 2,
                Ready = () => Kiln != null && Kiln.CanLoadKiln,
                Done = () => Kiln == null || Kiln.KilnOrder != null,
                Where = () => Kiln.Carrying ? LessonTarget.At(Kiln.KilnTrayWorld, 1.4f) : LessonTarget.At(Kiln.NextBasketBounds.center, 1.1f),
                Say = () => Kiln.Carrying ? "Spread it on the kiln tray."
                           : Malt != null && Malt.Vat ? "Take the basket of green malt from the rack." : "Take the sack of green malt from the rack.",
            },
            new Lesson
            {
                Id = "stoke", MinLevel = 1, Station = 2,
                Ready = () => Kiln != null && Kiln.KilnOrder != null && Kiln.CanStoke,
                Done = () => Kiln == null || Kiln.LogsBurning > 0 || Kiln.KilnOrder == null,
                Where = () => Kiln.Carrying ? LessonTarget.At(Kiln.FireboxWorld, 1.1f) : LessonTarget.At(Kiln.PileWorld, 1.0f),
                Say = () => Kiln.Carrying ? "Into the firebox." : "Feed a log to the fire. Keep the heat in the band.",
            },

            // ------------------------------------------------------------- prep
            new Lesson
            {
                Id = "leaves", MinLevel = 0, Station = 3,
                Ready = () => PrepMix != null && !PrepMix.AllLeavesIn,
                Done = () => PrepMix == null || PrepMix.AllLeavesIn,
                Where = () => HeldLeaf() ? LessonTarget.At(Prep.MortarWorld, 1.3f)
                              : NextLeaf() is Vector2 leaf ? LessonTarget.At(leaf, 0.75f) : (LessonTarget?)null,
                Say = () => HeldLeaf() ? "Toss it into the bowl." : "Grab the leaf the note asks for.",
            },
            new Lesson
            {
                Id = "pestle", MinLevel = 0, Station = 3,
                Ready = () => PrepMix != null && PrepMix.AllLeavesIn && !PrepMix.Ground && !PrepBench.Crusher,
                Done = () => PrepMix == null || PrepMix.Ground,
                Where = () => LessonTarget.At(Prep.MortarWorld, 1.6f),
                Say = () => "Press and hold the bowl, then let go to strike.",
            },

            // ---------------------------------------------------------- cauldron
            new Lesson
            {
                Id = "stir", MinLevel = 0, Station = 4,
                Ready = () => Pot != null && Pot.Working != null && !Pot.IsBrewComplete && !PhysicsCauldronManager.AutoStir,
                Done = () => Pot == null || Pot.Working == null || Pot.BrewProgress01 >= 0.2f,
                Where = () => LessonTarget.At(Pot.MouthCentre, Pot.MouthRadiusX + 0.8f),
                Say = () => Pot.RequiredClockwise ? "Circle over the pot, clockwise. Keep the needle green."
                                                  : "Circle over the pot, counter-clockwise. Keep the needle green.",
            },
            new Lesson
            {
                Id = "flip", MinLevel = 2, Station = 4,
                Ready = () => Pot != null && Pot.Working != null && !Pot.RequiredClockwise && !Pot.IsBrewComplete && !PhysicsCauldronManager.AutoStir,
                Done = () => Pot == null || Pot.Working == null || Pot.StirringCorrectly || Pot.IsBrewComplete,
                Where = () => LessonTarget.At(Pot.MouthCentre, Pot.MouthRadiusX + 0.8f),
                Say = () => "Today's brew turns the other way: counter-clockwise.",
            },

            // ---------------------------------------------------------- bottling
            new Lesson
            {
                Id = "pour", MinLevel = 0, Station = 5,
                Ready = () => Bottle != null && Bottle.Working != null && Bottle.Current == BottlingBench.Step.Pour && !BottlingBench.Funnel,
                Done = () => Bottle == null || Bottle.Working == null || Bottle.Current != BottlingBench.Step.Pour,
                Where = () => LessonTarget.At(Bottle.LadleWorld, 1.2f),
                Reveal = () => Bottle.FlaskWorldRect,
                Say = () => Bottle.Fill01 >= BottlingBench.TargetLow
                    ? "That's the band. Let go of the ladle."
                    : "Drag the ladle down to pour. Stop between the gold lines.",
            },
            new Lesson
            {
                Id = "cork", MinLevel = 0, Station = 5,
                Ready = () => Bottle != null && Bottle.Working != null && Bottle.Current == BottlingBench.Step.Seal,
                Done = () => Bottle == null || Bottle.Working == null || Bottle.CorkSeated || Bottle.Current != BottlingBench.Step.Seal,
                Where = () => Bottle.CorkCarried ? LessonTarget.At(Bottle.FlaskMouthWorld, 1.0f) : LessonTarget.At(Bottle.CorkDishWorld, 0.8f),
                Say = () => Bottle.CorkCarried ? "Let go over the neck." : "Pick up the cork.",
            },
            new Lesson
            {
                Id = "label", MinLevel = 1, Station = 5,
                Ready = () => Bottle != null && Bottle.Working != null && Bottle.Current == BottlingBench.Step.Label && !Bottle.Labelled,
                Done = () => Bottle == null || Bottle.Working == null || Bottle.Labelled,
                Where = () => Bottle.LabelCarried ? LessonTarget.At(Bottle.LabelSpotWorld, 1.1f)
                                                  : LessonTarget.At(Bottle.TagWorld(Bottle.Working.element), 0.7f),
                Say = () => Bottle.LabelCarried ? "Stick it on the flask." : "Drag the matching label onto the flask.",
            },

            // ---------------------------------------------------------- delivery
            new Lesson
            {
                Id = "deliver", MinLevel = 0, Station = 6,
                Ready = () => Delivery != null && Delivery.Waiting != null,
                Done = () => Delivery == null || Delivery.Waiting == null,
                Where = () => Delivery.Waiting is ActiveOrder w ? LessonTarget.At(Delivery.FlaskWorld(w), 0.8f) : (LessonTarget?)null,
                Reveal = () => Delivery.Waiting is ActiveOrder w
                    ? Rect.MinMaxRect(Delivery.FighterWorld(w.heroId).x - 0.9f, Delivery.FighterWorld(w.heroId).y - 1.3f,
                                      Delivery.FighterWorld(w.heroId).x + 0.5f, Delivery.FighterWorld(w.heroId).y + 1.0f)
                    : (Rect?)null,
                Say = () => Delivery.Waiting is ActiveOrder w ? $"Drag the flask to {w.heroName}. It is theirs to carry." : "",
            },

            // -------------------------------------------------------------- send
            new Lesson
            {
                Id = "send", MinLevel = 0, Station = -1,
                Ready = () => MorningScreen.ReadyToSend && Screen != null && Screen.SendButtonRect != null,
                Done = () => GameLoopManager.Instance == null || GameLoopManager.Instance.Phase != GamePhase.Morning,
                Where = () => LessonTarget.On(Screen.SendButtonRect, 8f),
                Say = () => "Every flask is ready. Send them out.",
            },

            // ------------------------------------------------ the first evening
            // A short walk round the Evening, tab by tab: what came home, where
            // the gold goes, who goes out, and the diary that carries the story.
            new Lesson
            {
                Id = "ev_report", MinLevel = 0, OnScreen = ScreenId.Evening, ClickToGo = true,
                Ready = () => Evening != null && Evening.ActiveTabIndex == 0,
                Done = () => false,
                Where = () => LessonTarget.Over(Evening.BodyRect),
                Say = () => "Tonight's report: how the road went and what you earned.",
            },
            new Lesson
            {
                Id = "ev_upgrades", MinLevel = 0, OnScreen = ScreenId.Evening,
                Ready = () => Evening != null && Evening.ActiveTabIndex != 1,
                Done = () => Evening != null && Evening.ActiveTabIndex == 1,
                Where = () => LessonTarget.On(Evening.TabRect(1), 6f),
                Say = () => "Upgrades: spend your gold here.",
            },
            new Lesson
            {
                Id = "ev_upgrades_body", MinLevel = 0, OnScreen = ScreenId.Evening, ClickToGo = true,
                Ready = () => Evening != null && Evening.ActiveTabIndex == 1,
                Done = () => false,
                Where = () => LessonTarget.Over(Evening.BodyRect),
                Say = () => "Bench upgrades change how a bench works. Road upgrades go out with the party.",
            },
            new Lesson
            {
                Id = "ev_party", MinLevel = 0, OnScreen = ScreenId.Evening,
                Ready = () => Evening != null && Evening.ActiveTabIndex != 2,
                Done = () => Evening != null && Evening.ActiveTabIndex == 2,
                Where = () => LessonTarget.On(Evening.TabRect(2), 6f),
                Say = () => "Party: hire fighters and choose who walks tomorrow.",
            },
            new Lesson
            {
                Id = "ev_party_body", MinLevel = 0, OnScreen = ScreenId.Evening, ClickToGo = true,
                Ready = () => Evening != null && Evening.ActiveTabIndex == 2,
                Done = () => false,
                Where = () => LessonTarget.Over(Evening.BodyRect),
                Say = () => "Every fighter orders their own flask, so a bigger party means more to brew.",
            },
            new Lesson
            {
                Id = "ev_diary", MinLevel = 0, OnScreen = ScreenId.Evening,
                Ready = () => Evening != null,
                Done = () => UIManager.Instance != null && UIManager.Instance.Current == ScreenId.Diary,
                Where = () => LessonTarget.On(Evening.TabRect(3), 6f),
                Say = () => "Nell's diary: read it to follow the story.",
            },
            new Lesson
            {
                Id = "ev_sleep", MinLevel = 0, OnScreen = ScreenId.Evening,
                Ready = () => Evening != null,
                Done = () => UIManager.Instance != null && UIManager.Instance.Current != ScreenId.Evening
                             && UIManager.Instance.Current != ScreenId.Diary,
                Where = () => LessonTarget.On(Evening.SleepRect, 6f),
                Say = () => "When you are ready, sleep and pick tomorrow's road.",
            },
        };

        private static EveningScreen Evening =>
            UIManager.Instance != null ? UIManager.Instance.ScreenOf(ScreenId.Evening) as EveningScreen : null;

        /// <summary>The rail names, for "Open the Prep bench."</summary>
        public static readonly string[] StationNames = { "Counter", "Steep", "Kiln", "Prep", "Cauldron", "Bottling", "Delivery" };

        // ------------------------------------------------------------ helpers

        private static bool HeldLeaf()
        {
            if (Prep == null) return false;
            foreach (var leaf in Prep.Leaves)
                if (leaf != null && leaf.Grab != null && leaf.Grab.IsHeld) return true;
            return false;
        }

        /// <summary>The nearest loose leaf of the element the recipe wants next.</summary>
        private static Vector2? NextLeaf()
        {
            BrewMixture mix = PrepMix;
            if (mix == null || mix.AllLeavesIn) return null;
            ElementType want = mix.NextStep;
            Vector2? best = null;
            float bestD = float.MaxValue;
            foreach (var leaf in Prep.Leaves)
            {
                if (leaf == null || leaf.InBowl || leaf.Body == null || leaf.Data == null) continue;
                float d = (leaf.Body.position - Prep.MortarWorld).sqrMagnitude;
                bool match = leaf.Data.Element == want;
                if (!match) d += 1000f;   // any leaf at all beats nothing, the right one beats any
                if (d < bestD) { bestD = d; best = leaf.Body.position; }
            }
            return best;
        }

        private static Vector2? FloatingHusk()
        {
            if (Malt == null || TubStep != SteepStep.Soaking) return null;
            foreach (var g in Malt.TubGrains)
                if (g != null && g.Husk && !g.Skimmed && g.Body != null) return g.Body.position;
            return null;
        }
    }
}
