using System.Text;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using AlchemistsArsenal.Core;
using AlchemistsArsenal.Combat;
using AlchemistsArsenal.Crafting;
using AlchemistsArsenal.Data;
using AlchemistsArsenal.Systems;
using AlchemistsArsenal.Audio;

namespace AlchemistsArsenal.UI
{
    /// <summary>
    /// The morning shell (DESIGN.md §7.6). Top bar + a four-tab rail (Counter,
    /// Cauldron, Prep, Bottling), the active station panel, and the right order dock
    /// with the live <c>QualityMeter</c> + deduction log. All four stations are
    /// functional from day 1 — Cauldron/Prep/Bottling unlock together the moment the
    /// Counter order is accepted (<see cref="TutorialManager.StationsUnlocked"/>);
    /// there is no separate multi-day unlock gate (playtest: that gate previously had
    /// no unlock path at all, so Prep/Bottling were permanently locked).
    /// </summary>
    public class MorningScreen : GameScreen
    {
        // Named StationTab, not Tab — this class also has a Tab(...) rail-button builder method.
        private enum StationTab { Counter, Cauldron, Prep, Bottling }

        private TextMeshProUGUI _dayText, _goldText, _statusText, _qualityText, _logText;
        private Image _clockFill, _heatFill, _heatBand, _qualityFill, _brewFill, _bg;
        private RectTransform _counterPanel, _cauldronPanel, _prepPanel, _bottlingPanel;
        private Button _counterTab, _cauldronTab, _prepTab, _bottlingTab, _sendBtn;
        private ElementType _chosenElement = ElementType.Fire;
        private StationTab _activeTab = StationTab.Counter;

        // Prep — pick herbs matching the order's element for a quality bonus.
        private TextMeshProUGUI _prepHint;
        private readonly System.Collections.Generic.List<Button> _herbButtons = new System.Collections.Generic.List<Button>();
        private int _prepPicksLeft;
        private const int PrepPicksPerDay = 2;

        // Bottling — seal the flask while a needle sits in the sweet band.
        private TextMeshProUGUI _bottlingHint;
        private Image _sealBand, _sealNeedle;
        private Button _sealBtn;
        private int _sealAttemptsLeft;
        private const int SealAttemptsPerDay = 3;

