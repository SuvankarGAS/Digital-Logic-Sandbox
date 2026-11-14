using System;
using DigitalLogicSandbox.Core.Ports;
using DigitalLogicSandbox.Core.Signals;
using DigitalLogicSandbox.Simulation.Context;
using DigitalLogicSandbox.Simulation.Propagation;

namespace DigitalLogicSandbox.Simulation.Engine
{
    public enum SimulationState : byte
    {
        /// <summary>Input changes propagate immediately and the clock (if any) ticks in real time.</summary>
        Running = 0,

        /// <summary>Input changes are applied but propagation waits for Step or Play; the clock is stopped.</summary>
        Paused = 1,
    }

    /// <summary>
    /// The live simulation used by the editor: owns the current <see cref="SimulationContext"/> and implements
    /// Play / Pause / Step / Reset, manual input toggling and the global clock. Engine-free and frame-independent:
    /// the caller passes elapsed time to <see cref="Advance"/>; the presentation layer reads <see cref="Context"/>
    /// and its change log after each call.
    /// <para>
    /// Clock semantics: one clock edge (half a period) toggles the clock net and, while running, settles the
    /// circuit completely before the next edge. Edge timing is real time but never affects logic: each edge is
    /// applied and settled in order, at most <see cref="MaxEdgesPerAdvance"/> per call.
    /// </para>
    /// </summary>
    public sealed class SimulationRunner
    {
        public const float MinClockFrequency = 0.1f;
        public const float MaxClockFrequency = 50f;
        public const float DefaultClockFrequency = 2f;

        /// <summary>Upper bound on clock edges applied by one <see cref="Advance"/> call (frame hitch protection).</summary>
        public const int MaxEdgesPerAdvance = 8;

        private readonly SimulationOptions _options;
        private float _clockFrequency = DefaultClockFrequency;
        private double _clockAccumulator;

        public SimulationRunner(SimulationOptions options = null)
        {
            _options = options ?? SimulationOptions.Default;
        }

        /// <summary>Raised after a new compiled circuit is loaded (views should do a full refresh).</summary>
        public event Action Loaded;

        /// <summary>Raised whenever Play/Pause changes the state.</summary>
        public event Action<SimulationState> StateChanged;

        public SimulationState State { get; private set; } = SimulationState.Running;

        /// <summary>Null until a circuit is loaded.</summary>
        public SimulationContext Context { get; private set; }

        public CompiledCircuit Circuit => Context?.Circuit;

        public SettleResult LastResult => Context?.LastResult ?? default;

        public long EvaluationCount => Context?.TotalEvaluations ?? 0;

        public bool HasPendingWork => Context != null && Context.HasPendingWork;

        /// <summary>Raised after each clock edge with the new clock value.</summary>
        public event Action<Signal> ClockTicked;

        /// <summary>True when the loaded circuit contains a clock node.</summary>
        public bool HasClock => Context != null && Context.Circuit.HasClock;

        /// <summary>Full clock cycles per second while running (clamped to the supported range).</summary>
        public float ClockFrequency
        {
            get => _clockFrequency;
            set => _clockFrequency = Math.Max(MinClockFrequency, Math.Min(MaxClockFrequency, value));
        }

        /// <summary>When false the clock does not tick in real time (it can still be stepped).</summary>
        public bool ClockEnabled { get; set; } = true;

        /// <summary>Clock edges applied since the last load or reset.</summary>
        public long ClockEdges { get; private set; }

        /// <summary>Rising clock edges since the last load or reset.</summary>
        public long ClockCycles => (ClockEdges + 1) / 2;

        /// <summary>Initial state of feedback loops on load and reset.</summary>
        public PowerOnState PowerOnState
        {
            get => _options.PowerOnState;
            set
            {
                _options.PowerOnState = value;
                if (Context != null)
                {
                    Context.PowerOnState = value;
                }
            }
        }

        /// <summary>
        /// Replaces the simulated circuit (e.g. after the user edits it). Input values are carried over by stable
        /// port id, so editing a circuit does not reset the switches the user has set.
        /// </summary>
        public void Load(CompiledCircuit circuit)
        {
            if (circuit == null)
            {
                throw new ArgumentNullException(nameof(circuit));
            }

            SimulationContext previous = Context;
            var next = new SimulationContext(circuit, _options);
            ClockEdges = 0;
            _clockAccumulator = 0d;

            if (previous != null)
            {
                CompiledCircuit old = previous.Circuit;
                for (int i = 0; i < old.InputCount; i++)
                {
                    // Carried over per bit: a bus that changed width keeps the bits both versions have.
                    int index = circuit.IndexOfInput(old.GetInputPort(i), old.GetInputBit(i));
                    if (index >= 0)
                    {
                        next.SetInput(index, previous.GetInput(i));
                    }
                }

                if (State == SimulationState.Running)
                {
                    next.Settle();
                }

                next.Changes.Clear();
            }

            Context = next;
            Loaded?.Invoke();
        }

