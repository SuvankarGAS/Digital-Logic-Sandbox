using System;
using System.Collections.Generic;
using DigitalLogicSandbox.Core.Circuit;
using DigitalLogicSandbox.Core.Nodes;
using DigitalLogicSandbox.Core.Ports;

namespace DigitalLogicSandbox.GateLibrary.Custom
{
    /// <summary>How a newly saved gate version affects existing references.</summary>
    public enum VersionUpdateMode
    {
        /// <summary>Publish the new version only; every existing instance keeps using the old one.</summary>
        KeepReferences = 0,

        /// <summary>Also switch instances in the main circuit to the new version.</summary>
        UpdateMainCircuit = 1,

        /// <summary>
        /// Also create new versions of every custom gate that (transitively) uses the edited one, and switch the
        /// main circuit to all of them.
        /// </summary>
        UpdateEverywhere = 2,
    }

    /// <summary>The set of new versions to publish and the old → new reference mapping.</summary>
    public sealed class VersionPlan
    {
        internal VersionPlan(CircuitDefinition draft, DefinitionRef previous)
        {
            Draft = draft;
            Previous = previous;
            Map.Add(previous, draft.Reference);
            Signatures.Add(draft.Reference, draft.GetSignature());
        }

        /// <summary>The edited gate as its new version.</summary>
        public CircuitDefinition Draft { get; }

        /// <summary>The version the user opened for editing.</summary>
        public DefinitionRef Previous { get; }

        /// <summary>New versions of dependent gates, in publication order (dependencies first).</summary>
        public List<CircuitDefinition> Cascaded { get; } = new List<CircuitDefinition>();

        /// <summary>Old reference → new reference for every definition in the plan.</summary>
        public Dictionary<DefinitionRef, DefinitionRef> Map { get; } = new Dictionary<DefinitionRef, DefinitionRef>();

        /// <summary>Signatures of the new versions (they are not published while planning).</summary>
        public Dictionary<DefinitionRef, ComponentSignature> Signatures { get; } =
            new Dictionary<DefinitionRef, ComponentSignature>();

        /// <summary>Connections inside cascaded gates lost because the edited gate's interface changed.</summary>
        public int DroppedConnections { get; internal set; }

        /// <summary>All definitions to publish, in order.</summary>
        public IEnumerable<CircuitDefinition> AllNewDefinitions()
        {
            yield return Draft;
            for (int i = 0; i < Cascaded.Count; i++)
            {
                yield return Cascaded[i];
            }
        }

        public ComponentSignature SignatureOf(DefinitionRef reference)
        {
            return Signatures.TryGetValue(reference, out ComponentSignature signature) ? signature : null;
        }
    }

    /// <summary>Impact summary shown before saving a new version.</summary>
    public sealed class VersionImpact
    {
        public int MainCircuitInstances { get; internal set; }

        public int MainCircuitDroppedConnections { get; internal set; }

        /// <summary>Latest versions of other gates that use the edited version directly.</summary>
        public List<CircuitDefinition> DirectDependents { get; } = new List<CircuitDefinition>();

        /// <summary>Gates that would get a new version under <see cref="VersionUpdateMode.UpdateEverywhere"/>.</summary>
        public int CascadeCount { get; internal set; }

        public int CascadeDroppedConnections { get; internal set; }
    }

    /// <summary>
    /// Versioning rules for custom gates. Published versions are immutable; editing creates a draft with the same
    /// id and the next version number. Saving publishes the draft and, depending on <see cref="VersionUpdateMode"/>,
    /// switches references to it. Old versions always stay in the library, so nothing that references them breaks.
    /// </summary>
    public static class DefinitionVersioning
    {
        /// <summary>Mutable copy of <paramref name="published"/> numbered as the next version of its id.</summary>
        public static CircuitDefinition CreateDraft(DefinitionLibrary library, CircuitDefinition published)
        {
            if (library == null)
            {
                throw new ArgumentNullException(nameof(library));
            }

            if (published == null)
            {
                throw new ArgumentNullException(nameof(published));
            }

            return published.CloneAsVersion(library.NextVersion(published.Id));
        }

        /// <summary>Latest versions of other definitions whose graphs directly instantiate <paramref name="reference"/>.</summary>
        public static List<CircuitDefinition> FindDirectDependents(DefinitionLibrary library, DefinitionRef reference)
        {
            var results = new List<CircuitDefinition>();
            var latest = new List<CircuitDefinition>();
            library.GetLatestDefinitions(latest);
            for (int i = 0; i < latest.Count; i++)
            {
                if (latest[i].Id != reference.Id && ComponentRetargeting.CountInstances(latest[i], reference) > 0)
                {
                    results.Add(latest[i]);
                }
            }

            return results;
        }

