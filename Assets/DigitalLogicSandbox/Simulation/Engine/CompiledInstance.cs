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
        // One net per bit of each output pin (bit 0 first). Single-bit pins hold one-element arrays.
        private Dictionary<PinRef, int[]> _outputPinNets = new Dictionary<PinRef, int[]>();

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

        /// <summary>
        /// Net driven by an output pin of a node in this scope (including input-boundary pins). For a bus pin this
        /// is bit 0; use the overload with a bit index or <see cref="GetOutputPinWidth"/> for the others.
        /// </summary>
        public bool TryGetOutputPinNet(PinRef outputPin, out int net)
        {
            return TryGetOutputPinNet(outputPin, 0, out net);
        }

        /// <summary>Net carrying bit <paramref name="bit"/> of an output pin in this scope.</summary>
        public bool TryGetOutputPinNet(PinRef outputPin, int bit, out int net)
        {
            if (_outputPinNets.TryGetValue(outputPin, out int[] nets) && bit >= 0 && bit < nets.Length)
            {
                net = nets[bit];
                return true;
            }

            net = CompiledCircuit.FloatingNet;
            return false;
        }

        /// <summary>Bits of an output pin in this scope (0 when the pin is unknown).</summary>
        public int GetOutputPinWidth(PinRef outputPin)
        {
            return _outputPinNets.TryGetValue(outputPin, out int[] nets) ? nets.Length : 0;
        }

        /// <summary>
        /// Net read by bit <paramref name="bit"/> of an input pin of a node in this scope: the net of whatever
        /// drives it, or the floating (constant LOW) net when unconnected.
        /// </summary>
        public int GetInputPinNet(PinRef inputPin, int bit = 0)
        {
            if (Definition.TryGetConnectionTo(inputPin, out ConnectionDefinition connection) &&
                _outputPinNets.TryGetValue(connection.Source, out int[] nets) && bit >= 0 && bit < nets.Length)
            {
                return nets[bit];
            }

            return CompiledCircuit.FloatingNet;
        }

        /// <summary>Bits delivered to an input pin by its driver (1 when unconnected).</summary>
        public int GetInputPinWidth(PinRef inputPin)
        {
            return Definition.TryGetConnectionTo(inputPin, out ConnectionDefinition connection) &&
                   _outputPinNets.TryGetValue(connection.Source, out int[] nets)
                ? nets.Length
                : 1;
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

        internal void SetRawOutputPinNets(PinRef pin, int[] rawNets)
        {
            _outputPinNets[pin] = rawNets;
        }

        /// <summary>Raw nets of an output pin during compilation (null when unknown).</summary>
        internal int[] GetRawOutputPinNets(PinRef pin)
        {
            return _outputPinNets.TryGetValue(pin, out int[] nets) ? nets : null;
        }

        /// <summary>Rewrites raw (pre-union) net ids to compacted net indices after compilation.</summary>
        internal void RemapNets(int[] rawToDense)
        {
            var remapped = new Dictionary<PinRef, int[]>(_outputPinNets.Count);
            foreach (KeyValuePair<PinRef, int[]> pair in _outputPinNets)
            {
                int[] raw = pair.Value;
                var dense = new int[raw.Length];
                for (int i = 0; i < raw.Length; i++)
                {
                    dense[i] = rawToDense[raw[i]];
                }

                remapped.Add(pair.Key, dense);
            }

            _outputPinNets = remapped;
        }
    }
}
