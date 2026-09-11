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
    /// The morning shell (DESIGN.md §7.6). Phase 0: top bar + a two-tab rail
    /// (Counter, Cauldron), the active station panel, and the right order dock with
    /// the live <c>QualityMeter</c> + deduction log. Prep / Bottling are shown
    /// locked (Day-2 / Day-3 unlocks — §7.6 W-R2a).
    /// </summary>
    public class MorningScreen : GameScreen
    {
        private TextMeshProUGUI _dayText, _goldText, _statusText, _qualityText, _logText;
        private Image _clockFill, _heatFill, _heatBand, _qualityFill, _brewFill, _bg;
        private RectTransform _counterPanel, _cauldronPanel;
        private Button _counterTab, _cauldronTab, _sendBtn;
        private ElementType _chosenElement = ElementType.Fire;
        private bool _cauldron;

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
            _counterTab = Tab(railStack.transform, "COUNTER", () => SwitchTab(false));
            _cauldronTab = Tab(railStack.transform, "CAULDRON", () => SwitchTab(true));
            LockedTab(railStack.transform, "PREP");
            LockedTab(railStack.transform, "BOTTLING");

            // centre panels
            _counterPanel = BuildCounterPanel();
            _cauldronPanel = BuildCauldronPanel();

            // order dock
            BuildOrderDock();

            SwitchTab(false);
        }

        private TextMeshProUGUI _biomeChip, _forecastText, _recText, _ticketName;
        private Image _ticketBadge;

        private RectTransform BuildCounterPanel()
        {
            var p = UIFactory.Panel(transform, UITheme.Ink800, "CounterPanel");
            p.rectTransform.anchorMin = new Vector2(0.09f, 0f); p.rectTransform.anchorMax = new Vector2(0.72f, 0.93f);
            p.rectTransform.offsetMin = new Vector2(16, 16); p.rectTransform.offsetMax = new Vector2(-16, -16);

            UIFactory.Label(p.transform, "COUNTER — read the afternoon, pick what to brew", 16, UITheme.Candle,
                TextAlignmentOptions.TopLeft).rectTransform.offsetMin = new Vector2(16, -44);

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

            UIFactory.Label(p, "CAULDRON — mouse over the pot and stir in circles, hold the green", 16, UITheme.Candle,
                TextAlignmentOptions.TopLeft).rectTransform.offsetMin = new Vector2(4, -44);

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

        private void LockedTab(Transform parent, string text)
        {
            var b = UIFactory.Button(parent, text + "\n(locked)", null, primary: false);
            b.interactable = false;
            b.gameObject.AddComponent<LayoutElement>().minHeight = 76;
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
        }

        protected override void OnHide()
        {
            if (GameLoopManager.Instance != null)
                GameLoopManager.Instance.OnMorningTimeChanged -= SetClock;
            if (PhysicsCauldronManager.Instance != null)
                PhysicsCauldronManager.Instance.OnHeatChanged -= SetHeat;
            UnhookOrder();
        }

        private void SwitchTab(bool cauldron)
        {
            if (cauldron && !TutorialManager.CauldronUnlocked) return; // Day-1 gate
            _cauldron = cauldron;
            _counterPanel.gameObject.SetActive(!cauldron);
            _cauldronPanel.gameObject.SetActive(cauldron);
            if (_bg != null) _bg.enabled = !cauldron; // let the world pot show on the Cauldron tab
            Tint(_counterTab, !cauldron);
            Tint(_cauldronTab, cauldron);
            if (_cauldronTab != null) _cauldronTab.interactable = TutorialManager.CauldronUnlocked;
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
            HookOrder();
            RefreshOrder();
            SwitchTab(true);
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
    }
}
