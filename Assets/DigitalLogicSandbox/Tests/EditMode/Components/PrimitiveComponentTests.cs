using System.Collections.Generic;
using DigitalLogicSandbox.Components.Abstractions;
using DigitalLogicSandbox.Components.BuiltIn;
using DigitalLogicSandbox.Core.Ports;
using DigitalLogicSandbox.Core.Signals;
using NUnit.Framework;

namespace DigitalLogicSandbox.Tests.EditMode.Components
{
    public sealed class PrimitiveComponentTests
    {
        private static Signal EvaluateAnd(Signal a, Signal b)
        {
            var inputs = new[] { a, b };
            var outputs = new Signal[1];
            AndGate.Instance.Evaluate(inputs, outputs);
            return outputs[0];
        }

        private static Signal EvaluateNot(Signal a)
        {
            var inputs = new[] { a };
            var outputs = new Signal[1];
            NotGate.Instance.Evaluate(inputs, outputs);
            return outputs[0];
        }

        [TestCase(Signal.Low, Signal.Low, Signal.Low)]
        [TestCase(Signal.Low, Signal.High, Signal.Low)]
        [TestCase(Signal.High, Signal.Low, Signal.Low)]
        [TestCase(Signal.High, Signal.High, Signal.High)]
        public void And_BinaryTruthTable(Signal a, Signal b, Signal expected)
        {
            Assert.That(EvaluateAnd(a, b), Is.EqualTo(expected));
        }

        [TestCase(Signal.Low, Signal.Unknown, Signal.Low)]
        [TestCase(Signal.Unknown, Signal.Low, Signal.Low)]
        [TestCase(Signal.High, Signal.Unknown, Signal.Unknown)]
        [TestCase(Signal.HighImpedance, Signal.High, Signal.Unknown)]
        public void And_NonBinaryInputs_LowDominatesOtherwiseUnknown(Signal a, Signal b, Signal expected)
        {
            Assert.That(EvaluateAnd(a, b), Is.EqualTo(expected));
        }

        [TestCase(Signal.Low, Signal.High)]
        [TestCase(Signal.High, Signal.Low)]
        [TestCase(Signal.Unknown, Signal.Unknown)]
        [TestCase(Signal.HighImpedance, Signal.Unknown)]
        public void Not_TruthTable(Signal input, Signal expected)
        {
            Assert.That(EvaluateNot(input), Is.EqualTo(expected));
        }

        [Test]
        public void DefaultRegistry_ContainsOnlyAndAndNot()
        {
            PrimitiveRegistry registry = PrimitiveRegistry.CreateDefault();

            Assert.That(registry.All.Count, Is.EqualTo(2));
            Assert.That(registry.TryGet(BuiltInComponentIds.And, out _), Is.True);
            Assert.That(registry.TryGet(BuiltInComponentIds.Not, out _), Is.True);
        }

        [Test]
        public void Registry_RejectsDuplicateIds()
        {
            PrimitiveRegistry registry = PrimitiveRegistry.CreateDefault();
            Assert.Throws<System.InvalidOperationException>(() => registry.Register(AndGate.Instance));
        }

        [Test]
        public void PrimitiveSignatures_HaveUniquePortIdsAcrossDirections()
        {
            foreach (IPrimitiveComponent primitive in PrimitiveRegistry.CreateDefault().All)
            {
                var ids = new HashSet<PortId>();
                foreach (PortDescriptor port in primitive.Signature.Inputs)
                {
                    Assert.That(ids.Add(port.Id), Is.True, primitive.Id + " duplicate port " + port.Id);
                }

                foreach (PortDescriptor port in primitive.Signature.Outputs)
                {
                    Assert.That(ids.Add(port.Id), Is.True, primitive.Id + " duplicate port " + port.Id);
                }
            }
        }

        [Test]
        public void SignalDisplayChars()
        {
            Assert.That(Signal.Low.ToDisplayChar(), Is.EqualTo('0'));
            Assert.That(Signal.High.ToDisplayChar(), Is.EqualTo('1'));
            Assert.That(Signal.Unknown.ToDisplayChar(), Is.EqualTo('X'));
            Assert.That(Signal.HighImpedance.ToDisplayChar(), Is.EqualTo('Z'));
            Assert.That(SignalExtensions.FromBool(true), Is.EqualTo(Signal.High));
        }
    }
}