        protected override void Build()
        {
            // Opaque backdrop for the Counter tab; hidden on the Cauldron tab so the
            // world cauldron (rendered by ShopCamera behind this overlay) shows through.
            _bg = UIFactory.Box(transform, UITheme.Ink900, Rt);

            // top bar
            var bar = UIFactory.Panel(transform, UITheme.Ink800, "TopBar");
            bar.rectTransform.anchorMin = new Vector2(0f, 0.93f);
            bar.rectTransform.anchorMax = new Vector2(1f, 1f);
            bar.rectTransform.offsetMin = bar.rectTransform.offsetMax = Vector2.zero;
            _dayText = UIFactory.Label(bar.transform, "DAY 1", 20, UITheme.Candle, TextAlignmentOptions.Left, true);
            _dayText.rectTransform.anchorMin = new Vector2(0.02f, 0f); _dayText.rectTransform.anchorMax = new Vector2(0.16f, 1f);
            _dayText.rectTransform.offsetMin = _dayText.rectTransform.offsetMax = Vector2.zero;
            _goldText = UIFactory.Label(bar.transform, "0 g", 20, UITheme.Parchment, TextAlignmentOptions.Left);
            _goldText.rectTransform.anchorMin = new Vector2(0.17f, 0f); _goldText.rectTransform.anchorMax = new Vector2(0.28f, 1f);
            _goldText.rectTransform.offsetMin = _goldText.rectTransform.offsetMax = Vector2.zero;
            var clockBg = UIFactory.Bar(bar.transform, UITheme.Ink700, UITheme.Candle, out _clockFill);
            clockBg.rectTransform.anchorMin = new Vector2(0.3f, 0.35f); clockBg.rectTransform.anchorMax = new Vector2(0.55f, 0.65f);
            clockBg.rectTransform.offsetMin = clockBg.rectTransform.offsetMax = Vector2.zero;
            var biomeChip = UIFactory.Label(bar.transform, "", 16, UITheme.ParchmentDim, TextAlignmentOptions.Left);
            biomeChip.rectTransform.anchorMin = new Vector2(0.58f, 0f); biomeChip.rectTransform.anchorMax = new Vector2(0.85f, 1f);
            biomeChip.rectTransform.offsetMin = biomeChip.rectTransform.offsetMax = Vector2.zero;
            _biomeChip = biomeChip;
            var cog = UIFactory.Button(bar.transform, "MENU", () => UIManager.Instance.Show(ScreenId.Settings), primary: false);
            cog.image.rectTransform.anchorMin = new Vector2(0.92f, 0.15f); cog.image.rectTransform.anchorMax = new Vector2(0.99f, 0.85f);
            cog.image.rectTransform.offsetMin = cog.image.rectTransform.offsetMax = Vector2.zero;

            // station rail
            var rail = UIFactory.Panel(transform, UITheme.Ink800, "Rail");
            rail.rectTransform.anchorMin = new Vector2(0f, 0f); rail.rectTransform.anchorMax = new Vector2(0.09f, 0.93f);
            rail.rectTransform.offsetMin = rail.rectTransform.offsetMax = Vector2.zero;
            var railStack = UIFactory.VStack(rail.transform, 8f, new RectOffset(8, 8, 12, 12));
            UIFactory.Stretch((RectTransform)railStack.transform);
            _counterTab = Tab(railStack.transform, "COUNTER", () => SwitchTab(StationTab.Counter));
            _cauldronTab = Tab(railStack.transform, "CAULDRON", () => SwitchTab(StationTab.Cauldron));
            _prepTab = Tab(railStack.transform, "PREP", () => SwitchTab(StationTab.Prep));
            _bottlingTab = Tab(railStack.transform, "BOTTLING", () => SwitchTab(StationTab.Bottling));

            // centre panels
            _counterPanel = BuildCounterPanel();
            _cauldronPanel = BuildCauldronPanel();
            _prepPanel = BuildPrepPanel();
            _bottlingPanel = BuildBottlingPanel();

            // order dock
            BuildOrderDock();

            SwitchTab(StationTab.Counter);
        }

        private TextMeshProUGUI _biomeChip, _forecastText, _recText, _ticketName;
        private Image _ticketBadge;

        private RectTransform BuildCounterPanel()
        {
            var p = UIFactory.Panel(transform, UITheme.Ink800, "CounterPanel");
            p.rectTransform.anchorMin = new Vector2(0.09f, 0f); p.rectTransform.anchorMax = new Vector2(0.72f, 0.93f);
            p.rectTransform.offsetMin = new Vector2(16, 16); p.rectTransform.offsetMax = new Vector2(-16, -16);

            UIFactory.TopLabel(p.transform, "COUNTER — read the afternoon, pick what to brew", 16, UITheme.Candle);

            _forecastText = UIFactory.Label(p.transform, "", 17, UITheme.Parchment, TextAlignmentOptions.TopLeft);
            var frt = _forecastText.rectTransform;
            frt.anchorMin = new Vector2(0f, 0.35f); frt.anchorMax = new Vector2(1f, 0.86f);
            frt.offsetMin = new Vector2(18, 0); frt.offsetMax = new Vector2(-18, 0);

            _recText = UIFactory.Label(p.transform, "", 18, UITheme.CandleHot, TextAlignmentOptions.TopLeft);
            var rrt = _recText.rectTransform;
            rrt.anchorMin = new Vector2(0f, 0.2f); rrt.anchorMax = new Vector2(1f, 0.34f);
            rrt.offsetMin = new Vector2(18, 0); rrt.offsetMax = new Vector2(-18, 0);

            _acceptBtn = UIFactory.Button(p.transform, "ACCEPT ORDER", AcceptOrder);
            var art = _acceptBtn.image.rectTransform;
            art.anchorMin = new Vector2(0.28f, 0.05f); art.anchorMax = new Vector2(0.72f, 0.16f);
            art.offsetMin = art.offsetMax = Vector2.zero;
            return p.rectTransform;
        }

