using System.Text;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using AlchemistsArsenal.Art;
using AlchemistsArsenal.Audio;
using AlchemistsArsenal.Combat;
using AlchemistsArsenal.Core;
using AlchemistsArsenal.Crafting;
using AlchemistsArsenal.Data;
using AlchemistsArsenal.Systems;
using AlchemistsArsenal.UI.Stations;

namespace AlchemistsArsenal.UI
{
    /// <summary>
    /// The morning shell (DESIGN.md §7.6): top bar, the station rail, the centre
    /// column the active <see cref="StationPanel"/> fills, and the right dock: every
    /// fighter's flask with the stage it has reached, then the quality meter and
    /// ledger of the flask on the bench in front of you.
    ///
    /// Several flasks are in flight at once (one per fighter), each bench working
    /// its own; the rail says how many are waiting at each.
    ///
    /// The shell owns none of the crafting: each station is its own class under
    /// <c>UI/Stations</c> and tells the shell, through one callback, when it has
    /// moved quality. All four are functional from day 1 — the Cauldron, Prep and
    /// Bottling open the moment a job is accepted at the Counter
    /// (<see cref="TutorialManager.StationsUnlocked"/>); there is no multi-day gate.
    /// </summary>
    public class MorningScreen : GameScreen
    {
        // Named StationTab, not Tab — the headless playtest driver reflects on this
        // name and on SwitchTab(StationTab) to drive the screen like a real click.
        // Prep comes before the Cauldron: you crush and add the leaves, then you
        // stir them. The Cauldron refuses to brew until the mixture is ready, so the
        // rail order is the order the work actually happens in.
        // Malting comes right after the Counter: the grain is malted before anything
        // else, and nothing reaches Prep until its malt is done.
        private enum StationTab { Counter, Malting, Prep, Cauldron, Bottling }

        private readonly StationPanel[] _stations =
        {
            new CounterStation(), new MaltingStation(), new PrepStation(), new CauldronStation(), new BottlingStation(),
        };
        private readonly UIKit.RailTab[] _tabs = new UIKit.RailTab[5];

        private StationTab _activeTab = StationTab.Counter;
        private bool _everSwitched;
        private int _builtForDay = -1;

        private Image _bg;
        private TextMeshProUGUI _dayText, _goldText, _biomeChip, _clockPct;
        private Image _clockFill;
        private UIKit.MeterView _quality;

        // order dock
        private Transform _flaskRows;
        private TextMeshProUGUI _focusTitle, _gradeText, _logText, _sendHint;
        private Button _sendBtn;
        private ActiveOrder _focus;
        private int _dockSig = int.MinValue;

        // ------------------------------------------------------------------ build

        protected override void Build()
        {
            // Opaque ground for every station except the Cauldron, which needs the
            // world camera behind the overlay to show through.
            _bg = UIFactory.Box(transform, UITheme.Ground, Rt);

            BuildTopBar();
            BuildCentre();
            BuildRail();
            BuildOrderDock();

            SwitchTab(StationTab.Counter);
        }

        private void BuildTopBar()
        {
            var bar = UIFactory.Panel(transform, UITheme.Surface, "TopBar");
            UIFactory.Place(bar.rectTransform, 0f, 0.935f, 1f, 1f);
            var edge = UIFactory.Panel(bar.transform, UITheme.Line, "Edge");
            UIFactory.Place(edge.rectTransform, 0f, 0f, 1f, 0f);
            edge.rectTransform.sizeDelta = new Vector2(0f, 2f);

            _dayText = UIFactory.Title(bar.transform, "DAY 1", UITheme.SizeHeading + 3, UITheme.Candle,
                TextAlignmentOptions.Left);
            UIFactory.Place(_dayText.rectTransform, 0.015f, 0f, 0.10f, 1f);

            var coin = UIFactory.Icon(bar.transform, PixelSprites.Coin(), 22f);
            UIFactory.Place(coin.rectTransform, 0.105f, 0.3f, 0.125f, 0.72f);
            _goldText = UIFactory.MonoLabel(bar.transform, "0 g", UITheme.SizeBody, UITheme.TextHi,
                TextAlignmentOptions.Left);
            UIFactory.Place(_goldText.rectTransform, 0.128f, 0f, 0.22f, 1f);

            // The clock reads left-to-right on one line: label, track, percentage.
            // A stacked meter here put its caption above the top edge of the bar and
            // clipped it (playtest screenshot: "MORNING" sliced in half).
            var clockLabel = UIFactory.Heading(bar.transform, "Morning", UITheme.TextLow, UITheme.SizeTiny,
                TextAlignmentOptions.Left);
            UIFactory.Place(clockLabel.rectTransform, 0.275f, 0.2f, 0.355f, 0.8f);

            var clockTrack = UIFactory.Bar(bar.transform, UITheme.Ground, UITheme.Candle, out _clockFill);
            UIFactory.Place(clockTrack.rectTransform, 0.36f, 0.34f, 0.55f, 0.66f);

            _clockPct = UIFactory.MonoLabel(bar.transform, "", UITheme.SizeTiny, UITheme.TextLow,
                TextAlignmentOptions.Left);
            UIFactory.Place(_clockPct.rectTransform, 0.558f, 0.2f, 0.60f, 0.8f);

            _biomeChip = UIFactory.Label(bar.transform, "", UITheme.SizeSmall, UITheme.TextMid,
                TextAlignmentOptions.Left);
            UIFactory.Place(_biomeChip.rectTransform, 0.60f, 0f, 0.86f, 1f);

            var menu = UIFactory.Button(bar.transform, "MENU", () => UIManager.Instance.Show(ScreenId.Settings),
                primary: false);
            UIFactory.Place(menu.image.rectTransform, 0.90f, 0.16f, 0.985f, 0.84f);
        }

