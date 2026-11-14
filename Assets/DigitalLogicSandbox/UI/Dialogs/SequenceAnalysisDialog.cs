using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using DigitalLogicSandbox.CircuitEditor.Workspace;
using DigitalLogicSandbox.Core.Circuit;
using DigitalLogicSandbox.Core.Signals;
using DigitalLogicSandbox.Core.Validation;
using DigitalLogicSandbox.Simulation.Engine;
using DigitalLogicSandbox.TruthTable.Generation;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace DigitalLogicSandbox.UI.Dialogs
{
    /// <summary>
    /// Sequence analysis panel for sequential circuits (feedback loops or a clock), where an ordinary truth table
    /// does not apply. The user chooses a power-on state and one 0/1 pattern per input; each step applies the
    /// inputs and, for clocked circuits, one full clock pulse. Row 0 shows the power-on outputs. Runs in an
    /// isolated simulation context through <see cref="SequenceAnalyzer"/>, so the live circuit is untouched.
    /// </summary>
    public sealed class SequenceAnalysisDialog : MonoBehaviour
    {
        /// <summary>Upper bound on steps shown in the panel (the table is a single text block).</summary>
        public const int MaxDisplaySteps = 256;

        /// <summary>Default pattern length: a binary count over the inputs, capped.</summary>
        public const int MaxDefaultSteps = 16;

        private const float StepColumnWidth = 84f;
        private const float MinColumnWidth = 34f;
        private const float GroupGap = 18f;

        private readonly StringBuilder _builder = new StringBuilder(2048);
        private readonly List<TMP_InputField> _patternFields = new List<TMP_InputField>();

        private UiFactory _ui;
        private WorkspaceController _workspace;
        private RectTransform _layoutRoot;
        private TextMeshProUGUI _title;
        private TextMeshProUGUI _summary;
        private RectTransform _patternRoot;
        private TMP_InputField _stepsField;
        private Toggle _pulseClock;
        private Toggle _powerOnUnknown;
        private TextMeshProUGUI _tableText;
        private TextMeshProUGUI _status;
        private Button _copyButton;
        private Button _exportButton;

        private CircuitDefinition _target;
        private string _targetTitle;
        private bool _targetHasClock;
        private SequenceTable _table;
        private string _hexHigh;
        private string _hexLow;
        private string _hexUnknown;

        public bool IsOpen => gameObject.activeSelf;

        /// <summary>The last successful result, or null.</summary>
        public SequenceTable Table => _table;

        public string TitleText => _title.text;

        public string StatusText => _status.text;

        public string TableText => _tableText.text;

        public IReadOnlyList<TMP_InputField> PatternFields => _patternFields;

        public TMP_InputField StepsField => _stepsField;

        public Toggle PulseClockToggle => _pulseClock;

        public Toggle PowerOnUnknownToggle => _powerOnUnknown;

        public Button CopyButton => _copyButton;

        public Button ExportButton => _exportButton;

        /// <summary>Folder for CSV exports (defaults to persistentDataPath/Exports).</summary>
        public string ExportDirectory { get; set; }

        /// <summary>Raised after the clipboard text has been produced (tests read it without the OS clipboard).</summary>
        public event Action<string> Copied;

        public static SequenceAnalysisDialog Create(Transform parent, UiFactory ui, WorkspaceController workspace)
        {
            Image blocker = ui.Panel("Sequence Analysis Dialog", parent, new Color(0f, 0f, 0f, 0.55f));
            UiFactory.Stretch(blocker.rectTransform);
            var dialog = blocker.gameObject.AddComponent<SequenceAnalysisDialog>();
            dialog._ui = ui;
            dialog._workspace = workspace;
            dialog._hexHigh = "#" + ColorUtility.ToHtmlStringRGB(ui.Theme.SignalColor(Signal.High));
            dialog._hexLow = "#" + ColorUtility.ToHtmlStringRGB(ui.Theme.TextSecondary);
            dialog._hexUnknown = "#" + ColorUtility.ToHtmlStringRGB(ui.Theme.Warning);
            dialog.ExportDirectory = Path.Combine(Application.persistentDataPath, "Exports");
            dialog.Build(blocker.rectTransform);
            blocker.gameObject.SetActive(false);
            return dialog;
        }

        // ------------------------------------------------------------------------------------------------
        // Opening
        // ------------------------------------------------------------------------------------------------

        /// <summary>Analyses the circuit shown in the workspace.</summary>
        public void OpenForCurrentCircuit()
        {
            _workspace.Sync();
            CircuitDefinition circuit = _workspace.Circuit;
            if (circuit != null)
            {
                Open(circuit, circuit.Version > 1 ? circuit.Name + " v" + circuit.Version : circuit.Name);
            }
        }

        /// <summary>Prepares default patterns for <paramref name="target"/> and runs them once.</summary>
        public void Open(CircuitDefinition target, string title)
        {
            _target = target ?? throw new ArgumentNullException(nameof(target));
            _targetTitle = string.IsNullOrEmpty(title) ? target.Name : title;
            _targetHasClock = ContainsClock(target);
            _title.text = "Sequence analysis - " + _targetTitle;
            _summary.text = _targetHasClock
                ? "Each step sets the inputs, then pulses the clock once (rising and falling edge). Row 0 is the power-on state."
                : "Each step sets the inputs and lets the circuit settle. Row 0 is the power-on state.";
            BuildPatternFields(target);
            _stepsField.SetTextWithoutNotify(string.Empty);
            _pulseClock.SetIsOnWithoutNotify(_targetHasClock);
            _pulseClock.interactable = _targetHasClock;
            _powerOnUnknown.SetIsOnWithoutNotify(_workspace.PowerOnState == PowerOnState.Unknown);

            gameObject.SetActive(true);
            transform.SetAsLastSibling();
            LayoutRebuilder.ForceRebuildLayoutImmediate(_layoutRoot);
            _workspace.InputSuspended = true;
            Run();
        }

        public void Close()
        {
            if (!IsOpen)
            {
                return;
            }

            gameObject.SetActive(false);
            _workspace.InputSuspended = false;
        }

        // ------------------------------------------------------------------------------------------------
        // Actions
        // ------------------------------------------------------------------------------------------------

        /// <summary>Runs the current patterns. Returns the table, or null when the input is invalid.</summary>
        public SequenceTable Run()
        {
            _table = null;
            _tableText.text = string.Empty;
            if (_target == null)
            {
                return null;
            }

            SequenceSpec spec;
            try
            {
                var patterns = new List<string>(_patternFields.Count);
                for (int i = 0; i < _patternFields.Count; i++)
                {
                    patterns.Add(_patternFields[i].text);
                }

                int steps = -1;
                string stepsText = _stepsField.text.Trim();
                if (stepsText.Length > 0 &&
                    (!int.TryParse(stepsText, NumberStyles.Integer, CultureInfo.InvariantCulture, out steps) || steps < 1))
                {
                    ShowError("Steps must be a whole number of at least 1 (leave it empty to use the longest pattern).");
                    return null;
                }

                spec = SequenceSpec.FromPatterns(patterns, steps);
            }
            catch (FormatException exception)
            {
                ShowError(exception.Message);
                return null;
            }

            spec.PulseClock = _pulseClock.isOn;
            spec.PowerOnState = _powerOnUnknown.isOn ? PowerOnState.Unknown : PowerOnState.Resolved;
            var analyzer = new SequenceAnalyzer(_workspace.Catalog) { MaxSteps = MaxDisplaySteps };
            var report = new ValidationReport();
            SequenceTable table = analyzer.Run(_target, spec, report, _targetTitle);
            if (table == null)
            {
                ShowError(FirstError(report) ?? "The circuit cannot be analysed.");
                RefreshButtons();
                return null;
            }

            _table = table;
            Render(table);
            _builder.Clear();
            _builder.Append(spec.StepCount).Append(spec.StepCount == 1 ? " step" : " steps").Append(" from ")
                .Append(spec.PowerOnState == PowerOnState.Unknown ? "unknown (X)" : "resolved").Append(" power-on")
                .Append(table.Clocked ? ", one clock pulse per step." : ".");
            _builder.Append(" This shows one input sequence, not every behaviour of the circuit.");
            AppendWarnings(report);
            _status.text = _builder.ToString();
            RefreshButtons();
            return table;
        }

        /// <summary>Copies the result as tab-separated text. Returns the text (null when nothing to copy).</summary>
        public string CopyToClipboard()
        {
            if (_table == null)
            {
                return null;
            }

            string text = _table.ToTabSeparated();
            GUIUtility.systemCopyBuffer = text;
            Copied?.Invoke(text);
            _workspace.PostNotification("Sequence copied (" + (_table.RowCount - 1) + " steps).");
            return text;
        }

        /// <summary>Writes the result to a CSV file. Returns the path, or null on failure.</summary>
        public string ExportCsv()
        {
            if (_table == null)
            {
                return null;
            }

            try
            {
                Directory.CreateDirectory(ExportDirectory);
                string path = Path.Combine(ExportDirectory, SafeFileName(_table.Title) + " sequence.csv");
                string temp = path + ".tmp";
                File.WriteAllText(temp, _table.ToCsv(), new UTF8Encoding(false));
                if (File.Exists(path))
                {
                    File.Delete(path);
                }

                File.Move(temp, path);
                _workspace.PostNotification("Exported sequence to " + path);
                _status.text = "Exported to " + path;
                return path;
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                ShowError("Export failed: " + exception.Message);
                return null;
            }
        }

        // ------------------------------------------------------------------------------------------------
        // Rendering
        // ------------------------------------------------------------------------------------------------

        private void Render(SequenceTable table)
        {
            int inputs = table.Inputs.Count;
            int outputs = table.Outputs.Count;
            var centers = new float[inputs + outputs];
            float x = StepColumnWidth + 8f;
            _builder.Clear();
            _builder.Append("<b>").Append(table.StepHeader);
            for (int c = 0; c < centers.Length; c++)
            {
                if (c == inputs)
                {
                    x += GroupGap;
                }

                string name = c < inputs ? table.Inputs[c].Name : table.Outputs[c - inputs].Name;
                float width = Mathf.Max(MinColumnWidth, _tableText.GetPreferredValues(name).x + 16f);
                centers[c] = x + width * 0.5f;
                float left = centers[c] - _tableText.GetPreferredValues(name).x * 0.5f;
                _builder.Append("<pos=").Append(Mathf.RoundToInt(left)).Append('>');
                if (c >= inputs)
                {
                    _builder.Append("<color=#").Append(ColorUtility.ToHtmlStringRGB(_ui.Theme.Accent)).Append('>')
                        .Append(name).Append("</color>");
                }
                else
                {
                    _builder.Append(name);
                }

                x += width;
            }

            _builder.Append("</b>\n");
            float digitHalf = _tableText.GetPreferredValues("0").x * 0.5f;
            for (int row = 0; row < table.RowCount; row++)
            {
                _builder.Append(table.RowLabel(row));
                if (!table.IsStable(row))
                {
                    _builder.Append(" <color=").Append(_hexUnknown).Append(">~</color>");
                }

                for (int c = 0; c < inputs; c++)
                {
                    if (row == 0)
                    {
                        AppendCell(centers[c], digitHalf, '-', _hexLow);
                    }
                    else
                    {
                        AppendValue(centers[c], digitHalf, table.GetInput(row, c));
                    }
                }

                for (int o = 0; o < outputs; o++)
                {
                    AppendValue(centers[inputs + o], digitHalf, table.GetOutput(row, o));
                }

                _builder.Append('\n');
            }

            _tableText.text = _builder.ToString();
        }

        private void AppendValue(float center, float digitHalf, Signal value)
        {
            string color = value == Signal.High ? _hexHigh : value == Signal.Low ? _hexLow : _hexUnknown;
            if (value == Signal.High)
            {
                _builder.Append("<b>");
            }

            AppendCell(center, digitHalf, TruthTableData.Symbol(value), color);
            if (value == Signal.High)
            {
                _builder.Append("</b>");
            }
        }

        private void AppendCell(float center, float digitHalf, char symbol, string color)
        {
            _builder.Append("<pos=").Append(Mathf.RoundToInt(center - digitHalf)).Append("><color=").Append(color)
                .Append('>').Append(symbol).Append("</color>");
        }

        // ------------------------------------------------------------------------------------------------
        // Helpers
        // ------------------------------------------------------------------------------------------------

        private void BuildPatternFields(CircuitDefinition target)
        {
            _patternFields.Clear();
            UiFactory.DestroyChildren(_patternRoot);
            int count = target.Inputs.Count;
            if (count == 0)
            {
                TextMeshProUGUI none = _ui.Text("No Inputs", _patternRoot,
                    "This circuit has no inputs; steps only pulse the clock.", 12f, _ui.Theme.TextSecondary);
                none.gameObject.AddComponent<LayoutElement>().preferredHeight = 22f;
                _stepsField?.SetTextWithoutNotify("8");
                return;
            }

            for (int i = 0; i < count; i++)
            {
                TMP_InputField field = _ui.FieldRow(_patternRoot, target.Inputs[i].Name, DefaultPattern(i, count), null);
                field.characterLimit = MaxDisplaySteps * 2;
                field.onSubmit.AddListener(_ => Run());
                _patternFields.Add(field);
            }
        }

        /// <summary>Binary count over the inputs (first input is the most significant bit), like a truth table.</summary>
        public static string DefaultPattern(int input, int inputCount)
        {
            int steps = inputCount >= 4 ? MaxDefaultSteps : 1 << inputCount;
            int bit = inputCount - 1 - input;
            var builder = new StringBuilder(steps + steps / 4);
            for (int step = 0; step < steps; step++)
            {
                if (step > 0 && step % 4 == 0)
                {
                    builder.Append(' ');
                }

                builder.Append(bit < 31 && ((step >> bit) & 1) == 1 ? '1' : '0');
            }

            return builder.ToString();
        }

        private bool ContainsClock(CircuitDefinition target)
        {
            var result = new CircuitCompiler(_workspace.Catalog).Compile(target);
            return result.Succeeded && result.Circuit.HasClock;
        }

        private void ShowError(string message)
        {
            _status.text = "<color=#" + ColorUtility.ToHtmlStringRGB(_ui.Theme.Error) + ">" + message + "</color>";
            RefreshButtons();
        }

        private static string FirstError(ValidationReport report)
        {
            IReadOnlyList<Diagnostic> diagnostics = report.Diagnostics;
            for (int i = 0; i < diagnostics.Count; i++)
            {
                if (diagnostics[i].Severity == DiagnosticSeverity.Error)
                {
                    return diagnostics[i].Message;
                }
            }

            return null;
        }

        private void AppendWarnings(ValidationReport report)
        {
            IReadOnlyList<Diagnostic> diagnostics = report.Diagnostics;
            for (int i = 0; i < diagnostics.Count; i++)
            {
                // Feedback-loop warnings are expected here; oscillation is worth repeating.
                if (diagnostics[i].Severity == DiagnosticSeverity.Warning && diagnostics[i].Code == DiagnosticCode.Oscillation)
                {
                    _builder.Append("\n<color=#").Append(ColorUtility.ToHtmlStringRGB(_ui.Theme.Warning)).Append(">Warning: ")
                        .Append(diagnostics[i].Message).Append("</color>");
                }
            }
        }

        private void RefreshButtons()
        {
            _copyButton.interactable = _table != null;
            _exportButton.interactable = _table != null;
        }

        private static string SafeFileName(string name)
        {
            char[] invalid = Path.GetInvalidFileNameChars();
            var builder = new StringBuilder(name.Length);
            foreach (char c in name)
            {
                builder.Append(Array.IndexOf(invalid, c) >= 0 ? '_' : c);
            }

            return builder.Length == 0 ? "circuit" : builder.ToString();
        }

        private void Update()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
            {
                Close();
            }
        }

        // ------------------------------------------------------------------------------------------------
        // Construction
        // ------------------------------------------------------------------------------------------------

        private void Build(RectTransform blocker)
        {
            Image window = _ui.Panel("Window", blocker, _ui.Theme.PanelBackground, rounded: true);
            RectTransform rect = window.rectTransform;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(640f, 680f);

            RectTransform column = UiFactory.Column("Content", rect, 6f, new RectOffset(20, 20, 14, 16));
            UiFactory.Stretch(column);
            _layoutRoot = column;

            RectTransform titleRow = UiFactory.CreateRect("Title Row", column);
            UiFactory.Row(titleRow, 8f, new RectOffset(0, 0, 0, 0));
            titleRow.gameObject.AddComponent<LayoutElement>().preferredHeight = 30f;
            _title = _ui.Text("Title", titleRow, "Sequence analysis", 18f, _ui.Theme.TextPrimary,
                TextAlignmentOptions.MidlineLeft, FontStyles.Bold);
            _title.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
            _ui.Button("Close", titleRow, "X", Close, 30f);

            _summary = _ui.Text("Summary", column, string.Empty, 12.5f, _ui.Theme.TextSecondary, TextAlignmentOptions.TopLeft);
            _summary.enableWordWrapping = true;
            _summary.gameObject.AddComponent<LayoutElement>().preferredHeight = 34f;

            _ui.Caption(column, "Input patterns (0/1 per step; an input holds its last value)");
            _patternRoot = UiFactory.Column("Patterns", column, 2f, new RectOffset(0, 0, 0, 0));

            _stepsField = _ui.FieldRow(column, "Steps", string.Empty, null);
            _stepsField.contentType = TMP_InputField.ContentType.IntegerNumber;
            _stepsField.onSubmit.AddListener(_ => Run());
            ((TextMeshProUGUI)_stepsField.placeholder).text = "longest pattern";
            _pulseClock = _ui.Toggle("PulseClock", column, "Pulse the clock once per step", true, _ => Run());
            _powerOnUnknown = _ui.Toggle("PowerOnUnknown", column, "Power on loops as unknown (X)", false, _ => Run());

            Image tableFrame = _ui.Panel("Table", column, _ui.Theme.WorkspaceBackground, rounded: true);
            var tableLayout = tableFrame.gameObject.AddComponent<LayoutElement>();
            tableLayout.flexibleHeight = 1f;
            tableLayout.minHeight = 180f;
            RectTransform tableContent = _ui.ScrollView("Scroll", tableFrame.rectTransform, out ScrollRect scroll);
            UiFactory.Stretch((RectTransform)scroll.transform);
            _tableText = _ui.Text("Rows", tableContent, string.Empty, 14f, _ui.Theme.TextPrimary, TextAlignmentOptions.TopLeft);
            _tableText.richText = true;
            _tableText.enableWordWrapping = false;
            _tableText.overflowMode = TextOverflowModes.Overflow;
            _tableText.lineSpacing = 8f;

            _status = _ui.Text("Status", column, string.Empty, 12f, _ui.Theme.TextSecondary, TextAlignmentOptions.TopLeft);
            _status.enableWordWrapping = true;
            _status.richText = true;
            _status.overflowMode = TextOverflowModes.Ellipsis;
            _status.gameObject.AddComponent<LayoutElement>().preferredHeight = 52f;

            RectTransform buttons = UiFactory.CreateRect("Buttons", column);
            UiFactory.Row(buttons, 8f, new RectOffset(0, 0, 0, 0), TextAnchor.MiddleRight);
            buttons.gameObject.AddComponent<LayoutElement>().preferredHeight = 34f;
            _ui.Button("Run", buttons, "Run", () => Run(), 80f);
            _copyButton = _ui.Button("Copy", buttons, "Copy", () => CopyToClipboard(), 80f);
            _exportButton = _ui.Button("Export", buttons, "Export CSV", () => ExportCsv(), 110f);
            UiFactory.Flexible(buttons);
            _ui.Button("Done", buttons, "Close", Close, 80f);
            RefreshButtons();
        }
    }
}
