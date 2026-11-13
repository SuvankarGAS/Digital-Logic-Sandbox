using System;
using System.Collections.Generic;
using DigitalLogicSandbox.Core.Circuit;
using DigitalLogicSandbox.Core.Nodes;

namespace DigitalLogicSandbox.Core.Validation
{
    /// <summary>
    /// Walks the custom-gate reference tree below a circuit and reports circular references (a definition that
    /// transitively contains any version of itself), missing nested definitions and excessive depth.
    /// Recursion depth is bounded by the configured maximum, so malformed data cannot overflow the stack.
    /// </summary>
    public sealed class HierarchyValidator
    {
        private readonly IDefinitionProvider _provider;
        private readonly int _maxDepth;
        private readonly Dictionary<DefinitionRef, int> _heights = new Dictionary<DefinitionRef, int>();
        private readonly HashSet<DefinitionId> _path = new HashSet<DefinitionId>();
        private readonly HashSet<DefinitionRef> _reportedMissing = new HashSet<DefinitionRef>();
        private ValidationReport _report;
        private bool _tooDeep;

        public HierarchyValidator(IDefinitionProvider provider, int maxDepth = ValidationOptions.DefaultMaxHierarchyDepth)
        {
            _provider = provider ?? throw new ArgumentNullException(nameof(provider));
            if (maxDepth < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(maxDepth), maxDepth, "Depth limit must be at least 1.");
            }

            _maxDepth = maxDepth;
        }

        /// <summary>
        /// Validates the hierarchy below <paramref name="root"/>. Returns the nesting depth (0 = no custom gates).
        /// Missing definitions referenced directly by <paramref name="root"/> are not reported here; the circuit
        /// validator reports those against the node.
        /// </summary>
        public int Validate(CircuitDefinition root, ValidationReport report)
        {
            if (root == null)
            {
                throw new ArgumentNullException(nameof(root));
            }

            _report = report ?? throw new ArgumentNullException(nameof(report));
            _heights.Clear();
            _path.Clear();
            _reportedMissing.Clear();
            _tooDeep = false;

            _path.Add(root.Id);
            int height = Height(root, 0, NodeId.None);
            _path.Clear();

            if (_tooDeep)
            {
                report.Add(new Diagnostic(DiagnosticSeverity.Error, DiagnosticCode.HierarchyTooDeep,
                    "Custom gates are nested more than " + _maxDepth + " levels deep."));
            }

            _report = null;
            return height;
        }

        private int Height(CircuitDefinition definition, int depth, NodeId rootNode)
        {
            int height = 0;
            IReadOnlyList<NodeDefinition> nodes = definition.Nodes;
            for (int i = 0; i < nodes.Count && !_tooDeep; i++)
            {
                NodeDefinition node = nodes[i];
                if (node.Component.Kind != ComponentKind.Custom)
                {
                    continue;
                }

                NodeId reportNode = depth == 0 ? node.Id : rootNode;
                DefinitionRef reference = node.Component.Definition;

                if (_path.Contains(reference.Id))
                {
                    _report.Add(new Diagnostic(DiagnosticSeverity.Error, DiagnosticCode.CircularReference,
                        "Circular reference: '" + definition.Name + "' contains a gate that (directly or indirectly) contains itself (" +
                        reference + ").", reportNode, default, reference));
                    continue;
                }

                if (!_heights.TryGetValue(reference, out int childHeight))
                {
                    if (!_provider.TryGetDefinition(reference, out CircuitDefinition child))
                    {
                        if (depth > 0 && _reportedMissing.Add(reference))
                        {
                            _report.Add(new Diagnostic(DiagnosticSeverity.Error, DiagnosticCode.MissingDefinition,
                                "Nested gate definition " + reference + " used inside '" + definition.Name +
                                "' is missing from the library.", reportNode, default, reference));
                        }

                        continue;
                    }

                    if (depth + 1 > _maxDepth)
                    {
                        _tooDeep = true;
                        return height;
                    }

                    _path.Add(reference.Id);
                    childHeight = Height(child, depth + 1, reportNode);
                    _path.Remove(reference.Id);

                    if (_tooDeep)
                    {
                        return height;
                    }

                    _heights[reference] = childHeight;
                }

                if (depth + 1 + childHeight > _maxDepth)
                {
                    _tooDeep = true;
                    return height;
                }

                if (childHeight + 1 > height)
                {
                    height = childHeight + 1;
                }
            }

            return height;
        }
    }
}
