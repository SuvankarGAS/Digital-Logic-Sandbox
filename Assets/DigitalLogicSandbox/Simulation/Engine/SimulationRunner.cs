using System;
using DigitalLogicSandbox.Core.Ports;
using DigitalLogicSandbox.Core.Signals;
using DigitalLogicSandbox.Simulation.Context;
using DigitalLogicSandbox.Simulation.Propagation;

namespace DigitalLogicSandbox.Simulation.Engine
{
    public enum SimulationState : byte
    {
        /// <summary>Input changes propagate immediately.</summary>
        Running = 0,

        /// <summary>Input changes are applied but propagation waits for Step or Play.</summary>
        Paused = 1,
    }

    /// <summary>
    /// The live simulation used by the editor: owns the current <see cref="SimulationContext"/> and implements
    /// Play / Pause / Step / Reset and manual input toggling. Engine-free and frame-independent; the presentation
    /// layer reads <see cref="Context"/> and its change log after each call.
    /// </summary>
    public sealed class SimulationRunner
    {
        private readonly SimulationOptions _options;

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

            if (previous != null)
            {
                CompiledCircuit old = previous.Circuit;
                for (int i = 0; i < old.InputCount; i++)
                {
                    int index = circuit.IndexOfInput(old.GetInputPort(i));
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

        /// <summary>Propagates one wave. Returns true while more work remains.</summary>
        public bool Step()
        {
            EnsureLoaded();
            return Context.StepWave().Status == SettleStatus.Pending;
        }

        /// <summary>All signals (including inputs) back to LOW and re-settled.</summary>
        public SettleResult Reset()
        {
            EnsureLoaded();
            return Context.Reset();
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
