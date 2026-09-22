using System.Collections.Generic;
using UnityEngine;
using AlchemistsArsenal.Core;

namespace AlchemistsArsenal.Vfx
{
    /// <summary>
    /// Owns a world camera's position: a <see cref="BasePosition"/> that pans ease
    /// toward, plus screen shake on top. Everything that wants to move the camera
    /// goes through here, because a shake that nudged the transform directly and a
    /// pan that set it directly would fight over the same field every frame.
    ///
    /// The camera is presentation only. Nothing in combat reads it, so shaking it
    /// cannot change a fight.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public class CameraRig : MonoBehaviour
    {
        private static readonly List<CameraRig> _live = new List<CameraRig>();

        public Vector3 BasePosition { get; private set; }

        private Vector3 _panFrom, _panTo;
        private float _panT = 1f, _panSeconds;
        private float _trauma;
        private readonly System.Random _rng = new System.Random(77);

        [Tooltip("Largest shake offset, in world units, at full trauma.")]
        [SerializeField] private float maxOffset = 0.32f;

        public void Configure(Vector3 basePosition)
        {
            BasePosition = basePosition;
            _panTo = basePosition;
            _panT = 1f;
            transform.position = basePosition;
        }

        /// <summary>Glide to <paramref name="target"/> over <paramref name="seconds"/> (0 = cut).</summary>
        public void PanTo(Vector3 target, float seconds)
        {
            if (seconds <= 0f || SettingsService.ReduceMotion)
            {
                BasePosition = target;
                _panTo = target;
                _panT = 1f;
                return;
            }
            _panFrom = BasePosition;
            _panTo = target;
            _panSeconds = seconds;
            _panT = 0f;
        }

        /// <summary>Shake every live world camera. <paramref name="amount"/> 0..1 of trauma.</summary>
        public static void Shake(float amount)
        {
            if (!SettingsService.ScreenShake || SettingsService.ReduceMotion) return;
            for (int i = 0; i < _live.Count; i++)
                if (_live[i] != null) _live[i]._trauma = Mathf.Clamp01(_live[i]._trauma + amount);
        }

        private void OnEnable() => _live.Add(this);
        private void OnDisable() => _live.Remove(this);

        private void LateUpdate()
        {
            float dt = Time.unscaledDeltaTime;
            if (_panT < 1f)
            {
                _panT = Mathf.Min(1f, _panT + dt / Mathf.Max(0.01f, _panSeconds));
                float e = _panT * _panT * (3f - 2f * _panT);
                BasePosition = Vector3.Lerp(_panFrom, _panTo, e);
            }

            Vector3 offset = Vector3.zero;
            if (_trauma > 0f)
            {
                float k = _trauma * _trauma * maxOffset;
                offset = new Vector3(((float)_rng.NextDouble() * 2f - 1f) * k, ((float)_rng.NextDouble() * 2f - 1f) * k, 0f);
                _trauma = Mathf.Max(0f, _trauma - dt * 2.2f);
            }
            transform.position = BasePosition + offset;
        }
    }
}