        private Button _acceptBtn;

        private RectTransform BuildCauldronPanel()
        {
            // Transparent centre so the world cauldron behind the overlay shows through
            // and mouse-stirring reaches PhysicsCauldronManager (DESIGN.md §7.6.3 / §11).
            var p = UIFactory.Root(transform, "CauldronPanel");
            p.anchorMin = new Vector2(0.09f, 0f); p.anchorMax = new Vector2(0.72f, 0.93f);
            p.offsetMin = new Vector2(16, 16); p.offsetMax = new Vector2(-16, -16);

            UIFactory.TopLabel(p, "CAULDRON — mouse over the pot and stir in circles, hold the green", 16, UITheme.Candle, padX: 4f);

            // brew-progress bar (fills while stirring in the green — the minigame's end)
            var brewBg = UIFactory.Bar(p, UITheme.Ink700, UITheme.Candle, out _brewFill);
            var wrt = brewBg.rectTransform;
            wrt.anchorMin = new Vector2(0.1f, 0.16f); wrt.anchorMax = new Vector2(0.9f, 0.2f);
            wrt.offsetMin = wrt.offsetMax = Vector2.zero;
            _brewFill.fillAmount = 0f;
            UIFactory.Label(brewBg.transform, "BREW", 12, UITheme.Ink900, TextAlignmentOptions.Left, true)
                .rectTransform.offsetMin = new Vector2(6, 0);

            var gaugeBg = UIFactory.Bar(p, UITheme.Ink700, UITheme.Ok, out _heatFill);
            var grt = gaugeBg.rectTransform;
            grt.anchorMin = new Vector2(0.1f, 0.08f); grt.anchorMax = new Vector2(0.9f, 0.14f);
            grt.offsetMin = grt.offsetMax = Vector2.zero;
            _heatBand = UIFactory.Panel(gaugeBg.transform, new Color(UITheme.Ok.r, UITheme.Ok.g, UITheme.Ok.b, 0.35f), "Band");
            var brt = _heatBand.rectTransform;
            brt.anchorMin = new Vector2(0.4f, 0f); brt.anchorMax = new Vector2(0.7f, 1f);
            brt.offsetMin = brt.offsetMax = Vector2.zero;

            _statusText = UIFactory.Label(p, "ACCEPT AN ORDER FIRST", 20, UITheme.ParchmentDim, TextAlignmentOptions.Center, true);
            var srt = _statusText.rectTransform;
            srt.anchorMin = new Vector2(0.1f, 0.24f); srt.anchorMax = new Vector2(0.9f, 0.32f);
            srt.offsetMin = srt.offsetMax = Vector2.zero;
            return p;
        }

        private RectTransform BuildPrepPanel()
        {
            var p = UIFactory.Panel(transform, UITheme.Ink800, "PrepPanel");
            p.rectTransform.anchorMin = new Vector2(0.09f, 0f); p.rectTransform.anchorMax = new Vector2(0.72f, 0.93f);
            p.rectTransform.offsetMin = new Vector2(16, 16); p.rectTransform.offsetMax = new Vector2(-16, -16);

            UIFactory.TopLabel(p.transform, $"PREP — add matching-element herbs for a bonus ({PrepPicksPerDay} per day)", 16, UITheme.Candle);

            _prepHint = UIFactory.Label(p.transform, "", 16, UITheme.ParchmentDim, TextAlignmentOptions.TopLeft);
            var hrt = _prepHint.rectTransform;
            hrt.anchorMin = new Vector2(0f, 0.68f); hrt.anchorMax = new Vector2(1f, 0.82f);
            hrt.offsetMin = new Vector2(18, 0); hrt.offsetMax = new Vector2(-18, 0);

            var row = UIFactory.HStack(p.transform, 14f, new RectOffset(24, 24, 0, 0));
            var rrt = (RectTransform)row.transform;
            rrt.anchorMin = new Vector2(0f, 0.32f); rrt.anchorMax = new Vector2(1f, 0.64f);
            rrt.offsetMin = rrt.offsetMax = Vector2.zero;

            _herbButtons.Clear();
            foreach (ElementType e in new[]
                     { ElementType.Nature, ElementType.Fire, ElementType.Water, ElementType.Poison, ElementType.Arcane })
            {
                ElementType element = e; // capture per-iteration value
                var b = UIFactory.Button(row.transform, element.ToString().ToUpper(), () => PickHerb(element), primary: false);
                b.image.color = UITheme.Element(element);
                b.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1;
                _herbButtons.Add(b);
            }
            return p.rectTransform;
        }

