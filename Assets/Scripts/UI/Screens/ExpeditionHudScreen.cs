using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using AlchemistsArsenal.Core;
using AlchemistsArsenal.Data;
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
        /// <summary>Each party body's display name, resolved once with its dock card so
        /// the ticker and the dock can never disagree about who threw.</summary>
        private readonly Dictionary<ICombatant, string> _throwerNames = new Dictionary<ICombatant, string>();
        private TimeControl.Handle _pauseHandle;
        private TimeControl.Handle _speedHandle;
        private bool _paused, _fast;

        protected override void Build()
        {
            // no full-screen box — the arena camera renders behind the overlay
            var top = UIFactory.Panel(transform, UITheme.Alpha(UITheme.Surface, 0.92f), "Banner");
            top.rectTransform.anchorMin = new Vector2(0.32f, 0.88f); top.rectTransform.anchorMax = new Vector2(0.68f, 0.99f);
            top.rectTransform.offsetMin = top.rectTransform.offsetMax = Vector2.zero;
            _banner = UIFactory.Label(top.transform, "", UITheme.SizeHeading, UITheme.Candle,
                TextAlignmentOptions.Center, true);
            _banner.characterSpacing = 4f;
            UIFactory.Stretch(_banner.rectTransform, 6f);

            var bossBg = UIFactory.Bar(transform, UITheme.Ground, UITheme.Danger, out _bossFill);
            bossBg.rectTransform.anchorMin = new Vector2(0.3f, 0.83f); bossBg.rectTransform.anchorMax = new Vector2(0.7f, 0.86f);
            bossBg.rectTransform.offsetMin = bossBg.rectTransform.offsetMax = Vector2.zero;
            bossBg.gameObject.SetActive(false);
            _bossBar = bossBg;

            // The boss phase strip. It used to be four bare stripes with no captions:
            // you could see one light up and had no way to know what it meant or
            // whether anything was actually happening (playtest: "weird stripes that
            // don't have description"). Each segment is named now, and the line under
            // it says what the live phase is doing to your damage.
            var pipsStack = UIFactory.HStack(transform, 4f);
            _pips = (RectTransform)pipsStack.transform;
            _pips.anchorMin = new Vector2(0.3f, 0.785f); _pips.anchorMax = new Vector2(0.7f, 0.825f);
            _pips.offsetMin = _pips.offsetMax = Vector2.zero;
            foreach (var name in new[] { "NEUTRAL", "ENRAGED", "WARD", "RECOVER" })
            {
                var pip = UIFactory.Panel(_pips, UITheme.SurfaceHi, name);
                UIFactory.Flex(pip.gameObject, 1f, 1f, minHeight: 22f);
                var label = UIFactory.Label(pip.transform, name, UITheme.SizeTiny, UITheme.TextLow,
                    TextAlignmentOptions.Center, true);
                label.characterSpacing = 2f;
                UIFactory.Stretch(label.rectTransform, 2f);
                _pipLabels.Add(label);
            }
            _pips.gameObject.SetActive(false);

            _bossPhaseText = UIFactory.Label(transform, "", UITheme.SizeSmall, UITheme.TextMid,
                TextAlignmentOptions.Center);
            UIFactory.Place(_bossPhaseText.rectTransform, 0.22f, 0.735f, 0.78f, 0.782f);
            _bossPhaseText.gameObject.SetActive(false);

            // controls
            var ctrl = UIFactory.HStack(transform, 6f);
            var crt = (RectTransform)ctrl.transform;
            crt.anchorMin = new Vector2(0.86f, 0.9f); crt.anchorMax = new Vector2(0.99f, 0.97f);
            crt.offsetMin = crt.offsetMax = Vector2.zero;
            _speed1 = MiniBtn(ctrl.transform, "1x", () => SetFast(false));
            _speed2 = MiniBtn(ctrl.transform, "2x", () => SetFast(true));
            _pause = MiniBtn(ctrl.transform, "II", TogglePause);

            // NEXT WAVE — clear the current wave and move on (playtest: waves drag).
            _nextWave = UIFactory.Button(transform, "NEXT WAVE", () =>
            {
                if (_world != null && _world.Expedition != null) _world.Expedition.SkipCurrentWave();
            }, primary: false);
            // Out of the arena's middle: the fighter circles now and spent real time
            // standing behind this button and behind the ticker below it.
            UIFactory.Place(_nextWave.image.rectTransform, 0.855f, 0.175f, 0.985f, 0.235f);
            _nextWave.gameObject.SetActive(false);

            // The AI ticker gets its own slab so it reads as HUD rather than as text
            // floating in the middle of the fight.
            var tickerBg = UIFactory.Panel(transform, UITheme.Alpha(UITheme.Ground, 0.78f), "TickerBg");
            UIFactory.Place(tickerBg.rectTransform, 0.28f, 0.165f, 0.72f, 0.215f);
            _ticker = UIFactory.MonoLabel(tickerBg.transform, "", UITheme.SizeSmall, UITheme.TextMid,
                TextAlignmentOptions.Center);
            UIFactory.Stretch(_ticker.rectTransform, 4f);

            _dock = UIFactory.Root(transform, "PartyDock");
            _dock.anchorMin = new Vector2(0f, 0f); _dock.anchorMax = new Vector2(1f, 0.16f);
            _dock.offsetMin = _dock.offsetMax = Vector2.zero;

            _slabPanel = UIFactory.Root(transform, "Slab");
            var scrim = UIFactory.Box(_slabPanel, new Color(0f, 0f, 0f, 0.6f), _slabPanel);
            _slab = UIFactory.Title(_slabPanel, "", UITheme.SizeDisplay + 14, UITheme.Candle, TextAlignmentOptions.Center);
            UIFactory.Stretch(_slab.rectTransform);
            _continueBtn = UIFactory.Button(_slabPanel, "CONTINUE", () => GameLoopManager.Instance.BeginEvening());
            var cbrt = _continueBtn.image.rectTransform;
            cbrt.anchorMin = new Vector2(0.4f, 0.3f); cbrt.anchorMax = new Vector2(0.6f, 0.38f);
            cbrt.offsetMin = cbrt.offsetMax = Vector2.zero;
            _slabPanel.gameObject.SetActive(false);
        }

        private Image _bossBar;
        private Button _nextWave;
        private TextMeshProUGUI _bossPhaseText;
        private readonly List<TextMeshProUGUI> _pipLabels = new List<TextMeshProUGUI>();

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
            _lastBanner = ""; _lastBossFill = -1f; _lastPip = -1;
            _paused = false; _fast = false;
            SetFast(SettingsService.DefaultExpeditionSpeed == 2);

            _ai.Clear();
            _throwerNames.Clear();
            BuildPartyDock();

            BombProjectile2D.OnDetonatedGlobal += OnDetonated;
            if (_world != null && _world.Expedition != null)
                _world.Expedition.OnFinished += OnFinished;

            // The screen object is reused day to day, and Update does not run on the
            // frame it is shown — so the first afternoon frame showed yesterday's
            // banner ("Cinder Peaks — WAVE 3/3" on a Whispering Woods replay) and
            // yesterday's last ticker line. Paint today's state now.
            _ticker.text = "";
            Refresh();
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

            for (int i = 0; i < _world.Party.Count; i++)
            {
                var body = _world.Party[i];
                if (body == null) continue;

                // Identity comes from the roster; the contract buyer is only the
                // fallback for the bootstrap path, which has no roster.
                HeroRecord record = i < _world.PartyRecords.Count ? _world.PartyRecords[i] : null;
                string who = record != null ? record.displayName : (i == 0 ? _world.FighterName : "Rookie");
                string whoId = record != null ? record.portraitId : (i == 0 ? _world.FighterId : "rookie");
                _throwerNames[body] = who;

                Image card = UIKit.Surface(row.transform, out Transform inner,
                    UITheme.Alpha(UITheme.Surface, 0.92f), UITheme.Line, "Card");
                UIFactory.Flex(card.gameObject, 1f, 1f, minWidth: 300f, minHeight: 108f);

                var portrait = UIKit.Portrait(inner, Art.PixelSprites.Buyer(whoId), 78f);
                var frame = (RectTransform)portrait.transform.parent.parent;
                UIFactory.Place(frame, 0.02f, 0.08f, 0.26f, 0.92f);

                var name = UIFactory.Label(inner, who.ToUpperInvariant(), UITheme.SizeSmall, UITheme.Candle,
                    TextAlignmentOptions.Left, true);
                name.characterSpacing = 3f;
                UIFactory.Place(name.rectTransform, 0.29f, 0.68f, 0.98f, 0.95f);

                var hp = UIFactory.Bar(inner, UITheme.Ground, UITheme.Ok, out var hpFill);
                UIFactory.Place(hp.rectTransform, 0.29f, 0.50f, 0.98f, 0.65f);

                // Flasks left and the throw cooldown. Without these the fighter simply
                // stops throwing when the belt runs dry or while a bomb is recharging,
                // and there is nothing on screen that says why.
                var flasks = UIFactory.MonoLabel(inner, "", UITheme.SizeSmall, UITheme.TextHi,
                    TextAlignmentOptions.Left);
                UIFactory.Place(flasks.rectTransform, 0.29f, 0.26f, 0.62f, 0.46f);

                var flaskIcon = UIFactory.Icon(inner, Art.PixelSprites.Flask(ElementType.Nature), 22f);
                UIFactory.Place(flaskIcon.rectTransform, 0.255f, 0.28f, 0.30f, 0.44f);

                var cd = UIFactory.Bar(inner, UITheme.Ground, UITheme.Candle, out var cdFill);
                UIFactory.Place(cd.rectTransform, 0.29f, 0.08f, 0.80f, 0.22f);
                var cdText = UIFactory.MonoLabel(inner, "", UITheme.SizeTiny, UITheme.TextMid,
                    TextAlignmentOptions.Left);
                UIFactory.Place(cdText.rectTransform, 0.82f, 0.06f, 0.99f, 0.24f);

                var ai = body.GetComponent<UtilityAI_CombatController>();
                if (ai != null) { _ai.Add(ai); ai.OnBombThrowRequested += OnThrow; }

                var tracker = card.gameObject.AddComponent<PortraitTracker>();
                tracker.Init(body, hpFill, card, ai, flasks, cdFill, cdText, flaskIcon);
            }
        }

        private string _lastBanner = "";
        private float _lastBossFill = -1f;
        private int _lastPip = -1;

        private void Update() => Refresh();

        private void Refresh()
        {
            if (_world == null || _world.Expedition == null) return;
            var exp = _world.Expedition;

            bool boss = exp.Phase == ExpeditionPhase.BossFight && exp.BossInstance != null;
            if (_bossBar.gameObject.activeSelf != boss) _bossBar.gameObject.SetActive(boss);
            if (_pips.gameObject.activeSelf != boss) _pips.gameObject.SetActive(boss);
            if (_bossPhaseText != null && _bossPhaseText.gameObject.activeSelf != boss)
                _bossPhaseText.gameObject.SetActive(boss);

            bool canSkip = exp.Phase == ExpeditionPhase.Waves;
            if (_nextWave != null && _nextWave.gameObject.activeSelf != canSkip)
                _nextWave.gameObject.SetActive(canSkip);

            string banner;
            if (boss)
            {
                var bb = exp.BossInstance.GetComponent<CombatantBody>();
                var bp = exp.BossInstance.GetComponent<BossPhaseManager>();
                float f = bb != null && bb.MaxHP > 0 ? (float)bb.CurrentHP / bb.MaxHP : 0f;
                if (!Mathf.Approximately(f, _lastBossFill)) { _bossFill.fillAmount = f; _lastBossFill = f; }
                int pip = bp != null ? (int)bp.CurrentPhase : 0;
                if (pip != _lastPip)
                {
                    SetPips(pip);
                    if (_bossPhaseText != null) _bossPhaseText.text = DescribePhase(bp);
                    _lastPip = pip;
                }
                banner = "THE BOSS";
            }
            else
            {
                banner = exp.Phase == ExpeditionPhase.Warmup
                    ? $"{BiomeName()} — GET READY"
                    : $"{BiomeName()} — WAVE {Mathf.Max(1, exp.WaveNumber)} / {exp.TotalWaves}";
            }

            if (banner != _lastBanner) { _banner.text = banner; _lastBanner = banner; }
        }

        private string BiomeName() => GameLoopManager.Instance != null
            ? BiomeLibrary.Name(GameLoopManager.Instance.TargetBiomeIndex) : "";

        private void SetPips(int active)
        {
            for (int i = 0; i < _pips.childCount; i++)
            {
                var img = _pips.GetChild(i).GetComponent<Image>();
                if (img != null) img.color = i == active ? UITheme.Witch : UITheme.SurfaceHi;
                if (i < _pipLabels.Count && _pipLabels[i] != null)
                    _pipLabels[i].color = i == active ? UITheme.TextHi : UITheme.TextLow;
            }
        }

        /// <summary>What the boss's current phase actually does to you, in one line.</summary>
        private static string DescribePhase(BossPhaseManager bp)
        {
            if (bp == null) return "";
            switch (bp.CurrentPhase)
            {
                case BossPhase.Enraged:
                    return "ENRAGED — hurt, and hitting faster for it. Keep your distance.";
                case BossPhase.ElementalWard:
                    return $"ELEMENTAL WARD — shrugging off <b>{bp.WardElement}</b> " +
                           $"(x{bp.WardMultiplier:0.00} damage). Throw something else.";
                case BossPhase.Recovering:
                    return "RECOVERING — the ward just dropped. This is the window.";
                default:
                    return "NEUTRAL — measured attacks, nothing resisted.";
            }
        }

        private void OnThrow(BombThrowRequest r)
        {
            if (r.Bomb == null) return;
            // Name the hero who actually threw. This used to be the contract's
            // buyer (ExpeditionWorld.FighterName) for every throw, so a customer
            // who never left the shop was credited with the whole party's work.
            if (r.Thrower == null || !_throwerNames.TryGetValue(r.Thrower, out string who)) who = "The party";
            _ticker.text = $"{who} throws <b>{r.Bomb.DisplayName}</b> — {r.Bomb.Element}";
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
            private CombatantBody _body;
            private Image _hp, _card, _cdFill, _flaskIcon;
            private UtilityAI_CombatController _ai;
            private TextMeshProUGUI _flasks, _cdText;
            private float _last = -1f;
            private int _lastFlasks = -1;
            private bool _dead;

            public void Init(CombatantBody b, Image hp, Image card, UtilityAI_CombatController ai,
                TextMeshProUGUI flasks, Image cdFill, TextMeshProUGUI cdText, Image flaskIcon)
            {
                _body = b; _hp = hp; _card = card; _ai = ai;
                _flasks = flasks; _cdFill = cdFill; _cdText = cdText; _flaskIcon = flaskIcon;
                Refresh(); // a new card has no Update before its first frame is drawn
            }

            private void Update() => Refresh();

            private void Refresh()
            {
                if (_body == null) return;

                float f = _body.MaxHP > 0 ? Mathf.Clamp01((float)_body.CurrentHP / _body.MaxHP) : 0f;
                if (!Mathf.Approximately(f, _last))
                {
                    _hp.fillAmount = f;
                    _hp.color = f > 0.5f ? UITheme.Ok : f > 0.25f ? UITheme.Candle : UITheme.Danger;
                    _last = f;
                }
                if (!_dead && !_body.IsAlive && _card != null)
                {
                    _card.color = new Color(0.2f, 0.15f, 0.18f);
                    _dead = true;
                }

                if (_ai == null) return;

                int left = _ai.FlasksLeft;
                if (left != _lastFlasks)
                {
                    _lastFlasks = left;
                    if (_flasks != null)
                    {
                        _flasks.text = left > 0 ? $"x{left} flasks" : "OUT OF FLASKS";
                        _flasks.color = left > 3 ? UITheme.TextHi : left > 0 ? UITheme.Candle : UITheme.Danger;
                    }
                    if (_flaskIcon != null)
                        _flaskIcon.color = left > 0 ? Color.white : UITheme.Alpha(Color.white, 0.3f);
                }

                float remaining = _ai.CooldownRemaining;
                if (_cdFill != null)
                {
                    _cdFill.fillAmount = _ai.CooldownFraction;
                    _cdFill.color = remaining <= 0f ? UITheme.Ok : UITheme.Candle;
                }
                if (_cdText != null)
                    _cdText.text = left <= 0 ? "--" : remaining <= 0f ? "READY" : $"{remaining:0.0}s";
            }
        }
    }
}
