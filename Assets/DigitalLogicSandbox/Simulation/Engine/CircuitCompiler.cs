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
        private readonly List<int> _floatingTies = new List<int>();
        private readonly List<GateBuild> _gates = new List<GateBuild>();
        private readonly List<CompiledInstance> _instances = new List<CompiledInstance>();
        private readonly HashSet<DefinitionId> _path = new HashSet<DefinitionId>();
        private readonly Dictionary<PortId, int[]> _rootInputs = new Dictionary<PortId, int[]>();
        private readonly Dictionary<PortId, int[]> _rootOutputs = new Dictionary<PortId, int[]>();
        private ValidationReport _report;
        private int _maxDepth;
        private bool _depthReported;
        private int _rawClock;

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
            _floatingTies.Clear();
            _gates.Clear();
            _instances.Clear();
            _path.Clear();
            _rootInputs.Clear();
            _rootOutputs.Clear();
            _maxDepth = 0;
            _depthReported = false;
            _rawClock = -1;
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

        /// <param name="boundInputs">For nested scopes: raw nets (per bit) feeding each interface input. Null for the root.</param>
        /// <param name="boundOutputs">For nested scopes: raw nets (per bit) of the custom node's output pins. Null for the root.</param>
        private void ExpandScope(CompiledInstance scope, Dictionary<PortId, int[]> boundInputs,
            Dictionary<PortId, int[]> boundOutputs)
        {
            CircuitDefinition definition = scope.Definition;
            IReadOnlyList<NodeDefinition> nodes = definition.Nodes;
            bool isRoot = scope.Parent == null;
            if (scope.Depth > _maxDepth)
            {
                _maxDepth = scope.Depth;
            }

            var signatures = new ComponentSignature[nodes.Count];

            // Pass 1: allocate one raw net per bit of every output pin in this scope.
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
                IReadOnlyList<PortDescriptor> outputs = signature.Outputs;
                for (int p = 0; p < outputs.Count; p++)
                {
                    scope.SetRawOutputPinNets(new PinRef(node.Id, outputs[p].Id), NewNets(outputs[p].Width));
                }

                switch (node.Component.Kind)
                {
                    case ComponentKind.InputBoundary:
                    {
                        int[] raw = scope.GetRawOutputPinNets(new PinRef(node.Id, BoundarySignatures.Pin));
                        if (isRoot)
                        {
                            _rootInputs[node.InterfacePort] = raw;
                        }
                        else if (boundInputs.TryGetValue(node.InterfacePort, out int[] outer))
                        {
                            UnionBits(raw, outer);
                        }
                        else
                        {
                            UnionBits(raw, null);
                        }

                        break;
                    }

                    case ComponentKind.Clock:
                    {
                        // Every clock node, in every scope, carries the one global clock: union them into one net.
                        int raw = scope.GetRawOutputPinNets(new PinRef(node.Id, BoundarySignatures.Pin))[0];
                        if (_rawClock < 0)
                        {
                            _rawClock = raw;
                        }
                        else
                        {
                            Union(raw, _rawClock);
                        }

                        break;
                    }
                }
            }

            // Pass 2: wire gates and bus utilities, bind outputs, and expand custom gates.
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
                    case ComponentKind.TriState:
                        AddGates(scope, node, signature);
                        break;

                    case ComponentKind.OutputBoundary:
                    {
                        int width = signature.Inputs[0].Width;
                        int[] raw = ReadRawBits(scope, new PinRef(node.Id, BoundarySignatures.Pin), width);
                        if (isRoot)
                        {
                            _rootOutputs[node.InterfacePort] = raw;
                        }
                        else if (boundOutputs.TryGetValue(node.InterfacePort, out int[] outer))
                        {
                            UnionBits(outer, raw);
                        }

                        break;
                    }

                    case ComponentKind.Splitter:
                    {
                        // Pure wiring: each single-bit output is the same net as one bit of the bus.
                        int width = node.Component.Width;
                        int[] bus = ReadRawBits(scope, new PinRef(node.Id, BoundarySignatures.Pin), width);
                        for (int b = 0; b < width; b++)
                        {
                            Union(scope.GetRawOutputPinNets(new PinRef(node.Id, BoundarySignatures.BitPin(b)))[0], bus[b]);
                        }

                        break;
                    }

                    case ComponentKind.Merger:
                    {
                        int[] bus = scope.GetRawOutputPinNets(new PinRef(node.Id, BoundarySignatures.Pin));
                        for (int b = 0; b < bus.Length; b++)
                        {
                            Union(bus[b], ReadRawBits(scope, new PinRef(node.Id, BoundarySignatures.BitPin(b)), 1)[0]);
                        }

                        break;
                    }

                    case ComponentKind.BusJoin:
                    {
                        // All connected drivers share the joined nets; unconnected inputs contribute nothing.
                        int[] bus = scope.GetRawOutputPinNets(new PinRef(node.Id, BoundarySignatures.Pin));
                        for (int d = 0; d < node.Component.Arity; d++)
                        {
                            var pin = new PinRef(node.Id, BoundarySignatures.BitPin(d));
                            if (!scope.Definition.IsDriven(pin))
                            {
                                continue;
                            }

                            int[] driver = ReadRawBits(scope, pin, bus.Length);
                            for (int b = 0; b < bus.Length; b++)
                            {
                                if (driver[b] != RawFloating)
                                {
                                    Union(bus[b], driver[b]);
                                }
                            }
                        }

                        break;
                    }

                    case ComponentKind.Custom:
                        ExpandCustom(scope, node, signature);
                        break;
                }
            }
        }

        /// <summary>
        /// Adds the evaluators of a primitive or tri-state node. Bus pins are sliced: a node whose widest pin has
        /// <c>w</c> bits becomes <c>w</c> one-bit gates; single-bit pins (such as an enable) feed every slice.
        /// </summary>
        private void AddGates(CompiledInstance scope, NodeDefinition node, ComponentSignature signature)
        {
            if (!_catalog.TryGetPrimitive(node.Component, out IPrimitiveComponent primitive))
            {
                Error(DiagnosticCode.MissingComponent, scope,
                    "Primitive '" + node.Component + "' in '" + scope.Path + "' has no evaluator.", node.Id, default);
                return;
            }

            IReadOnlyList<PortDescriptor> inputs = signature.Inputs;
            IReadOnlyList<PortDescriptor> outputs = signature.Outputs;
            int slices = 1;
            var inputBits = new int[inputs.Count][];
            var outputBits = new int[outputs.Count][];
            for (int p = 0; p < inputs.Count; p++)
            {
                inputBits[p] = ReadRawBits(scope, new PinRef(node.Id, inputs[p].Id), inputs[p].Width);
                slices = Math.Max(slices, inputs[p].Width);
            }

            for (int p = 0; p < outputs.Count; p++)
            {
                outputBits[p] = scope.GetRawOutputPinNets(new PinRef(node.Id, outputs[p].Id));
                slices = Math.Max(slices, outputs[p].Width);
            }

            for (int b = 0; b < slices; b++)
            {
                var rawInputs = new int[inputs.Count];
                var rawOutputs = new int[outputs.Count];
                for (int p = 0; p < inputs.Count; p++)
                {
                    int[] bits = inputBits[p];
                    rawInputs[p] = bits.Length > 1 ? bits[b] : bits[0];
                }

                for (int p = 0; p < outputs.Count; p++)
                {
                    int[] bits = outputBits[p];
                    rawOutputs[p] = bits.Length > 1 ? bits[b] : bits[0];
                }

                _gates.Add(new GateBuild(primitive, rawInputs, rawOutputs, scope, node.Id));
            }
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

            var boundInputs = new Dictionary<PortId, int[]>(signature.InputCount);
            IReadOnlyList<PortDescriptor> inputs = signature.Inputs;
            for (int p = 0; p < inputs.Count; p++)
            {
                boundInputs[inputs[p].Id] = ReadRawBits(scope, new PinRef(node.Id, inputs[p].Id), inputs[p].Width);
            }

            var boundOutputs = new Dictionary<PortId, int[]>(signature.OutputCount);
            IReadOnlyList<PortDescriptor> outputs = signature.Outputs;
            for (int p = 0; p < outputs.Count; p++)
            {
                boundOutputs[outputs[p].Id] = scope.GetRawOutputPinNets(new PinRef(node.Id, outputs[p].Id));
            }

            var instance = new CompiledInstance(child, scope, node.Id);
            scope.AddChild(node.Id, instance);
            _instances.Add(instance);

            _path.Add(reference.Id);
            ExpandScope(instance, boundInputs, boundOutputs);
            _path.Remove(reference.Id);
        }

        /// <summary>
        /// Raw nets read by the <paramref name="width"/> bits of an input pin in <paramref name="scope"/>: its
        /// driver's nets, or floating. Bits the driver does not provide (a width mismatch, reported by validation)
        /// read floating.
        /// </summary>
        private int[] ReadRawBits(CompiledInstance scope, PinRef inputPin, int width)
        {
            var bits = new int[width]; // RawFloating (0) by default
            if (!scope.Definition.TryGetConnectionTo(inputPin, out ConnectionDefinition connection))
            {
                return bits;
            }

            int[] source = scope.GetRawOutputPinNets(connection.Source);
            if (source == null)
            {
                _report.Add(new Diagnostic(DiagnosticSeverity.Warning, DiagnosticCode.InvalidSourcePort,
                    "Connection " + connection + " in '" + scope.Path + "' starts at an unknown output pin; treated as unconnected.",
                    scope.Parent == null ? connection.Destination.Node : scope.RootNode(), connection.Destination));
                return bits;
            }

            int shared = Math.Min(width, source.Length);
            for (int b = 0; b < shared; b++)
            {
                bits[b] = source[b];
            }

            return bits;
        }

        /// <summary>Unions bit by bit; a null or shorter <paramref name="b"/> ties the remaining bits to floating.</summary>
        private void UnionBits(int[] a, int[] b)
        {
            for (int i = 0; i < a.Length; i++)
            {
                Union(a[i], b != null && i < b.Length ? b[i] : RawFloating);
            }
        }

        // ------------------------------------------------------------------------------------------------
        // Build
        // ------------------------------------------------------------------------------------------------

        private CompiledCircuit Build(CircuitDefinition rootDefinition, CompiledInstance rootInstance, ValidationReport report)
        {
            ApplyFloatingTies();

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
            var driverCount = new int[netCount];
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
                    if (net == CompiledCircuit.FloatingNet)
                    {
                        report.Add(new Diagnostic(DiagnosticSeverity.Error, DiagnosticCode.MultipleDrivers,
                            "Internal error: a gate output was merged with the floating net (" + gate.Instance.Path +
                            "/" + gate.Node + ")."));
                        continue;
                    }

                    if (driver[net] < 0)
                    {
                        driver[net] = g;
                    }

                    driverCount[net]++;
                }
            }

            // Shared buses (nets joined by a bus join): every driving output slot, resolved at runtime.
            var sharedOffsets = new int[netCount + 1];
            for (int n = 0; n < netCount; n++)
            {
                sharedOffsets[n + 1] = sharedOffsets[n] + (driverCount[n] > 1 ? driverCount[n] : 0);
            }

            var sharedSlots = new int[sharedOffsets[netCount]];
            var sharedFill = new int[netCount];
            for (int slot = 0; slot < outputNets.Length; slot++)
            {
                int net = outputNets[slot];
                if (net != CompiledCircuit.FloatingNet && driverCount[net] > 1)
                {
                    sharedSlots[sharedOffsets[net] + sharedFill[net]++] = slot;
                }
            }

            // External sources (root input bits, the clock) must stay the only driver of their net.
            var externalSource = new string[netCount];

            // Root interface, flattened to bits (bit 0 of each port first).
            IReadOnlyList<InterfacePort> rootPorts = rootDefinition.Inputs;
            var rootInputNets = new List<int>();
            var rootInputPorts = new List<PortId>();
            var rootInputBits = new List<int>();
            for (int i = 0; i < rootPorts.Count; i++)
            {
                InterfacePort port = rootPorts[i];
                _rootInputs.TryGetValue(port.Id, out int[] raw);
                for (int b = 0; b < port.Width; b++)
                {
                    int net = raw != null && b < raw.Length ? rawToDense[raw[b]] : CompiledCircuit.FloatingNet;
                    rootInputNets.Add(net);
                    rootInputPorts.Add(port.Id);
                    rootInputBits.Add(b);
                    ClaimExternal(externalSource, driverCount, net, "input '" + port.Name + "'", report);
                }
            }

            int clockNet = _rawClock >= 0 ? rawToDense[_rawClock] : CompiledCircuit.FloatingNet;
            ClaimExternal(externalSource, driverCount, clockNet, "the clock", report);

            rootPorts = rootDefinition.Outputs;
            var rootOutputNets = new List<int>();
            var rootOutputPorts = new List<PortId>();
            var rootOutputBits = new List<int>();
            for (int i = 0; i < rootPorts.Count; i++)
            {
                InterfacePort port = rootPorts[i];
                _rootOutputs.TryGetValue(port.Id, out int[] raw);
                for (int b = 0; b < port.Width; b++)
                {
                    rootOutputNets.Add(raw != null && b < raw.Length ? rawToDense[raw[b]] : CompiledCircuit.FloatingNet);
                    rootOutputPorts.Add(port.Id);
                    rootOutputBits.Add(b);
                }
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
                outputOffsets, outputNets, readerOffsets, readers, driver, gateInstances, gateNodes,
                rootInputNets.ToArray(), rootInputPorts.ToArray(), rootInputBits.ToArray(), rootOutputNets.ToArray(),
                rootOutputPorts.ToArray(), rootOutputBits.ToArray(), _maxDepth, cycles, clockNet, sharedOffsets,
                sharedSlots);

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

                builder.Append(". The circuit stores state (sequential logic); a loop that never settles is shown as unknown (X).");
                report.Add(new Diagnostic(DiagnosticSeverity.Warning, DiagnosticCode.FeedbackLoop,
                    builder.ToString(), RootNodeOf(gateInstances[cycle[0]], gateNodes[cycle[0]])));
            }

            return circuit;
        }

        /// <summary>
        /// Records that <paramref name="net"/> is driven from outside the gate graph. A bus join that ties such a
        /// net to gate outputs or to another external source is a user error (the value would be ambiguous).
        /// </summary>
        private static void ClaimExternal(string[] externalSource, int[] driverCount, int net, string name,
            ValidationReport report)
        {
            if (net == CompiledCircuit.FloatingNet)
            {
                return;
            }

            if (externalSource[net] != null || driverCount[net] > 0)
            {
                report.Add(new Diagnostic(DiagnosticSeverity.Error, DiagnosticCode.InvalidBusJoin,
                    "A bus join connects " + name + " to " + (externalSource[net] ?? "gate outputs") +
                    ". Circuit inputs and the clock cannot share a bus; drive the bus through tri-state drivers."));
                return;
            }

            externalSource[net] = name;
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

        private int[] NewNets(int width)
        {
            var nets = new int[width];
            for (int i = 0; i < width; i++)
            {
                nets[i] = NewNet();
            }

            return nets;
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

        /// <summary>
        /// Ties every undriven class that was asked to float to the floating net. Ties are deferred until all
        /// unions are known: a class that ends up with a real driver (a gate output, a root input or the clock,
        /// e.g. through a bus join) ignores them, so an unconnected bus-join input contributes nothing.
        /// </summary>
        private void ApplyFloatingTies()
        {
            if (_floatingTies.Count == 0)
            {
                return;
            }

            var driven = new bool[_parent.Count];
            for (int g = 0; g < _gates.Count; g++)
            {
                int[] outputs = _gates[g].Outputs;
                for (int p = 0; p < outputs.Length; p++)
                {
                    driven[Find(outputs[p])] = true;
                }
            }

            foreach (int[] raw in _rootInputs.Values)
            {
                for (int b = 0; b < raw.Length; b++)
                {
                    driven[Find(raw[b])] = true;
                }
            }

            if (_rawClock >= 0)
            {
                driven[Find(_rawClock)] = true;
            }

            for (int i = 0; i < _floatingTies.Count; i++)
            {
                int root = Find(_floatingTies[i]);
                if (root != RawFloating && !driven[root])
                {
                    _parent[root] = RawFloating;
                }
            }

            _floatingTies.Clear();
        }

        /// <summary>
        /// Merges two classes. A union with the floating net is deferred (see <see cref="ApplyFloatingTies"/>).
        /// </summary>
        private void Union(int a, int b)
        {
            if (a == RawFloating || b == RawFloating)
            {
                if (a != b)
                {
                    _floatingTies.Add(a == RawFloating ? b : a);
                }

                return;
            }

            int rootA = Find(a);
            int rootB = Find(b);
            if (rootA == rootB)
            {
                return;
            }

            _parent[rootA] = rootB;
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