        private void BuildCentre()
        {
            var centre = UIFactory.Rect(transform, "Centre", new Vector2(0.085f, 0f), new Vector2(0.70f, 0.935f),
                new Vector2(14, 14), new Vector2(-14, -14));
            foreach (var station in _stations)
                station.Build(centre, OnStationChanged);
        }

        private void BuildRail()
        {
            var rail = UIFactory.Panel(transform, UITheme.Surface, "Rail");
            UIFactory.Place(rail.rectTransform, 0f, 0f, 0.085f, 0.935f);
            var edge = UIFactory.Panel(rail.transform, UITheme.Line, "Edge");
            edge.rectTransform.anchorMin = new Vector2(1f, 0f);
            edge.rectTransform.anchorMax = new Vector2(1f, 1f);
            edge.rectTransform.sizeDelta = new Vector2(2f, 0f);

            var stack = UIFactory.VStack(rail.transform, 8f, new RectOffset(8, 8, 14, 14));
            UIFactory.Stretch((RectTransform)stack.transform);

            for (int i = 0; i < _stations.Length; i++)
            {
                var tab = (StationTab)i;
                _tabs[i] = UIKit.StationTab(stack.transform, _stations[i].RailName.ToUpperInvariant(),
                    (i + 1).ToString(), _stations[i].RailIcon, () => SwitchTab(tab));
            }
        }

        private void BuildOrderDock()
        {
            var dock = UIFactory.Panel(transform, UITheme.Surface, "OrderDock");
            UIFactory.Place(dock.rectTransform, 0.70f, 0f, 1f, 0.935f);
            var edge = UIFactory.Panel(dock.transform, UITheme.Line, "Edge");
            edge.rectTransform.anchorMin = new Vector2(0f, 0f);
            edge.rectTransform.anchorMax = new Vector2(0f, 1f);
            edge.rectTransform.sizeDelta = new Vector2(2f, 0f);

            // --- every fighter's flask ---------------------------------------
            var flasksCard = UIKit.Card(dock.transform, "Today's flasks", out Transform flasks, spacing: 4f);
            UIFactory.Place(flasksCard.rectTransform, 0.04f, 0.62f, 0.96f, 0.975f);
            // The rows get a container of their own: rebuilding them cleared the card,
            // heading and all.
            var rows = UIFactory.VStack(flasks, 4f);
            UIFactory.Flex(rows.gameObject, 1f, 1f);
            _flaskRows = rows.transform;

            // --- the flask in front of you -----------------------------------
            var qualityCard = UIKit.Card(dock.transform, "Potion quality", out Transform quality, spacing: 5f);
            UIFactory.Place(qualityCard.rectTransform, 0.04f, 0.42f, 0.96f, 0.60f);

            _focusTitle = UIFactory.Label(quality, "", UITheme.SizeSmall, UITheme.Candle, TextAlignmentOptions.Left, true);
            UIFactory.FixedHeight(_focusTitle.gameObject, 20f);
            _quality = UIKit.Meter(quality, "Score", UITheme.Candle, withBand: false, height: 18f);
            UIKit.GradeScale(quality);
            _gradeText = UIFactory.Label(quality, "", UITheme.SizeSmall, UITheme.TextMid,
                TextAlignmentOptions.Left, true);
            UIFactory.Flex(_gradeText.gameObject, 1f, 0f, minHeight: 22f);

            // --- ledger ------------------------------------------------------
            var logCard = UIKit.Card(dock.transform, "What the flask has been through", out Transform log);
            UIFactory.Place(logCard.rectTransform, 0.04f, 0.13f, 0.96f, 0.40f);
            _logText = UIFactory.MonoLabel(log, "", UITheme.SizeTiny, UITheme.TextMid);
            UIFactory.Flex(_logText.gameObject, 1f, 1f);

            // --- send --------------------------------------------------------
            _sendHint = UIFactory.Label(dock.transform, "", UITheme.SizeTiny, UITheme.TextLow,
                TextAlignmentOptions.Center);
            UIFactory.Place(_sendHint.rectTransform, 0.04f, 0.085f, 0.96f, 0.12f);

            _sendBtn = UIFactory.Button(dock.transform, "SEND TO EXPEDITION",
                () => GameLoopManager.Instance.BeginHandoff());
            UIFactory.Place(_sendBtn.image.rectTransform, 0.04f, 0.02f, 0.96f, 0.08f);
        }

