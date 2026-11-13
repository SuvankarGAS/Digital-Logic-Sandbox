using System;
using System.Collections.Generic;
using System.Text;
using DigitalLogicSandbox.Components.Abstractions;
using DigitalLogicSandbox.Core.Circuit;
using DigitalLogicSandbox.Core.Connections;
using DigitalLogicSandbox.Core.Nodes;
using DigitalLogicSandbox.Core.Ports;
using DigitalLogicSandbox.Core.Validation;
using DigitalLogicSandbox.Utilities;

namespace DigitalLogicSandbox.Simulation.Engine
{
    /// <summary>
    /// Flattens a circuit hierarchy into a <see cref="CompiledCircuit"/>.
    /// <para>
    /// Every output pin in every expanded scope gets a raw net id. Custom-gate boundaries do not create logic;
    /// they are resolved by union-find: an inner input boundary is unioned with the net feeding the corresponding
    /// pin on the custom node, and the custom node's output pin is unioned with the net feeding the inner output
    /// boundary. Raw ids are then compacted. Expansion order is therefore irrelevant, and feedback loops through
    /// custom gates compile correctly (they are reported as warnings, and the engine guards them at runtime).
    /// </para>
    /// <para>
    /// Every placed custom gate is expanded separately, so instances never share state. Definitions themselves
    /// are shared by reference and never copied.
    /// </para>
    /// </summary>
    public sealed class CircuitCompiler
    {
        private const int RawFloating = 0;

        private readonly ComponentCatalog _catalog;
        private readonly SimulationOptions _options;

        private readonly List<int> _parent = new List<int>();
        private readonly List<GateBuild> _gates = new List<GateBuild>();
        private readonly List<CompiledInstance> _instances = new List<CompiledInstance>();
        private readonly HashSet<DefinitionId> _path = new HashSet<DefinitionId>();
        private readonly Dictionary<PortId, int> _rootInputs = new Dictionary<PortId, int>();
        private readonly Dictionary<PortId, int> _rootOutputs = new Dictionary<PortId, int>();
        private ValidationReport _report;
        private int _maxDepth;
        private bool _depthReported;

        public CircuitCompiler(ComponentCatalog catalog, SimulationOptions options = null)
        {
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            _options = options ?? SimulationOptions.Default;
        }

        public CompileResult Compile(CircuitDefinition root)
        {
            if (root == null)
            {
                throw new ArgumentNullException(nameof(root));
            }

            _report = new ValidationReport();
            _parent.Clear();
            _gates.Clear();
            _instances.Clear();
            _path.Clear();
            _rootInputs.Clear();
            _rootOutputs.Clear();
            _maxDepth = 0;
            _depthReported = false;
            _parent.Add(RawFloating);

            var rootInstance = new CompiledInstance(root, null, NodeId.None);
            _instances.Add(rootInstance);
            _path.Add(root.Id);
            ExpandScope(rootInstance, null, null);
            _path.Clear();

            ValidationReport report = _report;
            _report = null;
            if (report.HasErrors)
            {
                return new CompileResult(null, report);
            }

            CompiledCircuit circuit = Build(root, rootInstance, report);
            return new CompileResult(report.HasErrors ? null : circuit, report);
        }

        // ------------------------------------------------------------------------------------------------
        // Expansion
        // ------------------------------------------------------------------------------------------------

