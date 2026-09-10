using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using AlchemistsArsenal.Core;
using AlchemistsArsenal.Combat;

namespace AlchemistsArsenal.UI
{
    /// <summary>
    /// Afternoon expedition HUD (DESIGN.md §7.8). Replaces the old
    /// <c>ExpeditionBootstrap.OnGUI</c>. Wave/boss banner, party dock, always-on
    /// AI ticker (<c>[floor]</c> — cat 8 visible), speed control via
    /// <see cref="TimeControl"/>, result slab.
    /// </summary>
    public class ExpeditionHudScreen : GameScreen
    {
        private TextMeshProUGUI _banner, _ticker, _slab;
        private Image _bossFill; private RectTransform _pips;
        private RectTransform _dock;
        private Button _speed1, _speed2, _pause, _continueBtn;
        private RectTransform _slabPanel;

        private ExpeditionWorld _world;
        private readonly List<UtilityAI_CombatController> _ai = new List<UtilityAI_CombatController>();
        private TimeControl.Handle _pauseHandle;
        private TimeControl.Handle _speedHandle;
        private bool _paused, _fast;

        protected override void Build()
        {
            // no full-screen box — the arena camera renders behind the overlay
            var top = UIFactory.Panel(transform, UITheme.Ink800, "Banner");
            top.rectTransform.anchorMin = new Vector2(0.32f, 0.88f); top.rectTransform.anchorMax = new Vector2(0.68f, 0.99f);
            top.rectTransform.offsetMin = top.rectTransform.offsetMax = Vector2.zero;
            _banner = UIFactory.Label(top.transform, "", 18, UITheme.Candle, TextAlignmentOptions.Center, true);
            UIFactory.Stretch(_banner.rectTransform, 6f);

            var bossBg = UIFactory.Bar(transform, UITheme.Ink700, UITheme.Danger, out _bossFill);
            bossBg.rectTransform.anchorMin = new Vector2(0.3f, 0.83f); bossBg.rectTransform.anchorMax = new Vector2(0.7f, 0.86f);
            bossBg.rectTransform.offsetMin = bossBg.rectTransform.offsetMax = Vector2.zero;
            bossBg.gameObject.SetActive(false);
            _bossBar = bossBg;

            var pipsStack = UIFactory.HStack(transform, 4f);
            _pips = (RectTransform)pipsStack.transform;
            _pips.anchorMin = new Vector2(0.3f, 0.80f); _pips.anchorMax = new Vector2(0.7f, 0.82f);
            _pips.offsetMin = _pips.offsetMax = Vector2.zero;
            foreach (var name in new[] { "NEUTRAL", "ENRAGED", "WARD", "RECOVER" })
            {
                var pip = UIFactory.Panel(_pips, UITheme.Ink700, name);
                var le = pip.gameObject.AddComponent<LayoutElement>(); le.flexibleWidth = 1; le.minHeight = 10;
            }
            _pips.gameObject.SetActive(false);

            // controls
            var ctrl = UIFactory.HStack(transform, 6f);
            var crt = (RectTransform)ctrl.transform;
            crt.anchorMin = new Vector2(0.86f, 0.9f); crt.anchorMax = new Vector2(0.99f, 0.97f);
            crt.offsetMin = crt.offsetMax = Vector2.zero;
            _speed1 = MiniBtn(ctrl.transform, "1x", () => SetFast(false));
            _speed2 = MiniBtn(ctrl.transform, "2x", () => SetFast(true));
            _pause = MiniBtn(ctrl.transform, "II", TogglePause);

            _ticker = UIFactory.Label(transform, "", 15, UITheme.Parchment, TextAlignmentOptions.Center);
            var trt = _ticker.rectTransform;
            trt.anchorMin = new Vector2(0.25f, 0.18f); trt.anchorMax = new Vector2(0.75f, 0.22f);
            trt.offsetMin = trt.offsetMax = Vector2.zero;

            _dock = UIFactory.Root(transform, "PartyDock");
            _dock.anchorMin = new Vector2(0f, 0f); _dock.anchorMax = new Vector2(1f, 0.16f);
            _dock.offsetMin = _dock.offsetMax = Vector2.zero;

            _slabPanel = UIFactory.Root(transform, "Slab");
            var scrim = UIFactory.Box(_slabPanel, new Color(0f, 0f, 0f, 0.6f), _slabPanel);
            _slab = UIFactory.Label(_slabPanel, "", 60, UITheme.Candle, TextAlignmentOptions.Center, true);
            UIFactory.Stretch(_slab.rectTransform);
            _continueBtn = UIFactory.Button(_slabPanel, "CONTINUE  ▶", () => GameLoopManager.Instance.BeginEvening());
            var cbrt = _continueBtn.image.rectTransform;
            cbrt.anchorMin = new Vector2(0.4f, 0.3f); cbrt.anchorMax = new Vector2(0.6f, 0.38f);
            cbrt.offsetMin = cbrt.offsetMax = Vector2.zero;
            _slabPanel.gameObject.SetActive(false);
        }

