using System;
using System.Collections.Generic;
using DigitalLogicSandbox.Core.Circuit;
using DigitalLogicSandbox.Core.Connections;
using DigitalLogicSandbox.Core.Nodes;

namespace DigitalLogicSandbox.CircuitEditor.Selection
{
    /// <summary>
    /// Current editor selection: any number of nodes and at most one wire (identified by its destination pin, which
    /// is unique because every input pin has a single driver). Pure data; views observe <see cref="Changed"/>.
    /// </summary>
    public sealed class SelectionModel
    {
        private readonly HashSet<NodeId> _nodes = new HashSet<NodeId>();
        private readonly List<NodeId> _scratch = new List<NodeId>();

        public event Action Changed;

        public IReadOnlyCollection<NodeId> Nodes => _nodes;

        public int NodeCount => _nodes.Count;

        public PinRef? Wire { get; private set; }

        public bool IsEmpty => _nodes.Count == 0 && !Wire.HasValue;

        public bool Contains(NodeId node)
        {
            return _nodes.Contains(node);
        }

        /// <summary>Selected node ids in ascending id order (deterministic for commands).</summary>
        public NodeId[] ToSortedArray()
        {
            var result = new NodeId[_nodes.Count];
            _nodes.CopyTo(result);
            Array.Sort(result);
            return result;
        }

        public void Clear()
        {
            if (IsEmpty)
            {
                return;
            }

            _nodes.Clear();
            Wire = null;
            Changed?.Invoke();
        }

        /// <summary>Replaces the selection with a single node.</summary>
        public void Select(NodeId node)
        {
            if (_nodes.Count == 1 && _nodes.Contains(node) && !Wire.HasValue)
            {
                return;
            }

            _nodes.Clear();
            Wire = null;
            _nodes.Add(node);
            Changed?.Invoke();
        }

        public void Add(NodeId node)
        {
            bool changed = _nodes.Add(node);
            if (Wire.HasValue)
            {
                Wire = null;
                changed = true;
            }

            if (changed)
            {
                Changed?.Invoke();
            }
        }

        public void Toggle(NodeId node)
        {
            if (!_nodes.Remove(node))
            {
                _nodes.Add(node);
            }

            Wire = null;
            Changed?.Invoke();
        }

        /// <summary>Replaces (or, with <paramref name="additive"/>, extends) the node selection.</summary>
        public void SetNodes(IEnumerable<NodeId> nodes, bool additive)
        {
            if (!additive)
            {
                _nodes.Clear();
            }

            Wire = null;
            foreach (NodeId node in nodes)
            {
                _nodes.Add(node);
            }

            Changed?.Invoke();
        }

        /// <summary>Selects one wire (and nothing else).</summary>
        public void SelectWire(PinRef destination)
        {
            if (_nodes.Count == 0 && Wire == destination)
            {
                return;
            }

            _nodes.Clear();
            Wire = destination;
            Changed?.Invoke();
        }

        /// <summary>Drops entries that no longer exist in <paramref name="circuit"/> (after undo, delete, ...).</summary>
        public void Prune(CircuitDefinition circuit)
        {
            bool changed = false;
            _scratch.Clear();
            foreach (NodeId node in _nodes)
            {
                if (circuit == null || !circuit.ContainsNode(node))
                {
                    _scratch.Add(node);
                }
            }

            for (int i = 0; i < _scratch.Count; i++)
            {
                _nodes.Remove(_scratch[i]);
                changed = true;
            }

            if (Wire.HasValue && (circuit == null || !circuit.IsDriven(Wire.Value)))
            {
                Wire = null;
                changed = true;
            }

            if (changed)
            {
                Changed?.Invoke();
            }
        }
    }
}
