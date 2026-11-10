using UnityEngine;
using AlchemistsArsenal.UI;

namespace AlchemistsArsenal.DebugTools
{
    /// <summary>
    /// The rules behind how the benches read: the spotlight's round soft edge, the shop cards'
    /// composition, the wrong-way warning, the cauldron's fire, the Steep turn window and the
    /// vat's fill bar. Pure functions only; the live benches are checked by the playtest.
    /// </summary>
    public class BenchFeelSimulationTest : MonoBehaviour, ISimulationSuite
    {
        public bool Done { get; private set; }

        private int _pass, _fail;

        private void Start()
        {
            Debug.Log("<color=cyan><b>=== BENCH FEEL SIMULATION ===</b></color>");
            TestSpotlight();
            TestShopCards();
            TestWrongWay();
            TestFire();
            TestSprouting();
            TestVatBar();
            Done = true;
            Debug.Log($"<color=cyan><b>=== DONE — {_pass} pass, {_fail} fail ===</b></color>");
        }

        private void Check(bool ok, string what)
        {
            if (ok) { _pass++; Debug.Log($"<color=green>✔</color> {what}"); }
            else { _fail++; Debug.LogError($"❌ {what}"); }
        }

        private void TestSprouting()
        {
            float g = Crafting.SteepBench.GerminateSeconds, from = Crafting.SteepBench.TurnFrom, by = Crafting.SteepBench.TurnBy;
            Check(g >= 9f && g <= 11f, $"sprouting: the sprout takes about ten seconds ({g:0.0} s)");
            Check(from * g >= 3.5f, $"sprouting: there is time to see it coming before the window opens ({from * g:0.0} s)");
            Check((by - from) * g >= 4f, $"sprouting: the window to turn the bed is long enough to hit ({(by - from) * g:0.0} s)");
            Check((1f - by) * g >= 1f, $"sprouting: the bed mats before it is done, with a second to spare ({(1f - by) * g:0.0} s)");
        }

        private void TestVatBar()
        {
            int target = Data.QualityBudget.GrainTarget, slack = Data.QualityBudget.GrainSlack;
            var bare = Crafting.SteepBench.FillGauge(vat: false);
            Check(Mathf.Approximately(bare.full, target * 1.6f)
                  && Mathf.Approximately(bare.bandMin01, (target - slack) / (target * 1.6f))
                  && Mathf.Approximately(bare.bandMax01, (target + slack * 2f) / (target * 1.6f)),
                "vat bar: a bare jar's gauge is as it always was");
            var vat = Crafting.SteepBench.FillGauge(vat: true);
            Check(Mathf.Approximately(vat.full, target), $"vat bar: the bar runs from nothing to the {target} grains the machine needs");
            Check(Mathf.Approximately(vat.bandMax01, 1f), "vat bar: the green box ends at the end of the bar");
            Check(vat.bandMax01 - vat.bandMin01 > 0f && vat.bandMax01 - vat.bandMin01 <= 0.15f, $"vat bar: the green box is small ({vat.bandMax01 - vat.bandMin01:P0} of the bar)");
        }

        private void TestFire()
        {
            Check(Crafting.CauldronView.FireBurns(brewFinished: false, anotherWaiting: false), "fire: a pot with a brew to do is lit");
            Check(Crafting.CauldronView.FireBurns(brewFinished: false, anotherWaiting: true), "fire: a pot with more waiting is lit");
            Check(!Crafting.CauldronView.FireBurns(brewFinished: true, anotherWaiting: false), "fire: a finished pot with nothing waiting goes out");
            Check(Crafting.CauldronView.FireBurns(brewFinished: true, anotherWaiting: true), "fire: a finished pot relights when another recipe waits");
        }

        private void TestWrongWay()
        {
            var w = new Crafting.WrongWayWarning();
            for (int i = 0; i < 9; i++) w.Tick(true, 0.1f);
            Check(!w.Visible, "wrong way: 0.9 s of it is not yet worth a warning");
            for (int i = 0; i < 2; i++) w.Tick(true, 0.1f);
            Check(w.Visible, "wrong way: more than a second of it shows the warning");
            w.Tick(false, 0.02f);
            Check(!w.Visible, "wrong way: the warning is gone the moment the spoon is right again");
            for (int i = 0; i < 9; i++) w.Tick(true, 0.1f);
            Check(!w.Visible, "wrong way: after a right stretch the second starts again from nothing");

            w.Tick(true, 0.3f);
            float lo = 1f, hi = 0f;
            for (int i = 0; i < 10; i++) { w.Tick(true, 0.1f); lo = Mathf.Min(lo, w.Blink01); hi = Mathf.Max(hi, w.Blink01); }
            Check(lo < 0.2f && hi > 0.8f, $"wrong way: the warning blinks ({lo:0.00} to {hi:0.00})");
            Check(Crafting.WrongWayWarning.Text.Split(' ').Length <= 4, $"wrong way: the warning is four words or fewer (\"{Crafting.WrongWayWarning.Text}\")");
        }

        private void TestShopCards()
        {
            var box = new Vector2(200f, 160f);
            bool centred = true, inside = true, kept = true;
            foreach (var content in new[] { new Rect(-4f, -1f, 8f, 2f), new Rect(-1f, -4f, 2f, 8f), new Rect(1f, 2f, 4f, 4f) })
            {
                var (scale, offset) = ShopPreview.Fit(content, box);
                Vector2 mid = content.center * scale + offset;
                centred &= mid.magnitude < 0.01f;
                inside &= content.width * scale <= box.x * 0.96f + 0.01f && content.height * scale <= box.y * 0.96f + 0.01f;
                kept &= scale > 0f && (content.width * scale >= box.x * 0.8f || content.height * scale >= box.y * 0.8f);   // as big as fits
            }
            Check(centred, "cards: a picture is centred in its card");
            Check(inside, "cards: a picture sits inside its card with a margin");
            Check(kept, "cards: a picture is as large as the card allows, at one scale for every layer");
        }

        private void TestSpotlight()
        {
            Check(Spotlight.HoleAlpha(0f) == 0f && Spotlight.HoleAlpha(0.5f) == 0f, "spotlight: the middle of the hole is fully clear");
            Check(Spotlight.HoleAlpha(1.3f) == 1f, "spotlight: well outside the hole it is fully dark");
            bool rising = true;
            float prev = 0f, d10 = -1f, d90 = -1f;
            for (float d = 0f; d <= 1.3f; d += 0.01f)
            {
                float a = Spotlight.HoleAlpha(d);
                rising &= a >= prev - 1e-5f;
                prev = a;
                if (d10 < 0f && a >= 0.1f) d10 = d;
                if (d90 < 0f && a >= 0.9f) d90 = d;
            }
            Check(rising, "spotlight: the dark only ever gets darker moving out");
            Check(d90 - d10 >= 0.15f, $"spotlight: the edge is a soft fade, not a rim ({d90 - d10:0.00} of a radius from 10% to 90%)");

            var box = new Rect(100f, 100f, 20f, 20f);
            Check(Spotlight.RectAlpha(110f, 110f, box, 3f) == 0f, "spotlight: the middle of a lit box is clear");
            Check(Spotlight.RectAlpha(110f, 100f, box, 3f) == 0f, "spotlight: the box's own edge is fully lit");
            Check(Spotlight.RectAlpha(200f, 200f, box, 3f) == 1f, "spotlight: far from the box it is dark");
            Check(Spotlight.RectAlpha(120f, 120f, box, 3f) > 0.3f, "spotlight: the corner of a lit box is rounded, not square");
        }
    }
}