        /// <param name="boundInputs">For nested scopes: raw net feeding each interface input. Null for the root.</param>
        /// <param name="boundOutputs">For nested scopes: raw net of the custom node's output pins. Null for the root.</param>
        private void ExpandScope(CompiledInstance scope, Dictionary<PortId, int> boundInputs,
            Dictionary<PortId, int> boundOutputs)
        {
            CircuitDefinition definition = scope.Definition;
            IReadOnlyList<NodeDefinition> nodes = definition.Nodes;
            bool isRoot = scope.Parent == null;
            if (scope.Depth > _maxDepth)
            {
                _maxDepth = scope.Depth;
            }

            var signatures = new ComponentSignature[nodes.Count];

            // Pass 1: allocate a raw net for every output pin in this scope.
            for (int i = 0; i < nodes.Count; i++)
            {
                NodeDefinition node = nodes[i];
                if (!_catalog.TryGetSignature(node.Component, out ComponentSignature signature))
                {
                    Error(DiagnosticCode.MissingComponent, scope,
                        "Component '" + node.Component + "' used by " + node.Id + " in '" + scope.Path +
                        "' cannot be resolved.", node.Id, node.Component.Definition);
                    continue;
                }

                signatures[i] = signature;
                switch (node.Component.Kind)
                {
                    case ComponentKind.InputBoundary:
                    {
                        int raw = NewNet();
                        scope.SetRawOutputPinNet(new PinRef(node.Id, BoundarySignatures.Pin), raw);
                        if (isRoot)
                        {
                            _rootInputs[node.InterfacePort] = raw;
                        }
                        else if (boundInputs.TryGetValue(node.InterfacePort, out int outer))
                        {
                            Union(raw, outer);
                        }
                        else
                        {
                            Union(raw, RawFloating);
                        }

                        break;
                    }

                    case ComponentKind.OutputBoundary:
                        break;

                    default:
                    {
                        IReadOnlyList<PortDescriptor> outputs = signature.Outputs;
                        for (int p = 0; p < outputs.Count; p++)
                        {
                            scope.SetRawOutputPinNet(new PinRef(node.Id, outputs[p].Id), NewNet());
                        }

                        break;
                    }
                }
            }

            // Pass 2: wire gates, bind outputs, and expand custom gates.
            for (int i = 0; i < nodes.Count; i++)
            {
                NodeDefinition node = nodes[i];
                ComponentSignature signature = signatures[i];
                if (signature == null)
                {
                    continue;
                }

                switch (node.Component.Kind)
                {
                    case ComponentKind.Primitive:
                        AddGate(scope, node, signature);
                        break;

                    case ComponentKind.OutputBoundary:
                    {
                        int raw = ReadRaw(scope, new PinRef(node.Id, BoundarySignatures.Pin));
                        if (isRoot)
                        {
                            _rootOutputs[node.InterfacePort] = raw;
                        }
                        else if (boundOutputs.TryGetValue(node.InterfacePort, out int outer))
                        {
                            Union(outer, raw);
                        }

                        break;
                    }

                    case ComponentKind.Custom:
                        ExpandCustom(scope, node, signature);
                        break;
                }
            }
        }

        private void AddGate(CompiledInstance scope, NodeDefinition node, ComponentSignature signature)
        {
            if (!_catalog.TryGetPrimitive(node.Component, out IPrimitiveComponent primitive))
            {
                Error(DiagnosticCode.MissingComponent, scope,
                    "Primitive '" + node.Component + "' in '" + scope.Path + "' has no evaluator.", node.Id, default);
                return;
            }

            IReadOnlyList<PortDescriptor> inputs = signature.Inputs;
            IReadOnlyList<PortDescriptor> outputs = signature.Outputs;
            var rawInputs = new int[inputs.Count];
            var rawOutputs = new int[outputs.Count];
            for (int p = 0; p < inputs.Count; p++)
            {
                rawInputs[p] = ReadRaw(scope, new PinRef(node.Id, inputs[p].Id));
            }

            for (int p = 0; p < outputs.Count; p++)
            {
                scope.TryGetOutputPinNet(new PinRef(node.Id, outputs[p].Id), out rawOutputs[p]);
            }

            _gates.Add(new GateBuild(primitive, rawInputs, rawOutputs, scope, node.Id));
        }

