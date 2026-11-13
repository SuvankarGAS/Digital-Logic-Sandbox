using DigitalLogicSandbox.Core.Validation;

namespace DigitalLogicSandbox.Simulation.Engine
{
    public sealed class CompileResult
    {
        internal CompileResult(CompiledCircuit circuit, ValidationReport report)
        {
            Circuit = circuit;
            Report = report;
        }

        /// <summary>The compiled circuit, or null when compilation failed (see <see cref="Report"/>).</summary>
        public CompiledCircuit Circuit { get; }

        /// <summary>Errors that prevented compilation and warnings (e.g. feedback loops) that did not.</summary>
        public ValidationReport Report { get; }

        public bool Succeeded => Circuit != null;
    }
}
