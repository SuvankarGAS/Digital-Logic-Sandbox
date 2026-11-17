using System;
using System.Collections.Generic;
using DigitalLogicSandbox.Components.Abstractions;
using DigitalLogicSandbox.Core.Ports;
using DigitalLogicSandbox.Core.Signals;
using DigitalLogicSandbox.Simulation.Engine;
using DigitalLogicSandbox.Simulation.Propagation;
using DigitalLogicSandbox.Utilities;

namespace DigitalLogicSandbox.Simulation.Context
{
    /// <summary>
    /// Mutable signal state for one <see cref="CompiledCircuit"/>, plus the event-driven propagation loop.
    /// <para>
    /// <b>Timing model: unit-delay waves.</b> Changing an input schedules only the gates reading that net. Scheduled
    /// gates are evaluated in waves: every gate in a wave reads the net values committed at the start of the wave,
    /// and all output changes of the wave are committed together at its end, which schedules the readers of the
    /// changed nets for the next wave. Each gate therefore behaves as one unit of delay, and results never depend
    /// on the order in which gates were created. This makes gate-built latches and flip-flops well defined.
    /// Combinational circuits reach the same final values as with any other order; only the intermediate waves
    /// (used by animation) follow logical depth.
    /// </para>
    /// <para>
    /// <b>Sequential state.</b> Feedback loops hold state in their nets. A reset sets every gate-driven net to
    /// UNKNOWN and settles; with <see cref="PowerOnState.Resolved"/>, nets that are still unknown inside feedback
    /// loops are then forced LOW one at a time (in compiled gate order) until the circuit settles, giving a
    /// repeatable binary power-on state. A loop that never settles within the evaluation budget (an oscillator) has
    /// its active nets set to UNKNOWN, which is a fixed point, and the settle reports
    /// <see cref="SettleStatus.Oscillating"/>.
    /// </para>
    /// <para>
    /// <b>Clock.</b> Each clock domain (one per divider, see <see cref="ClockSchedule"/>) is a source net like a root
    /// input (<see cref="ApplyClockEdge"/>, <see cref="SetClock"/>). Clock edges are sequential steps driven from outside (<see cref="SimulationRunner"/>, sequence analysis); propagation of each
    /// edge is ordinary settling.
    /// </para>
    /// <para>
    /// <b>Shared buses.</b> A net joined by a bus join has several driving gates. Each driver's last output is kept
    /// per output slot, and the net is resolved when a driver changes: all Z gives Z, any UNKNOWN gives UNKNOWN,
    /// HIGH and LOW together give UNKNOWN and mark the net as contended, otherwise the agreed value.
    /// </para>
    /// <para>
    /// After construction no method on the propagation path allocates: buffers are preallocated from the compiled
    /// topology. Contexts are fully independent; any number can share one compiled circuit.
    /// </para>
    /// </summary>
    public sealed class SimulationContext
    {
        private readonly CompiledCircuit _circuit;
        private readonly Signal[] _nets;
        private readonly bool[] _queued;
        private readonly IntRingQueue _queue;
        private readonly Signal[] _inputScratch;
        private readonly Signal[] _outputScratch;
        private readonly int[] _pendingNets;
        private readonly Signal[] _pendingValues;
        private readonly SignalChangeLog _changes;
        private readonly List<int> _unstableGates = new List<int>();
        private readonly int _evaluationBudget;
        private readonly Signal[] _slots;
        private readonly bool[] _sharedPending;
        private readonly bool[] _contended;

        private int _pendingCount;
        private int _currentWave;
        private bool _collapsing;

        public SimulationContext(CompiledCircuit circuit, SimulationOptions options = null)
        {
            _circuit = circuit ?? throw new ArgumentNullException(nameof(circuit));
            options ??= SimulationOptions.Default;

            _nets = new Signal[circuit.NetCount];
            _queued = new bool[circuit.GateCount];
            _queue = new IntRingQueue(Math.Max(1, circuit.GateCount));
            _inputScratch = new Signal[Math.Max(1, circuit.MaxGateInputs)];
            _outputScratch = new Signal[Math.Max(1, circuit.MaxGateOutputs)];
            _pendingNets = new int[Math.Max(1, circuit.NetCount)];
            _pendingValues = new Signal[Math.Max(1, circuit.NetCount)];
            if (circuit.HasSharedNets)
            {
                _slots = new Signal[circuit.GateOutputNets.Length];
                _sharedPending = new bool[circuit.NetCount];
                _contended = new bool[circuit.NetCount];
            }
            _changes = new SignalChangeLog(256, options.MaxRecordedChanges);
            _evaluationBudget = options.EvaluationBudgetFor(circuit.GateCount);
            RecordChanges = options.RecordChanges;
            PowerOnState = options.PowerOnState;

            Reset();
        }

