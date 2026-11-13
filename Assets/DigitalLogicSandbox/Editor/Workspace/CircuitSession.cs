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
