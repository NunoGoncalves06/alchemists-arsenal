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
    /// The morning shell (DESIGN.md §7.6): top bar, the four-station rail, the centre
    /// column the active <see cref="StationPanel"/> fills, and the right dock that
    /// carries the job you took and the live quality meter.
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
        private enum StationTab { Counter, Cauldron, Prep, Bottling }

        private readonly StationPanel[] _stations =
        {
            new CounterStation(), new CauldronStation(), new PrepStation(), new BottlingStation(),
        };
        private readonly UIKit.RailTab[] _tabs = new UIKit.RailTab[4];

        private StationTab _activeTab = StationTab.Counter;
        private int _builtForDay = -1;

        private Image _bg;
        private TextMeshProUGUI _dayText, _goldText, _biomeChip, _clockPct;
        private Image _clockFill;
        private UIKit.MeterView _quality;

        // order dock
        private Image _buyerPortrait;
        private TextMeshProUGUI _buyerName, _jobTitle, _jobTerms, _gradeText, _logText, _sendHint;
        private Button _sendBtn;

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

            // --- the job -----------------------------------------------------
            var jobCard = UIKit.Card(dock.transform, "Today's job", out Transform job, spacing: 6f);
            UIFactory.Place(jobCard.rectTransform, 0.04f, 0.62f, 0.96f, 0.975f);

            var who = UIFactory.HStack(job, 10f);
            who.childAlignment = TextAnchor.MiddleLeft;
            UIFactory.Flex(who.gameObject, 1f, 0f, minHeight: 62f);
            _buyerPortrait = UIKit.Portrait(who.transform, PixelSprites.Buyer("rookie"), 58f);
            UIFactory.Flex(_buyerPortrait.transform.parent.parent.gameObject, 0f, 0f, minWidth: 58f, minHeight: 58f);
            _buyerName = UIFactory.Label(who.transform, "", UITheme.SizeBody, UITheme.TextHi,
                TextAlignmentOptions.Left, true);
            UIFactory.Flex(_buyerName.gameObject, 1f, 1f);

            _jobTitle = UIFactory.Label(job, "", UITheme.SizeBody, UITheme.Candle);
            UIFactory.Flex(_jobTitle.gameObject, 1f, 0f, minHeight: 24f);
            _jobTerms = UIFactory.Label(job, "", UITheme.SizeSmall, UITheme.TextMid);
            UIFactory.Flex(_jobTerms.gameObject, 1f, 1f, minHeight: 44f);

            // --- quality -----------------------------------------------------
            var qualityCard = UIKit.Card(dock.transform, "Potion quality", out Transform quality, spacing: 6f);
            UIFactory.Place(qualityCard.rectTransform, 0.04f, 0.42f, 0.96f, 0.60f);

            _quality = UIKit.Meter(quality, "Score", UITheme.Candle, withBand: false, height: 20f);
            UIKit.GradeScale(quality);
            _gradeText = UIFactory.Label(quality, "", UITheme.SizeBody, UITheme.TextMid,
                TextAlignmentOptions.Left, true);
            UIFactory.Flex(_gradeText.gameObject, 1f, 0f, minHeight: 24f);

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
            HookOrder();
            RefreshDock();
            SwitchTab(StationTab.Counter); // a fresh morning starts back at the Counter
        }

        protected override void OnHide()
        {
            if (GameLoopManager.Instance != null)
                GameLoopManager.Instance.OnMorningTimeChanged -= SetClock;
            _stations[(int)_activeTab].OnExit();
            UnhookOrder();
        }

        private void SwitchTab(StationTab tab)
        {
            // Day-1 gate, until the job is accepted. TutorialManager.StationsUnlocked
            // flips on ITS OWN coroutine once it notices the order, which is not
            // guaranteed to have happened in the same frame the Counter creates that
            // order and immediately switches here — checking CurrentOrder directly as
            // a fallback closes that race (it previously silently failed to switch).
            bool hasOrder = CraftingManager.Instance != null && CraftingManager.Instance.CurrentOrder != null;
            if (tab != StationTab.Counter && !TutorialManager.StationsUnlocked && !hasOrder) return;

            if (_stations[(int)_activeTab] != null) _stations[(int)_activeTab].OnExit();
            _activeTab = tab;

            for (int i = 0; i < _stations.Length; i++)
                _stations[i].Root.gameObject.SetActive(i == (int)tab);

            StationPanel active = _stations[(int)tab];
            active.OnEnter();

            // Only the Cauldron shows the world behind the UI.
            if (_bg != null) _bg.enabled = !active.ShowsWorld;

            RefreshRail();
            AudioManager.Play(Sfx.Tab);
        }

        private void RefreshRail()
        {
            bool unlocked = TutorialManager.StationsUnlocked
                            || (CraftingManager.Instance != null && CraftingManager.Instance.CurrentOrder != null);
            for (int i = 0; i < _tabs.Length; i++)
            {
                if (_tabs[i] == null) continue;
                bool locked = i != (int)StationTab.Counter && !unlocked;
                _tabs[i].SetState(i == (int)_activeTab, locked, _stations[i].Complete);
            }
        }

        /// <summary>
        /// Take the first job on the board. Kept as the same private name the
        /// headless playtest driver reflects on, so the harness still exercises the
        /// real Counter path rather than calling the manager API behind it.
        /// </summary>
        private void AcceptOrder()
        {
            SwitchTab(StationTab.Counter);
            RunState s = SaveSystem.Instance != null ? SaveSystem.Instance.State : null;
            var offers = ContractBoard.Offers(s != null ? s.day : 1, s != null ? s.TargetBiomeIndex : 0);
            if (offers.Count == 0 || GameLoopManager.Instance == null) return;

            GameLoopManager.Instance.AcceptContract(offers[0].Clone());
            AudioManager.Play(Sfx.Confirm);
            var pot = PhysicsCauldronManager.Instance;
            if (pot != null) pot.BeginBrew(s != null ? s.day : 1);

            HookOrder();
            foreach (var station in _stations) station.Refresh();
            RefreshDock();
            SwitchTab(StationTab.Cauldron);
        }

        private void OnStationChanged()
        {
            RefreshDock();
            RefreshRail();
        }

        // --- order binding ---

        private bool _orderHooked;

        private void HookOrder()
        {
            var order = CraftingManager.Instance != null ? CraftingManager.Instance.CurrentOrder : null;
            if (order == null || _orderHooked) return;
            order.OnQualityChanged += OnQuality;
            _orderHooked = true;
        }

        private void UnhookOrder()
        {
            var order = CraftingManager.Instance != null ? CraftingManager.Instance.CurrentOrder : null;
            if (order != null) order.OnQualityChanged -= OnQuality;
            _orderHooked = false;
        }

        private void OnQuality(int q) => RefreshDock();

        private void RefreshDock()
        {
            // Self-healing: an order created any way other than through the Counter
            // (the headless driver, a load) would otherwise leave the dock stale,
            // because nothing had subscribed to OnQualityChanged yet. HookOrder is
            // idempotent, so calling it here costs nothing.
            HookOrder();

            ActiveOrder order = CraftingManager.Instance != null ? CraftingManager.Instance.CurrentOrder : null;
            ContractRecord job = SaveSystem.Instance != null && SaveSystem.Instance.State != null
                ? SaveSystem.Instance.State.contract : null;
            bool has = order != null;

            _sendBtn.interactable = has;

            if (job != null && job.accepted)
            {
                CustomerDefinition buyer = CustomerCatalog.ById(job.buyerId);
                _buyerPortrait.sprite = PixelSprites.Buyer(buyer.PortraitId);
                _buyerName.text = $"{job.buyerName}\n<size=80%><color=#{ColorUtility.ToHtmlStringRGB(UITheme.TextLow)}>{buyer.Title}</color></size>";
                _jobTitle.text = $"{job.title} — <b>{job.PotionName}</b>";
                _jobTerms.text =
                    $"Wants <color=#{ColorUtility.ToHtmlStringRGB(UITheme.GradeColor(job.RequiredGrade))}>" +
                    $"{job.RequiredGrade.ToString().ToUpperInvariant()}</color> or better.\n" +
                    $"Pays {job.fee} g, +{job.bonus} g if it lands. Below grade pays half.";
            }
            else
            {
                _buyerName.text = "Nobody served yet";
                _jobTitle.text = "";
                _jobTerms.text = "Step up to the Counter and take one of the three jobs on the board.";
            }

            if (!has)
            {
                _quality.Set(0f, "—");
                _gradeText.text = "No potion on the bench.";
                _logText.text = "";
                _sendHint.text = "Take a job first.";
                return;
            }

            var grade = order.GetGrade();
            _quality.Set(order.qualityScore / 100f, $"{order.qualityScore} / 100");
            _quality.SetFillColor(UITheme.GradeColor(grade));
            _gradeText.text =
                $"<color=#{ColorUtility.ToHtmlStringRGB(UITheme.GradeColor(grade))}>{grade.ToString().ToUpperInvariant()}</color>" +
                (job != null && job.accepted
                    ? job.Meets(grade) ? "  — meets the contract" : "  — below what they asked for"
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

            var pot = PhysicsCauldronManager.Instance;
            bool brewed = pot != null && pot.IsBrewComplete;
            bool bottled = _stations[(int)StationTab.Bottling].Complete;
            _sendHint.text = bottled ? "Sealed and labelled — good to go."
                : brewed ? "Brewed. Bottle it before you send it."
                : "You can send it early — it just won't be as good.";
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
        }
    }
}