        public CompiledCircuit Circuit => _circuit;

        /// <summary>Net changes since the consumer last called <see cref="SignalChangeLog.Clear"/>.</summary>
        public SignalChangeLog Changes => _changes;

        /// <summary>When false, no changes are recorded (faster headless analysis).</summary>
        public bool RecordChanges { get; set; }

        /// <summary>How the next <see cref="Reset"/> initialises feedback loops.</summary>
        public PowerOnState PowerOnState { get; set; }

        /// <summary>True while dirty gates are waiting to be evaluated.</summary>
        public bool HasPendingWork => _queue.Count > 0;

        /// <summary>Total gate evaluations since construction or the last <see cref="Reset"/>.</summary>
        public long TotalEvaluations { get; private set; }

        /// <summary>Result of the most recent <see cref="Settle"/>, <see cref="StepWave"/> or <see cref="Reset"/>.</summary>
        public SettleResult LastResult { get; private set; }

        /// <summary>The safety limit on evaluations per settle.</summary>
        public int EvaluationBudget => _evaluationBudget;

        /// <summary>Loop nets forced LOW by the last reset (<see cref="PowerOnState.Resolved"/>).</summary>
        public int ResolvedLoopNets { get; private set; }

        /// <summary>Shared buses currently driven HIGH and LOW at the same time (bus contention).</summary>
        public int ContentionCount { get; private set; }

        /// <summary>True when the shared bus <paramref name="net"/> is currently contended.</summary>
        public bool IsContended(int net)
        {
            return _contended != null && _contended[net];
        }

        // ------------------------------------------------------------------------------------------------
        // State access
        // ------------------------------------------------------------------------------------------------

        public Signal GetNet(int net)
        {
            return _nets[net];
        }

        public Signal GetInput(int index)
        {
            return _nets[_circuit.InputNets[index]];
        }

        public Signal GetOutput(int index)
        {
            return _nets[_circuit.OutputNets[index]];
        }

        /// <summary>
        /// Value of a whole root input port as an unsigned integer (bit 0 = least significant). Returns false when
        /// a bit is not binary or the port is unknown.
        /// </summary>
        public bool TryGetInputValue(PortId port, out ulong value)
        {
            return TryReadBits(_circuit.InputNets, _circuit.IndexOfInput(port), _circuit.GetInputWidth(port), out value);
        }

        public bool TryGetOutputValue(PortId port, out ulong value)
        {
            return TryReadBits(_circuit.OutputNets, _circuit.IndexOfOutput(port), _circuit.GetOutputWidth(port), out value);
        }

        /// <summary>
        /// Sets every bit of a root input port from <paramref name="value"/> (bit 0 = least significant). Does not
        /// propagate. Returns true if any bit changed.
        /// </summary>
        public bool SetInputValue(PortId port, ulong value)
        {
            int first = _circuit.IndexOfInput(port);
            if (first < 0)
            {
                throw new ArgumentException("Port " + port + " is not an input of the root circuit.", nameof(port));
            }

            int width = _circuit.GetInputWidth(port);
            bool changed = false;
            for (int b = 0; b < width; b++)
            {
                changed |= SetInput(first + b, ((value >> b) & 1UL) != 0 ? Signal.High : Signal.Low);
            }

            return changed;
        }

