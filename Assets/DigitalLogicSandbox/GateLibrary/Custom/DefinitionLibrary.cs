using System;
using System.Collections.Generic;
using DigitalLogicSandbox.Core.Circuit;

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
