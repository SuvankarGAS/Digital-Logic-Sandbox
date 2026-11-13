using System.Collections.Generic;
using System.Text;
using DigitalLogicSandbox.Core.Circuit;
using DigitalLogicSandbox.Core.Connections;
using DigitalLogicSandbox.Core.Nodes;

namespace DigitalLogicSandbox.Simulation.Engine
{
    /// <summary>
    /// One expanded circuit scope in the flattened hierarchy: the root circuit, or one specific placed instance of
    /// a custom gate. Maps the pins of that scope's nodes to net indices in the shared flat simulation, which is how
    /// an opened custom gate shows live values for <em>that</em> instance (instances never share nets).
    /// </summary>
    public sealed class CompiledInstance
    {
        private readonly Dictionary<NodeId, CompiledInstance> _children = new Dictionary<NodeId, CompiledInstance>();
        private Dictionary<PinRef, int> _outputPinNets = new Dictionary<PinRef, int>();

        internal CompiledInstance(CircuitDefinition definition, CompiledInstance parent, NodeId nodeInParent)
        {
            Definition = definition;
            Parent = parent;
            NodeInParent = nodeInParent;
            Depth = parent == null ? 0 : parent.Depth + 1;
        }

        /// <summary>The definition this scope expands.</summary>
        public CircuitDefinition Definition { get; }

        /// <summary>Null for the root.</summary>
        public CompiledInstance Parent { get; }

        /// <summary>The custom-gate node in the parent scope that this instance expands; None for the root.</summary>
        public NodeId NodeInParent { get; }

        /// <summary>0 for the root circuit.</summary>
        public int Depth { get; }

        public IReadOnlyDictionary<NodeId, CompiledInstance> Children => _children;

        public bool TryGetChild(NodeId node, out CompiledInstance child)
        {
            return _children.TryGetValue(node, out child);
        }

        /// <summary>Net driven by an output pin of a node in this scope (including input-boundary pins).</summary>
        public bool TryGetOutputPinNet(PinRef outputPin, out int net)
        {
            return _outputPinNets.TryGetValue(outputPin, out net);
        }

        /// <summary>
        /// Net read by an input pin of a node in this scope: the net of whatever drives it, or the floating
        /// (constant LOW) net when unconnected.
        /// </summary>
        public int GetInputPinNet(PinRef inputPin)
        {
            if (Definition.TryGetConnectionTo(inputPin, out ConnectionDefinition connection) &&
                _outputPinNets.TryGetValue(connection.Source, out int net))
            {
                return net;
            }

            return CompiledCircuit.FloatingNet;
        }

        /// <summary>
        /// The node in the root circuit under which this instance lives (None for the root itself). Lets the UI
        /// highlight the top-level gate responsible for a problem deep inside it.
        /// </summary>
        public NodeId RootNode()
        {
            CompiledInstance current = this;
            while (current.Parent != null && current.Parent.Parent != null)
            {
                current = current.Parent;
            }

            return current.NodeInParent;
        }

        /// <summary>Human-readable location such as "Main/N3:OR/N2:NAND".</summary>
        public string Path
        {
            get
            {
                var builder = new StringBuilder();
                AppendPath(builder);
                return builder.ToString();
            }
        }

        internal void AppendPath(StringBuilder builder)
        {
            if (Parent != null)
            {
                Parent.AppendPath(builder);
                builder.Append('/').Append(NodeInParent).Append(':').Append(Definition.Name);
            }
            else
            {
                builder.Append(Definition.Name);
            }
        }

        internal void AddChild(NodeId node, CompiledInstance child)
        {
            _children.Add(node, child);
        }

        internal void SetRawOutputPinNet(PinRef pin, int rawNet)
        {
            _outputPinNets[pin] = rawNet;
        }

        /// <summary>Rewrites raw (pre-union) net ids to compacted net indices after compilation.</summary>
        internal void RemapNets(int[] rawToDense)
        {
            var remapped = new Dictionary<PinRef, int>(_outputPinNets.Count);
            foreach (KeyValuePair<PinRef, int> pair in _outputPinNets)
            {
                remapped.Add(pair.Key, rawToDense[pair.Value]);
            }

            _outputPinNets = remapped;
        }
    }
}