        private void ExpandCustom(CompiledInstance scope, NodeDefinition node, ComponentSignature signature)
        {
            DefinitionRef reference = node.Component.Definition;

            if (_path.Contains(reference.Id))
            {
                Error(DiagnosticCode.CircularReference, scope,
                    "Circular reference: " + node.Id + " in '" + scope.Path + "' instantiates " + reference +
                    ", which contains itself.", node.Id, reference);
                return;
            }

            if (scope.Depth + 1 > _options.MaxHierarchyDepth)
            {
                if (!_depthReported)
                {
                    _depthReported = true;
                    Error(DiagnosticCode.HierarchyTooDeep, scope,
                        "Custom gates are nested more than " + _options.MaxHierarchyDepth + " levels deep (at '" +
                        scope.Path + "').", node.Id, reference);
                }

                return;
            }

            if (!_catalog.TryGetDefinition(reference, out CircuitDefinition child))
            {
                Error(DiagnosticCode.MissingDefinition, scope,
                    "Definition " + reference + " used in '" + scope.Path + "' is missing.", node.Id, reference);
                return;
            }

            var boundInputs = new Dictionary<PortId, int>(signature.InputCount);
            IReadOnlyList<PortDescriptor> inputs = signature.Inputs;
            for (int p = 0; p < inputs.Count; p++)
            {
                boundInputs[inputs[p].Id] = ReadRaw(scope, new PinRef(node.Id, inputs[p].Id));
            }

            var boundOutputs = new Dictionary<PortId, int>(signature.OutputCount);
            IReadOnlyList<PortDescriptor> outputs = signature.Outputs;
            for (int p = 0; p < outputs.Count; p++)
            {
                scope.TryGetOutputPinNet(new PinRef(node.Id, outputs[p].Id), out int raw);
                boundOutputs[outputs[p].Id] = raw;
            }

            var instance = new CompiledInstance(child, scope, node.Id);
            scope.AddChild(node.Id, instance);
            _instances.Add(instance);

            _path.Add(reference.Id);
            ExpandScope(instance, boundInputs, boundOutputs);
            _path.Remove(reference.Id);
        }

        /// <summary>Raw net read by an input pin in <paramref name="scope"/>: its driver's net, or floating.</summary>
        private int ReadRaw(CompiledInstance scope, PinRef inputPin)
        {
            if (!scope.Definition.TryGetConnectionTo(inputPin, out ConnectionDefinition connection))
            {
                return RawFloating;
            }

            if (scope.TryGetOutputPinNet(connection.Source, out int raw))
            {
                return raw;
            }

            _report.Add(new Diagnostic(DiagnosticSeverity.Warning, DiagnosticCode.InvalidSourcePort,
                "Connection " + connection + " in '" + scope.Path + "' starts at an unknown output pin; treated as unconnected.",
                scope.Parent == null ? connection.Destination.Node : scope.RootNode(), connection.Destination));
            return RawFloating;
        }

        // ------------------------------------------------------------------------------------------------
        // Build
        // ------------------------------------------------------------------------------------------------