        private RectTransform BuildBottlingPanel()
        {
            var p = UIFactory.Panel(transform, UITheme.Ink800, "BottlingPanel");
            p.rectTransform.anchorMin = new Vector2(0.09f, 0f); p.rectTransform.anchorMax = new Vector2(0.72f, 0.93f);
            p.rectTransform.offsetMin = new Vector2(16, 16); p.rectTransform.offsetMax = new Vector2(-16, -16);

            UIFactory.TopLabel(p.transform, $"BOTTLING — seal it while the needle is in the band ({SealAttemptsPerDay} per day)", 16, UITheme.Candle);

            _bottlingHint = UIFactory.Label(p.transform, "", 18, UITheme.ParchmentDim, TextAlignmentOptions.Center, true);
            var brt = _bottlingHint.rectTransform;
            brt.anchorMin = new Vector2(0.1f, 0.62f); brt.anchorMax = new Vector2(0.9f, 0.72f);
            brt.offsetMin = brt.offsetMax = Vector2.zero;

            var gaugeBg = UIFactory.Panel(p.transform, UITheme.Ink700, "SealGauge");
            var grt = gaugeBg.rectTransform;
            grt.anchorMin = new Vector2(0.1f, 0.46f); grt.anchorMax = new Vector2(0.9f, 0.56f);
            grt.offsetMin = grt.offsetMax = Vector2.zero;

            _sealBand = UIFactory.Panel(gaugeBg.transform, new Color(UITheme.Ok.r, UITheme.Ok.g, UITheme.Ok.b, 0.45f), "Band");
            _sealBand.rectTransform.anchorMin = new Vector2(0.42f, 0f); _sealBand.rectTransform.anchorMax = new Vector2(0.58f, 1f);
            _sealBand.rectTransform.offsetMin = _sealBand.rectTransform.offsetMax = Vector2.zero;

            _sealNeedle = UIFactory.Panel(gaugeBg.transform, UITheme.Candle, "Needle");
            _sealNeedle.rectTransform.anchorMin = _sealNeedle.rectTransform.anchorMax = new Vector2(0f, 0.5f);
            _sealNeedle.rectTransform.sizeDelta = new Vector2(6, 44);

            _sealBtn = UIFactory.Button(p.transform, "SEAL", Seal);
            var sart = _sealBtn.image.rectTransform;
            sart.anchorMin = new Vector2(0.38f, 0.2f); sart.anchorMax = new Vector2(0.62f, 0.32f);
            sart.offsetMin = sart.offsetMax = Vector2.zero;
            return p.rectTransform;
        }