        // -------------------------------------------------------------- behaviour

        protected override void OnShow()
        {
            RunState s = SaveSystem.Instance.State;
            _dayText.text = $"DAY {s.day}";
            _goldText.text = $"{s.gold} g";
            _biomeChip.text = $"{BiomeLibrary.Name(s.TargetBiomeIndex)} — today's road";

            if (_builtForDay != s.day)
            {
                _builtForDay = s.day;
                foreach (var station in _stations) station.NewDay();
            }

            if (GameLoopManager.Instance != null)
                GameLoopManager.Instance.OnMorningTimeChanged += SetClock;
            _dockSig = int.MinValue;
            RefreshDock();
            _everSwitched = false;          // cut, don't glide, to the Counter on a new morning
            SwitchTab(StationTab.Counter); // a fresh morning starts back at the Counter
        }

        protected override void OnHide()
        {
            if (GameLoopManager.Instance != null)
                GameLoopManager.Instance.OnMorningTimeChanged -= SetClock;
            _stations[(int)_activeTab].OnExit();
        }

        private void SwitchTab(StationTab tab)
        {
            // Day-1 gate, until the job is accepted. TutorialManager.StationsUnlocked
            // flips on ITS OWN coroutine once it notices the order, which is not
            // guaranteed to have happened in the same frame the Counter creates that
            // order and immediately switches here — checking CurrentOrder directly as
            // a fallback closes that race (it previously silently failed to switch).
            bool hasOrder = CraftingManager.Instance != null && CraftingManager.Instance.Orders.Count > 0;
            if (tab != StationTab.Counter && !TutorialManager.StationsUnlocked && !hasOrder) return;

            if (_stations[(int)_activeTab] != null) _stations[(int)_activeTab].OnExit();
            _activeTab = tab;

            for (int i = 0; i < _stations.Length; i++)
                _stations[i].Root.gameObject.SetActive(i == (int)tab);

            StationPanel active = _stations[(int)tab];
            active.OnEnter();

            // The benches stand side by side in one room; the camera walks to this one.
            // Stations that play out in the world (Prep, Cauldron, Bottling) let it
            // show through; the Counter is all paper and talk.
            if (ShopWorld.Instance != null) ShopWorld.Instance.Focus((int)tab, instant: !_everSwitched);
            _everSwitched = true;
            if (_bg != null) _bg.enabled = !active.ShowsWorld;

            RefreshRail();
            _dockSig = int.MinValue;   // the dock follows the flask on this bench
            AudioManager.Play(Sfx.Tab);
        }

        private void RefreshRail()
        {
            var cm = CraftingManager.Instance;
            bool unlocked = TutorialManager.StationsUnlocked || (cm != null && cm.Orders.Count > 0);
            for (int i = 0; i < _tabs.Length; i++)
            {
                if (_tabs[i] == null) continue;
                bool locked = i != (int)StationTab.Counter && !unlocked;
                _tabs[i].SetState(i == (int)_activeTab, locked, _stations[i].Complete);
                _tabs[i].SetCount(WaitingAt((StationTab)i));
            }
        }

