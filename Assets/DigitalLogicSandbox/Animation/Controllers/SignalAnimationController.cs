using System;
using System.Collections.Generic;
using DigitalLogicSandbox.Animation.Signals;
using DigitalLogicSandbox.Animation.Wires;
using DigitalLogicSandbox.Core.Connections;
using DigitalLogicSandbox.Core.Signals;
using DigitalLogicSandbox.Rendering;
using DigitalLogicSandbox.Rendering.Wires;
using DigitalLogicSandbox.Simulation.Propagation;
using UnityEngine;

namespace DigitalLogicSandbox.Animation.Controllers
{
    /// <summary>
    /// Centralized presentation of signal propagation. It consumes the simulation's change log (which the engine
    /// has already fully applied) and replays it visually: each changed net becomes a <em>pulse</em> whose front
    /// travels from the driver along every wire of the net (fan-out) and updates the destination pins on arrival.
    /// Pulses of propagation wave k start after the wires of wave k-1 have arrived, so the display follows the
    /// logical propagation order.
    /// <para>
    /// The simulation never waits for this class, and it never writes logical values. When every pulse has
    /// finished, all views show exactly the simulation's values. One <see cref="Tick"/> per frame drives
    /// everything (no per-wire coroutines or Updates). Pulses and particles are pooled.
    /// </para>
    /// </summary>
    public sealed class SignalAnimationController : IDisposable
    {
        public const int DefaultParticleCapacity = 256;

        private readonly CircuitPresenter _presenter;
        private readonly SignalAnimationSettings _settings;
        private readonly SignalStyle _particleStyle;
        private readonly SignalParticlePool _particles;
        private readonly List<Pulse> _pulses = new List<Pulse>();
        private readonly Stack<Pulse> _pool = new Stack<Pulse>();
        private readonly Dictionary<int, Pulse> _latestByNet = new Dictionary<int, Pulse>();
        private readonly List<PinRef> _scratchWires = new List<PinRef>(8);
        private readonly float _particleSize;
        private float[] _waveDuration = new float[16];
        private float[] _waveStart = new float[16];
        private float _time;
        private int _batch;
        private int _animatedWires;
        private bool _wasAnimating;

        public SignalAnimationController(CircuitPresenter presenter, VisualTheme theme, SignalAnimationSettings settings,
            int particleCapacity = DefaultParticleCapacity)
        {
            _presenter = presenter ?? throw new ArgumentNullException(nameof(presenter));
            if (theme == null)
            {
                throw new ArgumentNullException(nameof(theme));
            }

            _settings = settings ?? new SignalAnimationSettings();
            _particleStyle = new SignalStyle(theme);
            _particleSize = theme.WireWidth(Signal.High) * 3.2f;
            _particles = new SignalParticlePool(presenter.Root, particleCapacity, theme.UnlitMaterial);
            _presenter.SignalsRefreshed += OnSignalsRefreshed;
            _settings.Changed += OnSettingsChanged;
        }

        /// <summary>Raised when <see cref="IsAnimating"/> changes (for status displays).</summary>
        public event Action ActivityChanged;

        public SignalAnimationSettings Settings => _settings;

        /// <summary>True while any pulse is pending or travelling.</summary>
        public bool IsAnimating => _pulses.Count > 0;

        /// <summary>Pulses (changed nets) pending or travelling.</summary>
        public int PulseCount => _pulses.Count;

        /// <summary>Wires currently travelling or waiting for their wave.</summary>
        public int AnimatedWireCount => _animatedWires;

        public int ActiveParticleCount => _particles.ActiveCount;

        /// <summary>Particles ever created (bounded by the pool capacity).</summary>
        public int CreatedParticleCount => _particles.CreatedCount;

        /// <summary>
        /// Takes the changes recorded since the last call and either animates them or (when disabled, detached or
        /// overflowing) applies them instantly. The caller clears the log afterwards.
        /// </summary>
        public void Consume(SignalChangeLog changes)
        {
            if (changes == null || (changes.Count == 0 && !changes.Overflowed))
            {
                return;
            }

            if (!_settings.Enabled || !_presenter.HasSimulation || changes.Overflowed)
            {
                CompleteAll();
                _presenter.ApplyChanges(changes); // an overflow triggers a full refresh, which also ends pulses
                return;
            }

            ReadOnlySpan<SignalChange> span = changes.AsSpan();
            int waves = MeasureWaves(span);
            float scale = ScheduleWaves(waves);
            _batch++;

            for (int i = 0; i < span.Length; i++)
            {
                SignalChange change = span[i];
                if (_latestByNet.TryGetValue(change.Net, out Pulse previous) && previous.Batch != _batch)
                {
                    // Rapid toggling: an older pulse on this net is superseded; show its end state immediately.
                    Complete(previous);
                }

                _scratchWires.Clear();
                _presenter.GetNetWires(change.Net, _scratchWires);
                if (_scratchWires.Count == 0 || _animatedWires + _scratchWires.Count > _settings.MaxAnimatedWires)
                {
                    ApplyInstantly(change.Net, change.Current, _scratchWires);
                    continue;
                }

                Pulse pulse = _pool.Count > 0 ? _pool.Pop() : new Pulse();
                pulse.Reset(change.Net, change.Previous, change.Current, _batch,
                    _time + _waveStart[change.Wave] * scale);
                for (int w = 0; w < _scratchWires.Count; w++)
                {
                    float length = _presenter.TryGetWireView(_scratchWires[w], out WireView view) ? view.Length : 0f;
                    float duration = Mathf.Max(0.0001f, length / _settings.UnitsPerSecond * scale);
                    pulse.AddTrip(new WireTrip(_scratchWires[w], duration));
                }

                _animatedWires += pulse.Wires.Count;
                _pulses.Add(pulse);
                _latestByNet[change.Net] = pulse;
            }

            RaiseActivityIfChanged();
        }