        private void BuildOrderDock()
        {
            var dock = UIFactory.FramedPanel(transform, "OrderDock");
            dock.rectTransform.anchorMin = new Vector2(0.72f, 0f); dock.rectTransform.anchorMax = new Vector2(1f, 0.93f);
            dock.rectTransform.offsetMin = dock.rectTransform.offsetMax = Vector2.zero;

            var ticket = UIFactory.Panel(dock.transform, UITheme.Parchment, "Ticket");
            ticket.rectTransform.anchorMin = new Vector2(0.06f, 0.72f); ticket.rectTransform.anchorMax = new Vector2(0.94f, 0.96f);
            ticket.rectTransform.offsetMin = ticket.rectTransform.offsetMax = Vector2.zero;
            _ticketName = UIFactory.Label(ticket.transform, "No order yet", 18, UITheme.Ink900, TextAlignmentOptions.TopLeft, true);
            UIFactory.Stretch(_ticketName.rectTransform, 10f);
            _ticketBadge = UIFactory.ElementBadge(ticket.transform, ElementType.Fire, 22);
            _ticketBadge.rectTransform.anchorMin = new Vector2(1f, 1f); _ticketBadge.rectTransform.anchorMax = new Vector2(1f, 1f);
            _ticketBadge.rectTransform.anchoredPosition = new Vector2(-18, -18);
            _ticketBadge.enabled = false;

            var qBg = UIFactory.Bar(dock.transform, UITheme.Ink700, UITheme.Candle, out _qualityFill);
            qBg.rectTransform.anchorMin = new Vector2(0.06f, 0.64f); qBg.rectTransform.anchorMax = new Vector2(0.94f, 0.69f);
            qBg.rectTransform.offsetMin = qBg.rectTransform.offsetMax = Vector2.zero;
            _qualityFill.fillAmount = ActiveOrder.StartingQuality / 100f;
            _qualityText = UIFactory.Label(dock.transform, "QUALITY —", 16, UITheme.Candle, TextAlignmentOptions.Left, true);
            _qualityText.rectTransform.anchorMin = new Vector2(0.06f, 0.58f); _qualityText.rectTransform.anchorMax = new Vector2(0.94f, 0.63f);
            _qualityText.rectTransform.offsetMin = _qualityText.rectTransform.offsetMax = Vector2.zero;

            var logBg = UIFactory.Panel(dock.transform, UITheme.Ink900, "Log");
            logBg.rectTransform.anchorMin = new Vector2(0.06f, 0.16f); logBg.rectTransform.anchorMax = new Vector2(0.94f, 0.56f);
            logBg.rectTransform.offsetMin = logBg.rectTransform.offsetMax = Vector2.zero;
            _logText = UIFactory.Label(logBg.transform, "", 14, UITheme.Parchment, TextAlignmentOptions.TopLeft);
            UIFactory.Stretch(_logText.rectTransform, 8f);

            _sendBtn = UIFactory.Button(dock.transform, "SEND TO EXPEDITION", () => GameLoopManager.Instance.BeginHandoff());
            _sendBtn.image.rectTransform.anchorMin = new Vector2(0.06f, 0.04f);
            _sendBtn.image.rectTransform.anchorMax = new Vector2(0.94f, 0.13f);
            _sendBtn.image.rectTransform.offsetMin = _sendBtn.image.rectTransform.offsetMax = Vector2.zero;
        }

        private Button Tab(Transform parent, string text, System.Action onClick)
        {
            var b = UIFactory.Button(parent, text, onClick, primary: false);
            b.gameObject.AddComponent<LayoutElement>().minHeight = 76;
            return b;
        }

        // ------------------------------------------------------------- behaviour

        protected override void OnShow()
        {
            var s = SaveSystem.Instance.State;
            _dayText.text = $"DAY {s.day}";
            _goldText.text = $"{s.gold} g";
            int biome = s.TargetBiomeIndex;
            _biomeChip.text = $"{BiomeLibrary.Name(biome)} — today's run";

            BuildForecast(biome);

            // The adventurer "speaks" their request in Animalese (cat 7).
            if (CraftingManager.Instance == null || CraftingManager.Instance.CurrentOrder == null)
                AudioManager.Speak($"bru nu {_chosenElement} fla she wi tch", 1.15f);

            if (GameLoopManager.Instance != null)
                GameLoopManager.Instance.OnMorningTimeChanged += SetClock;
            HookOrder();
            RefreshOrder();
            SwitchTab(StationTab.Counter); // fresh day starts back at the Counter
        }

        protected override void OnHide()
        {
            if (GameLoopManager.Instance != null)
                GameLoopManager.Instance.OnMorningTimeChanged -= SetClock;
            if (PhysicsCauldronManager.Instance != null)
                PhysicsCauldronManager.Instance.OnHeatChanged -= SetHeat;
            UnhookOrder();
        }

