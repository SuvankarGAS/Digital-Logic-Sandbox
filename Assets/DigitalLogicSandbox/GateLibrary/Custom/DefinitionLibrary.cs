using System;
using System.Collections.Generic;
using DigitalLogicSandbox.Core.Circuit;
using DigitalLogicSandbox.Core.Nodes;

namespace DigitalLogicSandbox.GateLibrary.Custom
{
    /// <summary>
    /// Store of published custom gate definitions, keyed by stable id and explicit version. Published versions
    /// are frozen on insertion and never replaced, so every <see cref="DefinitionRef"/> keeps resolving to the
    /// exact graph it was created against. Shared by all instances; graphs are never duplicated per instance.
    /// </summary>
    public sealed class DefinitionLibrary : IDefinitionProvider
    {
        private readonly Dictionary<DefinitionId, SortedList<int, CircuitDefinition>> _versions =
            new Dictionary<DefinitionId, SortedList<int, CircuitDefinition>>();

        private readonly List<CircuitDefinition> _all = new List<CircuitDefinition>();

        /// <summary>Raised after a definition version is published.</summary>
        public event Action<CircuitDefinition> DefinitionAdded;

        /// <summary>Raised after a definition version is withdrawn (only when undoing its publication).</summary>
        public event Action<CircuitDefinition> DefinitionRemoved;

        /// <summary>Every published version in insertion order.</summary>
        public IReadOnlyList<CircuitDefinition> AllVersions => _all;

        public int DefinitionCount => _versions.Count;

        /// <summary>
        /// Publishes a definition version. The definition is frozen. Throws if that exact id and version already
        /// exists.
        /// </summary>
        public void Add(CircuitDefinition definition)
        {
            if (definition == null)
            {
                throw new ArgumentNullException(nameof(definition));
            }

            if (!_versions.TryGetValue(definition.Id, out SortedList<int, CircuitDefinition> versions))
            {
                versions = new SortedList<int, CircuitDefinition>();
                _versions.Add(definition.Id, versions);
            }

            if (versions.ContainsKey(definition.Version))
            {
                throw new InvalidOperationException(
                    "Definition " + definition.Reference + " ('" + definition.Name + "') is already published.");
            }

            definition.Freeze();
            versions.Add(definition.Version, definition);
            _all.Add(definition);
            DefinitionAdded?.Invoke(definition);
        }

        /// <summary>
        /// Withdraws a published version. This exists only to undo a publication. It is refused while another
        /// published definition still references the version, so it can never leave a dangling reference inside
        /// the library. Circuits outside the library are the caller's responsibility (the undo history removes
        /// later instances first).
        /// </summary>
        public bool Remove(DefinitionRef reference)
        {
            if (!_versions.TryGetValue(reference.Id, out SortedList<int, CircuitDefinition> versions) ||
                !versions.TryGetValue(reference.Version, out CircuitDefinition definition))
            {
                return false;
            }

            if (FindReferencingDefinition(reference) != null)
            {
                throw new InvalidOperationException("Definition " + reference + " ('" + definition.Name +
                    "') is used by another custom gate and cannot be removed.");
            }

            versions.Remove(reference.Version);
            if (versions.Count == 0)
            {
                _versions.Remove(reference.Id);
            }

            _all.Remove(definition);
            DefinitionRemoved?.Invoke(definition);
            return true;
        }

        /// <summary>First published definition that directly contains an instance of <paramref name="reference"/>, or null.</summary>
        public CircuitDefinition FindReferencingDefinition(DefinitionRef reference)
        {
            for (int i = 0; i < _all.Count; i++)
            {
                IReadOnlyList<NodeDefinition> nodes = _all[i].Nodes;
                for (int n = 0; n < nodes.Count; n++)
                {
                    if (nodes[n].Component.Kind == ComponentKind.Custom && nodes[n].Component.Definition == reference)
                    {
                        return _all[i];
                    }
                }
            }

            return null;
        }

        /// <summary>True when a definition with this display name (case-insensitive) is already published.</summary>
        public bool ContainsName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return false;
            }

            string trimmed = name.Trim();
            for (int i = 0; i < _all.Count; i++)
            {
                if (string.Equals(_all[i].Name?.Trim(), trimmed, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        public bool Contains(DefinitionRef reference)
        {
            return _versions.TryGetValue(reference.Id, out SortedList<int, CircuitDefinition> versions) &&
                versions.ContainsKey(reference.Version);
        }

        public bool TryGetDefinition(DefinitionRef reference, out CircuitDefinition definition)
        {
            if (reference.IsValid && _versions.TryGetValue(reference.Id, out SortedList<int, CircuitDefinition> versions))
            {
                return versions.TryGetValue(reference.Version, out definition);
            }

            definition = null;
            return false;
        }

        public bool TryGetLatest(DefinitionId id, out CircuitDefinition definition)
        {
            if (_versions.TryGetValue(id, out SortedList<int, CircuitDefinition> versions) && versions.Count > 0)
            {
                definition = versions.Values[versions.Count - 1];
                return true;
            }

            definition = null;
            return false;
        }

        /// <summary>Next free version number for an id (1 for unknown ids).</summary>
        public int NextVersion(DefinitionId id)
        {
            return TryGetLatest(id, out CircuitDefinition latest) ? latest.Version + 1 : 1;
        }

        /// <summary>Appends all versions of a definition, ascending, to <paramref name="results"/>.</summary>
        public void GetVersions(DefinitionId id, List<CircuitDefinition> results)
        {
            if (_versions.TryGetValue(id, out SortedList<int, CircuitDefinition> versions))
            {
                results.AddRange(versions.Values);
            }
        }

        /// <summary>Appends the latest version of every definition, in first-publication order.</summary>
        public void GetLatestDefinitions(List<CircuitDefinition> results)
        {
            for (int i = 0; i < _all.Count; i++)
            {
                CircuitDefinition definition = _all[i];
                if (TryGetLatest(definition.Id, out CircuitDefinition latest) && ReferenceEquals(latest, definition))
                {
                    results.Add(definition);
                }
            }
        }
    }
}
