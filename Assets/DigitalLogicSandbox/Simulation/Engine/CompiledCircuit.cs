using System.Collections.Generic;
using System.Text;
using DigitalLogicSandbox.Components.Abstractions;
using DigitalLogicSandbox.Core.Circuit;
using DigitalLogicSandbox.Core.Nodes;
using DigitalLogicSandbox.Core.Ports;

namespace DigitalLogicSandbox.Simulation.Engine
{
    /// <summary>
    /// Immutable, flattened topology of a circuit hierarchy: primitive gate instances and the nets between them,
    /// in compact CSR arrays. Contains no signal state, so one compiled circuit can back any number of independent
    /// <see cref="Context.SimulationContext"/>s (the live editor, truth-table analysis, tests).
    /// </summary>
    public sealed class CompiledCircuit
    {
        /// <summary>Shared constant-LOW net read by every unconnected input. Never driven.</summary>
        public const int FloatingNet = 0;

        internal CompiledCircuit(
            CircuitDefinition rootDefinition,
            CompiledInstance root,
            int netCount,
            IPrimitiveComponent[] gateComponents,
            int[] gateInputOffsets,
            int[] gateInputNets,
            int[] gateOutputOffsets,
            int[] gateOutputNets,
            int[] netReaderOffsets,
            int[] netReaders,
            int[] netDriverGate,
            CompiledInstance[] gateInstances,
            NodeId[] gateNodes,
            int[] inputNets,
            PortId[] inputPorts,
            int[] outputNets,
            PortId[] outputPorts,
            int hierarchyDepth,
            List<int[]> combinationalCycles,
            int clockNet = FloatingNet)
        {
            RootDefinition = rootDefinition;
            Root = root;
            NetCount = netCount;
            GateComponents = gateComponents;
            GateInputOffsets = gateInputOffsets;
            GateInputNets = gateInputNets;
            GateOutputOffsets = gateOutputOffsets;
            GateOutputNets = gateOutputNets;
            NetReaderOffsets = netReaderOffsets;
            NetReaders = netReaders;
            NetDriverGate = netDriverGate;
            GateInstances = gateInstances;
            GateNodes = gateNodes;
            InputNets = inputNets;
            InputPorts = inputPorts;
            OutputNets = outputNets;
            OutputPorts = outputPorts;
            HierarchyDepth = hierarchyDepth;
            CombinationalCycles = combinationalCycles;
            ClockNet = clockNet;

            for (int g = 0; g < gateComponents.Length; g++)
            {
                int inputs = gateInputOffsets[g + 1] - gateInputOffsets[g];
                int outputs = gateOutputOffsets[g + 1] - gateOutputOffsets[g];
                if (inputs > MaxGateInputs)
                {
                    MaxGateInputs = inputs;
                }

                if (outputs > MaxGateOutputs)
                {
                    MaxGateOutputs = outputs;
                }
            }
        }

        public CircuitDefinition RootDefinition { get; }

        /// <summary>Root of the instance tree (hierarchy map) for per-instance signal inspection.</summary>
        public CompiledInstance Root { get; }

        public int NetCount { get; }

        public int GateCount => GateComponents.Length;

        /// <summary>Custom-gate nesting depth (0 = only primitives).</summary>
        public int HierarchyDepth { get; }

        public int InputCount => InputNets.Length;

        public int OutputCount => OutputNets.Length;

        /// <summary>Gate-index groups forming feedback loops in the flattened graph (empty for combinational circuits).</summary>
        public IReadOnlyList<int[]> CombinationalCycles { get; }

        public bool HasCombinationalCycles => CombinationalCycles.Count > 0;

        /// <summary>Net carrying the global clock, or <see cref="FloatingNet"/> when the circuit has no clock node.</summary>
        public int ClockNet { get; }

        public bool HasClock => ClockNet != FloatingNet;

        /// <summary>
        /// True when outputs can depend on history: the circuit has feedback loops (latches, flip-flops) or a clock.
        /// A combinational truth table does not describe such a circuit; use sequence analysis instead.
        /// </summary>
        public bool IsSequential => HasCombinationalCycles || HasClock;

        public int MaxGateInputs { get; }

        public int MaxGateOutputs { get; }

        internal IPrimitiveComponent[] GateComponents { get; }

        internal int[] GateInputOffsets { get; }

        internal int[] GateInputNets { get; }

        internal int[] GateOutputOffsets { get; }

        internal int[] GateOutputNets { get; }

        internal int[] NetReaderOffsets { get; }

        internal int[] NetReaders { get; }

        /// <summary>Driving gate per net, or -1 for root inputs and the floating net.</summary>
        internal int[] NetDriverGate { get; }

        internal CompiledInstance[] GateInstances { get; }

        internal NodeId[] GateNodes { get; }

        /// <summary>Net per root interface input, in interface order.</summary>
        internal int[] InputNets { get; }

        internal PortId[] InputPorts { get; }

        /// <summary>Net observed by each root interface output, in interface order.</summary>
        internal int[] OutputNets { get; }

        internal PortId[] OutputPorts { get; }

        public int GetInputNet(int index)
        {
            return InputNets[index];
        }

        public int GetOutputNet(int index)
        {
            return OutputNets[index];
        }

        public PortId GetInputPort(int index)
        {
            return InputPorts[index];
        }

        public PortId GetOutputPort(int index)
        {
            return OutputPorts[index];
        }

        /// <summary>Interface index of a root input port, or -1.</summary>
        public int IndexOfInput(PortId port)
        {
            for (int i = 0; i < InputPorts.Length; i++)
            {
                if (InputPorts[i] == port)
                {
                    return i;
                }
            }

            return -1;
        }

        /// <summary>Interface index of a root output port, or -1.</summary>
        public int IndexOfOutput(PortId port)
        {
            for (int i = 0; i < OutputPorts.Length; i++)
            {
                if (OutputPorts[i] == port)
                {
                    return i;
                }
            }

            return -1;
        }

        /// <summary>Number of gates that read the given net (fan-out).</summary>
        public int GetFanOut(int net)
        {
            return NetReaderOffsets[net + 1] - NetReaderOffsets[net];
        }

        /// <summary>Human-readable location of a flattened gate, e.g. "Main/N3:OR/N2:NAND/N4(builtin.and)".</summary>
        public string DescribeGate(int gate)
        {
            var builder = new StringBuilder();
            GateInstances[gate].AppendPath(builder);
            builder.Append('/').Append(GateNodes[gate]).Append('(').Append(GateComponents[gate].DisplayName).Append(')');
            return builder.ToString();
        }

        /// <summary>The scope and node a flattened gate came from.</summary>
        public void GetGateOrigin(int gate, out CompiledInstance instance, out NodeId node)
        {
            instance = GateInstances[gate];
            node = GateNodes[gate];
        }
    }
}