        private CompiledCircuit Build(CircuitDefinition rootDefinition, CompiledInstance rootInstance, ValidationReport report)
        {
            // Compact union-find classes into dense net indices; the floating class is always net 0.
            int rawCount = _parent.Count;
            var denseOfRoot = new int[rawCount];
            for (int i = 0; i < rawCount; i++)
            {
                denseOfRoot[i] = -1;
            }

            denseOfRoot[RawFloating] = CompiledCircuit.FloatingNet;
            int netCount = 1;
            var rawToDense = new int[rawCount];
            for (int raw = 0; raw < rawCount; raw++)
            {
                int root = Find(raw);
                if (denseOfRoot[root] < 0)
                {
                    denseOfRoot[root] = netCount++;
                }

                rawToDense[raw] = denseOfRoot[root];
            }

            int gateCount = _gates.Count;
            var components = new IPrimitiveComponent[gateCount];
            var gateInstances = new CompiledInstance[gateCount];
            var gateNodes = new NodeId[gateCount];
            var inputOffsets = new int[gateCount + 1];
            var outputOffsets = new int[gateCount + 1];
            for (int g = 0; g < gateCount; g++)
            {
                inputOffsets[g + 1] = inputOffsets[g] + _gates[g].Inputs.Length;
                outputOffsets[g + 1] = outputOffsets[g] + _gates[g].Outputs.Length;
            }

            var inputNets = new int[inputOffsets[gateCount]];
            var outputNets = new int[outputOffsets[gateCount]];
            var driver = new int[netCount];
            for (int n = 0; n < netCount; n++)
            {
                driver[n] = -1;
            }

            for (int g = 0; g < gateCount; g++)
            {
                GateBuild gate = _gates[g];
                components[g] = gate.Component;
                gateInstances[g] = gate.Instance;
                gateNodes[g] = gate.Node;
                for (int p = 0; p < gate.Inputs.Length; p++)
                {
                    inputNets[inputOffsets[g] + p] = rawToDense[gate.Inputs[p]];
                }

                for (int p = 0; p < gate.Outputs.Length; p++)
                {
                    int net = rawToDense[gate.Outputs[p]];
                    outputNets[outputOffsets[g] + p] = net;
                    if (net == CompiledCircuit.FloatingNet || driver[net] >= 0)
                    {
                        report.Add(new Diagnostic(DiagnosticSeverity.Error, DiagnosticCode.MultipleDrivers,
                            "Internal error: net " + net + " has more than one driver (" + gate.Instance.Path + "/" +
                            gate.Node + ")."));
                        continue;
                    }

                    driver[net] = g;
                }
            }

            // Root interface.
            IReadOnlyList<InterfacePort> rootPorts = rootDefinition.Inputs;
            var rootInputNets = new int[rootPorts.Count];
            var rootInputPorts = new PortId[rootPorts.Count];
            for (int i = 0; i < rootPorts.Count; i++)
            {
                rootInputPorts[i] = rootPorts[i].Id;
                rootInputNets[i] = _rootInputs.TryGetValue(rootPorts[i].Id, out int raw)
                    ? rawToDense[raw]
                    : CompiledCircuit.FloatingNet;
                if (rootInputNets[i] != CompiledCircuit.FloatingNet && driver[rootInputNets[i]] >= 0)
                {
                    report.Add(new Diagnostic(DiagnosticSeverity.Error, DiagnosticCode.MultipleDrivers,
                        "Internal error: input '" + rootPorts[i].Name + "' is also driven by a gate."));
                }
            }

            rootPorts = rootDefinition.Outputs;
            var rootOutputNets = new int[rootPorts.Count];
            var rootOutputPorts = new PortId[rootPorts.Count];
            for (int i = 0; i < rootPorts.Count; i++)
            {
                rootOutputPorts[i] = rootPorts[i].Id;
                rootOutputNets[i] = _rootOutputs.TryGetValue(rootPorts[i].Id, out int raw)
                    ? rawToDense[raw]
                    : CompiledCircuit.FloatingNet;
            }

            // Fan-out (net -> reading gates), each gate listed once per net.
            var readerOffsets = new int[netCount + 1];
            var lastReader = new int[netCount];
            for (int n = 0; n < netCount; n++)
            {
                lastReader[n] = -1;
            }

            for (int g = 0; g < gateCount; g++)
            {
                for (int e = inputOffsets[g]; e < inputOffsets[g + 1]; e++)
                {
                    int net = inputNets[e];
                    if (lastReader[net] != g)
                    {
                        lastReader[net] = g;
                        readerOffsets[net + 1]++;
                    }
                }
            }

            for (int n = 0; n < netCount; n++)
            {
                readerOffsets[n + 1] += readerOffsets[n];
                lastReader[n] = -1;
            }

            var readers = new int[readerOffsets[netCount]];
            var fill = new int[netCount];
            for (int g = 0; g < gateCount; g++)
            {
                for (int e = inputOffsets[g]; e < inputOffsets[g + 1]; e++)
                {
                    int net = inputNets[e];
                    if (lastReader[net] != g)
                    {
                        lastReader[net] = g;
                        readers[readerOffsets[net] + fill[net]++] = g;
                    }
                }
            }

            for (int i = 0; i < _instances.Count; i++)
            {
                _instances[i].RemapNets(rawToDense);
            }

            List<int[]> cycles = FindGateCycles(gateCount, outputOffsets, outputNets, readerOffsets, readers);

            var circuit = new CompiledCircuit(rootDefinition, rootInstance, netCount, components, inputOffsets, inputNets,
                outputOffsets, outputNets, readerOffsets, readers, driver, gateInstances, gateNodes, rootInputNets,
                rootInputPorts, rootOutputNets, rootOutputPorts, _maxDepth, cycles);

            for (int c = 0; c < cycles.Count; c++)
            {
                int[] cycle = cycles[c];
                var builder = new StringBuilder();
                builder.Append("Feedback loop through ").Append(cycle.Length).Append(" gate(s): ");
                int shown = Math.Min(cycle.Length, 5);
                for (int i = 0; i < shown; i++)
                {
                    if (i > 0)
                    {
                        builder.Append(", ");
                    }

                    builder.Append(circuit.DescribeGate(cycle[i]));
                }

                if (cycle.Length > shown)
                {
                    builder.Append(", …");
                }

                builder.Append(". The circuit may oscillate.");
                report.Add(new Diagnostic(DiagnosticSeverity.Warning, DiagnosticCode.CombinationalCycle,
                    builder.ToString(), RootNodeOf(gateInstances[cycle[0]], gateNodes[cycle[0]])));
            }

            return circuit;
        }