        /// <summary>How many flasks (or, at the Counter, fighters) are waiting at a bench.</summary>
        private static int WaitingAt(StationTab tab)
        {
            switch (tab)
            {
                case StationTab.Counter:
                {
                    RunState s = SaveSystem.Instance != null ? SaveSystem.Instance.State : null;
                    if (s == null) return 0;
                    int n = 0;
                    foreach (HeroRecord h in s.DeployedParty()) if (s.ContractFor(h.id) == null) n++;
                    return n;
                }
                case StationTab.Malting: return StationPanel.CountAt(BrewStage.Malting);
                case StationTab.Prep: return StationPanel.CountAt(BrewStage.Prep);
                case StationTab.Cauldron: return StationPanel.CountAt(BrewStage.Cauldron);
                default: return StationPanel.CountAt(BrewStage.Bottling);
            }
        }

        /// <summary>
        /// Serve whoever is at the Counter with the first job on the board. Kept as
        /// the same private name the headless playtest driver reflects on, so the
        /// harness still exercises the real Counter path rather than calling the
        /// manager API behind it. Once every fighter has ordered, on to Prep.
        /// </summary>
        private void AcceptOrder()
        {
            SwitchTab(StationTab.Counter);
            if (GameLoopManager.Instance == null) return;

            // Delegate rather than duplicate: the Counter owns accepting a job,
            // including the scoring it applies for reading the road.
            if (_stations[(int)StationTab.Counter] is not CounterStation counter) return;
            if (!counter.AcceptFirstOffer()) return;

            foreach (var station in _stations) station.Refresh();
            _dockSig = int.MinValue;
            RefreshDock();
            if (CounterStation.AllServed) SwitchTab(StationTab.Malting);   // the grain is malted first
        }

        private void OnStationChanged()
        {
            RefreshDock();
            RefreshRail();
        }

        // --- the dock ---

        /// <summary>A cheap fingerprint of everything the dock shows; it redraws only when it changes.</summary>
        private int DockSignature()
        {
            int sig = (int)_activeTab * 7919;
            var cm = CraftingManager.Instance;
            if (cm != null)
                for (int i = 0; i < cm.Orders.Count; i++)
                {
                    ActiveOrder o = cm.Orders[i];
                    sig = sig * 31 + o.qualityScore * 7 + (int)o.stage * 1009 + o.deductions.Count * 131;
                }
            RunState s = SaveSystem.Instance != null ? SaveSystem.Instance.State : null;
            if (s != null && s.contracts != null) sig = sig * 17 + s.contracts.Count;
            ActiveOrder shown = _stations[(int)_activeTab].ShownOrder;
            if (shown != null) sig = sig * 13 + shown.queueIndex + 1;
            return sig;
        }

        private static string StageName(BrewStage stage) => stage switch
        {
            BrewStage.Malting => "MALTING",
            BrewStage.Prep => "PREP",
            BrewStage.Cauldron => "CAULDRON",
            BrewStage.Bottling => "BOTTLING",
            _ => "READY",
        };

        /// <summary>One row per fighter going out: face, name, element, where their flask is, its score.</summary>
        private void BuildFlaskRows()
        {
            for (int i = _flaskRows.childCount - 1; i >= 0; i--) Destroy(_flaskRows.GetChild(i).gameObject);
            RunState s = SaveSystem.Instance != null ? SaveSystem.Instance.State : null;
            var cm = CraftingManager.Instance;
            if (s == null) return;

            var party = s.DeployedParty();
            foreach (HeroRecord h in party)
            {
                ActiveOrder o = cm != null ? cm.OrderFor(h.id) : null;
                bool focused = o != null && o == _focus;
                Image row = UIKit.Surface(_flaskRows, out Transform inner,
                    focused ? UITheme.SurfaceTop : UITheme.SurfaceHi, focused ? UITheme.Candle : UITheme.LineSoft, "FlaskRow");
                UIFactory.FixedHeight(row.gameObject, 40f);

                var face = UIFactory.Icon(inner, PixelSprites.Buyer(h.portraitId), 30f);
                UIFactory.Place(face.rectTransform, 0.01f, 0.08f, 0.14f, 0.92f);
                var name = UIFactory.Label(inner, h.displayName, UITheme.SizeSmall, UITheme.TextHi, TextAlignmentOptions.Left, true);
                UIFactory.Place(name.rectTransform, 0.16f, 0f, 0.52f, 1f);

                if (o == null)
                {
                    var wait = UIFactory.Label(inner, "AT THE COUNTER", UITheme.SizeTiny, UITheme.TextLow, TextAlignmentOptions.Left);
                    UIFactory.Place(wait.rectTransform, 0.62f, 0f, 0.99f, 1f);
                    continue;
                }

                var badge = UIFactory.ElementBadge(inner, o.element, 20f);
                UIFactory.Place(badge.rectTransform, 0.53f, 0.22f, 0.60f, 0.78f);
                var stage = UIFactory.Label(inner, StageName(o.stage), UITheme.SizeTiny,
                    o.Finished ? UITheme.Ok : UITheme.Candle, TextAlignmentOptions.Left);
                UIFactory.Place(stage.rectTransform, 0.62f, 0f, 0.86f, 1f);
                var score = UIFactory.MonoLabel(inner, o.qualityScore.ToString(), UITheme.SizeSmall,
                    UITheme.GradeColor(o.GetGrade()), TextAlignmentOptions.Right);
                UIFactory.Place(score.rectTransform, 0.84f, 0f, 0.98f, 1f);
            }
            if (party.Count == 0)
                UIFactory.Label(_flaskRows, "Nobody is going out today.", UITheme.SizeSmall, UITheme.TextLow);
        }

