using System;
using DigitalLogicSandbox.Components.Abstractions;
using DigitalLogicSandbox.Core.Ports;
using DigitalLogicSandbox.Core.Signals;

namespace DigitalLogicSandbox.Components.BuiltIn
{
    /// <summary>Inverter. Non-binary input yields UNKNOWN.</summary>
    public sealed class NotGate : IPrimitiveComponent
    {
        public static readonly PortId Input = new PortId(0);
        public static readonly PortId Output = new PortId(1);

        public static readonly NotGate Instance = new NotGate();

        private NotGate()
        {
            Signature = new ComponentSignature(
                new[] { new PortDescriptor(Input, "IN") },
                new[] { new PortDescriptor(Output, "OUT") });
        }

        public string Id => BuiltInComponentIds.Not;

        public string DisplayName => "NOT";

        public ComponentSignature Signature { get; }

        public void Evaluate(ReadOnlySpan<Signal> inputs, Span<Signal> outputs)
        {
            Signal a = inputs[0];
            outputs[0] = a == Signal.Low ? Signal.High : a == Signal.High ? Signal.Low : Signal.Unknown;
        }
    }
}