        private void SwitchTab(StationTab tab)
        {
            // Day-1 gate, until the order is accepted. TutorialManager.StationsUnlocked
            // flips on ITS OWN coroutine once it notices the order, which is not
            // guaranteed to have happened yet in the very same frame AcceptOrder()
            // creates that order and immediately tries to switch here — checking
            // CurrentOrder directly as a fallback closes that race (confirmed via a
            // headless-playtest screenshot: ACCEPT ORDER silently failed to switch to
            // the Cauldron tab despite creating the order correctly).
            bool hasOrder = CraftingManager.Instance != null && CraftingManager.Instance.CurrentOrder != null;
            if (tab != StationTab.Counter && !TutorialManager.StationsUnlocked && !hasOrder) return;

            _activeTab = tab;
            _counterPanel.gameObject.SetActive(tab == StationTab.Counter);
            _cauldronPanel.gameObject.SetActive(tab == StationTab.Cauldron);
            _prepPanel.gameObject.SetActive(tab == StationTab.Prep);
            _bottlingPanel.gameObject.SetActive(tab == StationTab.Bottling);
            if (_bg != null) _bg.enabled = tab != StationTab.Cauldron; // let the world pot show only on the Cauldron tab

            Tint(_counterTab, tab == StationTab.Counter);
            Tint(_cauldronTab, tab == StationTab.Cauldron);
            Tint(_prepTab, tab == StationTab.Prep);
            Tint(_bottlingTab, tab == StationTab.Bottling);
            bool unlocked = TutorialManager.StationsUnlocked;
            if (_cauldronTab != null) _cauldronTab.interactable = unlocked;
            if (_prepTab != null) _prepTab.interactable = unlocked;
            if (_bottlingTab != null) _bottlingTab.interactable = unlocked;

            if (tab == StationTab.Prep) RefreshPrep();
            if (tab == StationTab.Bottling) RefreshBottling();
            AudioManager.Play(Sfx.Tab);
        }

        private static void Tint(Button b, bool active)
        {
            if (b != null) b.image.color = active ? UITheme.Candle : UITheme.Ink700;
        }

        private void BuildForecast(int biome)
        {
            BiomeData data = BiomeLibrary.Get(biome);
            var counts = new int[5];
            var sb = new StringBuilder("INCOMING WAVES\n\n");
            foreach (var w in data.Waves)
            {
                sb.AppendLine($"  ×{w.count}  {w.monster.DisplayName}  ({w.monster.Element})");
                counts[(int)w.monster.Element] += w.count;
            }
            int total = 0; foreach (int c in counts) total += c;
            int dominant = 0; for (int i = 1; i < counts.Length; i++) if (counts[i] > counts[dominant]) dominant = i;
            _forecastText.text = sb.ToString();

            var domElem = (ElementType)dominant;
            _chosenElement = CounterElement(domElem);
            _recText.text = total > 0
                ? $"Mostly {domElem} out there — brew {_chosenElement} for the ×2 matchup."
                : "Quiet day. Brew whatever you like.";
            if (_acceptBtn != null)
            {
                var lbl = _acceptBtn.GetComponentInChildren<TextMeshProUGUI>();
                if (lbl != null) lbl.text = $"ACCEPT ORDER — BREW {_chosenElement.ToString().ToUpper()}";
            }
        }

        private static ElementType CounterElement(ElementType attacker) => attacker switch
        {
            ElementType.Fire => ElementType.Water,
            ElementType.Nature => ElementType.Fire,
            ElementType.Water => ElementType.Nature,
            ElementType.Poison => ElementType.Arcane,
            _ => ElementType.Fire,
        };

        private void AcceptOrder()
        {
            GameLoopManager.Instance.ConfirmOrder($"{_chosenElement} Flask", _chosenElement);
            AudioManager.Play(Sfx.Confirm);
            _prepPicksLeft = PrepPicksPerDay;
            _sealAttemptsLeft = SealAttemptsPerDay;
            HookOrder();
            RefreshOrder();
            SwitchTab(StationTab.Cauldron);
        }