        private void RefreshDock()
        {
            var cm = CraftingManager.Instance;
            _focus = _stations[(int)_activeTab].ShownOrder ?? (cm != null ? cm.CurrentOrder : null);
            BuildFlaskRows();

            bool any = cm != null && cm.Orders.Count > 0;
            _sendBtn.interactable = any;
            _sendHint.text = SendHint();

            ActiveOrder order = _focus;
            if (order == null)
            {
                _focusTitle.text = "";
                _quality.Set(0f, "—");
                _gradeText.text = "No potion on the bench.";
                _logText.text = "";
                return;
            }

            ContractRecord job = order.contract;
            bool hasJob = job != null && job.accepted;
            _focusTitle.text = (string.IsNullOrEmpty(order.heroName) ? "" : $"{order.heroName}'s ") + $"{order.element} flask" +
                               (hasJob ? $"  <size=85%><color=#{ColorUtility.ToHtmlStringRGB(UITheme.TextLow)}>{job.title} · {job.fee} g +{job.bonus}</color></size>" : "");

            var grade = order.GetGrade();
            _quality.Set(order.qualityScore / 100f, $"{order.qualityScore} / 100");
            _quality.SetFillColor(UITheme.GradeColor(grade));
            _gradeText.text =
                $"<color=#{ColorUtility.ToHtmlStringRGB(UITheme.GradeColor(grade))}>{grade.ToString().ToUpperInvariant()}</color>" +
                (hasJob
                    ? job.Meets(grade) ? "  — meets the job" : $"  — they want {job.RequiredGrade.ToString().ToUpperInvariant()}"
                    : "");

            var sb = new StringBuilder();
            int start = Mathf.Max(0, order.deductions.Count - 9);
            for (int i = start; i < order.deductions.Count; i++)
            {
                var d = order.deductions[i];
                string col = d.pointsDelta >= 0 ? "#4fae5a" : "#d64550";
                sb.AppendLine($"<color={col}>{(d.pointsDelta >= 0 ? "+" : "")}{d.pointsDelta,-3}</color> {d.station}: {d.reason}");
            }
            if (order.deductions.Count == 0) sb.AppendLine("Nothing done to it yet.");
            _logText.text = sb.ToString();
        }

        private static string SendHint()
        {
            var cm = CraftingManager.Instance;
            if (cm == null || cm.Orders.Count == 0) return "Take a job at the Counter first.";
            HeroRecord next = CounterStation.NextFighter;
            if (next != null) return $"{next.displayName} hasn't ordered yet — they would carry sludge.";
            if (cm.AllDone) return "Every flask sealed and labelled — good to go.";
            int left = 0;
            for (int i = 0; i < cm.Orders.Count; i++) if (!cm.Orders[i].Finished) left++;
            return $"{left} flask{(left == 1 ? "" : "s")} still on the benches — you can send early, it just won't be as good.";
        }

        private void SetClock(float t01)
        {
            if (_clockFill != null)
            {
                _clockFill.fillAmount = Mathf.Clamp01(t01);
                _clockFill.color = t01 < 0.25f ? UITheme.Danger : UITheme.Candle;
            }
            if (_clockPct != null) _clockPct.text = $"{Mathf.CeilToInt(t01 * 100f)}%";
        }

        private void Update()
        {
            StationPanel active = _stations[(int)_activeTab];
            if (active != null) active.Tick();

            int sig = DockSignature();
            if (sig != _dockSig)
            {
                _dockSig = sig;
                RefreshDock();
                RefreshRail();
            }
        }
    }
}