        private Image _bossBar;

        private Button MiniBtn(Transform p, string t, System.Action a)
        {
            var b = UIFactory.Button(p, t, a, primary: false);
            var le = b.gameObject.AddComponent<LayoutElement>(); le.minWidth = 40; le.minHeight = 28;
            return b;
        }

        protected override void OnShow()
        {
            _world = GameLoopManager.Instance != null ? GameLoopManager.Instance.CurrentExpedition : null;
            _slabPanel.gameObject.SetActive(false);
            _paused = false; _fast = false;
            SetFast(SettingsService.DefaultExpeditionSpeed == 2);

            _ai.Clear();
            BuildPartyDock();

            BombProjectile2D.OnDetonatedGlobal += OnDetonated;
            if (_world != null && _world.Expedition != null)
                _world.Expedition.OnFinished += OnFinished;
        }

        protected override void OnHide()
        {
            BombProjectile2D.OnDetonatedGlobal -= OnDetonated;
            if (_world != null && _world.Expedition != null) _world.Expedition.OnFinished -= OnFinished;
            foreach (var ai in _ai) if (ai != null) ai.OnBombThrowRequested -= OnThrow;
            _pauseHandle.Dispose();
            _speedHandle.Dispose();
        }

        private void BuildPartyDock()
        {
            foreach (Transform c in _dock) Destroy(c.gameObject);
            if (_world == null) return;
            var row = UIFactory.HStack(_dock, 12f, new RectOffset(20, 20, 12, 12));
            UIFactory.Stretch((RectTransform)row.transform);

            foreach (var body in _world.Party)
            {
                if (body == null) continue;
                var card = UIFactory.Panel(row.transform, UITheme.Ink900, "Card");
                var le = card.gameObject.AddComponent<LayoutElement>(); le.minWidth = 220; le.minHeight = 84;
                var v = UIFactory.VStack(card.transform, 4f, new RectOffset(8, 8, 6, 6));
                UIFactory.Stretch((RectTransform)v.transform);
                UIFactory.Label(v.transform, "ROOKIE", 14, UITheme.Candle, TextAlignmentOptions.Left, true)
                    .gameObject.AddComponent<LayoutElement>().minHeight = 18;
                var hp = UIFactory.Bar(v.transform, UITheme.Ink700, UITheme.Ok, out var hpFill);
                hp.gameObject.AddComponent<LayoutElement>().minHeight = 12;

                var ai = body.GetComponent<UtilityAI_CombatController>();
                if (ai != null) { _ai.Add(ai); ai.OnBombThrowRequested += OnThrow; }

                var tracker = card.gameObject.AddComponent<PortraitTracker>();
                tracker.Init(body, hpFill, card);
            }
        }

