using System;
using System.Collections.Generic;
using UnityEngine;

namespace AlchemistsArsenal.Core
{
    /// <summary>
    /// The single owner of <see cref="Time.timeScale"/> (DESIGN.md §2.5).
    ///
    /// Consumers push a request and get a token back; releasing the token restores
    /// the previous state. Requests stack, so "pause during 2×" resumes at 2×.
    /// Nothing else in the project may write <c>Time.timeScale</c> — the morning
    /// budget uses <see cref="Time.unscaledDeltaTime"/> instead.
    /// </summary>
    public class TimeControl : MonoBehaviour
    {
        public static TimeControl Instance { get; private set; }

        private readonly List<Request> _stack = new List<Request>();

        public float CurrentScale => _stack.Count > 0 ? _stack[_stack.Count - 1].scale : 1f;
        public bool IsPaused => Mathf.Approximately(CurrentScale, 0f);

        public event Action<float> OnScaleChanged;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            Time.timeScale = 1f;
        }

        /// <summary>Push a timescale request. Dispose (or <see cref="Release"/>) to pop it.</summary>
        public Handle Push(float scale, string reason)
        {
            var req = new Request { scale = Mathf.Max(0f, scale), reason = reason, id = ++_nextId };
            _stack.Add(req);
            Apply();
            return new Handle(this, req.id);
        }

        public void Release(int id)
        {
            int i = _stack.FindIndex(r => r.id == id);
            if (i < 0) return;
            _stack.RemoveAt(i);
            Apply();
        }

        /// <summary>Convenience: a pause request that the pause menu holds and releases.</summary>
        public Handle PushPause() => Push(0f, "pause");

        private void Apply()
        {
            float s = CurrentScale;
            if (!Mathf.Approximately(Time.timeScale, s))
            {
                Time.timeScale = s;
                OnScaleChanged?.Invoke(s);
            }
        }

        private int _nextId;

        private struct Request { public float scale; public string reason; public int id; }

        public readonly struct Handle : IDisposable
        {
            private readonly TimeControl _owner;
            private readonly int _id;
            public Handle(TimeControl owner, int id) { _owner = owner; _id = id; }
            public void Dispose() => _owner?.Release(_id);
        }
    }
}
