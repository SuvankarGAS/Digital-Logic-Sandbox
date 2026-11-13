using System;
using DigitalLogicSandbox.Components.Abstractions;
using DigitalLogicSandbox.Core.Ports;
using DigitalLogicSandbox.Core.Signals;

namespace DigitalLogicSandbox.Components.BuiltIn
{
    /// <summary>Two-input AND. LOW dominates; otherwise any non-binary input yields UNKNOWN.</summary>
    public sealed class AndGate : IPrimitiveComponent
    {
        public static readonly PortId InputA = new PortId(0);
        public static readonly PortId InputB = new PortId(1);
        public static readonly PortId Output = new PortId(2);

        public static readonly AndGate Instance = new AndGate();

        private AndGate()
        {
            Signature = new ComponentSignature(
                new[] { new PortDescriptor(InputA, "A"), new PortDescriptor(InputB, "B") },
                new[] { new PortDescriptor(Output, "OUT") });
        }

        public string Id => BuiltInComponentIds.And;

        public string DisplayName => "AND";

        public ComponentSignature Signature { get; }

        public void Evaluate(ReadOnlySpan<Signal> inputs, Span<Signal> outputs)
        {
            Signal a = inputs[0];
            Signal b = inputs[1];
            if (a == Signal.Low || b == Signal.Low)
            {
                outputs[0] = Signal.Low;
            }
            else if (a == Signal.High && b == Signal.High)
            {
                outputs[0] = Signal.High;
            }
            else
            {
                outputs[0] = Signal.Unknown;
            }
        }
    }
}