        // --- order + heat binding ---

        private bool _orderHooked;

        private void HookOrder()
        {
            var order = CraftingManager.Instance != null ? CraftingManager.Instance.CurrentOrder : null;
            if (order == null || _orderHooked) return;
            order.OnQualityChanged += OnQuality;
            _orderHooked = true;
            if (PhysicsCauldronManager.Instance != null)
                PhysicsCauldronManager.Instance.OnHeatChanged += SetHeat;
        }

        private void UnhookOrder()
        {
            var order = CraftingManager.Instance != null ? CraftingManager.Instance.CurrentOrder : null;
            if (order != null) order.OnQualityChanged -= OnQuality;
            _orderHooked = false;
        }

        private void OnQuality(int q) => RefreshOrder();

        private void RefreshOrder()
        {
            // Self-healing: HookOrder() previously only ran from OnShow (when an
            // order may not exist yet) and AcceptOrder (the ACCEPT button's own
            // handler) — an order created any other way (verified via the headless
            // playtest's driver, which calls GameLoopManager.ConfirmOrder directly)
            // left the ticket panel permanently stale since nothing had subscribed
            // to OnQualityChanged yet. HookOrder() is idempotent, so calling it here
            // too costs nothing and makes this screen correct regardless of how the
            // order came to exist.
            HookOrder();

            var order = CraftingManager.Instance != null ? CraftingManager.Instance.CurrentOrder : null;
            bool has = order != null;
            _sendBtn.interactable = has;
            _ticketBadge.enabled = has;
            if (!has)
            {
                _ticketName.text = "No order yet — accept one at the Counter";
                _qualityText.text = "QUALITY —";
                _statusText.text = "ACCEPT AN ORDER FIRST";
                return;
            }
            _ticketName.text = $"{order.potionName}\n<size=70%>{order.element}</size>";
            _ticketBadge.sprite = Art.PixelSprites.ElementIcon(order.element);
            _qualityFill.fillAmount = order.qualityScore / 100f;
            var grade = order.GetGrade();
            _qualityText.text = $"QUALITY {order.qualityScore} — <color=#{ColorUtility.ToHtmlStringRGB(UITheme.GradeColor(grade))}>{grade.ToString().ToUpper()}</color>";

            var sb = new StringBuilder("DEDUCTION LOG\n");
            int start = Mathf.Max(0, order.deductions.Count - 8);
            for (int i = start; i < order.deductions.Count; i++)
            {
                var d = order.deductions[i];
                string col = d.pointsDelta >= 0 ? "#4fae5a" : "#d64550";
                sb.AppendLine($"<color={col}>{(d.pointsDelta >= 0 ? "+" : "")}{d.pointsDelta}</color> {d.station} — {d.reason}");
            }
            _logText.text = sb.ToString();
        }

        private void SetClock(float t01) => _clockFill.fillAmount = t01;

        private void SetHeat(float heat01)
        {
            if (PhysicsCauldronManager.Instance == null) return;
            _heatFill.fillAmount = heat01;
            float lo = PhysicsCauldronManager.Instance.MinOptimalHeat;
            float hi = PhysicsCauldronManager.Instance.MaxOptimalHeat;
            _heatBand.rectTransform.anchorMin = new Vector2(lo, 0f);
            _heatBand.rectTransform.anchorMax = new Vector2(hi, 1f);

            if (CraftingManager.Instance == null || CraftingManager.Instance.CurrentOrder == null) return;

            var pot = PhysicsCauldronManager.Instance;
            if (_brewFill != null) _brewFill.fillAmount = pot.BrewProgress01;

            if (pot.IsBrewComplete)
            {
                _statusText.text = "BREW READY — SEND IT OFF";
                _statusText.color = UITheme.Candle; _heatFill.color = UITheme.Candle;
            }
            else if (!pot.MouseOverCauldron)
            {
                _statusText.text = "MOVE THE MOUSE OVER THE POT";
                _statusText.color = UITheme.ParchmentDim; _heatFill.color = UITheme.ParchmentDim;
            }
            else if (heat01 < lo) { _statusText.text = "TOO COLD — STIR FASTER"; _statusText.color = UITheme.Water; _heatFill.color = UITheme.Water; }
            else if (heat01 > hi) { _statusText.text = "OVERHEATING — EASE OFF"; _statusText.color = UITheme.Danger; _heatFill.color = UITheme.Danger; }
            else { _statusText.text = "BREWING PERFECTLY — QUALITY CLIMBING"; _statusText.color = UITheme.Ok; _heatFill.color = UITheme.Ok; }
        }

