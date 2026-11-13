using DigitalLogicSandbox.Core.Circuit;
using DigitalLogicSandbox.Core.Signals;
using DigitalLogicSandbox.Simulation.Context;
using DigitalLogicSandbox.Simulation.Engine;
using NUnit.Framework;

namespace DigitalLogicSandbox.Tests.EditMode.Support
{
    internal static class SimulationTestUtil
    {
        public static CompiledCircuit Compile(TestEnvironment env, CircuitDefinition circuit,
            SimulationOptions options = null)
        {
            CompileResult result = new CircuitCompiler(env.Catalog, options).Compile(circuit);
            Assert.That(result.Succeeded, Is.True, "Compilation failed: " + result.Report);
            return result.Circuit;
        }

        public static SimulationContext CreateContext(TestEnvironment env, CircuitDefinition circuit,
            SimulationOptions options = null)
        {
            return new SimulationContext(Compile(env, circuit, options), options);
        }

        /// <summary>Applies binary inputs (interface order), settles, and returns the outputs as 0/1 ints.</summary>
        public static int[] Evaluate(SimulationContext context, params int[] inputs)
        {
            Assert.That(inputs.Length, Is.EqualTo(context.Circuit.InputCount), "Input count mismatch.");
            for (int i = 0; i < inputs.Length; i++)
            {
                context.SetInput(i, SignalExtensions.FromBool(inputs[i] != 0));
            }

            Assert.That(context.Settle().IsStable, Is.True, "Circuit did not settle.");
            var outputs = new int[context.Circuit.OutputCount];
            for (int i = 0; i < outputs.Length; i++)
            {
                Signal value = context.GetOutput(i);
                Assert.That(value.IsBinary(), Is.True, "Output " + i + " is " + value);
                outputs[i] = value == Signal.High ? 1 : 0;
            }

            return outputs;
        }

        /// <summary>Asserts a single-output circuit's full truth table (rows in binary counting order).</summary>
        public static void AssertTruthTable(SimulationContext context, params int[] expectedOutputs)
        {
            int inputCount = context.Circuit.InputCount;
            Assert.That(expectedOutputs.Length, Is.EqualTo(1 << inputCount));
            for (int row = 0; row < expectedOutputs.Length; row++)
            {
                var inputs = new int[inputCount];
                for (int i = 0; i < inputCount; i++)
                {
                    inputs[i] = (row >> (inputCount - 1 - i)) & 1;
                }

                int[] outputs = Evaluate(context, inputs);
                Assert.That(outputs[0], Is.EqualTo(expectedOutputs[row]),
                    "Row " + row + " inputs " + string.Join("", inputs));
            }
        }
    }
}