        /// <summary>
        /// Plans the new versions for saving <paramref name="draft"/> (opened from <paramref name="previous"/>). With
        /// <paramref name="cascade"/>, every latest definition that uses a planned definition gets a retargeted new
        /// version too, until a fixed point is reached. Nothing is published.
        /// </summary>
        public static VersionPlan Plan(DefinitionLibrary library, CircuitDefinition draft, DefinitionRef previous, bool cascade)
        {
            if (draft.Id != previous.Id)
            {
                throw new ArgumentException("The draft must be a version of the definition it was opened from.");
            }

            var plan = new VersionPlan(draft, previous);
            if (!cascade)
            {
                return plan;
            }

            var processed = new HashSet<DefinitionId> { draft.Id };
            var latest = new List<CircuitDefinition>();
            var records = new List<RetargetRecord>();
            library.GetLatestDefinitions(latest);
            bool changed = true;
            while (changed)
            {
                changed = false;
                for (int i = 0; i < latest.Count; i++)
                {
                    CircuitDefinition candidate = latest[i];
                    if (processed.Contains(candidate.Id) || !UsesAny(candidate, plan.Map))
                    {
                        continue;
                    }

                    CircuitDefinition next = candidate.CloneAsVersion(library.NextVersion(candidate.Id));
                    records.Clear();
                    plan.DroppedConnections += ComponentRetargeting.Retarget(next, plan.Map, plan.SignatureOf, records);
                    plan.Cascaded.Add(next);
                    plan.Map.Add(candidate.Reference, next.Reference);
                    plan.Signatures.Add(next.Reference, next.GetSignature());
                    processed.Add(candidate.Id);
                    changed = true;
                }
            }

            return plan;
        }

        /// <summary>Computes what each update mode would do, without changing anything.</summary>
        public static VersionImpact AnalyzeImpact(DefinitionLibrary library, CircuitDefinition draft, DefinitionRef previous,
            CircuitDefinition mainCircuit)
        {
            var impact = new VersionImpact();
            impact.DirectDependents.AddRange(FindDirectDependents(library, previous));
            VersionPlan direct = Plan(library, draft, previous, cascade: false);
            VersionPlan everywhere = Plan(library, draft, previous, cascade: true);
            impact.CascadeCount = everywhere.Cascaded.Count;
            impact.CascadeDroppedConnections = everywhere.DroppedConnections;

            if (mainCircuit != null)
            {
                impact.MainCircuitInstances = ComponentRetargeting.CountInstances(mainCircuit, previous);
                CircuitDefinition probe = mainCircuit.Clone(DefinitionId.New(), 1);
                impact.MainCircuitDroppedConnections =
                    ComponentRetargeting.Retarget(probe, direct.Map, direct.SignatureOf, null);
            }

            return impact;
        }

        /// <summary>
        /// True when <paramref name="reference"/> is a version of <paramref name="id"/> or (transitively) contains
        /// one. Placing such a gate inside a draft of <paramref name="id"/> would create a circular hierarchy.
        /// Missing definitions are ignored; the walk is bounded by the visited set.
        /// </summary>
        public static bool DependsOn(IDefinitionProvider provider, DefinitionRef reference, DefinitionId id)
        {
            if (provider == null)
            {
                throw new ArgumentNullException(nameof(provider));
            }

            var visited = new HashSet<DefinitionRef>();
            var pending = new Stack<DefinitionRef>();
            pending.Push(reference);
            while (pending.Count > 0)
            {
                DefinitionRef current = pending.Pop();
                if (current.Id == id)
                {
                    return true;
                }

                if (!visited.Add(current) || !provider.TryGetDefinition(current, out CircuitDefinition definition))
                {
                    continue;
                }

                IReadOnlyList<NodeDefinition> nodes = definition.Nodes;
                for (int i = 0; i < nodes.Count; i++)
                {
                    if (nodes[i].Component.Kind == ComponentKind.Custom)
                    {
                        pending.Push(nodes[i].Component.Definition);
                    }
                }
            }

            return false;
        }

        private static bool UsesAny(CircuitDefinition definition, Dictionary<DefinitionRef, DefinitionRef> map)
        {
            IReadOnlyList<NodeDefinition> nodes = definition.Nodes;
            for (int i = 0; i < nodes.Count; i++)
            {
                if (nodes[i].Component.Kind == ComponentKind.Custom && map.ContainsKey(nodes[i].Component.Definition))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
