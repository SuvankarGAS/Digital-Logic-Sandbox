using System;
using System.Collections.Generic;
using DigitalLogicSandbox.Core.Circuit;

namespace DigitalLogicSandbox.GateLibrary.Programs
{
    /// <summary>A user instruction set stored in the project: its spec source and the parsed set (null if invalid).</summary>
    public sealed class InstructionSetEntry
    {
        public InstructionSetEntry(string id, string name, string source)
        {
            if (string.IsNullOrEmpty(id))
            {
                throw new ArgumentException("An instruction set needs an id.", nameof(id));
            }

            Id = id;
            Name = string.IsNullOrWhiteSpace(name) ? "Instruction set" : name.Trim();
            Source = source ?? string.Empty;
            InstructionSetParseResult result = InstructionSet.Parse(Source, Name);
            Set = result.Set;
            Errors = result.Errors;
        }

        /// <summary>Stable id (saved in project files and bindings).</summary>
        public string Id { get; }

        public string Name { get; }

        /// <summary>The spec text as the user wrote it.</summary>
        public string Source { get; }

        /// <summary>The parsed set, or null when <see cref="Source"/> has errors (e.g. a hand-edited file).</summary>
        public InstructionSet Set { get; }

        public IReadOnlyList<AssemblyError> Errors { get; }
    }

    /// <summary>The instruction set a program gate assembles with, after defaults are applied.</summary>
    public readonly struct ResolvedInstructionSet
    {
        public ResolvedInstructionSet(string id, string name, InstructionSet set, bool isBound)
        {
            Id = id;
            Name = name;
            Set = set;
            IsBound = isBound;
        }

        /// <summary><see cref="InstructionSetLibrary.ExampleCpuId"/>, <see cref="InstructionSetLibrary.RawId"/> or a user set's id.</summary>
        public string Id { get; }

        public string Name { get; }

        /// <summary>The set, or null for raw words.</summary>
        public InstructionSet Set { get; }

        /// <summary>True when the gate has an explicit binding (false when the default applies).</summary>
        public bool IsBound { get; }

        public bool IsUserSet => Id != InstructionSetLibrary.ExampleCpuId && Id != InstructionSetLibrary.RawId;
    }

    /// <summary>
    /// The project's instruction sets and which one each program gate uses. Bindings are keyed by the program gate's
    /// <see cref="DefinitionId"/> (not its version), so a new version of a ROM keeps its instruction set. Gates without
    /// a binding use the example CPU's set when their words are 8-bit and raw words otherwise. Pure data; changes go
    /// through undoable commands, and <see cref="Changed"/> is raised after each.
    /// </summary>
    public sealed class InstructionSetLibrary
    {
        /// <summary>Id of the built-in example CPU set.</summary>
        public const string ExampleCpuId = "builtin:example-cpu";

        /// <summary>Id meaning "no instruction set": words are edited as numbers.</summary>
        public const string RawId = "raw";

        public const string ExampleCpuName = "Example CPU";

        public const string RawName = "Raw words";

        private readonly List<InstructionSetEntry> _sets = new List<InstructionSetEntry>();
        private readonly Dictionary<DefinitionId, string> _bindings = new Dictionary<DefinitionId, string>();

        /// <summary>Raised after any set or binding changes.</summary>
        public event Action Changed;

        /// <summary>User instruction sets in creation order.</summary>
        public IReadOnlyList<InstructionSetEntry> Sets => _sets;

        /// <summary>All bindings (program gate definition → set id).</summary>
        public IReadOnlyDictionary<DefinitionId, string> Bindings => _bindings;

        public static string NewId()
        {
            return Guid.NewGuid().ToString("N");
        }

        public bool TryGet(string id, out InstructionSetEntry entry)
        {
            int index = IndexOf(id);
            entry = index >= 0 ? _sets[index] : null;
            return entry != null;
        }

        public int IndexOf(string id)
        {
            for (int i = 0; i < _sets.Count; i++)
            {
                if (string.Equals(_sets[i].Id, id, StringComparison.Ordinal))
                {
                    return i;
                }
            }

            return -1;
        }

        /// <summary>The explicit binding of <paramref name="owner"/>, or null.</summary>
        public string BindingOf(DefinitionId owner)
        {
            return _bindings.TryGetValue(owner, out string id) ? id : null;
        }