        private void Update()
        {
            if (_world == null || _world.Expedition == null) return;
            var exp = _world.Expedition;

            if (exp.Phase == ExpeditionPhase.BossFight && exp.BossInstance != null)
            {
                var bb = exp.BossInstance.GetComponent<CombatantBody>();
                var bp = exp.BossInstance.GetComponent<BossPhaseManager>();
                _bossBar.gameObject.SetActive(true); _pips.gameObject.SetActive(true);
                if (bb != null) _bossFill.fillAmount = bb.MaxHP > 0 ? (float)bb.CurrentHP / bb.MaxHP : 0f;
                _banner.text = "THE BOSS";
                if (bp != null) SetPips((int)bp.CurrentPhase);
            }
            else
            {
                _bossBar.gameObject.SetActive(false); _pips.gameObject.SetActive(false);
                _banner.text = exp.Phase == ExpeditionPhase.Warmup
                    ? $"{BiomeName()} — GET READY"
                    : $"{BiomeName()} — WAVE {Mathf.Max(1, exp.WaveNumber)} / {exp.TotalWaves}";
            }
        }

        private string BiomeName() => GameLoopManager.Instance != null
            ? BiomeLibrary.Name(GameLoopManager.Instance.TargetBiomeIndex) : "";

        private void SetPips(int active)
        {
            for (int i = 0; i < _pips.childCount; i++)
            {
                var img = _pips.GetChild(i).GetComponent<Image>();
                if (img != null) img.color = i == active ? UITheme.Arcane : UITheme.Ink700;
            }
        }

        private void OnThrow(BombThrowRequest r)
        {
            if (r.Bomb != null)
                _ticker.text = $"Rookie throws <b>{r.Bomb.DisplayName}</b> — {r.Bomb.Element}";
        }

        private void OnDetonated(DetonationInfo d)
        {
            string adv = d.HadElementalAdvantage ? "  <color=#f6d873>×2!</color>" : "";
            _ticker.text = $"{d.Element} bomb — {d.TotalDamage} dmg to {d.HitCount}{adv}";
        }

        private void OnFinished(bool won)
        {
            _slabPanel.gameObject.SetActive(true);
            _slab.text = won ? "VICTORY" : "DEFEAT";
            _slab.color = won ? UITheme.Ok : UITheme.Danger;
        }

        private void SetFast(bool fast)
        {
            _fast = fast;
            _speedHandle.Dispose();
            if (TimeControl.Instance != null && !_paused)
                _speedHandle = TimeControl.Instance.Push(fast ? 2f : 1f, "expedition-speed");
            Recolor();
        }

        private void TogglePause()
        {
            _paused = !_paused;
            if (_paused)
            {
                _speedHandle.Dispose();
                if (TimeControl.Instance != null) _pauseHandle = TimeControl.Instance.PushPause();
            }
            else
            {
                _pauseHandle.Dispose();
                SetFast(_fast);
            }
            Recolor();
        }

        private void Recolor()
        {
            if (_speed1 != null) _speed1.image.color = (!_fast && !_paused) ? UITheme.Candle : UITheme.Ink700;
            if (_speed2 != null) _speed2.image.color = (_fast && !_paused) ? UITheme.Candle : UITheme.Ink700;
            if (_pause != null) _pause.image.color = _paused ? UITheme.Candle : UITheme.Ink700;
        }

        /// <summary>Per-card HP + death visuals.</summary>
        private class PortraitTracker : MonoBehaviour
        {
            private CombatantBody _body; private Image _hp; private Image _card;
            public void Init(CombatantBody b, Image hp, Image card) { _body = b; _hp = hp; _card = card; }
            private void Update()
            {
                if (_body == null) return;
                _hp.fillAmount = _body.MaxHP > 0 ? Mathf.Clamp01((float)_body.CurrentHP / _body.MaxHP) : 0f;
                if (!_body.IsAlive && _card != null) _card.color = new Color(0.2f, 0.15f, 0.18f);
            }
        }
    }
}