        private bool TryReadBits(int[] nets, int first, int width, out ulong value)
        {
            value = 0;
            if (first < 0)
            {
                return false;
            }

            for (int b = 0; b < width; b++)
            {
                Signal signal = _nets[nets[first + b]];
                if (signal == Signal.High)
                {
                    value |= 1UL << b;
                }
                else if (signal != Signal.Low)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>Current value of the fastest clock domain present (LOW when the circuit has no clock).</summary>
        public Signal Clock => _nets[_circuit.ClockNet];

        /// <summary>Current value of the clock domain with divider 2^<paramref name="exponent"/> (LOW when absent).</summary>
        public Signal GetClock(int exponent)
        {
            return _nets[_circuit.GetClockNet(exponent)];
        }

        /// <summary>Copies all net values into <paramref name="destination"/> (length ≥ NetCount).</summary>
        public void CopyNets(Signal[] destination)
        {
            Array.Copy(_nets, destination, _nets.Length);
        }

        /// <summary>Gates that were still active when the last settle hit the evaluation budget.</summary>
        public IReadOnlyList<int> UnstableGates => _unstableGates;

        // ------------------------------------------------------------------------------------------------
        // Control
        // ------------------------------------------------------------------------------------------------

        /// <summary>
        /// Power-on reset: inputs and the clock go LOW, every gate-driven net becomes UNKNOWN, every gate is
        /// evaluated and the circuit settles. Combinational logic therefore resolves completely; feedback loops are
        /// initialised according to <see cref="PowerOnState"/>. The change log is cleared afterwards: consumers
        /// should treat a reset as a full refresh rather than animate it.
        /// </summary>
        public SettleResult Reset()
        {
            Array.Clear(_nets, 0, _nets.Length);
            int[] drivers = _circuit.NetDriverGate;
            for (int net = 0; net < _nets.Length; net++)
            {
                if (drivers[net] >= 0)
                {
                    _nets[net] = Signal.Unknown;
                }
            }

            if (_slots != null)
            {
                for (int i = 0; i < _slots.Length; i++)
                {
                    _slots[i] = Signal.Unknown;
                }

                Array.Clear(_sharedPending, 0, _sharedPending.Length);
                Array.Clear(_contended, 0, _contended.Length);
                ContentionCount = 0;
            }

            Array.Clear(_queued, 0, _queued.Length);
            _queue.Clear();
            _unstableGates.Clear();
            _pendingCount = 0;
            _currentWave = 0;
            TotalEvaluations = 0;
            ResolvedLoopNets = 0;

            for (int g = 0; g < _circuit.GateCount; g++)
            {
                _queued[g] = true;
                _queue.Enqueue(g);
            }

            bool record = RecordChanges;
            RecordChanges = false;
            SettleResult result = Settle();
            if (PowerOnState == PowerOnState.Resolved && _circuit.HasCombinationalCycles)
            {
                result = ResolveLoops(result);
            }

            RecordChanges = record;
            _changes.Clear();
            LastResult = result;
            return result;
        }

        /// <summary>
        /// Sets a root input (interface order) and schedules the gates that read it. Does not propagate; call
        /// <see cref="Settle"/> or <see cref="StepWave"/>. Returns true if the value changed.
        /// </summary>
        public bool SetInput(int index, Signal value)
        {
            return SetSourceNet(_circuit.InputNets[index], value);
        }

        public bool SetInput(PortId port, Signal value)
        {
            int index = _circuit.IndexOfInput(port);
            if (index < 0)
            {
                throw new ArgumentException("Port " + port + " is not an input of the root circuit.", nameof(port));
            }

            return SetInput(index, value);
        }

        /// <summary>
        /// Drives every clock domain to <paramref name="value"/> and schedules their readers. Does not propagate.
        /// Returns false when nothing changed or the circuit has no clock. Prefer <see cref="ApplyClockEdge"/>, which
        /// keeps slower domains on their schedule.
        /// </summary>
        public bool SetClock(Signal value)
        {
            bool changed = false;
            for (int k = 0; k < ClockSchedule.DomainCount; k++)
            {
                changed |= SetSourceNet(_circuit.GetClockNet(k), value);
            }

            return changed;
        }

        /// <summary>
        /// Sets every clock domain to its level after base edge <paramref name="edge"/> (see
        /// <see cref="ClockSchedule"/>) and schedules the readers of the domains that changed. Does not propagate.
        /// Returns true if any domain changed. Allocation-free.
        /// </summary>
        public bool ApplyClockEdge(long edge)
        {
            bool changed = false;
            for (int k = 0; k < ClockSchedule.DomainCount; k++)
            {
                int net = _circuit.GetClockNet(k);
                if (net != CompiledCircuit.FloatingNet)
                {
                    changed |= SetSourceNet(net, ClockSchedule.Level(k, edge));
                }
            }

            return changed;
        }

        /// <summary>Propagates until stable or until the evaluation budget is exhausted (oscillation).</summary>
        public SettleResult Settle()
        {
            int evaluations = 0;
            int waves = 0;
            if (!_collapsing)
            {
                _unstableGates.Clear();
            }

            while (_queue.Count > 0)
            {
                if (evaluations >= _evaluationBudget)
                {
                    return HandleOscillation(evaluations, waves);
                }

                evaluations += RunWave();
                waves++;
            }

            _currentWave = 0;
            LastResult = new SettleResult(SettleStatus.Stable, evaluations, waves);
            return LastResult;
        }

        /// <summary>
        /// Evaluates exactly one propagation wave (all gates scheduled before it began). Returns
        /// <see cref="SettleStatus.Pending"/> while more work remains.
        /// </summary>
        public SettleResult StepWave()
        {
            _unstableGates.Clear();
            if (_queue.Count == 0)
            {
                _currentWave = 0;
                LastResult = new SettleResult(SettleStatus.Stable, 0, 0);
                return LastResult;
            }

            int evaluations = RunWave();
            bool pending = _queue.Count > 0;
            if (!pending)
            {
                _currentWave = 0;
            }

            LastResult = new SettleResult(pending ? SettleStatus.Pending : SettleStatus.Stable, evaluations, 1);
            return LastResult;
        }

        // ------------------------------------------------------------------------------------------------
        // Propagation
        // ------------------------------------------------------------------------------------------------

        private bool SetSourceNet(int net, Signal value)
        {
            if (net == CompiledCircuit.FloatingNet)
            {
                return false;
            }

            Signal previous = _nets[net];
            if (previous == value)
            {
                return false;
            }

            _nets[net] = value;
            if (RecordChanges)
            {
                _changes.Add(new SignalChange(net, previous, value, _currentWave));
            }

            ScheduleReaders(net);
            return true;
        }

        /// <summary>Evaluates every gate scheduled for the current wave, then commits their outputs together.</summary>
        private int RunWave()
        {
            _currentWave++;
            int count = _queue.Count;
            for (int i = 0; i < count; i++)
            {
                EvaluateNext();
            }

            CommitWave();
            return count;
        }

        private void EvaluateNext()
        {
            int gate = _queue.Dequeue();
            _queued[gate] = false;
            TotalEvaluations++;

            int[] inputNets = _circuit.GateInputNets;
            int inputStart = _circuit.GateInputOffsets[gate];
            int inputCount = _circuit.GateInputOffsets[gate + 1] - inputStart;
            for (int i = 0; i < inputCount; i++)
            {
                _inputScratch[i] = _nets[inputNets[inputStart + i]];
            }

            int outputStart = _circuit.GateOutputOffsets[gate];
            int outputCount = _circuit.GateOutputOffsets[gate + 1] - outputStart;
            IPrimitiveComponent component = _circuit.GateComponents[gate];
            component.Evaluate(
                new ReadOnlySpan<Signal>(_inputScratch, 0, inputCount),
                new Span<Signal>(_outputScratch, 0, outputCount));

            // Outputs are buffered: other gates in this wave must still see the values from the wave's start.
            int[] outputNets = _circuit.GateOutputNets;
            for (int o = 0; o < outputCount; o++)
            {
                int net = outputNets[outputStart + o];
                Signal value = _outputScratch[o];
                if (_slots != null && _circuit.IsSharedNet(net))
                {
                    // Shared bus: remember this driver's value; the net is resolved once at commit.
                    int slot = outputStart + o;
                    if (_slots[slot] != value)
                    {
                        _slots[slot] = value;
                        if (!_sharedPending[net])
                        {
                            _sharedPending[net] = true;
                            _pendingNets[_pendingCount] = net;
                            _pendingCount++;
                        }
                    }

                    continue;
                }

                if (_nets[net] != value)
                {
                    _pendingNets[_pendingCount] = net;
                    _pendingValues[_pendingCount] = value;
                    _pendingCount++;
                }
            }
        }

        private void CommitWave()
        {
            for (int i = 0; i < _pendingCount; i++)
            {
                int net = _pendingNets[i];
                Signal value;
                if (_sharedPending != null && _sharedPending[net])
                {
                    _sharedPending[net] = false;
                    value = ResolveShared(net);
                }
                else
                {
                    value = _pendingValues[i];
                }

                Signal previous = _nets[net];
                if (previous == value)
                {
                    continue;
                }

                _nets[net] = value;
                if (RecordChanges)
                {
                    _changes.Add(new SignalChange(net, previous, value, _currentWave));
                }

                ScheduleReaders(net);
            }

            _pendingCount = 0;
        }

        /// <summary>Resolves a shared bus from the last value of each of its drivers (see class remarks).</summary>
        private Signal ResolveShared(int net)
        {
            bool high = false;
            bool low = false;
            bool unknown = false;
            int[] slots = _circuit.NetSharedSlots;
            int end = _circuit.NetSharedOffsets[net + 1];
            for (int i = _circuit.NetSharedOffsets[net]; i < end; i++)
            {
                switch (_slots[slots[i]])
                {
                    case Signal.High:
                        high = true;
                        break;
                    case Signal.Low:
                        low = true;
                        break;
                    case Signal.Unknown:
                        unknown = true;
                        break;
                }
            }

            bool contended = high && low;
            if (contended != _contended[net])
            {
                _contended[net] = contended;
                ContentionCount += contended ? 1 : -1;
            }

            if (unknown || contended)
            {
                return Signal.Unknown;
            }

            return high ? Signal.High : low ? Signal.Low : Signal.HighImpedance;
        }

        private void ScheduleReaders(int net)
        {
            int[] readers = _circuit.NetReaders;
            int end = _circuit.NetReaderOffsets[net + 1];
            for (int r = _circuit.NetReaderOffsets[net]; r < end; r++)
            {
                int gate = readers[r];
                if (!_queued[gate])
                {
                    _queued[gate] = true;
                    _queue.Enqueue(gate);
                }
            }
        }

        /// <summary>
        /// Budget exhausted: records the gates still switching, then sets their outputs to UNKNOWN and settles
        /// once more. UNKNOWN is a fixed point of AND/NOT loops, so the oscillation stops in a defined state.
        /// </summary>
        private SettleResult HandleOscillation(int evaluations, int waves)
        {
            if (_collapsing)
            {
                // Still switching after the collapse: stop here (the outer call reports the oscillation).
                DrainQueue(record: false);
                return new SettleResult(SettleStatus.Oscillating, evaluations, waves);
            }

            DrainQueue(record: true);
            _collapsing = true;
            _currentWave++;
            int[] outputNets = _circuit.GateOutputNets;
            for (int i = 0; i < _unstableGates.Count; i++)
            {
                int gate = _unstableGates[i];
                for (int o = _circuit.GateOutputOffsets[gate]; o < _circuit.GateOutputOffsets[gate + 1]; o++)
                {
                    if (_slots != null)
                    {
                        _slots[o] = Signal.Unknown;
                    }

                    SetSourceNet(outputNets[o], Signal.Unknown);
                }
            }

            SettleResult collapse = Settle();
            _collapsing = false;
            _currentWave = 0;
            LastResult = new SettleResult(SettleStatus.Oscillating, evaluations + collapse.Evaluations,
                waves + collapse.Waves);
            return LastResult;
        }

        private void DrainQueue(bool record)
        {
            if (record)
            {
                _unstableGates.Clear();
            }

            while (_queue.Count > 0)
            {
                int gate = _queue.Dequeue();
                _queued[gate] = false;
                if (record)
                {
                    _unstableGates.Add(gate);
                }
            }

            if (_sharedPending != null)
            {
                Array.Clear(_sharedPending, 0, _sharedPending.Length);
            }

            _pendingCount = 0;
        }

        /// <summary>
        /// Power-on resolution: forces each still-unknown net driven by a feedback-loop gate to LOW (in compiled
        /// gate order) and settles, until no loop net is unknown. Each candidate net is tried at most once.
        /// </summary>
        private SettleResult ResolveLoops(SettleResult result)
        {
            int evaluations = result.Evaluations;
            int waves = result.Waves;
            bool oscillated = result.Status == SettleStatus.Oscillating;
            IReadOnlyList<int[]> cycles = _circuit.CombinationalCycles;
            int[] outputNets = _circuit.GateOutputNets;
            for (int c = 0; c < cycles.Count; c++)
            {
                int[] cycle = cycles[c];
                for (int i = 0; i < cycle.Length; i++)
                {
                    int gate = cycle[i];
                    for (int o = _circuit.GateOutputOffsets[gate]; o < _circuit.GateOutputOffsets[gate + 1]; o++)
                    {
                        int net = outputNets[o];
                        if (_nets[net] != Signal.Unknown)
                        {
                            continue;
                        }

                        SetSourceNet(net, Signal.Low);
                        ResolvedLoopNets++;
                        SettleResult step = Settle();
                        evaluations += step.Evaluations;
                        waves += step.Waves;
                        oscillated |= step.Status == SettleStatus.Oscillating;
                    }
                }
            }

            return new SettleResult(oscillated ? SettleStatus.Oscillating : SettleStatus.Stable, evaluations, waves);
        }
    }
}
