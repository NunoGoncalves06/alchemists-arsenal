using System.Collections.Generic;
using UnityEngine;
using AlchemistsArsenal.Core;
using AlchemistsArsenal.Systems;
using AlchemistsArsenal.Audio;

namespace AlchemistsArsenal.UI
{
    /// <summary>
    /// Teaches each mechanic the first time it turns up, one at a time, with the
    /// <see cref="Spotlight"/>: the screen darkens round the thing to touch and one
    /// short line says what to do. The lessons themselves are data
    /// (<see cref="TutorialScript"/>); which ones apply depends on the road's level
    /// (<see cref="Data.Complexity"/>), so day one teaches the basic loop and the
    /// later roads teach only what they add. Each lesson plays once per save
    /// (<see cref="RunState.seenLessons"/>).
    ///
    /// <para>While a lesson is up the morning clock runs at a third of its speed.
    /// When the lesson's bench is not the one on screen, the circle points at that
    /// bench on the rail first.</para>
    ///
    /// <para>The first Evening gets a briefing the same way: the report, the
    /// upgrades, the party, the diary and sleep, one short line each. A briefing
    /// line is done when the player clicks to go on.</para>
    /// </summary>
    public class TutorialManager : MonoBehaviour
    {
        public static TutorialManager Instance { get; private set; }

        /// <summary>A lesson is on screen.</summary>
        public static bool Active { get; private set; }

        /// <summary>
        /// The benches stay shut until the first job of a new game is in the order
        /// book (there is nothing for them to work on before that).
        /// </summary>
        public static bool StationsUnlocked { get; private set; } = true;

        private const float SlowClock = 0.35f;

        private Spotlight _spot;
        private Lesson _current;
        private bool _onRail;
        private float _notReadyFor, _shownFor;

        /// <summary>The lesson on screen, or null (the harness checks the briefing with it).</summary>
        public static string CurrentLessonId => Instance != null && Instance._current != null ? Instance._current.Id : null;

        /// <summary>Go on from a briefing line, as a click would.</summary>
        public void Continue()
        {
            if (_current != null && _current.ClickToGo) Learnt(_current);
        }

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private void Start()
        {
            _spot = Spotlight.Create(transform, 200);
            _spot.SkipRequested += () => { if (_current != null) Learnt(_current); };
        }

        private static RunState State => SaveSystem.Instance != null ? SaveSystem.Instance.State : null;

        private static bool InMorning =>
            GameLoopManager.Instance != null && GameLoopManager.Instance.Phase == GamePhase.Morning &&
            UIManager.Instance != null && UIManager.Instance.Current == ScreenId.Morning;

        /// <summary>The lesson's screen is the one showing (and, for the benches, it is morning).</summary>
        private static bool OnItsScreen(Lesson l) =>
            UIManager.Instance != null && UIManager.Instance.Current == l.OnScreen &&
            (l.OnScreen != ScreenId.Morning || InMorning);

        private void Update()
        {
            if (_spot == null) return;
            RunState s = State;
            if (s == null)
            {
                if (_current != null) Drop();
                _spot.HideNow();
                StationsUnlocked = true;
                return;
            }
            s.seenLessons ??= new List<string>();
            StationsUnlocked = !InMorning || s.seenLessons.Contains("job") ||
                               (CraftingManager.Instance != null && CraftingManager.Instance.Orders.Count > 0);

            if (_current != null)
            {
                // Leaving the lesson's screen ends it at once, with no fade into the
                // next screen; one that was just done (the send, the diary) still counts.
                if (!OnItsScreen(_current))
                {
                    if (Safe(_current.Done)) Learnt(_current);
                    else Drop();
                    _spot.HideNow();
                    return;
                }
                _shownFor += Time.unscaledDeltaTime;
                if (_current.ClickToGo && _shownFor > 0.6f && Input.GetMouseButtonDown(0)) { Learnt(_current); return; }
                if (Safe(_current.Done)) { Learnt(_current); return; }
                // Somebody else moved the work on (another order, the clock): let go.
                if (!Safe(_current.Ready))
                {
                    _notReadyFor += Time.unscaledDeltaTime;
                    if (_notReadyFor > 1.5f) { Drop(); return; }
                }
                else _notReadyFor = 0f;
                FollowStation();
                return;
            }

            int level = Data.Complexity.Level(s);
            foreach (Lesson l in TutorialScript.Lessons)
            {
                if (l.MinLevel > level || s.seenLessons.Contains(l.Id) || !OnItsScreen(l)) continue;
                if (!Safe(l.Ready)) continue;
                Begin(l);
                break;
            }
        }

        private void Begin(Lesson l)
        {
            _current = l;
            _notReadyFor = 0f;
            _shownFor = 0f;
            _onRail = !OnStation(l);
            Active = true;
            if (l.OnScreen == ScreenId.Morning && GameLoopManager.Instance != null)
                GameLoopManager.Instance.BudgetRateMultiplier = SlowClock;
            AudioManager.Play(Sfx.Chime);
            ShowCurrent();
        }

        /// <summary>Point at the bench on the rail until the player opens it, then at the lesson itself.</summary>
        private void FollowStation()
        {
            bool off = !OnStation(_current);
            if (off == _onRail) return;
            _onRail = off;
            ShowCurrent();
        }

