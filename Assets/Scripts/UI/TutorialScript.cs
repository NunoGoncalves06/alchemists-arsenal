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

        public static LessonTarget At(Vector2 world, float radius) =>
            new LessonTarget { World = world, WorldRadius = radius, IsWorld = true };
        public static LessonTarget On(RectTransform rect, float pad = 10f) =>
            new LessonTarget { Rect = rect, Pad = pad };
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
        /// <summary>The bench's rail index (0 Counter .. 4 Bottling), or -1 for none.</summary>
        public int Station = -1;
        public Func<bool> Ready, Done;
        public Func<LessonTarget?> Where;
        public Func<string> Say;
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
        private static MaltingBench Malt => MaltingBench.Instance;
        private static PrepBench Prep => PrepBench.Instance;
        private static PhysicsCauldronManager Pot => PhysicsCauldronManager.Instance;
        private static BottlingBench Bottle => BottlingBench.Instance;
        private static MorningScreen Screen =>
            UIManager.Instance != null ? UIManager.Instance.ScreenOf(ScreenId.Morning) as MorningScreen : null;

        private static MaltStep TubStep => Malt != null && Malt.TubOrder != null ? Malt.TubOrder.maltStep : MaltStep.Waiting;
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
                Ready = () => TubStep == MaltStep.Filling && !Malt.CanSteep,
                Done = () => Malt == null || Malt.CanSteep || TubStep != MaltStep.Filling,
                Where = () => LessonTarget.At(Malt.SackWorld, 1.3f),
                Say = () => "Hold the sack to pour grain up to the green band.",
            },
            new Lesson
            {
                Id = "lid", MinLevel = 1, Station = 1,
                Ready = () => Malt != null && Malt.CanSteep,
                Done = () => TubStep != MaltStep.Filling,
                Where = () => Malt.Carrying ? LessonTarget.At(Malt.JarMouthWorld, 1.3f) : LessonTarget.At(Malt.LidWorld, 0.9f),
                Say = () => Malt.Carrying ? "Set the lid on the jar." : "Pick up the lid.",
            },
            new Lesson
            {
                Id = "skim", MinLevel = 2, Station = 1,
                Ready = () => FloatingHusk().HasValue,
                Done = () => !FloatingHusk().HasValue,
                Where = () => FloatingHusk() is Vector2 h ? LessonTarget.At(h, 0.7f) : (LessonTarget?)null,
                Say = () => "Husks float. Click them out before they spoil the steep.",
            },
            new Lesson
            {
                Id = "turn", MinLevel = 2, Station = 1,
                Ready = () => Malt != null && Malt.TurnDue,
                Done = () => Malt == null || !Malt.TurnDue,
                Where = () => LessonTarget.At(Malt.JarWorld, 1.4f),
                Say = () => "The bed is packing down. Click the jar to turn it.",
            },
            new Lesson
            {
                Id = "kiln", MinLevel = 1, Station = 1,
                Ready = () => Malt != null && Malt.CanLoadKiln,
                Done = () => Malt == null || Malt.KilnOrder != null,
                Where = () => Malt.Carrying ? LessonTarget.At(Malt.KilnTrayWorld, 1.4f) : LessonTarget.At(Malt.JarWorld, 1.4f),
                Say = () => Malt.Carrying ? "Spread it on the kiln tray." : "Scoop the green malt from the jar.",
            },
            new Lesson
            {
                Id = "stoke", MinLevel = 1, Station = 1,
                Ready = () => Malt != null && Malt.KilnOrder != null && Malt.CanStoke,
                Done = () => Malt == null || Malt.LogsBurning > 0 || Malt.KilnOrder == null,
                Where = () => Malt.Carrying ? LessonTarget.At(Malt.FireboxWorld, 1.1f) : LessonTarget.At(Malt.PileWorld, 1.0f),
                Say = () => Malt.Carrying ? "Into the firebox." : "Feed a log to the fire. Keep the heat in the band.",
            },

            // ------------------------------------------------------------- prep
            new Lesson
            {
                Id = "leaves", MinLevel = 0, Station = 2,
                Ready = () => PrepMix != null && !PrepMix.AllLeavesIn,
                Done = () => PrepMix == null || PrepMix.AllLeavesIn,
                Where = () => HeldLeaf() ? LessonTarget.At(Prep.MortarWorld, 1.3f)
                              : NextLeaf() is Vector2 leaf ? LessonTarget.At(leaf, 0.75f) : (LessonTarget?)null,
                Say = () => HeldLeaf() ? "Toss it into the bowl." : "Grab the leaf the note asks for.",
            },
            new Lesson
            {
                Id = "pestle", MinLevel = 0, Station = 2,
                Ready = () => PrepMix != null && PrepMix.AllLeavesIn && !PrepMix.Ground,
                Done = () => PrepMix == null || PrepMix.Ground,
                Where = () => LessonTarget.At(Prep.MortarWorld, 1.6f),
                Say = () => "Press and hold the bowl, then let go to strike.",
            },

            // ---------------------------------------------------------- cauldron
            new Lesson
            {
                Id = "stir", MinLevel = 0, Station = 3,
                Ready = () => Pot != null && Pot.Working != null && !Pot.IsBrewComplete,
                Done = () => Pot == null || Pot.Working == null || Pot.BrewProgress01 >= 0.2f,
                Where = () => LessonTarget.At(Pot.MouthCentre, Pot.MouthRadiusX + 0.8f),
                Say = () => Pot.RequiredClockwise ? "Circle over the pot, clockwise. Keep the needle green."
                                                  : "Circle over the pot, counter-clockwise. Keep the needle green.",
            },
            new Lesson
            {
                Id = "flip", MinLevel = 2, Station = 3,
                Ready = () => Pot != null && Pot.Working != null && !Pot.RequiredClockwise && !Pot.IsBrewComplete,
                Done = () => Pot == null || Pot.Working == null || Pot.StirringCorrectly || Pot.IsBrewComplete,
                Where = () => LessonTarget.At(Pot.MouthCentre, Pot.MouthRadiusX + 0.8f),
                Say = () => "Today's brew turns the other way: counter-clockwise.",
            },

            // ---------------------------------------------------------- bottling
            new Lesson
            {
                Id = "pour", MinLevel = 0, Station = 4,
                Ready = () => Bottle != null && Bottle.Working != null && Bottle.Current == BottlingBench.Step.Pour,
                Done = () => Bottle == null || Bottle.Working == null || Bottle.Current != BottlingBench.Step.Pour,
                Where = () => LessonTarget.At(Bottle.LadleWorld, 1.2f),
                Say = () => "Drag the ladle down to pour. Stop on the line.",
            },
            new Lesson
            {
                Id = "cork", MinLevel = 0, Station = 4,
                Ready = () => Bottle != null && Bottle.Working != null && Bottle.Current == BottlingBench.Step.Seal,
                Done = () => Bottle == null || Bottle.Working == null || Bottle.CorkSeated || Bottle.Current != BottlingBench.Step.Seal,
                Where = () => Bottle.CorkCarried ? LessonTarget.At(Bottle.FlaskMouthWorld, 1.0f) : LessonTarget.At(Bottle.CorkDishWorld, 0.8f),
                Say = () => Bottle.CorkCarried ? "Let go over the neck." : "Pick up the cork.",
            },
            new Lesson
            {
                Id = "label", MinLevel = 1, Station = 4,
                Ready = () => Bottle != null && Bottle.Working != null && Bottle.Current == BottlingBench.Step.Label && !Bottle.Labelled,
                Done = () => Bottle == null || Bottle.Working == null || Bottle.Labelled,
                Where = () => Bottle.LabelCarried ? LessonTarget.At(Bottle.LabelSpotWorld, 1.1f)
                                                  : LessonTarget.At(Bottle.TagWorld(Bottle.Working.element), 0.7f),
                Say = () => Bottle.LabelCarried ? "Stick it on the flask." : "Drag the matching label onto the flask.",
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
        };

        /// <summary>The rail names, for "Open the Prep bench."</summary>
        public static readonly string[] StationNames = { "Counter", "Malting", "Prep", "Cauldron", "Bottling" };

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
            if (Malt == null || TubStep != MaltStep.Soaking) return null;
            foreach (var g in Malt.TubGrains)
                if (g != null && g.Husk && !g.Skimmed && g.Body != null) return g.Body.position;
            return null;
        }
    }
}
