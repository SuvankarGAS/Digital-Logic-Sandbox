using System;
using System.Collections.Generic;
using DigitalLogicSandbox.Core.Circuit;
using DigitalLogicSandbox.Core.Nodes;
using DigitalLogicSandbox.Core.Ports;

namespace DigitalLogicSandbox.GateLibrary.Packaging
{
    /// <summary>One external port of the gate being packaged: which boundary it comes from and what it is called.</summary>
    public sealed class PortSpec
    {
        public PortSpec(PortId sourcePort, string name)
        {
            SourcePort = sourcePort;
            Name = name ?? string.Empty;
        }

        /// <summary>Interface port of the source circuit (identifies the boundary node). Kept as the gate's port id.</summary>
        public PortId SourcePort { get; }

        public string Name { get; set; }
    }

    /// <summary>
    /// What to package and how: gate metadata, the ordered and named external interface, and optionally a subset
    /// of nodes (the selection). Pure data, edited by the packaging dialog and validated by
    /// <see cref="CircuitPackager"/>.
    /// </summary>
    public sealed class PackagingSpec
    {
        public string Name { get; set; } = string.Empty;

        public string Description { get; set; } = string.Empty;

        public string Category { get; set; } = string.Empty;

        /// <summary>Short visual label (e.g. "≥1"). Purely cosmetic.</summary>
        public string Icon { get; set; } = string.Empty;

        /// <summary>External inputs in interface order.</summary>
        public List<PortSpec> Inputs { get; } = new List<PortSpec>();

        /// <summary>External outputs in interface order.</summary>
        public List<PortSpec> Outputs { get; } = new List<PortSpec>();

        /// <summary>Nodes to package, or null to package the whole circuit.</summary>
        public HashSet<NodeId> Nodes { get; private set; }

        public bool IsSubset => Nodes != null;

        /// <summary>
        /// Default spec for <paramref name="circuit"/> (or the subset <paramref name="nodes"/>): the boundaries in
        /// scope, in the circuit's interface order, with their current names.
        /// </summary>
        public static PackagingSpec FromCircuit(CircuitDefinition circuit, IEnumerable<NodeId> nodes = null)
        {
            if (circuit == null)
            {
                throw new ArgumentNullException(nameof(circuit));
            }

            var spec = new PackagingSpec();
            if (nodes != null)
            {
                spec.Nodes = new HashSet<NodeId>(nodes);
            }

            AddPorts(circuit, circuit.Inputs, spec.Inputs, spec.Nodes);
            AddPorts(circuit, circuit.Outputs, spec.Outputs, spec.Nodes);
            return spec;
        }

        /// <summary>Moves a port within its list (for reordering in the UI). Returns false if out of range.</summary>
        public static bool Move(List<PortSpec> ports, int from, int to)
        {
            if (from < 0 || from >= ports.Count || to < 0 || to >= ports.Count || from == to)
            {
                return false;
            }

            PortSpec port = ports[from];
            ports.RemoveAt(from);
            ports.Insert(to, port);
            return true;
        }

        public bool Contains(NodeId node)
        {
            return Nodes == null || Nodes.Contains(node);
        }

        private static void AddPorts(CircuitDefinition circuit, IReadOnlyList<InterfacePort> ports, List<PortSpec> target,
            HashSet<NodeId> subset)
        {
            for (int i = 0; i < ports.Count; i++)
            {
                if (subset != null &&
                    (!circuit.TryGetBoundaryNode(ports[i].Id, out NodeId node) || !subset.Contains(node)))
                {
                    continue;
                }

                target.Add(new PortSpec(ports[i].Id, ports[i].Name));
            }
        }
    }
}
