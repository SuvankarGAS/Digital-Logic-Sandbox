using System;
using DigitalLogicSandbox.Components.Abstractions;
using DigitalLogicSandbox.Core.Circuit;
using DigitalLogicSandbox.Core.Nodes;
using DigitalLogicSandbox.Core.Signals;
using DigitalLogicSandbox.Core.Validation;
using DigitalLogicSandbox.Simulation.Engine;

namespace DigitalLogicSandbox.CircuitEditor.Workspace
{
    /// <summary>
    /// Links an edited circuit with its live simulation. Structural model changes only mark the session dirty;
    /// <see cref="Rebuild"/> (called once per frame at most) recompiles and reloads the runner. Input values are kept
    /// by port id across rebuilds. Moves and renames never trigger recompilation. Contains no Unity objects.
    /// </summary>
    public sealed class CircuitSession : IDisposable
    {
        private readonly CircuitCompiler _compiler;
        private readonly SimulationRunner _runner;

        public CircuitSession(ComponentCatalog catalog, SimulationOptions options = null)
        {
            if (catalog == null)
            {
                throw new ArgumentNullException(nameof(catalog));
            }

            _compiler = new CircuitCompiler(catalog, options);
            _runner = new SimulationRunner(options);
        }

        /// <summary>Raised after every rebuild (successful or not).</summary>
        public event Action Rebuilt;

        public CircuitDefinition Circuit { get; private set; }

        public SimulationRunner Runner => _runner;

        /// <summary>Report of the last compilation (errors block simulation; warnings do not).</summary>
        public ValidationReport Report { get; private set; } = new ValidationReport();

        /// <summary>The compiled circuit currently simulated, or null when the last compilation failed.</summary>
        public CompiledCircuit Compiled { get; private set; }

        public bool IsSimulating => Compiled != null;

        public bool IsDirty { get; private set; }

        /// <summary>Incremented on every rebuild; lets observers detect a reloaded simulation.</summary>
        public int Revision { get; private set; }

        public void Bind(CircuitDefinition circuit)
        {
            if (Circuit != null)
            {
                Circuit.Changed -= OnCircuitChanged;
            }

            Circuit = circuit;
            Compiled = null;
            IsDirty = circuit != null;
            if (circuit != null)
            {
                circuit.Changed += OnCircuitChanged;
            }
        }

        /// <summary>Forces a recompile on the next <see cref="RebuildIfDirty"/> (e.g. the gate library changed).</summary>
        public void MarkDirty()
        {
            IsDirty = Circuit != null;
        }

        public bool RebuildIfDirty()
        {
            if (!IsDirty)
            {
                return false;
            }

            Rebuild();
            return true;
        }

        public void Rebuild()
        {
            IsDirty = false;
            Revision++;
            if (Circuit == null)
            {
                Compiled = null;
                Report = new ValidationReport();
                Rebuilt?.Invoke();
                return;
            }

            CompileResult result = _compiler.Compile(Circuit);
            Report = result.Report;
            Compiled = result.Circuit;
            if (result.Succeeded)
            {
                _runner.Load(result.Circuit);
            }

            Rebuilt?.Invoke();
        }

        /// <summary>Current value of the input boundary <paramref name="boundary"/>, or LOW when not simulated.</summary>
        public Signal GetInputValue(NodeId boundary)
        {
            int index = IndexOfInputBoundary(boundary);
            return index >= 0 ? _runner.Context.GetInput(index) : Signal.Low;
        }

        /// <summary>Flips an input boundary switch. Returns false if the node is not a simulated input.</summary>
        public bool ToggleInput(NodeId boundary)
        {
            int index = IndexOfInputBoundary(boundary);
            if (index < 0)
            {
                return false;
            }

            _runner.ToggleInput(index);
            return true;
        }

        public bool SetInput(NodeId boundary, Signal value)
        {
            int index = IndexOfInputBoundary(boundary);
            if (index < 0)
            {
                return false;
            }

            _runner.SetInput(index, value);
            return true;
        }

        /// <summary>Bit width of a boundary node (1 for anything else or an unknown node).</summary>
        public int GetBoundaryWidth(NodeId boundary)
        {
            return Circuit != null && Circuit.TryGetNode(boundary, out NodeDefinition node) ? node.Component.Width : 1;
        }

        /// <summary>All bits of an input boundary as an unsigned value (bit 0 = least significant); 0 when not simulated.</summary>
        public ulong GetInputBits(NodeId boundary)
        {
            int index = IndexOfInputBoundary(boundary);
            if (index < 0)
            {
                return 0;
            }

            ulong value = 0;
            int width = GetBoundaryWidth(boundary);
            for (int b = 0; b < width; b++)
            {
                if (_runner.Context.GetInput(index + b) == Signal.High)
                {
                    value |= 1UL << b;
                }
            }

            return value;
        }

