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
    /// Changing an input marks only the gates reading that net dirty. Dirty gates are evaluated in FIFO order;
    /// a gate's fan-out is scheduled only when one of its outputs actually changes. FIFO order groups work into
    /// propagation <em>waves</em>, which tag every recorded <see cref="SignalChange"/>.
    /// </para>
    /// <para>
    /// After construction no method on the propagation path allocates: buffers are preallocated from the compiled
    /// topology. Contexts are fully independent; any number can share one compiled circuit.
    /// </para>
    /// <para>
    /// This is the combinational phase. A future sequential phase (clocked commit) will run between settles,
    /// without changing this loop.
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
        private readonly SignalChangeLog _changes;
        private readonly List<int> _unstableGates = new List<int>();
        private readonly int _evaluationBudget;

        private int _currentWave;
        private int _remainingInWave;

        public SimulationContext(CompiledCircuit circuit, SimulationOptions options = null)
        {
            _circuit = circuit ?? throw new ArgumentNullException(nameof(circuit));
            options ??= SimulationOptions.Default;

            _nets = new Signal[circuit.NetCount];
            _queued = new bool[circuit.GateCount];
            _queue = new IntRingQueue(Math.Max(1, circuit.GateCount));
            _inputScratch = new Signal[Math.Max(1, circuit.MaxGateInputs)];
            _outputScratch = new Signal[Math.Max(1, circuit.MaxGateOutputs)];
            _changes = new SignalChangeLog(256, options.MaxRecordedChanges);
            _evaluationBudget = options.EvaluationBudgetFor(circuit.GateCount);
            RecordChanges = options.RecordChanges;

            Reset();
        }

        public CompiledCircuit Circuit => _circuit;

        /// <summary>Net changes since the consumer last called <see cref="SignalChangeLog.Clear"/>.</summary>
        public SignalChangeLog Changes => _changes;

        /// <summary>When false, no changes are recorded (faster headless analysis).</summary>
        public bool RecordChanges { get; set; }

        /// <summary>True while dirty gates are waiting to be evaluated.</summary>
        public bool HasPendingWork => _queue.Count > 0;

        /// <summary>Total gate evaluations since construction or the last <see cref="Reset"/>.</summary>
        public long TotalEvaluations { get; private set; }

        /// <summary>Result of the most recent <see cref="Settle"/> or <see cref="StepWave"/>.</summary>
        public SettleResult LastResult { get; private set; }

        /// <summary>The safety limit on evaluations per settle.</summary>
        public int EvaluationBudget => _evaluationBudget;

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
        /// Returns every net to LOW (including inputs), then evaluates every gate once and settles so that the
        /// state is consistent (e.g. NOT outputs HIGH). The change log is cleared afterwards: consumers should
        /// treat a reset as a full refresh rather than animate it.
        /// </summary>
        public SettleResult Reset()
        {
            Array.Clear(_nets, 0, _nets.Length);
            Array.Clear(_queued, 0, _queued.Length);
            _queue.Clear();
            _unstableGates.Clear();
            _currentWave = 0;
            _remainingInWave = 0;
            TotalEvaluations = 0;

            for (int g = 0; g < _circuit.GateCount; g++)
            {
                _queued[g] = true;
                _queue.Enqueue(g);
            }

            bool record = RecordChanges;
            RecordChanges = false;
            SettleResult result = Settle();
            RecordChanges = record;
            _changes.Clear();
            return result;
        }

        /// <summary>
        /// Sets a root input (interface order) and schedules the gates that read it. Does not propagate; call
        /// <see cref="Settle"/> or <see cref="StepWave"/>. Returns true if the value changed.
        /// </summary>
        public bool SetInput(int index, Signal value)
        {
            int net = _circuit.InputNets[index];
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

        public bool SetInput(PortId port, Signal value)
        {
            int index = _circuit.IndexOfInput(port);
            if (index < 0)
            {
                throw new ArgumentException("Port " + port + " is not an input of the root circuit.", nameof(port));
            }

            return SetInput(index, value);
        }

        /// <summary>Propagates until stable or until the evaluation budget is exhausted (oscillation).</summary>
        public SettleResult Settle()
        {
            int evaluations = 0;
            int waves = 0;
            _unstableGates.Clear();

            while (_queue.Count > 0)
            {
                if (_remainingInWave == 0)
                {
                    _currentWave++;
                    waves++;
                    _remainingInWave = _queue.Count;
                }

                if (evaluations >= _evaluationBudget)
                {
                    return Abort(evaluations, waves);
                }

                EvaluateNext();
                evaluations++;
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

            if (_remainingInWave == 0)
            {
                _currentWave++;
                _remainingInWave = _queue.Count;
            }

            int evaluations = 0;
            while (_remainingInWave > 0)
            {
                EvaluateNext();
                evaluations++;
            }

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

        private void EvaluateNext()
        {
            int gate = _queue.Dequeue();
            _remainingInWave--;
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

            int[] outputNets = _circuit.GateOutputNets;
            for (int o = 0; o < outputCount; o++)
            {
                int net = outputNets[outputStart + o];
                Signal value = _outputScratch[o];
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

        private SettleResult Abort(int evaluations, int waves)
        {
            for (int i = 0; i < _queue.Count; i++)
            {
                _unstableGates.Add(_queue.PeekAt(i));
            }

            for (int i = 0; i < _unstableGates.Count; i++)
            {
                _queued[_unstableGates[i]] = false;
            }

            _queue.Clear();
            _remainingInWave = 0;
            _currentWave = 0;
            LastResult = new SettleResult(SettleStatus.Oscillating, evaluations, waves);
            return LastResult;
        }
    }
}
