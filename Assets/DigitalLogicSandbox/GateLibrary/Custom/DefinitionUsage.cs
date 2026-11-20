using System;
using System.Collections.Generic;
using DigitalLogicSandbox.Core.Circuit;

namespace DigitalLogicSandbox.GateLibrary.Custom
{
    /// <summary>
    /// Where a custom gate is used: direct instances of any of its versions in the main circuit and inside other gates
    /// of the library. Nested use (a gate inside a gate that is placed) counts once, for the gate that contains it.
    /// </summary>
    public static class DefinitionUsage
    {
        /// <summary>
        /// Counts direct instances of every version of <paramref name="id"/> in <paramref name="rootCircuit"/> (may be
        /// null) and in the other definitions of the library. When <paramref name="users"/> is given, it receives the
        /// distinct names of the gates that contain an instance; <paramref name="rootInstances"/> is the count in the
        /// main circuit alone.
        /// </summary>
        public static int CountUsages(DefinitionLibrary library, CircuitDefinition rootCircuit, DefinitionId id,
            out int rootInstances, List<string> users = null)
        {
            if (library == null)
            {
                throw new ArgumentNullException(nameof(library));
            }

            var versions = new List<CircuitDefinition>();
            library.GetVersions(id, versions);
            rootInstances = 0;
            int total = 0;
            for (int v = 0; v < versions.Count; v++)
            {
                DefinitionRef reference = versions[v].Reference;
                if (rootCircuit != null)
                {
                    rootInstances += ComponentRetargeting.CountInstances(rootCircuit, reference);
                }

                IReadOnlyList<CircuitDefinition> all = library.AllVersions;
                for (int i = 0; i < all.Count; i++)
                {
                    if (all[i].Id == id)
                    {
                        continue;
                    }

                    int count = ComponentRetargeting.CountInstances(all[i], reference);
                    if (count > 0)
                    {
                        total += count;
                        if (users != null && !users.Contains(all[i].Name))
                        {
                            users.Add(all[i].Name);
                        }
                    }
                }
            }

            return total + rootInstances;
        }

        /// <summary>Total direct instances (main circuit plus other gates).</summary>
        public static int CountUsages(DefinitionLibrary library, CircuitDefinition rootCircuit, DefinitionId id)
        {
            return CountUsages(library, rootCircuit, id, out _);
        }

        /// <summary>
        /// A name not used by any gate in the library: <paramref name="baseName"/> itself, then "baseName 2", "baseName 3"…
        /// </summary>
        public static string UniqueName(DefinitionLibrary library, string baseName)
        {
            baseName = string.IsNullOrWhiteSpace(baseName) ? "Gate" : baseName.Trim();
            if (!library.ContainsName(baseName))
            {
                return baseName;
            }

            for (int n = 2; ; n++)
            {
                string candidate = baseName + " " + n.ToString(System.Globalization.CultureInfo.InvariantCulture);
                if (!library.ContainsName(candidate))
                {
                    return candidate;
                }
            }
        }
    }
}