        /// <summary>
        /// The set <paramref name="owner"/> assembles with: its binding when it names the example CPU, raw words or an
        /// existing user set; otherwise the default (example CPU for 8-bit words, raw words for other widths). A user
        /// set whose spec does not parse resolves to raw words under its own name.
        /// </summary>
        public ResolvedInstructionSet Resolve(DefinitionId owner, int wordWidth)
        {
            string bound = BindingOf(owner);
            if (bound == ExampleCpuId)
            {
                return new ResolvedInstructionSet(ExampleCpuId, ExampleCpuName, InstructionSet.ExampleCpu, true);
            }

            if (bound == RawId)
            {
                return new ResolvedInstructionSet(RawId, RawName, null, true);
            }

            if (bound != null && TryGet(bound, out InstructionSetEntry entry))
            {
                return new ResolvedInstructionSet(entry.Id, entry.Name, entry.Set, true);
            }

            return wordWidth == 8
                ? new ResolvedInstructionSet(ExampleCpuId, ExampleCpuName, InstructionSet.ExampleCpu, false)
                : new ResolvedInstructionSet(RawId, RawName, null, false);
        }

        /// <summary>An assembler for <paramref name="owner"/>'s words.</summary>
        public Assembler AssemblerFor(DefinitionId owner, int wordWidth)
        {
            return new Assembler(Resolve(owner, wordWidth).Set, wordWidth);
        }

        // ------------------------------------------------------------------------------------------------
        // Mutation (used by the undoable commands and by project loading)
        // ------------------------------------------------------------------------------------------------

        /// <summary>Inserts a set at <paramref name="index"/> (clamped). Ids must be unique.</summary>
        public void Insert(int index, InstructionSetEntry entry)
        {
            if (entry == null)
            {
                throw new ArgumentNullException(nameof(entry));
            }

            if (IndexOf(entry.Id) >= 0 || entry.Id == ExampleCpuId || entry.Id == RawId)
            {
                throw new ArgumentException("Instruction set id '" + entry.Id + "' is already used.", nameof(entry));
            }

            _sets.Insert(Math.Max(0, Math.Min(index, _sets.Count)), entry);
            Changed?.Invoke();
        }

        /// <summary>Replaces the set with the same id. Returns the previous entry.</summary>
        public InstructionSetEntry Replace(InstructionSetEntry entry)
        {
            if (entry == null)
            {
                throw new ArgumentNullException(nameof(entry));
            }

            int index = IndexOf(entry.Id);
            if (index < 0)
            {
                throw new KeyNotFoundException("No instruction set '" + entry.Id + "'.");
            }

            InstructionSetEntry previous = _sets[index];
            _sets[index] = entry;
            Changed?.Invoke();
            return previous;
        }

        /// <summary>Removes a set (bindings to it are left for the caller). Returns false when there is none.</summary>
        public bool Remove(string id)
        {
            int index = IndexOf(id);
            if (index < 0)
            {
                return false;
            }

            _sets.RemoveAt(index);
            Changed?.Invoke();
            return true;
        }

        /// <summary>Binds <paramref name="owner"/> to a set id, or clears the binding when <paramref name="setId"/> is null.</summary>
        public void SetBinding(DefinitionId owner, string setId)
        {
            if (!owner.IsValid)
            {
                throw new ArgumentException("A binding needs a valid definition id.", nameof(owner));
            }

            if (setId == null)
            {
                if (!_bindings.Remove(owner))
                {
                    return;
                }
            }
            else
            {
                if (_bindings.TryGetValue(owner, out string existing) && existing == setId)
                {
                    return;
                }

                _bindings[owner] = setId;
            }

            Changed?.Invoke();
        }

        /// <summary>Replaces everything (project load / new project). Raises <see cref="Changed"/> once.</summary>
        public void Reset(IEnumerable<InstructionSetEntry> sets, IEnumerable<KeyValuePair<DefinitionId, string>> bindings)
        {
            _sets.Clear();
            _bindings.Clear();
            if (sets != null)
            {
                foreach (InstructionSetEntry entry in sets)
                {
                    if (entry != null && IndexOf(entry.Id) < 0 && entry.Id != ExampleCpuId && entry.Id != RawId)
                    {
                        _sets.Add(entry);
                    }
                }
            }

            if (bindings != null)
            {
                foreach (KeyValuePair<DefinitionId, string> binding in bindings)
                {
                    if (binding.Key.IsValid && !string.IsNullOrEmpty(binding.Value))
                    {
                        _bindings[binding.Key] = binding.Value;
                    }
                }
            }

            Changed?.Invoke();
        }
    }
}
