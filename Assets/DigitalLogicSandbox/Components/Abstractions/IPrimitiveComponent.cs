using System;
using DigitalLogicSandbox.Core.Ports;
using DigitalLogicSandbox.Core.Signals;

namespace DigitalLogicSandbox.Components.Abstractions
{
    /// <summary>
    /// Evaluation contract for built-in logic primitives. Implementations must be stateless and pure
    /// (outputs depend only on inputs), allocation-free, and safe to share between any number of instances and
    /// simulation contexts. Stateful (sequential) primitives will use a separate contract.
    /// </summary>
    public interface IPrimitiveComponent
    {
        /// <summary>Stable component id persisted in files (e.g. "builtin.and"). Never shown as the source of behaviour.</summary>
        string Id { get; }

        /// <summary>Default display name for the library and gate body.</summary>
        string DisplayName { get; }

        ComponentSignature Signature { get; }

        /// <summary>
        /// Computes outputs from inputs. <paramref name="inputs"/> and <paramref name="outputs"/> are ordered as in
        /// <see cref="Signature"/>.
        /// </summary>
        void Evaluate(ReadOnlySpan<Signal> inputs, Span<Signal> outputs);
    }
}