        private static List<int[]> FindGateCycles(int gateCount, int[] outputOffsets, int[] outputNets,
            int[] readerOffsets, int[] readers)
        {
            var offsets = new int[gateCount + 1];
            for (int g = 0; g < gateCount; g++)
            {
                int edges = 0;
                for (int o = outputOffsets[g]; o < outputOffsets[g + 1]; o++)
                {
                    int net = outputNets[o];
                    edges += readerOffsets[net + 1] - readerOffsets[net];
                }

                offsets[g + 1] = offsets[g] + edges;
            }

            var targets = new int[offsets[gateCount]];
            for (int g = 0; g < gateCount; g++)
            {
                int position = offsets[g];
                for (int o = outputOffsets[g]; o < outputOffsets[g + 1]; o++)
                {
                    int net = outputNets[o];
                    for (int r = readerOffsets[net]; r < readerOffsets[net + 1]; r++)
                    {
                        targets[position++] = readers[r];
                    }
                }
            }

            var cycles = new List<int[]>();
            CycleFinder.FindCyclicComponents(gateCount, offsets, targets, cycles);
            return cycles;
        }

        private static NodeId RootNodeOf(CompiledInstance instance, NodeId node)
        {
            return instance.Parent == null ? node : instance.RootNode();
        }

        // ------------------------------------------------------------------------------------------------
        // Union-find
        // ------------------------------------------------------------------------------------------------

        private int NewNet()
        {
            int id = _parent.Count;
            _parent.Add(id);
            return id;
        }

        private int Find(int x)
        {
            int root = x;
            while (_parent[root] != root)
            {
                root = _parent[root];
            }

            while (_parent[x] != root)
            {
                int next = _parent[x];
                _parent[x] = root;
                x = next;
            }

            return root;
        }

        /// <summary>Merges two classes; the floating class always stays the representative.</summary>
        private void Union(int a, int b)
        {
            int rootA = Find(a);
            int rootB = Find(b);
            if (rootA == rootB)
            {
                return;
            }

            if (rootA == RawFloating)
            {
                _parent[rootB] = RawFloating;
            }
            else
            {
                _parent[rootA] = rootB;
            }
        }

        private void Error(DiagnosticCode code, CompiledInstance scope, string message, NodeId node, DefinitionRef definition)
        {
            NodeId reportNode = scope.Parent == null ? node : scope.RootNode();
            _report.Add(new Diagnostic(DiagnosticSeverity.Error, code, message, reportNode, default, definition));
        }

        private readonly struct GateBuild
        {
            public GateBuild(IPrimitiveComponent component, int[] inputs, int[] outputs, CompiledInstance instance, NodeId node)
            {
                Component = component;
                Inputs = inputs;
                Outputs = outputs;
                Instance = instance;
                Node = node;
            }

            public IPrimitiveComponent Component { get; }

            public int[] Inputs { get; }

            public int[] Outputs { get; }

            public CompiledInstance Instance { get; }

            public NodeId Node { get; }
        }
    }
}
