using DigitalLogicSandbox.Components.BuiltIn;
using DigitalLogicSandbox.Core.Circuit;
using DigitalLogicSandbox.Core.Connections;
using DigitalLogicSandbox.Core.Nodes;
using DigitalLogicSandbox.Core.Ports;
using static DigitalLogicSandbox.Tests.EditMode.Support.TestCircuits;

namespace DigitalLogicSandbox.Tests.EditMode.Support
{
    /// <summary>
    /// Reference sequential circuits built only from AND and NOT (and packaged definitions of them): latches,
    /// flip-flops, a ripple counter and a register. Nothing is a built-in sequential primitive.
    /// </summary>
    internal static class SequentialCircuits
    {
        /// <summary>NAND(a, b) as AND followed by NOT; returns the NOT output pin.</summary>
        public static PinRef Nand(CircuitDefinition circuit, PinRef a, PinRef b, out NodeDefinition and)
        {
            and = circuit.AddNode(And, CircuitPoint.Zero);
            NodeDefinition not = circuit.AddNode(Not, CircuitPoint.Zero);
            circuit.Connect(a, new PinRef(and.Id, AndGate.InputA));
            circuit.Connect(b, new PinRef(and.Id, AndGate.InputB));
            circuit.Connect(new PinRef(and.Id, AndGate.Output), new PinRef(not.Id, NotGate.Input));
            return new PinRef(not.Id, NotGate.Output);
        }

        public static PinRef NotOf(CircuitDefinition circuit, PinRef a)
        {
            NodeDefinition not = circuit.AddNode(Not, CircuitPoint.Zero);
            circuit.Connect(a, new PinRef(not.Id, NotGate.Input));
            return new PinRef(not.Id, NotGate.Output);
        }

        /// <summary>
        /// Cross-coupled NAND core: Q = NAND(sBar, QN), QN = NAND(rBar, Q). Returns (Q, QN). The two feedback inputs
        /// are connected after both NANDs exist.
        /// </summary>
        public static (PinRef q, PinRef qn) NandCore(CircuitDefinition circuit, PinRef sBar, PinRef rBar,
            bool reverseOrder = false)
        {
            // Placeholder sources so both gates can be created before the feedback connections.
            NodeDefinition andQ;
            NodeDefinition andQn;
            PinRef q;
            PinRef qn;
            if (!reverseOrder)
            {
                andQ = circuit.AddNode(And, CircuitPoint.Zero);
                NodeDefinition notQ = circuit.AddNode(Not, CircuitPoint.Zero);
                andQn = circuit.AddNode(And, CircuitPoint.Zero);
                NodeDefinition notQn = circuit.AddNode(Not, CircuitPoint.Zero);
                circuit.Connect(new PinRef(andQ.Id, AndGate.Output), new PinRef(notQ.Id, NotGate.Input));
                circuit.Connect(new PinRef(andQn.Id, AndGate.Output), new PinRef(notQn.Id, NotGate.Input));
                q = new PinRef(notQ.Id, NotGate.Output);
                qn = new PinRef(notQn.Id, NotGate.Output);
            }
            else
            {
                NodeDefinition notQn = circuit.AddNode(Not, CircuitPoint.Zero);
                andQn = circuit.AddNode(And, CircuitPoint.Zero);
                NodeDefinition notQ = circuit.AddNode(Not, CircuitPoint.Zero);
                andQ = circuit.AddNode(And, CircuitPoint.Zero);
                circuit.Connect(new PinRef(andQn.Id, AndGate.Output), new PinRef(notQn.Id, NotGate.Input));
                circuit.Connect(new PinRef(andQ.Id, AndGate.Output), new PinRef(notQ.Id, NotGate.Input));
                q = new PinRef(notQ.Id, NotGate.Output);
                qn = new PinRef(notQn.Id, NotGate.Output);
            }

            circuit.Connect(sBar, new PinRef(andQ.Id, AndGate.InputA));
            circuit.Connect(qn, new PinRef(andQ.Id, AndGate.InputB));
            circuit.Connect(rBar, new PinRef(andQn.Id, AndGate.InputA));
            circuit.Connect(q, new PinRef(andQn.Id, AndGate.InputB));
            return (q, qn);
        }

        /// <summary>Active-high SR latch: inputs S, R; outputs Q, QN. S=R=0 holds.</summary>
        public static CircuitDefinition BuildSrLatch(string name = "SR Latch")
        {
            var circuit = new CircuitDefinition(DefinitionId.New(), name);
            NodeDefinition s = circuit.AddInputBoundary("S", CircuitPoint.Zero);
            NodeDefinition r = circuit.AddInputBoundary("R", CircuitPoint.Zero);
            (PinRef q, PinRef qn) = NandCore(circuit, NotOf(circuit, BoundaryOut(s)), NotOf(circuit, BoundaryOut(r)));
            circuit.Connect(q, BoundaryIn(circuit.AddOutputBoundary("Q", CircuitPoint.Zero)));
            circuit.Connect(qn, BoundaryIn(circuit.AddOutputBoundary("QN", CircuitPoint.Zero)));
            return circuit;
        }

