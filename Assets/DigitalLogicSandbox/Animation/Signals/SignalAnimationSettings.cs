using System;
using UnityEngine;

namespace DigitalLogicSandbox.Animation.Signals
{
    /// <summary>
    /// User-adjustable presentation settings for signal propagation. They only affect what is drawn and when;
    /// logical values are never delayed or changed by them.
    /// </summary>
    [Serializable]
    public sealed class SignalAnimationSettings
    {
        public const float MinSpeed = 0.25f;
        public const float MaxSpeed = 4f;

        /// <summary>World units per second a signal front travels along a wire at speed 1.</summary>
        public const float BaseUnitsPerSecond = 28f;

        /// <summary>Pause between propagation waves at speed 1 (a gate "switching").</summary>
        public const float BaseWaveGap = 0.05f;

        [SerializeField] private bool _enabled = true;
        [SerializeField] private float _speed = 1f;
        [SerializeField] private bool _showParticles = true;
        [SerializeField] private float _maxBatchSeconds = 3f;
        [SerializeField] private int _maxAnimatedWires = 2048;

        /// <summary>Raised when any setting changes.</summary>
        public event Action Changed;

        /// <summary>When false, views switch to new values instantly.</summary>
        public bool Enabled
        {
            get => _enabled;
            set => Set(ref _enabled, value);
        }

        /// <summary>Playback speed multiplier, clamped to [<see cref="MinSpeed"/>, <see cref="MaxSpeed"/>].</summary>
        public float Speed
        {
            get => _speed;
            set => Set(ref _speed, Mathf.Clamp(value, MinSpeed, MaxSpeed));
        }

        /// <summary>Show a bright particle at each travelling front.</summary>
        public bool ShowParticles
        {
            get => _showParticles;
            set => Set(ref _showParticles, value);
        }

        /// <summary>
        /// Upper bound for one batch of changes (e.g. a deep circuit with hundreds of waves): timings are compressed
        /// to fit, so the display never lags far behind the simulation.
        /// </summary>
        public float MaxBatchSeconds
        {
            get => _maxBatchSeconds;
            set => Set(ref _maxBatchSeconds, Mathf.Max(0.05f, value));
        }

        /// <summary>Wires animated at once; beyond this, changes are shown instantly (huge circuits stay responsive).</summary>
        public int MaxAnimatedWires
        {
            get => _maxAnimatedWires;
            set => Set(ref _maxAnimatedWires, Mathf.Max(0, value));
        }

        public float UnitsPerSecond => BaseUnitsPerSecond * _speed;

        public float WaveGap => BaseWaveGap / _speed;

        public void CopyFrom(SignalAnimationSettings other)
        {
            if (other == null)
            {
                return;
            }

            _enabled = other._enabled;
            _speed = Mathf.Clamp(other._speed, MinSpeed, MaxSpeed);
            _showParticles = other._showParticles;
            _maxBatchSeconds = Mathf.Max(0.05f, other._maxBatchSeconds);
            _maxAnimatedWires = Mathf.Max(0, other._maxAnimatedWires);
            Changed?.Invoke();
        }

        private void Set<T>(ref T field, T value)
        {
            if (Equals(field, value))
            {
                return;
            }

            field = value;
            Changed?.Invoke();
        }
    }
}