        public void Play()
        {
            EnsureLoaded();
            SetState(SimulationState.Running);
            if (Context.HasPendingWork)
            {
                Context.Settle();
            }
        }

        public void Pause()
        {
            SetState(SimulationState.Paused);
        }

        /// <summary>
        /// Propagates one wave. When nothing is pending and the circuit has a clock, starts the next clock edge
        /// first, so paused stepping walks through clock cycles wave by wave. Returns true while more work remains.
        /// </summary>
        public bool Step()
        {
            EnsureLoaded();
            if (!Context.HasPendingWork && HasClock)
            {
                ApplyClockEdge(settle: false);
            }

            return Context.StepWave().Status == SettleStatus.Pending;
        }

        /// <summary>
        /// Power-on reset: inputs and the clock LOW, feedback loops re-initialised (<see cref="PowerOnState"/>),
        /// clock counters cleared.
        /// </summary>
        public SettleResult Reset()
        {
            EnsureLoaded();
            ClockEdges = 0;
            _clockAccumulator = 0d;
            return Context.Reset();
        }

        /// <summary>
        /// Applies one clock edge now (toggles the clock) and, while running, settles. Returns false when the
        /// circuit has no clock.
        /// </summary>
        public bool TickClock()
        {
            EnsureLoaded();
            if (!HasClock)
            {
                return false;
            }

            ApplyClockEdge(settle: State == SimulationState.Running);
            return true;
        }

        /// <summary>
        /// Advances real time: while running with an enabled clock, applies one edge per elapsed half period.
        /// Returns the number of edges applied.
        /// </summary>
        public int Advance(double deltaSeconds)
        {
            if (Context == null || !HasClock || !ClockEnabled || State != SimulationState.Running || deltaSeconds <= 0d)
            {
                return 0;
            }

            double halfPeriod = 0.5d / _clockFrequency;
            _clockAccumulator += deltaSeconds;
            int edges = 0;
            while (_clockAccumulator >= halfPeriod && edges < MaxEdgesPerAdvance)
            {
                _clockAccumulator -= halfPeriod;
                ApplyClockEdge(settle: true);
                edges++;
            }

            if (_clockAccumulator >= halfPeriod)
            {
                // Too far behind (long hitch): drop the backlog instead of spiralling.
                _clockAccumulator = 0d;
            }

            return edges;
        }

        private void ApplyClockEdge(bool settle)
        {
            Signal next = Context.Clock == Signal.High ? Signal.Low : Signal.High;
            Context.SetClock(next);
            ClockEdges++;
            if (settle)
            {
                Context.Settle();
            }

            ClockTicked?.Invoke(next);
        }

        public bool SetInput(int index, Signal value)
        {
            EnsureLoaded();
            bool changed = Context.SetInput(index, value);
            if (changed && State == SimulationState.Running)
            {
                Context.Settle();
            }

            return changed;
        }

        public bool SetInput(PortId port, Signal value)
        {
            EnsureLoaded();
            int index = Context.Circuit.IndexOfInput(port);
            if (index < 0)
            {
                throw new ArgumentException("Port " + port + " is not an input of the simulated circuit.", nameof(port));
            }

            return SetInput(index, value);
        }

        /// <summary>Sets every bit of a (bus) input port from an unsigned value, bit 0 = least significant.</summary>
        public bool SetInputValue(PortId port, ulong value)
        {
            EnsureLoaded();
            bool changed = Context.SetInputValue(port, value);
            if (changed && State == SimulationState.Running)
            {
                Context.Settle();
            }

            return changed;
        }

        /// <summary>Flips a binary input (anything non-HIGH becomes HIGH).</summary>
        public void ToggleInput(int index)
        {
            EnsureLoaded();
            SetInput(index, Context.GetInput(index) == Signal.High ? Signal.Low : Signal.High);
        }

        private void SetState(SimulationState state)
        {
            if (State == state)
            {
                return;
            }

            State = state;
            StateChanged?.Invoke(state);
        }

        private void EnsureLoaded()
        {
            if (Context == null)
            {
                throw new InvalidOperationException("No circuit is loaded into the simulation.");
            }
        }
    }
}