        /// <summary>Gated D latch: inputs D, E; outputs Q, QN. Transparent while E is HIGH.</summary>
        public static CircuitDefinition BuildDLatch(string name = "D Latch", bool reverseOrder = false)
        {
            var circuit = new CircuitDefinition(DefinitionId.New(), name);
            NodeDefinition d = circuit.AddInputBoundary("D", CircuitPoint.Zero);
            NodeDefinition e = circuit.AddInputBoundary("E", CircuitPoint.Zero);
            PinRef sBar;
            PinRef rBar;
            if (!reverseOrder)
            {
                sBar = Nand(circuit, BoundaryOut(d), BoundaryOut(e), out _);
                rBar = Nand(circuit, NotOf(circuit, BoundaryOut(d)), BoundaryOut(e), out _);
            }
            else
            {
                rBar = Nand(circuit, NotOf(circuit, BoundaryOut(d)), BoundaryOut(e), out _);
                sBar = Nand(circuit, BoundaryOut(d), BoundaryOut(e), out _);
            }

            (PinRef q, PinRef qn) = NandCore(circuit, sBar, rBar, reverseOrder);
            circuit.Connect(q, BoundaryIn(circuit.AddOutputBoundary("Q", CircuitPoint.Zero)));
            circuit.Connect(qn, BoundaryIn(circuit.AddOutputBoundary("QN", CircuitPoint.Zero)));
            return circuit;
        }

        /// <summary>
        /// Rising-edge master-slave D flip-flop from two packaged D latches: master enabled by NOT CLK, slave by CLK.
        /// Inputs D, CLK; outputs Q, QN.
        /// </summary>
        public static CircuitDefinition BuildDff(CircuitDefinition dLatch, string name = "D Flip-Flop")
        {
            var circuit = new CircuitDefinition(DefinitionId.New(), name);
            NodeDefinition d = circuit.AddInputBoundary("D", CircuitPoint.Zero);
            NodeDefinition clk = circuit.AddInputBoundary("CLK", CircuitPoint.Zero);
            PinRef notClk = NotOf(circuit, BoundaryOut(clk));
            NodeDefinition master = circuit.AddNode(ComponentRef.Custom(dLatch.Reference), CircuitPoint.Zero);
            NodeDefinition slave = circuit.AddNode(ComponentRef.Custom(dLatch.Reference), CircuitPoint.Zero);
            circuit.Connect(BoundaryOut(d), new PinRef(master.Id, dLatch.Inputs[0].Id));
            circuit.Connect(notClk, new PinRef(master.Id, dLatch.Inputs[1].Id));
            circuit.Connect(new PinRef(master.Id, dLatch.Outputs[0].Id), new PinRef(slave.Id, dLatch.Inputs[0].Id));
            circuit.Connect(BoundaryOut(clk), new PinRef(slave.Id, dLatch.Inputs[1].Id));
            circuit.Connect(new PinRef(slave.Id, dLatch.Outputs[0].Id),
                BoundaryIn(circuit.AddOutputBoundary("Q", CircuitPoint.Zero)));
            circuit.Connect(new PinRef(slave.Id, dLatch.Outputs[1].Id),
                BoundaryIn(circuit.AddOutputBoundary("QN", CircuitPoint.Zero)));
            return circuit;
        }

        /// <summary>
        /// Ripple up-counter driven by a clock node: each flip-flop toggles (D = QN); bit 0 is clocked by the clock,
        /// bit i by QN of bit i-1. Outputs Q0 (LSB) .. Q(bits-1).
        /// </summary>
        public static CircuitDefinition BuildRippleCounter(CircuitDefinition dff, int bits, string name = "Counter")
        {
            var circuit = new CircuitDefinition(DefinitionId.New(), name);
            NodeDefinition clock = circuit.AddNode(ComponentRef.Clock, CircuitPoint.Zero);
            var outputs = new NodeDefinition[bits];
            for (int i = 0; i < bits; i++)
            {
                outputs[i] = circuit.AddOutputBoundary("Q" + i, CircuitPoint.Zero);
            }

            PinRef previousQn = new PinRef(clock.Id, BoundarySignatures.Pin);
            for (int i = 0; i < bits; i++)
            {
                NodeDefinition ff = circuit.AddNode(ComponentRef.Custom(dff.Reference), CircuitPoint.Zero);
                var qn = new PinRef(ff.Id, dff.Outputs[1].Id);
                circuit.Connect(qn, new PinRef(ff.Id, dff.Inputs[0].Id));
                circuit.Connect(previousQn, new PinRef(ff.Id, dff.Inputs[1].Id));
                circuit.Connect(new PinRef(ff.Id, dff.Outputs[0].Id), BoundaryIn(outputs[i]));
                previousQn = qn;
            }

            return circuit;
        }

        /// <summary>Register of <paramref name="bits"/> flip-flops sharing a clock node. Inputs D0.., outputs Q0..</summary>
        public static CircuitDefinition BuildRegister(CircuitDefinition dff, int bits, string name = "Register")
        {
            var circuit = new CircuitDefinition(DefinitionId.New(), name);
            NodeDefinition clock = circuit.AddNode(ComponentRef.Clock, CircuitPoint.Zero);
            var clockPin = new PinRef(clock.Id, BoundarySignatures.Pin);
            for (int i = 0; i < bits; i++)
            {
                NodeDefinition d = circuit.AddInputBoundary("D" + i, CircuitPoint.Zero);
                NodeDefinition ff = circuit.AddNode(ComponentRef.Custom(dff.Reference), CircuitPoint.Zero);
                circuit.Connect(BoundaryOut(d), new PinRef(ff.Id, dff.Inputs[0].Id));
                circuit.Connect(clockPin, new PinRef(ff.Id, dff.Inputs[1].Id));
                circuit.Connect(new PinRef(ff.Id, dff.Outputs[0].Id),
                    BoundaryIn(circuit.AddOutputBoundary("Q" + i, CircuitPoint.Zero)));
            }

            return circuit;
        }
    }
}
