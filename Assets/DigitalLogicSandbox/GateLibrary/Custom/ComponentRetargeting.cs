using System;
using System.Collections.Generic;
using DigitalLogicSandbox.Core.Circuit;
using DigitalLogicSandbox.Core.Connections;
using DigitalLogicSandbox.Core.Nodes;
using DigitalLogicSandbox.Core.Ports;

namespace DigitalLogicSandbox.GateLibrary.Custom
{
    /// <summary>What happened to one node during retargeting (enough to undo it exactly).</summary>
    public sealed class RetargetRecord
    {
        internal RetargetRecord(NodeDefinition node, ComponentRef newComponent)
        {
            Node = node.Id;
            OldComponent = node.Component;
            NewComponent = newComponent;
            Position = node.Position;
            Rotation = node.Rotation;
            Label = node.Label;
        }

        public NodeId Node { get; }

        public ComponentRef OldComponent { get; }

        public ComponentRef NewComponent { get; }

        public CircuitPoint Position { get; }

        public Rotation Rotation { get; }

        public string Label { get; }

        /// <summary>Every connection that touched the node before retargeting.</summary>
        public List<ConnectionDefinition> Connections { get; } = new List<ConnectionDefinition>();

        /// <summary>Connections that could not be kept because the new version no longer has that pin.</summary>
        public List<ConnectionDefinition> Dropped { get; } = new List<ConnectionDefinition>();
    }

    /// <summary>
    /// Switches custom-gate instances from one definition version to another (e.g. "update references to v2").
    /// Each node keeps its id, position and rotation. Connections are kept wherever the new version still has the
    /// pin (pins are matched by stable port id); the rest are reported as dropped. The operation is exactly
    /// reversible with <see cref="Revert"/>.
    /// </summary>
    public static class ComponentRetargeting
    {
        /// <summary>
        /// Retargets every node of <paramref name="circuit"/> whose custom reference is a key of
        /// <paramref name="map"/>. <paramref name="signatureOf"/> returns the signature of a target version (it may be
        /// unpublished). Returns the number of dropped connections.
        /// </summary>
        public static int Retarget(CircuitDefinition circuit, IReadOnlyDictionary<DefinitionRef, DefinitionRef> map,
            Func<DefinitionRef, ComponentSignature> signatureOf, List<RetargetRecord> records)
        {
            if (circuit == null)
            {
                throw new ArgumentNullException(nameof(circuit));
            }

            if (map == null)
            {
                throw new ArgumentNullException(nameof(map));
            }

            if (signatureOf == null)
            {
                throw new ArgumentNullException(nameof(signatureOf));
            }

            var targets = new List<NodeDefinition>();
            IReadOnlyList<NodeDefinition> nodes = circuit.Nodes;
            for (int i = 0; i < nodes.Count; i++)
            {
                ComponentRef component = nodes[i].Component;
                if (component.Kind == ComponentKind.Custom && map.ContainsKey(component.Definition))
                {
                    targets.Add(nodes[i]);
                }
            }

            int dropped = 0;
            var scratch = new List<ConnectionDefinition>();
            for (int t = 0; t < targets.Count; t++)
            {
                NodeDefinition node = targets[t];
                DefinitionRef target = map[node.Component.Definition];
                ComponentSignature signature = signatureOf(target) ??
                    throw new InvalidOperationException("No signature available for " + target + ".");
                var record = new RetargetRecord(node, ComponentRef.Custom(target));

                scratch.Clear();
                circuit.GetConnectionsFrom(node.Id, scratch);
                circuit.GetConnectionsTo(node.Id, scratch);
                AddDistinct(record.Connections, scratch);

                circuit.RemoveNode(node.Id);
                circuit.RestoreNode(record.Node, record.NewComponent, record.Position, record.Rotation, record.Label);

                for (int c = 0; c < record.Connections.Count; c++)
                {
                    ConnectionDefinition connection = record.Connections[c];
                    bool valid = connection.Source.Node == record.Node
                        ? signature.HasOutput(connection.Source.Port)
                        : signature.HasInput(connection.Destination.Port);
                    if (valid && circuit.ContainsNode(connection.Source.Node) &&
                        circuit.ContainsNode(connection.Destination.Node) && !circuit.IsDriven(connection.Destination))
                    {
                        circuit.Connect(connection.Source, connection.Destination);
                    }
                    else
                    {
                        record.Dropped.Add(connection);
                        dropped++;
                    }
                }

                records?.Add(record);
            }

            return dropped;
        }

        /// <summary>Undoes <see cref="Retarget"/> (records in the order it produced them).</summary>
        public static void Revert(CircuitDefinition circuit, IReadOnlyList<RetargetRecord> records)
        {
            for (int i = records.Count - 1; i >= 0; i--)
            {
                RetargetRecord record = records[i];
                circuit.RemoveNode(record.Node);
                circuit.RestoreNode(record.Node, record.OldComponent, record.Position, record.Rotation, record.Label);
            }

            // Reconnect after every node is back, so links between two retargeted nodes are restored too.
            for (int i = 0; i < records.Count; i++)
            {
                List<ConnectionDefinition> connections = records[i].Connections;
                for (int c = 0; c < connections.Count; c++)
                {
                    ConnectionDefinition connection = connections[c];
                    if (circuit.ContainsNode(connection.Source.Node) && circuit.ContainsNode(connection.Destination.Node) &&
                        !circuit.IsDriven(connection.Destination))
                    {
                        circuit.Connect(connection.Source, connection.Destination);
                    }
                }
            }
        }

        /// <summary>Number of nodes in <paramref name="circuit"/> that instantiate <paramref name="reference"/>.</summary>
        public static int CountInstances(CircuitDefinition circuit, DefinitionRef reference)
        {
            int count = 0;
            IReadOnlyList<NodeDefinition> nodes = circuit.Nodes;
            for (int i = 0; i < nodes.Count; i++)
            {
                if (nodes[i].Component.Kind == ComponentKind.Custom && nodes[i].Component.Definition == reference)
                {
                    count++;
                }
            }

            return count;
        }

        private static void AddDistinct(List<ConnectionDefinition> target, List<ConnectionDefinition> source)
        {
            for (int i = 0; i < source.Count; i++)
            {
                if (!target.Contains(source[i]))
                {
                    target.Add(source[i]);
                }
            }
        }
    }
}
