using System;
using System.Collections.Generic;
using DigitalLogicSandbox.Core.Ports;
using DigitalLogicSandbox.Core.Signals;

namespace DigitalLogicSandbox.TruthTable.Generation
{
    /// <summary>One table column: an external port of the analysed circuit, in interface order.</summary>
    public readonly struct TruthTableColumn
    {
        public TruthTableColumn(PortId port, string name)
        {
            Port = port;
            Name = string.IsNullOrEmpty(name) ? port.ToString() : name;
        }

        public PortId Port { get; }

        public string Name { get; }

        public override string ToString()
        {
            return Name;
        }
    }

    /// <summary>
    /// A generated truth table. Rows are in binary counting order with the first input as the most significant
    /// bit (00, 01, 10, 11), so input values are derived from the row index and only outputs are stored
    /// (one byte per output per row). Filled progressively by a <see cref="Evaluation.TruthTableJob"/>; rows at
    /// or beyond <see cref="CompletedRows"/> are not valid yet.
    /// </summary>
    public sealed class TruthTableData
    {
        private readonly TruthTableColumn[] _inputs;
        private readonly TruthTableColumn[] _outputs;
        private readonly Signal[] _values;

        internal TruthTableData(string title, TruthTableColumn[] inputs, TruthTableColumn[] outputs, int rowCount)
        {
            if (rowCount < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(rowCount));
            }

            Title = title ?? string.Empty;
            _inputs = inputs ?? throw new ArgumentNullException(nameof(inputs));
            _outputs = outputs ?? throw new ArgumentNullException(nameof(outputs));
            RowCount = rowCount;
            _values = new Signal[rowCount * outputs.Length];
        }

        public string Title { get; }

        public IReadOnlyList<TruthTableColumn> Inputs => _inputs;

        public IReadOnlyList<TruthTableColumn> Outputs => _outputs;

        public int InputCount => _inputs.Length;

        public int OutputCount => _outputs.Length;

        /// <summary>Total rows (2^InputCount).</summary>
        public int RowCount { get; }

        /// <summary>Rows evaluated so far (rows are produced in order).</summary>
        public int CompletedRows { get; internal set; }

        public bool IsComplete => CompletedRows == RowCount;

        /// <summary>Value of input <paramref name="column"/> in row <paramref name="row"/> (first input = MSB).</summary>
        public Signal GetInput(int row, int column)
        {
            if ((uint)row >= (uint)RowCount || (uint)column >= (uint)_inputs.Length)
            {
                throw new ArgumentOutOfRangeException(row < 0 || row >= RowCount ? nameof(row) : nameof(column));
            }

            return InputBit(row, column, _inputs.Length) ? Signal.High : Signal.Low;
        }

        public Signal GetOutput(int row, int column)
        {
            if ((uint)row >= (uint)CompletedRows || (uint)column >= (uint)_outputs.Length)
            {
                throw new ArgumentOutOfRangeException(row < 0 || row >= CompletedRows ? nameof(row) : nameof(column));
            }

            return _values[row * _outputs.Length + column];
        }

        /// <summary>Index of the first output column with this name, or -1.</summary>
        public int IndexOfOutput(string name)
        {
            for (int i = 0; i < _outputs.Length; i++)
            {
                if (string.Equals(_outputs[i].Name, name, StringComparison.Ordinal))
                {
                    return i;
                }
            }

            return -1;
        }

        /// <summary>Row whose inputs equal <paramref name="inputs"/> (LOW/HIGH only), or -1.</summary>
        public int RowOf(IReadOnlyList<Signal> inputs)
        {
            if (inputs == null || inputs.Count != _inputs.Length)
            {
                return -1;
            }

            int row = 0;
            for (int i = 0; i < inputs.Count; i++)
            {
                if (inputs[i] != Signal.Low && inputs[i] != Signal.High)
                {
                    return -1;
                }

                row = (row << 1) | (inputs[i] == Signal.High ? 1 : 0);
            }

            return row;
        }

        /// <summary>Single-character cell text: 0, 1, X (unknown) or Z (high impedance).</summary>
        public static char Symbol(Signal signal)
        {
            switch (signal)
            {
                case Signal.Low: return '0';
                case Signal.High: return '1';
                case Signal.HighImpedance: return 'Z';
                default: return 'X';
            }
        }

        internal static bool InputBit(int row, int column, int inputCount)
        {
            return ((row >> (inputCount - 1 - column)) & 1) != 0;
        }

        internal void SetOutput(int row, int column, Signal value)
        {
            _values[row * _outputs.Length + column] = value;
        }
    }
}
