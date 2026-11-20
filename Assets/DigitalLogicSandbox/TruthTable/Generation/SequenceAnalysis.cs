using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using DigitalLogicSandbox.Components.Abstractions;
using DigitalLogicSandbox.Core.Circuit;
using DigitalLogicSandbox.Core.Ports;
using DigitalLogicSandbox.Core.Signals;
using DigitalLogicSandbox.Core.Validation;
using DigitalLogicSandbox.Simulation.Context;
using DigitalLogicSandbox.Simulation.Engine;
using DigitalLogicSandbox.Simulation.Propagation;

namespace DigitalLogicSandbox.TruthTable.Generation
{
    /// <summary>
    /// Explicit stimulus for analysing a sequential circuit: an initial state, then a list of steps. Each step
    /// applies one value per root input, settles, and (optionally) pulses the clock once.
    /// </summary>
    public sealed class SequenceSpec
    {
        public const int DefaultMaxSteps = 1024;

        private readonly List<Signal[]> _steps = new List<Signal[]>();

        public SequenceSpec(int inputCount)
        {
            if (inputCount < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(inputCount));
            }

            InputCount = inputCount;
        }

        public int InputCount { get; }

        /// <summary>How feedback loops are initialised before step 1. Must be chosen explicitly by the caller.</summary>
        public PowerOnState PowerOnState { get; set; } = PowerOnState.Resolved;

        /// <summary>
        /// When true and the circuit has a clock, every step ends with one full clock cycle: rising edge, settle,
        /// falling edge, settle. Outputs are sampled after the inputs, while the clock is high and after the cycle.
        /// </summary>
        public bool PulseClock { get; set; } = true;

        public int StepCount => _steps.Count;

        public IReadOnlyList<Signal[]> Steps => _steps;

        /// <summary>Appends a step. <paramref name="inputs"/> must have one binary value per root input.</summary>
        public void AddStep(params Signal[] inputs)
        {
            if (inputs == null || inputs.Length != InputCount)
            {
                throw new ArgumentException("Expected " + InputCount + " input values.", nameof(inputs));
            }

            _steps.Add((Signal[])inputs.Clone());
        }

        /// <summary>
        /// Builds a spec from one pattern per input (see <see cref="ParsePattern"/>). Each input holds its last
        /// value once its pattern ends; <paramref name="steps"/> defaults to the longest pattern.
        /// </summary>
        public static SequenceSpec FromPatterns(IReadOnlyList<string> patterns, int steps = -1)
        {
            if (patterns == null)
            {
                throw new ArgumentNullException(nameof(patterns));
            }

            var parsed = new List<Signal[]>(patterns.Count);
            int longest = 0;
            for (int i = 0; i < patterns.Count; i++)
            {
                Signal[] values = ParsePattern(patterns[i]);
                parsed.Add(values);
                longest = Math.Max(longest, values.Length);
            }

            int count = steps >= 0 ? steps : Math.Max(1, longest);
            var spec = new SequenceSpec(patterns.Count);
            var row = new Signal[patterns.Count];
            for (int step = 0; step < count; step++)
            {
                for (int i = 0; i < patterns.Count; i++)
                {
                    Signal[] values = parsed[i];
                    row[i] = values.Length == 0 ? Signal.Low : values[Math.Min(step, values.Length - 1)];
                }

                spec.AddStep(row);
            }

            return spec;
        }