        /// <summary>Advances all pulses by <paramref name="deltaTime"/> seconds (unscaled). Call once per frame.</summary>
        public void Tick(float deltaTime)
        {
            if (_pulses.Count == 0)
            {
                return;
            }

            _time += Mathf.Max(0f, deltaTime);
            for (int i = _pulses.Count - 1; i >= 0; i--)
            {
                // Completing a superseded pulse can remove entries anywhere in the list.
                if (i >= _pulses.Count)
                {
                    continue;
                }

                Pulse pulse = _pulses[i];
                if (_time < pulse.Start)
                {
                    continue;
                }

                if (!pulse.Started)
                {
                    Begin(pulse);
                }

                Advance(pulse, _time - pulse.Start);
                if (pulse.Remaining == 0)
                {
                    Finish(pulse);
                }
            }

            RaiseActivityIfChanged();
        }

        /// <summary>Ends every pulse immediately, showing final values (e.g. before disabling animation).</summary>
        public void CompleteAll()
        {
            while (_pulses.Count > 0)
            {
                Complete(_pulses[_pulses.Count - 1]);
            }

            RaiseActivityIfChanged();
        }

        /// <summary>"Reset visual state": drops all pulses and redraws every view from the simulation.</summary>
        public void ResetVisualState()
        {
            DropAll();
            _presenter.RefreshSignals();
            RaiseActivityIfChanged();
        }

        public void Dispose()
        {
            _presenter.SignalsRefreshed -= OnSignalsRefreshed;
            _settings.Changed -= OnSettingsChanged;
            DropAll();
            _particles.Destroy();
        }

        // ------------------------------------------------------------------------------------------------

        /// <summary>Longest wire per wave (at speed 1 timing, before compression). Returns the wave count.</summary>
        private int MeasureWaves(ReadOnlySpan<SignalChange> span)
        {
            int waves = 0;
            for (int i = 0; i < span.Length; i++)
            {
                waves = Mathf.Max(waves, span[i].Wave + 1);
            }

            EnsureWaveCapacity(waves);
            Array.Clear(_waveDuration, 0, waves);
            for (int i = 0; i < span.Length; i++)
            {
                _scratchWires.Clear();
                _presenter.GetNetWires(span[i].Net, _scratchWires);
                float longest = 0f;
                for (int w = 0; w < _scratchWires.Count; w++)
                {
                    if (_presenter.TryGetWireView(_scratchWires[w], out WireView view))
                    {
                        longest = Mathf.Max(longest, view.Length);
                    }
                }

                int wave = span[i].Wave;
                _waveDuration[wave] = Mathf.Max(_waveDuration[wave], longest / _settings.UnitsPerSecond);
            }

            return waves;
        }

        /// <summary>Start offset of each wave; returns the compression factor that keeps the batch within limits.</summary>
        private float ScheduleWaves(int waves)
        {
            float start = 0f;
            for (int w = 0; w < waves; w++)
            {
                _waveStart[w] = start;
                if (_waveDuration[w] > 0f)
                {
                    start += _waveDuration[w] + _settings.WaveGap;
                }
            }

            return start > _settings.MaxBatchSeconds ? _settings.MaxBatchSeconds / start : 1f;
        }

        private void Begin(Pulse pulse)
        {
            pulse.Started = true;
            if (_latestByNet.TryGetValue(pulse.Net, out Pulse other) && other != pulse && other.Batch == pulse.Batch &&
                other.Start <= pulse.Start && other.Started)
            {
                // A glitch: the same net changes again in a later wave of this batch before the first front arrived.
                Complete(other);
            }

            _presenter.ApplyNetSource(pulse.Net, pulse.To);
            if (!_settings.ShowParticles)
            {
                return;
            }

            Color color = _particleStyle.FrontColorOf(pulse.To);
            for (int w = 0; w < pulse.Wires.Count; w++)
            {
                WireTrip trip = pulse.Wires[w];
                trip.Particle = _particles.Acquire(color, _particleSize);
                pulse.Wires[w] = trip;
            }
        }