        // ------------------------------------------------------------------ Prep

        private void PickHerb(ElementType element)
        {
            var order = CraftingManager.Instance != null ? CraftingManager.Instance.CurrentOrder : null;
            if (order == null || _prepPicksLeft <= 0) return;

            bool match = element == order.element;
            if (match) order.ApplyBonus(10, "Prep", $"Added {element} essence — matches the order");
            else order.ApplyDeduction(6, "Prep", $"Added {element} essence — wrong element for {order.element}");

            _prepPicksLeft--;
            AudioManager.Play(match ? Sfx.Confirm : Sfx.Deny);
            RefreshPrep();
            RefreshOrder();
        }

        private void RefreshPrep()
        {
            if (_prepHint == null) return;
            var order = CraftingManager.Instance != null ? CraftingManager.Instance.CurrentOrder : null;
            if (order == null)
            {
                _prepHint.text = "Accept an order at the Counter first.";
                SetHerbButtonsInteractable(false);
                return;
            }
            _prepHint.text = _prepPicksLeft > 0
                ? $"{_prepPicksLeft} herb(s) left today — match the order's element ({order.element}) for a bonus."
                : "No herbs left today.";
            SetHerbButtonsInteractable(_prepPicksLeft > 0);
        }

        private void SetHerbButtonsInteractable(bool on)
        {
            foreach (var b in _herbButtons) if (b != null) b.interactable = on;
        }

        // -------------------------------------------------------------- Bottling

        private void Seal()
        {
            var order = CraftingManager.Instance != null ? CraftingManager.Instance.CurrentOrder : null;
            if (order == null || _sealAttemptsLeft <= 0) return;

            float needle01 = SealNeedle01();
            float dist = Mathf.Abs(needle01 - 0.5f); // 0 = dead centre of the 0.42-0.58 band
            bool inBand = dist <= 0.08f;

            _sealAttemptsLeft--;
            if (inBand)
            {
                int bonus = Mathf.RoundToInt(Mathf.Lerp(14f, 6f, dist / 0.08f));
                order.ApplyBonus(bonus, "Bottling", $"Sealed clean (+{bonus})");
                AudioManager.Play(Sfx.Seal);
            }
            else
            {
                order.ApplyDeduction(8, "Bottling", "Sealed off-centre");
                AudioManager.Play(Sfx.Deny);
            }
            RefreshBottling();
            RefreshOrder();
        }

        private void RefreshBottling()
        {
            if (_bottlingHint == null) return;
            var order = CraftingManager.Instance != null ? CraftingManager.Instance.CurrentOrder : null;
            if (order == null)
            {
                _bottlingHint.text = "Accept an order at the Counter first.";
                if (_sealBtn != null) _sealBtn.interactable = false;
                return;
            }
            _bottlingHint.text = _sealAttemptsLeft > 0 ? $"{_sealAttemptsLeft} seal(s) left today" : "No seals left today.";
            if (_sealBtn != null) _sealBtn.interactable = _sealAttemptsLeft > 0;
        }

        private static float SealNeedle01() => Mathf.PingPong(Time.unscaledTime * 0.6f, 1f);

        private void Update()
        {
            if (_activeTab != StationTab.Bottling || _sealNeedle == null) return;
            float t = SealNeedle01();
            _sealNeedle.rectTransform.anchorMin = new Vector2(t, 0.5f);
            _sealNeedle.rectTransform.anchorMax = new Vector2(t, 0.5f);
        }
    }
}
