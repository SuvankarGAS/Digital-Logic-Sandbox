using System;
using DigitalLogicSandbox.Components.Abstractions;
using DigitalLogicSandbox.Core.Ports;
using DigitalLogicSandbox.Core.Signals;

namespace DigitalLogicSandbox.Components.BuiltIn
{
    /// <summary>
    /// Per-bit evaluator of the tri-state bus driver utility (<c>ComponentKind.TriState</c>). It is deliberately
    /// not registered as a built-in gate: the gate library stays AND and NOT. EN HIGH passes D (a non-binary D
    /// gives UNKNOWN), EN LOW outputs high impedance (Z), and a non-binary EN gives UNKNOWN.
    /// </summary>
    public sealed class TriStateBuffer : IPrimitiveComponent
    {
        public static readonly TriStateBuffer Instance = new TriStateBuffer();

        private TriStateBuffer()
        {
            Signature = BoundarySignatures.TriState(1);
        }

        public string Id => "utility.tristate";

        public string DisplayName => "TRI";

        public ComponentSignature Signature { get; }

        public void Evaluate(ReadOnlySpan<Signal> inputs, Span<Signal> outputs)
        {
            Signal data = inputs[0];
            Signal enable = inputs[1];
            if (enable == Signal.Low)
            {
                outputs[0] = Signal.HighImpedance;
            }
            else if (enable == Signal.High)
            {
                outputs[0] = data.IsBinary() ? data : Signal.Unknown;
            }
            else
            {
                outputs[0] = Signal.Unknown;
            }
        }
    }
}
