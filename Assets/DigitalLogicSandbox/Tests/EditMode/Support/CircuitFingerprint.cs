using System.Collections.Generic;
using System.Globalization;
using System.Text;
using DigitalLogicSandbox.Core.Circuit;
using DigitalLogicSandbox.Core.Connections;
using DigitalLogicSandbox.Core.Nodes;
using DigitalLogicSandbox.Core.Ports;

namespace DigitalLogicSandbox.Tests.EditMode.Support
{
    /// <summary>
    /// Canonical text form of a circuit's structure (nodes, interface and connections, all with ids). Two equal
    /// fingerprints mean that undo restored the circuit exactly.
    /// </summary>
    internal static class CircuitFingerprint
    {
        public static string Of(CircuitDefinition circuit)
        {
            var builder = new StringBuilder();
            var nodes = new List<NodeDefinition>(circuit.Nodes);
            nodes.Sort((a, b) => a.Id.CompareTo(b.Id));
            foreach (NodeDefinition node in nodes)
            {
                builder.Append("N ").Append(node.Id).Append(' ').Append(node.Component)
                    .Append(" @").Append(node.Position.X.ToString(CultureInfo.InvariantCulture))
                    .Append(',').Append(node.Position.Y.ToString(CultureInfo.InvariantCulture))
                    .Append(" r").Append(node.Rotation)
                    .Append(" l=").Append(node.Label)
                    .Append(" p=").Append(node.InterfacePort)
                    .Append('\n');
            }

            AppendPorts(builder, "I", circuit.Inputs);
            AppendPorts(builder, "O", circuit.Outputs);

            var connections = new List<string>();
            foreach (ConnectionDefinition connection in circuit.Connections)
            {
                connections.Add("C " + connection.Source + " -> " + connection.Destination);
            }

            connections.Sort(System.StringComparer.Ordinal);
            foreach (string line in connections)
            {
                builder.Append(line).Append('\n');
            }

            return builder.ToString();
        }

        private static void AppendPorts(StringBuilder builder, string prefix, IReadOnlyList<InterfacePort> ports)
        {
            for (int i = 0; i < ports.Count; i++)
            {
                builder.Append(prefix).Append(' ').Append(i).Append(' ').Append(ports[i].Id).Append(' ')
                    .Append(ports[i].Name).Append('\n');
            }
        }
    }
}