        private void Advance(Pulse pulse, float elapsed)
        {
            for (int w = 0; w < pulse.Wires.Count; w++)
            {
                WireTrip trip = pulse.Wires[w];
                if (trip.Arrived)
                {
                    continue;
                }

                float progress = elapsed / trip.Duration;
                if (progress >= 1f || !_presenter.TryGetWireView(trip.Destination, out WireView view))
                {
                    Arrive(pulse, w);
                    continue;
                }

                view.SetTransition(pulse.From, pulse.To, progress);
                _particles.Move(trip.Particle, view.GetPointAt(progress));
            }
        }

        private void Arrive(Pulse pulse, int index)
        {
            WireTrip trip = pulse.Wires[index];
            _particles.Release(trip.Particle);
            trip.Particle = -1;
            trip.Arrived = true;
            pulse.Wires[index] = trip;
            pulse.Remaining--;
            _animatedWires--;
            _presenter.ApplyWireArrival(trip.Destination, pulse.To);
        }

        /// <summary>Shows the pulse's end state at once and removes it.</summary>
        private void Complete(Pulse pulse)
        {
            if (!pulse.Started)
            {
                pulse.Started = true;
                _presenter.ApplyNetSource(pulse.Net, pulse.To);
            }

            for (int w = 0; w < pulse.Wires.Count; w++)
            {
                if (!pulse.Wires[w].Arrived)
                {
                    Arrive(pulse, w);
                }
            }

            Finish(pulse);
        }

        private void Finish(Pulse pulse)
        {
            int index = _pulses.IndexOf(pulse);
            if (index < 0)
            {
                return;
            }

            int last = _pulses.Count - 1;
            _pulses[index] = _pulses[last];
            _pulses.RemoveAt(last);
            if (_latestByNet.TryGetValue(pulse.Net, out Pulse latest) && latest == pulse)
            {
                _latestByNet.Remove(pulse.Net);
            }

            _pool.Push(pulse);
        }

        /// <summary>Forgets every pulse without touching views (they are being refreshed from the simulation).</summary>
        private void DropAll()
        {
            for (int i = 0; i < _pulses.Count; i++)
            {
                _pool.Push(_pulses[i]);
            }

            _pulses.Clear();
            _latestByNet.Clear();
            _particles.ReleaseAll();
            _animatedWires = 0;
        }

        private void ApplyInstantly(int net, Signal value, List<PinRef> wires)
        {
            _presenter.ApplyNetSource(net, value);
            for (int w = 0; w < wires.Count; w++)
            {
                _presenter.ApplyWireArrival(wires[w], value);
            }
        }

        private void EnsureWaveCapacity(int waves)
        {
            if (waves <= _waveDuration.Length)
            {
                return;
            }

            int size = Mathf.NextPowerOfTwo(waves);
            _waveDuration = new float[size];
            _waveStart = new float[size];
        }

        private void OnSignalsRefreshed()
        {
            DropAll();
            RaiseActivityIfChanged();
        }

        private void OnSettingsChanged()
        {
            if (!_settings.Enabled)
            {
                CompleteAll();
            }
            else if (!_settings.ShowParticles)
            {
                _particles.ReleaseAll();
                for (int i = 0; i < _pulses.Count; i++)
                {
                    List<WireTrip> wires = _pulses[i].Wires;
                    for (int w = 0; w < wires.Count; w++)
                    {
                        WireTrip trip = wires[w];
                        trip.Particle = -1;
                        wires[w] = trip;
                    }
                }
            }
        }

        private void RaiseActivityIfChanged()
        {
            bool animating = IsAnimating;
            if (animating != _wasAnimating)
            {
                _wasAnimating = animating;
                ActivityChanged?.Invoke();
            }
        }

        private struct WireTrip
        {
            public WireTrip(PinRef destination, float duration)
            {
                Destination = destination;
                Duration = duration;
                Arrived = false;
                Particle = -1;
            }

            public PinRef Destination;
            public float Duration;
            public bool Arrived;
            public int Particle;
        }

        /// <summary>One net change being replayed (pooled).</summary>
        private sealed class Pulse
        {
            public readonly List<WireTrip> Wires = new List<WireTrip>(4);

            public int Net;
            public Signal From;
            public Signal To;
            public int Batch;
            public float Start;
            public bool Started;
            public int Remaining;

            public void Reset(int net, Signal from, Signal to, int batch, float start)
            {
                Net = net;
                From = from;
                To = to;
                Batch = batch;
                Start = start;
                Started = false;
                Wires.Clear();
                Remaining = 0;
            }

            public void AddTrip(WireTrip trip)
            {
                Wires.Add(trip);
                Remaining++;
            }
        }
    }
}
