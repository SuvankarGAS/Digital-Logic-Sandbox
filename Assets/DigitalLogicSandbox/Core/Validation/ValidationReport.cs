using System.Collections.Generic;
using System.Text;

namespace DigitalLogicSandbox.Core.Validation
{
    /// <summary>Accumulates diagnostics. Reusable via <see cref="Clear"/>.</summary>
    public sealed class ValidationReport
    {
        private readonly List<Diagnostic> _diagnostics = new List<Diagnostic>();

        public IReadOnlyList<Diagnostic> Diagnostics => _diagnostics;

        public int ErrorCount { get; private set; }

        public int WarningCount { get; private set; }

        public bool HasErrors => ErrorCount > 0;

        public bool HasWarnings => WarningCount > 0;

        public void Add(Diagnostic diagnostic)
        {
            _diagnostics.Add(diagnostic);
            if (diagnostic.Severity == DiagnosticSeverity.Error)
            {
                ErrorCount++;
            }
            else if (diagnostic.Severity == DiagnosticSeverity.Warning)
            {
                WarningCount++;
            }
        }

        public void AddRange(ValidationReport other)
        {
            for (int i = 0; i < other._diagnostics.Count; i++)
            {
                Add(other._diagnostics[i]);
            }
        }

        public void Clear()
        {
            _diagnostics.Clear();
            ErrorCount = 0;
            WarningCount = 0;
        }

        public bool Contains(DiagnosticCode code)
        {
            return Count(code) > 0;
        }

        public int Count(DiagnosticCode code)
        {
            int count = 0;
            for (int i = 0; i < _diagnostics.Count; i++)
            {
                if (_diagnostics[i].Code == code)
                {
                    count++;
                }
            }

            return count;
        }

        public override string ToString()
        {
            var builder = new StringBuilder();
            builder.Append(ErrorCount).Append(" error(s), ").Append(WarningCount).Append(" warning(s)");
            for (int i = 0; i < _diagnostics.Count; i++)
            {
                builder.AppendLine().Append("  ").Append(_diagnostics[i]);
            }

            return builder.ToString();
        }
    }
}