        private void ShowCurrent()
        {
            Lesson l = _current;
            if (_onRail)
            {
                int i = Mathf.Clamp(l.Station, 0, 4);
                string name = TutorialScript.StationNames[i];
                _spot.ReserveFoot = true;
                _spot.Show(() => RectPoint(Screen?.RailTabRect(i)), () => RectRadius(Screen?.RailTabRect(i), 6f),
                    () => $"Open the {name} bench.");
                return;
            }
            _spot.ReserveFoot = l.OnScreen == ScreenId.Morning;
            _spot.Show(() => Point(l), () => Radius(l),
                () => l.ClickToGo ? SafeSay(l) + " <i>Click to go on.</i>" : SafeSay(l), () => Reveals(l));
        }

        /// <summary>
        /// What stays lit besides the target, in screen pixels: every gauge on the
        /// bench (the dark must never hide the instrument that says when to stop),
        /// and whatever the lesson itself names (the flask while it fills).
        /// </summary>
        private static List<Rect> Reveals(Lesson l)
        {
            var rects = new List<Rect>();
            Camera cam = WorldCam;
            if (cam == null || l.Station < 1) return rects;   // the Counter is all UI
            foreach (Rect w in Crafting.BenchGauge.VisibleWorldRects()) rects.Add(WorldToScreen(cam, w, 6f));
            Rect? own = null;
            try { own = l.Reveal?.Invoke(); } catch (System.Exception) { own = null; }
            if (own.HasValue) rects.Add(WorldToScreen(cam, own.Value, 8f));
            return rects;
        }

        private static Rect WorldToScreen(Camera cam, Rect w, float padPx)
        {
            Vector3 a = cam.WorldToScreenPoint(new Vector3(w.xMin, w.yMin, 0f));
            Vector3 b = cam.WorldToScreenPoint(new Vector3(w.xMax, w.yMax, 0f));
            return Rect.MinMaxRect(Mathf.Min(a.x, b.x) - padPx, Mathf.Min(a.y, b.y) - padPx,
                                   Mathf.Max(a.x, b.x) + padPx, Mathf.Max(a.y, b.y) + padPx);
        }

        private void Learnt(Lesson l)
        {
            RunState s = State;
            if (s != null)
            {
                if (!s.seenLessons.Contains(l.Id)) s.seenLessons.Add(l.Id);
                if (l.Id == "send") s.tutorialCompleted = true;
                SaveSystem.Instance.MarkDirty();
            }
            Drop();
        }

        private void Drop()
        {
            _current = null;
            Active = false;
            _spot.Hide();
            if (GameLoopManager.Instance != null) GameLoopManager.Instance.BudgetRateMultiplier = 1f;
        }

        // ----------------------------------------------------------- geometry

        private static MorningScreen Screen =>
            UIManager.Instance != null ? UIManager.Instance.ScreenOf(ScreenId.Morning) as MorningScreen : null;

        private static bool OnStation(Lesson l) => l.Station < 0 || (Screen != null && Screen.ActiveTabIndex == l.Station);

        private static Camera WorldCam => ShopWorld.Instance != null ? ShopWorld.Instance.WorldCamera : null;

        private static Vector2? Point(Lesson l)
        {
            LessonTarget? t = SafeWhere(l);
            if (!t.HasValue) return null;
            if (!t.Value.IsWorld) return RectPoint(t.Value.Rect);
            Camera cam = WorldCam;
            if (cam == null) return null;
            Vector3 sp = cam.WorldToScreenPoint(t.Value.World);
            return new Vector2(sp.x, sp.y);
        }

        private static Vector2 Radius(Lesson l)
        {
            LessonTarget? t = SafeWhere(l);
            if (!t.HasValue) return Vector2.one * 60f;
            if (!t.Value.IsWorld) return t.Value.Inside ? RectInside(t.Value.Rect) : RectRadius(t.Value.Rect, t.Value.Pad);
            Camera cam = WorldCam;
            if (cam == null) return Vector2.one * 60f;
            float pxPerUnit = cam.pixelHeight / (2f * cam.orthographicSize);
            return Vector2.one * t.Value.WorldRadius * pxPerUnit;
        }

        private static readonly Vector3[] Corners = new Vector3[4];

        private static Vector2? RectPoint(RectTransform rt)
        {
            if (rt == null || !rt.gameObject.activeInHierarchy) return null;
            rt.GetWorldCorners(Corners);   // an overlay canvas: world corners are screen pixels
            return (Vector2)(Corners[0] + Corners[2]) * 0.5f;
        }

        /// <summary>
        /// The ellipse through the rect's corners (half-sizes times the square root
        /// of two), so a wide button is hugged rather than drowned in a big circle.
        /// </summary>
        private static Vector2 RectRadius(RectTransform rt, float pad)
        {
            if (rt == null) return Vector2.one * 60f;
            rt.GetWorldCorners(Corners);
            Vector2 size = Corners[2] - Corners[0];
            return size * 0.5f * 1.4142f + Vector2.one * pad;
        }

        /// <summary>An ellipse just inside a big rect: a whole panel lit, its corners left dark.</summary>
        private static Vector2 RectInside(RectTransform rt)
        {
            if (rt == null) return Vector2.one * 60f;
            rt.GetWorldCorners(Corners);
            return (Vector2)(Corners[2] - Corners[0]) * 0.5f * 1.02f;
        }

        // A lesson reads live bench state; a bench torn down mid-frame must not
        // throw out of Update every frame after.
        private static bool Safe(System.Func<bool> f)
        {
            try { return f != null && f(); } catch (System.Exception) { return false; }
        }

        private static LessonTarget? SafeWhere(Lesson l)
        {
            try { return l.Where?.Invoke(); } catch (System.Exception) { return null; }
        }

        private static string SafeSay(Lesson l)
        {
            try { return l.Say?.Invoke() ?? ""; } catch (System.Exception) { return ""; }
        }
    }
}