        /// <summary>
        /// Parses "0110 1" style patterns: '0' and '1' are values; spaces, commas, '_' and '|' are ignored as
        /// separators. Any other character is an error.
        /// </summary>
        public static Signal[] ParsePattern(string pattern)
        {
            var values = new List<Signal>();
            if (string.IsNullOrEmpty(pattern))
            {
                return values.ToArray();
            }

            for (int i = 0; i < pattern.Length; i++)
            {
                char c = pattern[i];
                switch (c)
                {
                    case '0':
                        values.Add(Signal.Low);
                        break;
                    case '1':
                        values.Add(Signal.High);
                        break;
                    case ' ':
                    case ',':
                    case '_':
                    case '|':
                    case '\t':
                        break;
                    default:
                        throw new FormatException("Invalid character '" + c + "' in pattern \"" + pattern +
                            "\". Use 0 and 1 (spaces and commas are ignored).");
                }
            }

            return values.ToArray();
        }
    }

    /// <summary>
    /// The moments sampled within one step. Without a clock pulse all three are the same sample.
    /// </summary>
    public enum SequencePhase : byte
    {
        /// <summary>The step's inputs are applied and settled; the clock is still LOW.</summary>
        AfterInputs = 0,

        /// <summary>After the rising clock edge (the clock is HIGH).</summary>
        ClockHigh = 1,

        /// <summary>After the falling clock edge: the end of the step (the value the table shows).</summary>
        AfterCycle = 2,
    }

    /// <summary>Ready-made input patterns for sequence analysis (strings in <see cref="SequenceSpec.ParsePattern"/> format).</summary>
    public static class SequencePresets
    {
        /// <summary>
        /// Binary count over the inputs, like a truth table: <paramref name="input"/> 0 is the most significant bit.
        /// Values are grouped in fours for readability.
        /// </summary>
        public static string BinaryCount(int input, int inputCount, int steps)
        {
            int bit = inputCount - 1 - input;
            var builder = new StringBuilder(steps + steps / 4);
            for (int step = 0; step < steps; step++)
            {
                AppendSeparator(builder, step);
                builder.Append(bit >= 0 && bit < 31 && ((step >> bit) & 1) == 1 ? '1' : '0');
            }

            return builder.ToString();
        }

        /// <summary>0101… : the input toggles every step, starting LOW.</summary>
        public static string Alternate(int steps)
        {
            var builder = new StringBuilder(steps + steps / 4);
            for (int step = 0; step < steps; step++)
            {
                AppendSeparator(builder, step);
                builder.Append((step & 1) == 1 ? '1' : '0');
            }

            return builder.ToString();
        }

        /// <summary>The same value for every step.</summary>
        public static string Constant(Signal value, int steps)
        {
            char symbol = value == Signal.High ? '1' : '0';
            var builder = new StringBuilder(steps + steps / 4);
            for (int step = 0; step < steps; step++)
            {
                AppendSeparator(builder, step);
                builder.Append(symbol);
            }

            return builder.ToString();
        }

        /// <summary>HIGH for exactly one step (<paramref name="at"/>, 0-based), LOW otherwise.</summary>
        public static string Pulse(int at, int steps)
        {
            var builder = new StringBuilder(steps + steps / 4);
            for (int step = 0; step < steps; step++)
            {
                AppendSeparator(builder, step);
                builder.Append(step == at ? '1' : '0');
            }

            return builder.ToString();
        }

        /// <summary>Formats values as a pattern string, grouped in fours.</summary>
        public static string Format(IReadOnlyList<Signal> values)
        {
            var builder = new StringBuilder(values.Count + values.Count / 4);
            for (int step = 0; step < values.Count; step++)
            {
                AppendSeparator(builder, step);
                builder.Append(values[step] == Signal.High ? '1' : '0');
            }

            return builder.ToString();
        }

        private static void AppendSeparator(StringBuilder builder, int step)
        {
            if (step > 0 && step % 4 == 0)
            {
                builder.Append(' ');
            }
        }
    }

    /// <summary>
    /// Result of a sequence analysis: row 0 is the power-on state, then one row per step. With a clock pulse each
    /// row holds three samples (<see cref="SequencePhase"/>); the plain accessors return the end of the step.
    /// </summary>
    public sealed class SequenceTable
    {
        private readonly List<Signal[]> _inputs = new List<Signal[]>();
        private readonly List<Signal[][]> _outputs = new List<Signal[][]>();
        private readonly List<Signal[][]> _clocks = new List<Signal[][]>();
        private readonly List<bool[]> _stable = new List<bool[]>();
        private readonly List<bool> _changed = new List<bool>();
        private readonly int[] _clockExponents;

        internal SequenceTable(string title, TruthTableColumn[] inputs, TruthTableColumn[] outputs, bool clocked,
            PowerOnState powerOn, int[] clockExponents)
        {
            Title = title ?? string.Empty;
            Inputs = inputs;
            Outputs = outputs;
            Clocked = clocked;
            PowerOnState = powerOn;
            _clockExponents = clockExponents ?? Array.Empty<int>();
        }

        public string Title { get; }

        public IReadOnlyList<TruthTableColumn> Inputs { get; }

        public IReadOnlyList<TruthTableColumn> Outputs { get; }

        /// <summary>True when each step pulsed the clock (three distinct samples per row).</summary>
        public bool Clocked { get; }

        public PowerOnState PowerOnState { get; }

        /// <summary>Clock dividers present, as exponents (0 = the base clock, 1 = ÷2, …). Empty without a clock.</summary>
        public IReadOnlyList<int> ClockExponents => _clockExponents;

        /// <summary>Rows including the power-on row (index 0).</summary>
        public int RowCount => _outputs.Count;

        /// <summary>Number of steps (rows after power-on).</summary>
        public int StepCount => Math.Max(0, RowCount - 1);

        /// <summary>Input value applied in <paramref name="row"/>. Row 0 holds the power-on values (all 0).</summary>
        public Signal GetInput(int row, int column)
        {
            return _inputs[row][column];
        }

        /// <summary>Output at the end of the step (after the full clock cycle when clocked).</summary>
        public Signal GetOutput(int row, int column)
        {
            return _outputs[row][(int)SequencePhase.AfterCycle][column];
        }

        /// <summary>Output at one moment of the step.</summary>
        public Signal GetOutput(int row, int column, SequencePhase phase)
        {
            return _outputs[row][(int)phase][column];
        }

        /// <summary>Level of clock domain <paramref name="domain"/> (an index into <see cref="ClockExponents"/>).</summary>
        public Signal GetClock(int row, int domain, SequencePhase phase)
        {
            return _clocks[row][(int)phase][domain];
        }

        /// <summary>False when the circuit oscillated anywhere in this row (affected outputs read X).</summary>
        public bool IsStable(int row)
        {
            bool[] stable = _stable[row];
            return stable[0] && stable[1] && stable[2];
        }

        public bool IsStable(int row, SequencePhase phase)
        {
            return _stable[row][(int)phase];
        }

        /// <summary>True when the end-of-step value of this output differs from the previous row's.</summary>
        public bool OutputChanged(int row, int column)
        {
            return row > 0 && GetOutput(row, column) != GetOutput(row - 1, column);
        }

        /// <summary>True when any output changed in this row (compared with the previous row).</summary>
        public bool AnyOutputChanged(int row)
        {
            return _changed[row];
        }

        /// <summary>Steps in which at least one output changed.</summary>
        public int ChangedRowCount { get; private set; }

        /// <summary>Rows in which the circuit oscillated (power-on included).</summary>
        public int UnstableRowCount { get; private set; }

        /// <summary>
        /// First row from which nothing changes any more: inputs, outputs and every internal signal stay the same
        /// up to the last row. -1 when the last row still differs from the one before it.
        /// </summary>
        public int SettledFromRow { get; internal set; } = -1;

        /// <summary>
        /// The earlier row whose complete state (every internal signal, input and clock level) came back later,
        /// or -1. Only the first such return is recorded, and returning to the immediately preceding row is not
        /// counted (that is "nothing changed", see <see cref="SettledFromRow"/>).
        /// </summary>
        public int RepeatFromRow { get; internal set; } = -1;

        /// <summary>The row at which the state of <see cref="RepeatFromRow"/> came back, or -1.</summary>
        public int RepeatRow { get; internal set; } = -1;

        /// <summary>Steps between the two equal states, or 0 when no state came back.</summary>
        public int RepeatPeriod => RepeatFromRow >= 0 && RepeatRow > RepeatFromRow ? RepeatRow - RepeatFromRow : 0;

        internal void Add(Signal[] inputs, Signal[][] outputs, Signal[][] clocks, bool[] stable)
        {
            int row = _outputs.Count;
            _inputs.Add(inputs);
            _outputs.Add(outputs);
            _clocks.Add(clocks);
            _stable.Add(stable);

            bool changed = false;
            if (row > 0)
            {
                Signal[] previous = _outputs[row - 1][(int)SequencePhase.AfterCycle];
                Signal[] current = outputs[(int)SequencePhase.AfterCycle];
                for (int i = 0; i < current.Length && !changed; i++)
                {
                    changed = current[i] != previous[i];
                }
            }

            _changed.Add(changed);
            if (changed)
            {
                ChangedRowCount++;
            }

            if (!(stable[0] && stable[1] && stable[2]))
            {
                UnstableRowCount++;
            }
        }

        /// <summary>Header label of the step column ("Step" or "Cycle").</summary>
        public string StepHeader => Clocked ? "Cycle" : "Step";

        /// <summary>Label of a row: "Power-on" for row 0, otherwise its step number.</summary>
        public string RowLabel(int row)
        {
            return row == 0 ? "Power-on" : row.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        /// <summary>Display name of a clock domain: "CLK", "CLK÷2", …</summary>
        public string ClockName(int domain)
        {
            int exponent = _clockExponents[domain];
            return exponent == 0
                ? "CLK"
                : "CLK÷" + (1 << exponent).ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        public string ToCsv()
        {
            var writer = new StringWriter();
            Write(writer, ',', quote: true);
            return writer.ToString();
        }

        public string ToTabSeparated()
        {
            var writer = new StringWriter();
            Write(writer, '\t', quote: false);
            return writer.ToString();
        }

        private void Write(TextWriter writer, char separator, bool quote)
        {
            var line = new StringBuilder();
            line.Append(Escape(StepHeader, quote));
            for (int i = 0; i < Inputs.Count; i++)
            {
                line.Append(separator).Append(Escape(Inputs[i].Name, quote));
            }

            for (int i = 0; i < Outputs.Count; i++)
            {
                line.Append(separator).Append(Escape(Outputs[i].Name, quote));
            }

            writer.Write(line.ToString());
            writer.Write("\r\n");
            for (int row = 0; row < RowCount; row++)
            {
                line.Clear();
                line.Append(Escape(RowLabel(row), quote));
                for (int i = 0; i < Inputs.Count; i++)
                {
                    line.Append(separator).Append(GetInput(row, i).ToDisplayChar());
                }

                for (int i = 0; i < Outputs.Count; i++)
                {
                    line.Append(separator).Append(GetOutput(row, i).ToDisplayChar());
                }

                writer.Write(line.ToString());
                writer.Write("\r\n");
            }
        }

        private static string Escape(string text, bool quote)
        {
            if (!quote || text.IndexOfAny(new[] { ',', '"', '\n', '\r' }) < 0)
            {
                return text;
            }

            return "\"" + text.Replace("\"", "\"\"") + "\"";
        }
    }

    /// <summary>
    /// Sequence analysis for sequential circuits (feedback loops and/or clock). Runs the real engine in a fresh,
    /// isolated <see cref="SimulationContext"/>: reset with the spec's power-on state and record row 0. Then for
    /// each step: apply the inputs and settle (sample "after inputs"); when pulsing the clock, apply the rising
    /// edge and settle (sample "clock high"), then the falling edge and settle (sample "after cycle"). The live
    /// simulation is never touched. Unlike a truth table, the result describes this one stimulus, not the circuit
    /// in general.
    /// </summary>
    public sealed class SequenceAnalyzer
    {
        private const ulong FnvOffset = 14695981039346656037UL;
        private const ulong FnvPrime = 1099511628211UL;

        private readonly ComponentCatalog _catalog;
        private readonly SimulationOptions _options;
        private readonly Dictionary<ulong, int> _stateRows = new Dictionary<ulong, int>();
        private readonly List<ulong> _stateHashes = new List<ulong>();
        private Signal[] _nets = Array.Empty<Signal>();

        public SequenceAnalyzer(ComponentCatalog catalog, SimulationOptions options = null)
        {
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            SimulationOptions source = options ?? SimulationOptions.Default;
            _options = new SimulationOptions
            {
                MaxHierarchyDepth = source.MaxHierarchyDepth,
                MinimumEvaluationBudget = source.MinimumEvaluationBudget,
                EvaluationsPerGate = source.EvaluationsPerGate,
                RecordChanges = false,
                MaxRecordedChanges = 1,
            };
        }

        public int MaxSteps { get; set; } = SequenceSpec.DefaultMaxSteps;

        /// <summary>Compiles and runs. Returns null and fills <paramref name="report"/> when it cannot run.</summary>
        public SequenceTable Run(CircuitDefinition definition, SequenceSpec spec, ValidationReport report,
            string title = null)
        {
            if (definition == null)
            {
                throw new ArgumentNullException(nameof(definition));
            }

            CompileResult compiled = new CircuitCompiler(_catalog, _options).Compile(definition);
            report?.AddRange(compiled.Report);
            return compiled.Succeeded ? Run(compiled.Circuit, spec, report, title) : null;
        }

        public SequenceTable Run(CompiledCircuit circuit, SequenceSpec spec, ValidationReport report,
            string title = null)
        {
            if (circuit == null)
            {
                throw new ArgumentNullException(nameof(circuit));
            }

            if (spec == null)
            {
                throw new ArgumentNullException(nameof(spec));
            }

            report ??= new ValidationReport();
            CircuitDefinition definition = circuit.RootDefinition;
            if (spec.InputCount != circuit.InputCount)
            {
                report.Add(new Diagnostic(DiagnosticSeverity.Error, DiagnosticCode.InterfaceMismatch,
                    "The sequence defines " + spec.InputCount + " input(s); '" + definition.Name + "' has " +
                    circuit.InputCount + "."));
                return null;
            }

            if (circuit.OutputCount == 0)
            {
                report.Add(new Diagnostic(DiagnosticSeverity.Error, DiagnosticCode.NoOutputs,
                    "'" + definition.Name + "' has no outputs, so there is nothing to observe. Add an output first."));
                return null;
            }

            if (spec.StepCount > MaxSteps)
            {
                report.Add(new Diagnostic(DiagnosticSeverity.Error, DiagnosticCode.TruthTableTooLarge,
                    "The sequence has " + spec.StepCount + " steps; the limit is " + MaxSteps + "."));
                return null;
            }

            bool clocked = spec.PulseClock && circuit.HasClock;
            TruthTableColumn[] inputColumns = TruthTableColumn.FromPorts(definition.Inputs);
            TruthTableColumn[] outputColumns = TruthTableColumn.FromPorts(definition.Outputs);
            int[] exponents = ClockExponents(circuit);
            var table = new SequenceTable(title ?? definition.Name, inputColumns, outputColumns, clocked,
                spec.PowerOnState, exponents);

            var options = new SimulationOptions
            {
                MaxHierarchyDepth = _options.MaxHierarchyDepth,
                MinimumEvaluationBudget = _options.MinimumEvaluationBudget,
                EvaluationsPerGate = _options.EvaluationsPerGate,
                RecordChanges = false,
                MaxRecordedChanges = 1,
                PowerOnState = spec.PowerOnState,
            };
            var context = new SimulationContext(circuit, options);
            if (_nets.Length < circuit.NetCount)
            {
                _nets = new Signal[circuit.NetCount];
            }

            _stateRows.Clear();
            _stateHashes.Clear();

            // Power-on: inputs and clocks are LOW (read back rather than assumed); one sample covers every phase.
            var powerOnInputs = new Signal[inputColumns.Length];
            for (int i = 0; i < powerOnInputs.Length; i++)
            {
                powerOnInputs[i] = context.GetInput(inputColumns[i].Index);
            }

            bool powerOnStable = context.LastResult.IsStable;
            table.Add(powerOnInputs, Same(ReadOutputs(context, outputColumns)), Same(ReadClocks(context, exponents)),
                new[] { powerOnStable, powerOnStable, powerOnStable });

            // Where the clock schedule stands is state too: the slowest divider repeats every 2^(k+1) base edges.
            long phaseMask = clocked && exponents.Length > 0 ? (2L << exponents[exponents.Length - 1]) - 1 : 0;
            RecordState(table, context, 0, 0);

            long clockEdge = 0;
            for (int step = 0; step < spec.StepCount; step++)
            {
                Signal[] inputs = spec.Steps[step];
                for (int i = 0; i < inputs.Length; i++)
                {
                    context.SetInput(inputColumns[i].Index, inputs[i]);
                }

                bool afterInputsStable = context.Settle().IsStable;
                Signal[][] outputs;
                Signal[][] clocks;
                bool[] stable;
                if (clocked)
                {
                    Signal[] afterInputs = ReadOutputs(context, outputColumns);
                    Signal[] afterInputsClocks = ReadClocks(context, exponents);

                    // One base clock cycle (two edges); slower domains follow their own schedule.
                    context.ApplyClockEdge(++clockEdge);
                    bool highStable = context.Settle().IsStable;
                    Signal[] high = ReadOutputs(context, outputColumns);
                    Signal[] highClocks = ReadClocks(context, exponents);

                    context.ApplyClockEdge(++clockEdge);
                    bool endStable = context.Settle().IsStable;
                    Signal[] end = ReadOutputs(context, outputColumns);
                    Signal[] endClocks = ReadClocks(context, exponents);

                    outputs = new[] { afterInputs, high, end };
                    clocks = new[] { afterInputsClocks, highClocks, endClocks };
                    stable = new[] { afterInputsStable, highStable, endStable };
                }
                else
                {
                    outputs = Same(ReadOutputs(context, outputColumns));
                    clocks = Same(ReadClocks(context, exponents));
                    stable = new[] { afterInputsStable, afterInputsStable, afterInputsStable };
                }

                table.Add((Signal[])inputs.Clone(), outputs, clocks, stable);
                RecordState(table, context, step + 1, clockEdge & phaseMask);
            }

            table.SettledFromRow = SettledFrom(_stateHashes);
            if (table.UnstableRowCount > 0)
            {
                report.Add(new Diagnostic(DiagnosticSeverity.Warning, DiagnosticCode.Oscillation,
                    "The circuit oscillated in " + table.UnstableRowCount + " row(s) (power-on included); " +
                    "affected signals are shown as X."));
            }

            return table;
        }

        /// <summary>
        /// Hashes every net (stored state, inputs and clock levels) plus the clock-schedule phase at the end of the
        /// row, and records the first return to an earlier state.
        /// </summary>
        private void RecordState(SequenceTable table, SimulationContext context, int row, long clockPhase)
        {
            int count = context.Circuit.NetCount;
            context.CopyNets(_nets);
            ulong hash = FnvOffset;
            for (int shift = 0; shift < 64; shift += 8)
            {
                hash ^= (byte)(clockPhase >> shift);
                hash *= FnvPrime;
            }

            for (int i = 0; i < count; i++)
            {
                hash ^= (byte)_nets[i];
                hash *= FnvPrime;
            }

            // Only a state that differs from the previous row can be a return to an earlier state. A state that
            // merely holds (A A A) is not a cycle, and comparing with the first occurrence keeps the period right
            // when states hold for several rows (H H L L H repeats every 4, not every 3).
            bool changed = _stateHashes.Count == 0 || _stateHashes[_stateHashes.Count - 1] != hash;
            _stateHashes.Add(hash);
            if (!changed)
            {
                return;
            }

            if (_stateRows.TryGetValue(hash, out int earlier))
            {
                if (table.RepeatRow < 0)
                {
                    table.RepeatFromRow = earlier;
                    table.RepeatRow = row;
                }

                return;
            }

            _stateRows.Add(hash, row);
        }

        /// <summary>First row of the unchanging tail, or -1 when the last two rows differ.</summary>
        private static int SettledFrom(List<ulong> hashes)
        {
            int last = hashes.Count - 1;
            if (last < 1 || hashes[last] != hashes[last - 1])
            {
                return -1;
            }

            int first = last - 1;
            while (first > 0 && hashes[first - 1] == hashes[last])
            {
                first--;
            }

            return first;
        }

        private static T[][] Same<T>(T[] sample)
        {
            return new[] { sample, sample, sample };
        }

        private static int[] ClockExponents(CompiledCircuit circuit)
        {
            if (!circuit.HasClock)
            {
                return Array.Empty<int>();
            }

            var exponents = new List<int>(circuit.ClockDomainCount);
            for (int k = 0; k < ClockSchedule.DomainCount; k++)
            {
                if (circuit.HasClockDomain(k))
                {
                    exponents.Add(k);
                }
            }

            return exponents.ToArray();
        }

        private static Signal[] ReadClocks(SimulationContext context, int[] exponents)
        {
            if (exponents.Length == 0)
            {
                return Array.Empty<Signal>();
            }

            var clocks = new Signal[exponents.Length];
            for (int i = 0; i < clocks.Length; i++)
            {
                clocks[i] = context.GetClock(exponents[i]);
            }

            return clocks;
        }

        private static Signal[] ReadOutputs(SimulationContext context, TruthTableColumn[] columns)
        {
            var outputs = new Signal[columns.Length];
            for (int i = 0; i < outputs.Length; i++)
            {
                outputs[i] = context.GetOutput(columns[i].Index);
            }

            return outputs;
        }
    }
}