        /// <summary>Sets all bits of an input boundary. Returns false if the node is not a simulated input.</summary>
        public bool SetInputBits(NodeId boundary, ulong value)
        {
            int index = IndexOfInputBoundary(boundary);
            if (index < 0)
            {
                return false;
            }

            int width = GetBoundaryWidth(boundary);
            for (int b = 0; b < width; b++)
            {
                _runner.SetInput(index + b, ((value >> b) & 1UL) != 0 ? Signal.High : Signal.Low);
            }

            return true;
        }

        /// <summary>One bit of an input or output boundary, or LOW when not simulated.</summary>
        public Signal GetBoundaryBit(NodeId boundary, int bit)
        {
            if (!IsSimulating || Circuit == null || !Circuit.TryGetNode(boundary, out NodeDefinition node) ||
                bit < 0 || bit >= node.Component.Width)
            {
                return Signal.Low;
            }

            int index;
            switch (node.Component.Kind)
            {
                case ComponentKind.InputBoundary:
                    index = Compiled.IndexOfInput(node.InterfacePort, bit);
                    return index >= 0 ? _runner.Context.GetInput(index) : Signal.Low;
                case ComponentKind.OutputBoundary:
                    index = Compiled.IndexOfOutput(node.InterfacePort, bit);
                    return index >= 0 ? _runner.Context.GetOutput(index) : Signal.Low;
                default:
                    return Signal.Low;
            }
        }

        /// <summary>
        /// All bits of an output boundary as an unsigned value. Returns false when not simulated or when a bit is
        /// not binary (UNKNOWN or Z).
        /// </summary>
        public bool TryGetOutputBits(NodeId boundary, out ulong value)
        {
            value = 0;
            if (!IsSimulating || Circuit == null || !Circuit.TryGetNode(boundary, out NodeDefinition node) ||
                node.Component.Kind != ComponentKind.OutputBoundary)
            {
                return false;
            }

            return _runner.Context.TryGetOutputValue(node.InterfacePort, out value);
        }

        /// <summary>
        /// Display text of a bus boundary: decimal value and bits (most significant first), e.g. "10 (1010)", or
        /// "? (1X10)" when a bit is not binary. Single-bit boundaries give their signal character.
        /// </summary>
        public string FormatBoundaryValue(NodeId boundary)
        {
            int width = GetBoundaryWidth(boundary);
            var bits = new System.Text.StringBuilder(width + 24);
            ulong value = 0;
            bool binary = true;
            for (int bit = width - 1; bit >= 0; bit--)
            {
                Signal signal = GetBoundaryBit(boundary, bit);
                bits.Append(signal.ToDisplayChar());
                if (signal == Signal.High)
                {
                    value |= 1UL << bit;
                }
                else if (signal != Signal.Low)
                {
                    binary = false;
                }
            }

            if (width == 1)
            {
                return bits.ToString();
            }

            return (binary ? value.ToString(System.Globalization.CultureInfo.InvariantCulture) : "?") + " (" + bits + ")";
        }

        /// <summary>Value at an output boundary, or LOW when not simulated.</summary>
        public Signal GetOutputValue(NodeId boundary)
        {
            if (!IsSimulating || Circuit == null || !Circuit.TryGetNode(boundary, out NodeDefinition node) ||
                node.Component.Kind != ComponentKind.OutputBoundary)
            {
                return Signal.Low;
            }

            int index = Compiled.IndexOfOutput(node.InterfacePort);
            return index >= 0 ? _runner.Context.GetOutput(index) : Signal.Low;
        }

        public void Dispose()
        {
            Bind(null);
        }

        private int IndexOfInputBoundary(NodeId boundary)
        {
            if (!IsSimulating || Circuit == null || !Circuit.TryGetNode(boundary, out NodeDefinition node) ||
                node.Component.Kind != ComponentKind.InputBoundary)
            {
                return -1;
            }

            return Compiled.IndexOfInput(node.InterfacePort);
        }

        private void OnCircuitChanged(CircuitChange change)
        {
            switch (change.Kind)
            {
                case CircuitChangeKind.NodeMoved:
                case CircuitChangeKind.NodeRotated:
                case CircuitChangeKind.NodeLabelChanged:
                case CircuitChangeKind.PropertiesChanged:
                case CircuitChangeKind.InterfacePortRenamed:
                    return;
                default:
                    IsDirty = true;
                    return;
            }
        }
    }
}
