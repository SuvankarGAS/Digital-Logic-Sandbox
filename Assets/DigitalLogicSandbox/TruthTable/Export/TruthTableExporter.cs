using System;
using System.IO;
using System.Text;
using DigitalLogicSandbox.TruthTable.Generation;

namespace DigitalLogicSandbox.TruthTable.Export
{
    /// <summary>
    /// Text exports of a truth table: RFC 4180 CSV (files) and tab-separated text (clipboard, pastes into
    /// spreadsheets). Only completed rows are written, so a cancelled table exports what was generated.
    /// </summary>
    public static class TruthTableExporter
    {
        public static string ToCsv(TruthTableData table)
        {
            var writer = new StringWriter();
            WriteCsv(table, writer);
            return writer.ToString();
        }

        public static void WriteCsv(TruthTableData table, TextWriter writer)
        {
            Write(table, writer, ',', true);
        }

        public static string ToTabSeparated(TruthTableData table)
        {
            var writer = new StringWriter();
            Write(table, writer, '\t', false);
            return writer.ToString();
        }

        /// <summary>A file-system safe base name for an export ("Full Adder" → "Full_Adder_truth_table").</summary>
        public static string SuggestFileName(TruthTableData table)
        {
            string title = table == null || string.IsNullOrWhiteSpace(table.Title) ? "circuit" : table.Title.Trim();
            var builder = new StringBuilder(title.Length + 12);
            char[] invalid = Path.GetInvalidFileNameChars();
            for (int i = 0; i < title.Length; i++)
            {
                char c = title[i];
                builder.Append(char.IsWhiteSpace(c) || Array.IndexOf(invalid, c) >= 0 ? '_' : c);
            }

            return builder.Append("_truth_table").ToString();
        }

        private static void Write(TruthTableData table, TextWriter writer, char separator, bool quote)
        {
            if (table == null)
            {
                throw new ArgumentNullException(nameof(table));
            }

            if (writer == null)
            {
                throw new ArgumentNullException(nameof(writer));
            }

            int columns = table.InputCount + table.OutputCount;
            for (int c = 0; c < columns; c++)
            {
                if (c > 0)
                {
                    writer.Write(separator);
                }

                string name = c < table.InputCount ? table.Inputs[c].Name : table.Outputs[c - table.InputCount].Name;
                writer.Write(quote ? EscapeCsv(name) : Sanitize(name, separator));
            }

            writer.Write("\r\n");
            for (int row = 0; row < table.CompletedRows; row++)
            {
                for (int i = 0; i < table.InputCount; i++)
                {
                    if (i > 0)
                    {
                        writer.Write(separator);
                    }

                    writer.Write(TruthTableData.Symbol(table.GetInput(row, i)));
                }

                for (int o = 0; o < table.OutputCount; o++)
                {
                    if (table.InputCount > 0 || o > 0)
                    {
                        writer.Write(separator);
                    }

                    writer.Write(TruthTableData.Symbol(table.GetOutput(row, o)));
                }

                writer.Write("\r\n");
            }
        }

        internal static string EscapeCsv(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            bool needsQuotes = value.IndexOfAny(new[] { ',', '"', '\r', '\n' }) >= 0 ||
                               value[0] == ' ' || value[value.Length - 1] == ' ';
            return needsQuotes ? "\"" + value.Replace("\"", "\"\"") + "\"" : value;
        }

        private static string Sanitize(string value, char separator)
        {
            return string.IsNullOrEmpty(value)
                ? string.Empty
                : value.Replace(separator, ' ').Replace('\r', ' ').Replace('\n', ' ');
        }
    }
}
