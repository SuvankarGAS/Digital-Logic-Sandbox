using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using DigitalLogicSandbox.CircuitEditor.Workspace;
using DigitalLogicSandbox.Core.Circuit;
using DigitalLogicSandbox.Core.Signals;
using DigitalLogicSandbox.Core.Validation;
using DigitalLogicSandbox.Rendering;
using DigitalLogicSandbox.Simulation.Engine;
using DigitalLogicSandbox.TruthTable.Generation;
using DigitalLogicSandbox.UI.Dialogs.Sequence;
using DigitalLogicSandbox.UI.Panels;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace DigitalLogicSandbox.UI.Dialogs
{
    /// <summary>
    /// Sequence analysis panel for sequential circuits (feedback loops or a clock), where an ordinary truth table
    /// does not apply. The user picks a power-on state and edits the input values per step directly on a timing
    /// diagram (or types 0/1 patterns); every change re-runs the analysis in an isolated simulation context
    /// through <see cref="SequenceAnalyzer"/>, so the live circuit is untouched. Results show as a timing diagram
    /// (with the clock and the moments before, during and after each clock pulse) or as a table, followed by a
    /// short summary: how many outputs changed, whether the circuit settled or returned to an earlier state, and
    /// any oscillation.
    /// </summary>
    public sealed class SequenceAnalysisDialog : MonoBehaviour
    {
        /// <summary>Upper bound on steps: the analyzer's own limit (rows and lanes are virtualised).</summary>
        public const int MaxDisplaySteps = SequenceSpec.DefaultMaxSteps;

        /// <summary>Height of one table row; only the rows in view exist as objects.</summary>
        public const float RowHeight = SequenceTableView.RowHeight;

        /// <summary>Default pattern length: a binary count over the inputs, capped.</summary>
        public const int MaxDefaultSteps = 16;

        /// <summary>Steps used when a circuit has no inputs (each step only pulses the clock).</summary>
        public const int DefaultClockOnlySteps = 8;

        private const int ChipCount = 4;

        /// <summary>The result views.</summary>
        public enum ResultView
        {
            Timing = 0,
            Table = 1,
            Patterns = 2,
        }

        private readonly StringBuilder _builder = new StringBuilder(1024);
        private readonly List<TMP_InputField> _patternFields = new List<TMP_InputField>();
        private readonly List<string> _inputNames = new List<string>();
        private readonly List<string> _patterns = new List<string>();
        private readonly List<Image> _chips = new List<Image>(ChipCount);
        private readonly List<TextMeshProUGUI> _chipTexts = new List<TextMeshProUGUI>(ChipCount);
        private readonly List<string> _chipPlain = new List<string>(ChipCount);

        private UiFactory _ui;
        private WorkspaceController _workspace;
        private RectTransform _blocker;
        private RectTransform _window;
        private RectTransform _layoutRoot;
        private TextMeshProUGUI _title;
        private TextMeshProUGUI _subtitle;
        private SegmentedControl _powerOn;
        private Toggle _powerOnUnknown;
        private Toggle _pulseClock;
        private TMP_InputField _stepsField;
        private Button _stepsMinus;
        private Button _stepsPlus;
        private Button _presetCount;
        private Button _presetLow;
        private Button _presetHigh;
        private SegmentedControl _viewSwitch;
        private RectTransform _zoomGroup;
        private TextMeshProUGUI _legend;
        private SequenceTimelineView _timeline;
        private RectTransform _timelineHost;
        private RectTransform _clockDivider;
        private SequenceTableView _tableView;
        private RectTransform _patternsPanel;
        private RectTransform _patternRoot;
        private TextMeshProUGUI _readout;
        private TextMeshProUGUI _caveat;
        private string _error;
        private Button _copyButton;
        private Button _exportButton;

        private CircuitDefinition _target;
        private string _targetTitle;
        private bool _targetHasClock;
        private SequenceTable _table;
        private SequenceSpec _spec;
        private int[] _patternLengths = Array.Empty<int>();
        private bool _dirty;
        private string _hexAccent;
        private string _hexMuted;
        private string _hexHigh;
        private string _hexLow;
        private string _hexUnknown;

        public bool IsOpen => gameObject.activeSelf;

        /// <summary>The last successful result, or null.</summary>
        public SequenceTable Table => _table;

        public string TitleText => _title.text;

        /// <summary>Plain text of the summary line (or the error when the analysis could not run).</summary>
        public string StatusText
        {
            get
            {
                if (!string.IsNullOrEmpty(_error))
                {
                    return _error;
                }

                _builder.Clear();
                for (int i = 0; i < _chipPlain.Count; i++)
                {
                    _builder.Append(_chipPlain[i]).Append(" · ");
                }

                _builder.Append(_caveat.text);
                return _builder.ToString();
            }
        }

        /// <summary>Header plus the table rows currently in view, as plain text (tests / accessibility).</summary>
        public string TableText => _tableView.VisibleText;

        /// <summary>Rows in the table (row 0 is the power-on state), or 0 when nothing is shown.</summary>
        public int DisplayedRowCount => _table?.RowCount ?? 0;

        public IReadOnlyList<TMP_InputField> PatternFields => _patternFields;

        public TMP_InputField StepsField => _stepsField;

        public Toggle PulseClockToggle => _pulseClock;

        /// <summary>Power-on as a switch (mirrors <see cref="PowerOnControl"/>: on = unknown).</summary>
        public Toggle PowerOnUnknownToggle => _powerOnUnknown;

        /// <summary>Power-on choice: 0 = resolved (loops settle to 0/1), 1 = unknown (X).</summary>
        public SegmentedControl PowerOnControl => _powerOn;

        /// <summary>Timing diagram · Table · Patterns.</summary>
        public SegmentedControl ViewSwitch => _viewSwitch;

        public ResultView CurrentView => (ResultView)Mathf.Max(0, _viewSwitch.Selected);

        public SequenceTimelineView Timeline => _timeline;

        public SequenceTableView TableView => _tableView;

        public Button StepsMinusButton => _stepsMinus;

        public Button StepsPlusButton => _stepsPlus;

        public Button CountUpButton => _presetCount;

        public Button CopyButton => _copyButton;

        public Button ExportButton => _exportButton;

        /// <summary>Plain text of the readout line under the results.</summary>
        public string ReadoutText
        {
            get
            {
                _readout.ForceMeshUpdate(true);
                return _readout.GetParsedText();
            }
        }

        /// <summary>Folder for CSV exports (defaults to persistentDataPath/Exports).</summary>
        public string ExportDirectory { get; set; }

        /// <summary>
        /// Optional save-location picker (folder, suggested file name) returning a full path, or null when the user
        /// cancels. Null means files go straight to <see cref="ExportDirectory"/>.
        /// </summary>
        public Func<string, string, string> PickExportPath { get; set; }

        /// <summary>Raised after the clipboard text has been produced (tests read it without the OS clipboard).</summary>
        public event Action<string> Copied;

        public static SequenceAnalysisDialog Create(Transform parent, UiFactory ui, WorkspaceController workspace)
        {
            Image blocker = ui.Panel("Sequence Analysis Dialog", parent, ui.Theme.Scrim);
            UiFactory.Stretch(blocker.rectTransform);
            var dialog = blocker.gameObject.AddComponent<SequenceAnalysisDialog>();
            dialog._ui = ui;
            dialog._workspace = workspace;
            dialog._blocker = blocker.rectTransform;
            dialog._hexAccent = "#" + ColorUtility.ToHtmlStringRGB(ui.Theme.Accent);
            dialog._hexMuted = "#" + ColorUtility.ToHtmlStringRGB(ui.Theme.TextMuted);
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
                Open(circuit, _workspace.Geometry != null ? _workspace.Geometry.NameOf(circuit) : circuit.Name);
            }
        }

        /// <summary>Prepares default patterns for <paramref name="target"/> and runs them once.</summary>
        public void Open(CircuitDefinition target, string title)
        {
            _target = target ?? throw new ArgumentNullException(nameof(target));
            _targetTitle = string.IsNullOrEmpty(title) ? target.Name : title;
            _targetHasClock = ContainsClock(target);
            _title.text = "Sequence analysis  <color=" + _hexMuted + ">·</color>  " + _targetTitle;
            _subtitle.text = _targetHasClock
                ? "Each step sets the inputs, then pulses the clock once (rises, then falls). Edit the inputs and " +
                  "watch the outputs respond."
                : "Each step sets the inputs and lets the circuit settle. Edit the inputs and watch the outputs respond.";
            BuildPatternFields(target);
            _pulseClock.SetIsOnWithoutNotify(_targetHasClock);
            _pulseClock.interactable = _targetHasClock;
            // Only circuits with a clock need the switch.
            _pulseClock.gameObject.SetActive(_targetHasClock);
            _clockDivider.gameObject.SetActive(_targetHasClock);
            bool unknown = _workspace.PowerOnState == PowerOnState.Unknown;
            _powerOnUnknown.SetIsOnWithoutNotify(unknown);
            _powerOn.SetSelectedWithoutNotify(unknown ? 1 : 0);
            bool hasInputs = _patternFields.Count > 0;
            _presetCount.interactable = hasInputs;
            _presetLow.interactable = hasInputs;
            _presetHigh.interactable = hasInputs;

            gameObject.SetActive(true);
            transform.SetAsLastSibling();
            FitWindow();
            ShowView(CurrentView);
            LayoutRebuilder.ForceRebuildLayoutImmediate(_layoutRoot);
            _timeline.ResetView();
            _workspace.InputSuspended = true;
            Run();
        }

        public void Close()
        {
            if (!IsOpen)
            {
                return;
            }

            _timeline.EndPaint();
            gameObject.SetActive(false);
            _workspace.InputSuspended = false;
        }

        private void FitWindow()
        {
            Rect bounds = _blocker.rect;
            float width = Mathf.Min(1040f, Mathf.Max(640f, bounds.width - 60f));
            float height = Mathf.Min(760f, Mathf.Max(520f, bounds.height - 60f));
            _window.sizeDelta = new Vector2(width, height);
        }

        // ------------------------------------------------------------------------------------------------
        // Actions
        // ------------------------------------------------------------------------------------------------

        /// <summary>Runs the current patterns. Returns the table, or null when the input is invalid.</summary>
        public SequenceTable Run()
        {
            _dirty = false;
            _table = null;
            if (_target == null)
            {
                return null;
            }

            int steps = -1;
            string stepsText = _stepsField.text.Trim();
            if (stepsText.Length > 0 &&
                (!int.TryParse(stepsText, NumberStyles.Integer, CultureInfo.InvariantCulture, out steps) || steps < 1))
            {
                return Fail("Steps must be a whole number of at least 1 (leave it empty to use the longest pattern).");
            }

            SequenceSpec spec;
            try
            {
                _patterns.Clear();
                if (_patternLengths.Length != _patternFields.Count)
                {
                    _patternLengths = new int[_patternFields.Count];
                }

                for (int i = 0; i < _patternFields.Count; i++)
                {
                    string pattern = _patternFields[i].text;
                    _patterns.Add(pattern);
                    _patternLengths[i] = SequenceSpec.ParsePattern(pattern).Length;
                }

                spec = SequenceSpec.FromPatterns(_patterns, steps);
            }
            catch (FormatException exception)
            {
                return Fail(exception.Message);
            }

            spec.PulseClock = _pulseClock.isOn;
            spec.PowerOnState = _powerOnUnknown.isOn ? PowerOnState.Unknown : PowerOnState.Resolved;
            _spec = spec;
            ((TextMeshProUGUI)_stepsField.placeholder).text = "auto · " +
                spec.StepCount.ToString(CultureInfo.InvariantCulture);

            var analyzer = new SequenceAnalyzer(_workspace.Catalog) { MaxSteps = MaxDisplaySteps };
            var report = new ValidationReport();
            SequenceTable table = analyzer.Run(_target, spec, report, _targetTitle);
            if (table == null)
            {
                _spec = null;
                return Fail(FirstError(report) ?? "The circuit cannot be analysed.");
            }

            _table = table;
            _error = null;
            _timeline.Bind(table, _inputNames, spec.Steps, _patternLengths);
            _tableView.Bind(table);
            ShowSummary(table, spec);
            RefreshReadout();
            RefreshButtons();
            return table;
        }

        private SequenceTable Fail(string message)
        {
            _table = null;
            _error = message;
            _timeline.Bind(null, _inputNames, _spec?.Steps, _patternLengths, message);
            _tableView.Bind(null);
            ShowError(message);
            RefreshReadout();
            RefreshButtons();
            return null;
        }

        /// <summary>
        /// Sets input <paramref name="input"/> to <paramref name="value"/> for steps <paramref name="from"/> to
        /// <paramref name="to"/> (1-based, inclusive) by rewriting its pattern, then re-runs. A pattern shorter than
        /// <paramref name="to"/> is first extended with its held (last) value.
        /// </summary>
        public bool SetCells(int input, int from, int to, Signal value)
        {
            if (input < 0 || input >= _patternFields.Count || from < 1 || to < from)
            {
                return false;
            }

            Signal[] parsed;
            try
            {
                parsed = SequenceSpec.ParsePattern(_patternFields[input].text);
            }
            catch (FormatException exception)
            {
                Fail(exception.Message);
                return false;
            }

            var values = new List<Signal>(Mathf.Max(parsed.Length, to));
            values.AddRange(parsed);
            Signal held = parsed.Length > 0 ? parsed[parsed.Length - 1] : Signal.Low;
            while (values.Count < to)
            {
                values.Add(held);
            }

            for (int step = from; step <= to; step++)
            {
                values[step - 1] = value == Signal.High ? Signal.High : Signal.Low;
            }

            _patternFields[input].SetTextWithoutNotify(SequencePresets.Format(values));
            Run();
            return true;
        }

        /// <summary>Fills the inputs with a binary count (like a truth table), long enough to cover every combination up to 16 steps.</summary>
        public void ApplyCountUp()
        {
            int count = _patternFields.Count;
            if (count == 0)
            {
                return;
            }

            int steps = Mathf.Max(CurrentSteps(), count >= 4 ? MaxDefaultSteps : 1 << count);
            for (int i = 0; i < count; i++)
            {
                _patternFields[i].SetTextWithoutNotify(SequencePresets.BinaryCount(i, count, steps));
            }

            _stepsField.SetTextWithoutNotify(string.Empty);
            Run();
        }

        /// <summary>Sets every input to <paramref name="value"/> for the current number of steps.</summary>
        public void ApplyConstant(Signal value)
        {
            int steps = CurrentSteps();
            for (int i = 0; i < _patternFields.Count; i++)
            {
                _patternFields[i].SetTextWithoutNotify(SequencePresets.Constant(value, steps));
            }

            Run();
        }

        /// <summary>Changes the number of steps by <paramref name="delta"/> (kept between 1 and the limit).</summary>
        public void ChangeSteps(int delta)
        {
            int steps = Mathf.Clamp(CurrentSteps() + delta, 1, MaxDisplaySteps);
            _stepsField.SetTextWithoutNotify(steps.ToString(CultureInfo.InvariantCulture));
            Run();
        }

        private int CurrentSteps()
        {
            if (int.TryParse(_stepsField.text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int steps) &&
                steps >= 1)
            {
                return Mathf.Min(steps, MaxDisplaySteps);
            }

            return _spec != null ? Mathf.Max(1, _spec.StepCount) : 1;
        }

        /// <summary>Switches between the timing diagram, the table and the text patterns.</summary>
        public void ShowView(ResultView view)
        {
            _viewSwitch.SetSelectedWithoutNotify((int)view);
            _timelineHost.gameObject.SetActive(view == ResultView.Timing);
            _tableView.gameObject.SetActive(view == ResultView.Table);
            _patternsPanel.gameObject.SetActive(view == ResultView.Patterns);
            _zoomGroup.gameObject.SetActive(view == ResultView.Timing);
            _legend.gameObject.SetActive(view == ResultView.Timing);
            _readout.gameObject.SetActive(view != ResultView.Patterns);
            RefreshReadout();
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

        /// <summary>Writes the result to a CSV file. Returns the path, or null on failure or cancel.</summary>
        public string ExportCsv()
        {
            if (_table == null)
            {
                return null;
            }

            try
            {
                Directory.CreateDirectory(ExportDirectory);
                string fileName = SafeFileName(_table.Title) + " sequence.csv";
                string path = PickExportPath != null
                    ? PickExportPath(ExportDirectory, fileName)
                    : Path.Combine(ExportDirectory, fileName);
                if (string.IsNullOrEmpty(path))
                {
                    return null; // cancelled
                }

                string temp = path + ".tmp";
                File.WriteAllText(temp, _table.ToCsv(), new UTF8Encoding(false));
                if (File.Exists(path))
                {
                    File.Delete(path);
                }

                File.Move(temp, path);
                _workspace.PostNotification("Exported sequence to " + path);
                _caveat.text = "Exported to " + path;
                return path;
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                _workspace.PostNotification("Export failed: " + exception.Message);
                _caveat.text = "<color=#" + ColorUtility.ToHtmlStringRGB(_ui.Theme.Error) + ">Export failed: " +
                    exception.Message + "</color>";
                return null;
            }
        }

        /// <summary>Plain text of a displayed table row, or null when the row is not in view.</summary>
        public string GetVisibleRowText(int row)
        {
            return _tableView.GetVisibleRowText(row);
        }

        /// <summary>Scrolls the table so that <paramref name="row"/> is the first visible row.</summary>
        public void ScrollToRow(int row)
        {
            _tableView.ScrollToRow(row);
        }

        /// <summary>Binary count over the inputs (first input is the most significant bit), like a truth table.</summary>
        public static string DefaultPattern(int input, int inputCount)
        {
            int steps = inputCount >= 4 ? MaxDefaultSteps : 1 << inputCount;
            return SequencePresets.BinaryCount(input, inputCount, steps);
        }

        // ------------------------------------------------------------------------------------------------
        // Summary and readout
        // ------------------------------------------------------------------------------------------------

        private enum Tone
        {
            Neutral,
            Good,
            Warning,
            Error,
        }

        private void ShowSummary(SequenceTable table, SequenceSpec spec)
        {
            int used = 0;
            int steps = spec.StepCount;
            string count = steps.ToString(CultureInfo.InvariantCulture);
            string unit = table.Clocked ? "cycle" : "step";
            string units = steps == 1 ? unit : unit + "s";
            if (table.Clocked)
            {
                SetChip(used++, Icons.Clock, count + " " + units, Tone.Neutral,
                    "Each cycle: set the inputs, then one clock pulse (rising edge, then falling edge).");
            }
            else if (_targetHasClock)
            {
                SetChip(used++, Icons.Clock, count + " " + units + " · clock off", Tone.Neutral,
                    "'Pulse clock' is off: the clock stays LOW, so only the inputs change.");
            }
            else
            {
                SetChip(used++, Icons.Step, count + " " + units, Tone.Neutral,
                    "Each step sets the inputs and lets the circuit settle.");
            }

            bool unknown = table.PowerOnState == PowerOnState.Unknown;
            SetChip(used++, Icons.Zap, unknown ? "Power-on: X" : "Power-on: resolved", Tone.Neutral, unknown
                ? "Feedback loops start unknown (X) until the inputs drive them."
                : "Feedback loops settle to a definite 0 or 1 at power-on.");

            if (table.SettledFromRow >= 0 && (table.RepeatRow < 0 || table.SettledFromRow <= table.RepeatFromRow))
            {
                string settled = table.SettledFromRow.ToString(CultureInfo.InvariantCulture);
                // With a clock, signals may still pulse within each cycle; only the end-of-cycle state is steady.
                SetChip(used++, Icons.Check, table.SettledFromRow == 0 ? "Steady from power-on" : "Steady from " +
                    unit + " " + settled, Tone.Good, table.Clocked
                    ? "From this cycle on, every cycle ends in exactly the same state (all internal signals equal). " +
                      "Signals may still pulse with the clock inside each cycle."
                    : "From this step on nothing changes any more: inputs, outputs and every internal signal stay the same.");
            }
            else if (table.RepeatPeriod > 0)
            {
                string period = table.RepeatPeriod.ToString(CultureInfo.InvariantCulture);
                string from = table.RepeatFromRow == 0
                    ? "the power-on state"
                    : "the state of " + unit + " " + table.RepeatFromRow.ToString(CultureInfo.InvariantCulture);
                SetChip(used++, Icons.Reset, "Repeats every " + period + " " + unit + "s", Tone.Good,
                    "At " + unit + " " + table.RepeatRow.ToString(CultureInfo.InvariantCulture) + " the circuit is " +
                    "back in exactly " + from + " (all internal signals and inputs equal). With the same inputs it " +
                    "would repeat from there.");
            }
            else
            {
                SetChip(used++, Icons.Activity, "Still changing", Tone.Neutral,
                    "The circuit was still changing at the last " + unit + ". Add steps to see where it goes.");
            }

            if (table.UnstableRowCount > 0)
            {
                string rows = table.UnstableRowCount.ToString(CultureInfo.InvariantCulture);
                SetChip(used++, Icons.Warning, "Oscillates in " + rows + (table.UnstableRowCount == 1 ? " row" : " rows"),
                    Tone.Warning, "The circuit never settled in these rows (a loop keeps toggling); the affected " +
                    "signals are shown as X.");
            }
            else
            {
                int changed = table.ChangedRowCount;
                SetChip(used++, Icons.Activity, changed == 0
                    ? "Outputs never change"
                    : "Outputs change in " + changed.ToString(CultureInfo.InvariantCulture) + " " +
                      (changed == 1 ? unit : unit + "s"), Tone.Neutral,
                    "Steps where an output differs from the step before (highlighted in the diagram and table).");
            }

            HideChipsFrom(used);
            _caveat.text = "For this input sequence only";
        }

        private void ShowError(string message)
        {
            SetChip(0, Icons.Alert, message, Tone.Error, message);
            HideChipsFrom(1);
            _caveat.text = string.Empty;
        }

        private void SetChip(int index, string glyph, string text, Tone tone, string tooltip)
        {
            Image chip = _chips[index];
            TextMeshProUGUI label = _chipTexts[index];
            Color fill;
            Color color;
            switch (tone)
            {
                case Tone.Good:
                    fill = _ui.Theme.AccentSoft;
                    color = _ui.Theme.TextPrimary;
                    break;
                case Tone.Warning:
                    fill = new Color(_ui.Theme.Warning.r, _ui.Theme.Warning.g, _ui.Theme.Warning.b, 0.16f);
                    color = _ui.Theme.Warning;
                    break;
                case Tone.Error:
                    fill = new Color(_ui.Theme.Error.r, _ui.Theme.Error.g, _ui.Theme.Error.b, 0.16f);
                    color = _ui.Theme.Error;
                    break;
                default:
                    fill = _ui.Theme.ButtonHover;
                    color = _ui.Theme.TextSecondary;
                    break;
            }

            chip.color = fill;
            label.color = color;
            label.text = glyph + "  " + text;
            label.enableWordWrapping = tone == Tone.Error;
            float width = label.GetPreferredValues(label.text).x + 22f;
            var layout = chip.GetComponent<LayoutElement>();
            layout.preferredWidth = tone == Tone.Error ? -1f : width;
            layout.minWidth = tone == Tone.Error ? -1f : width;
            TooltipTrigger trigger = chip.GetComponent<TooltipTrigger>();
            if (trigger != null)
            {
                trigger.Text = tooltip;
            }
            layout.flexibleWidth = tone == Tone.Error ? 1f : 0f;
            chip.gameObject.SetActive(true);
            while (_chipPlain.Count <= index)
            {
                _chipPlain.Add(string.Empty);
            }

            _chipPlain[index] = text;
        }

        private void HideChipsFrom(int index)
        {
            for (int i = index; i < _chips.Count; i++)
            {
                _chips[i].gameObject.SetActive(false);
            }

            if (_chipPlain.Count > index)
            {
                _chipPlain.RemoveRange(index, _chipPlain.Count - index);
            }
        }

        private void RefreshReadout()
        {
            if (_readout == null)
            {
                return;
            }

            int row = _timeline != null && CurrentView == ResultView.Timing ? _timeline.FocusRow : -1;
            if (_table == null || row < 0 || row >= _table.RowCount)
            {
                _readout.text = "<color=" + _hexMuted + ">" + (CurrentView == ResultView.Timing
                    ? "Hover a step to read its values · click to pin it, then ←/→ · click or drag on input lanes to change them"
                    : "Highlighted outputs changed in that row · " + Icons.Warning + " marks rows where the circuit oscillated") +
                    "</color>";
                return;
            }

            SequenceTable table = _table;
            _builder.Clear();
            _builder.Append("<b>").Append(row == 0 ? "Power-on" : table.StepHeader + " " +
                row.ToString(CultureInfo.InvariantCulture)).Append("</b>");
            if (table.Inputs.Count > 0)
            {
                _builder.Append("   <color=").Append(_hexMuted).Append(">in</color> ");
                AppendValues(row, inputs: true);
            }

            _builder.Append("   <color=").Append(_hexMuted).Append(">→ out</color> ");
            AppendValues(row, inputs: false);
            _readout.text = _builder.ToString();
        }

        private void AppendValues(int row, bool inputs)
        {
            SequenceTable table = _table;
            int count = inputs ? table.Inputs.Count : table.Outputs.Count;
            int shown = Mathf.Min(count, 10);
            for (int i = 0; i < shown; i++)
            {
                if (i > 0)
                {
                    _builder.Append("  ");
                }

                string name = inputs ? table.Inputs[i].Name : table.Outputs[i].Name;
                Signal value = inputs ? table.GetInput(row, i) : table.GetOutput(row, i);
                _builder.Append(name).Append('=');
                if (!inputs && table.OutputChanged(row, i))
                {
                    Signal before = table.GetOutput(row - 1, i);
                    _builder.Append("<color=").Append(_hexAccent).Append("><b>").Append(TruthTableData.Symbol(before))
                        .Append('→').Append(TruthTableData.Symbol(value)).Append("</b></color>");
                    if (table.Clocked)
                    {
                        // Did it change as soon as the inputs were applied, or only with the clock?
                        bool fromInputs = table.GetOutput(row, i, SequencePhase.AfterInputs) != before;
                        _builder.Append("<color=").Append(_hexMuted).Append("><size=85%> ")
                            .Append(fromInputs ? "(inputs)" : "(clock)").Append("</size></color>");
                    }
                }
                else
                {
                    string color = value == Signal.High ? _hexHigh : value == Signal.Low ? _hexLow : _hexUnknown;
                    _builder.Append("<color=").Append(color).Append('>').Append(TruthTableData.Symbol(value))
                        .Append("</color>");
                }
            }

            if (count > shown)
            {
                _builder.Append("  <color=").Append(_hexMuted).Append(">+").Append(count - shown).Append(" more</color>");
            }
        }

        // ------------------------------------------------------------------------------------------------
        // Helpers
        // ------------------------------------------------------------------------------------------------

        private void BuildPatternFields(CircuitDefinition target)
        {
            _patternFields.Clear();
            _inputNames.Clear();
            UiFactory.DestroyChildren(_patternRoot);
            TruthTableColumn[] columns = TruthTableColumn.FromPorts(target.Inputs);
            int count = columns.Length;
            _patternLengths = new int[count];
            if (count == 0)
            {
                TextMeshProUGUI none = _ui.Text("No Inputs", _patternRoot,
                    "This circuit has no inputs: every step only pulses the clock. Use Steps to choose how many.",
                    12.5f, _ui.Theme.TextSecondary);
                none.enableWordWrapping = true;
                none.gameObject.AddComponent<LayoutElement>().preferredHeight = 36f;
                _stepsField.SetTextWithoutNotify(DefaultClockOnlySteps.ToString(CultureInfo.InvariantCulture));
                return;
            }

            _stepsField.SetTextWithoutNotify(string.Empty);
            for (int i = 0; i < count; i++)
            {
                _inputNames.Add(columns[i].Name);
                TMP_InputField field = _ui.FieldRow(_patternRoot, columns[i].Name, DefaultPattern(i, count), null, 110f);
                field.characterLimit = MaxDisplaySteps * 2;
                field.onValueChanged.AddListener(_ => _dirty = true);
                field.onSubmit.AddListener(_ => Run());
                _patternFields.Add(field);
            }
        }

        private bool ContainsClock(CircuitDefinition target)
        {
            var result = new CircuitCompiler(_workspace.Catalog).Compile(target);
            return result.Succeeded && result.Circuit.HasClock;
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
            if (_dirty)
            {
                Run();
            }

            Keyboard keyboard = Keyboard.current;
            if (keyboard == null)
            {
                return;
            }

            if (keyboard.escapeKey.wasPressedThisFrame)
            {
                Close();
                return;
            }

            if (CurrentView != ResultView.Timing || EditingText())
            {
                return;
            }

            if (keyboard.leftArrowKey.wasPressedThisFrame)
            {
                _timeline.MoveCursor(-1);
            }
            else if (keyboard.rightArrowKey.wasPressedThisFrame)
            {
                _timeline.MoveCursor(1);
            }
        }

        private static bool EditingText()
        {
            GameObject selected = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
            return selected != null && selected.GetComponent<TMP_InputField>() != null;
        }

        // ------------------------------------------------------------------------------------------------
        // Construction
        // ------------------------------------------------------------------------------------------------

        private void Build(RectTransform blocker)
        {
            VisualTheme theme = _ui.Theme;
            Image window = _ui.Card("Window", blocker, theme.DialogBackground);
            _window = window.rectTransform;
            _window.anchorMin = _window.anchorMax = new Vector2(0.5f, 0.5f);
            _window.pivot = new Vector2(0.5f, 0.5f);
            _window.sizeDelta = new Vector2(1000f, 740f);

            RectTransform column = UiFactory.Column("Content", _window, 10f, new RectOffset(22, 22, 16, 18));
            UiFactory.Stretch(column);
            _layoutRoot = column;

            BuildHeader(column);
            BuildSetupBar(column);
            BuildViewBar(column);

            _timelineHost = UiFactory.CreateRect("Timeline Host", column);
            var hostLayout = _timelineHost.gameObject.AddComponent<LayoutElement>();
            hostLayout.flexibleHeight = 1f;
            hostLayout.minHeight = 200f;
            _timeline = SequenceTimelineView.Create(_timelineHost, _ui);
            UiFactory.Stretch((RectTransform)_timeline.transform.parent);
            _timeline.CellsEdited += (input, from, to, value) => SetCells(input, from, to, value);
            _timeline.CursorChanged += RefreshReadout;

            _tableView = SequenceTableView.Create(column, _ui);
            BuildPatternsPanel(column);

            _readout = _ui.Text("Readout", column, string.Empty, 12.5f, theme.TextPrimary);
            _readout.richText = true;
            _readout.gameObject.AddComponent<LayoutElement>().preferredHeight = 20f;

            BuildSummary(column);
            BuildFooter(column);
            ShowView(ResultView.Timing);
            RefreshButtons();
        }

        private void BuildHeader(RectTransform column)
        {
            VisualTheme theme = _ui.Theme;
            RectTransform titleRow = UiFactory.CreateRect("Title Row", column);
            UiFactory.Row(titleRow, 8f, new RectOffset(0, 0, 0, 0));
            titleRow.gameObject.AddComponent<LayoutElement>().preferredHeight = 30f;
            TextMeshProUGUI icon = _ui.Icon("Icon", titleRow, Icons.Activity, 18f, theme.Accent);
            icon.gameObject.AddComponent<LayoutElement>().preferredWidth = 22f;
            _title = _ui.Text("Title", titleRow, "Sequence analysis", 18f, theme.TextPrimary,
                TextAlignmentOptions.MidlineLeft, FontStyles.Bold);
            _title.richText = true;
            _title.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
            _ui.IconButton("Help", titleRow, Icons.Info,
                "<b>How it works</b>\n" +
                "• Power-on: inputs and clocks start at 0. 'Resolved' settles feedback loops to a definite 0/1; " +
                "'Unknown' leaves them X until the inputs drive them.\n" +
                "• Each step applies one value per input. After its pattern ends, an input keeps its last value " +
                "(drawn fainter).\n" +
                "• With a clock, each step is split into: after inputs → clock high → after the falling edge.\n" +
                "• Results describe this input sequence only, not every possible behaviour.", null, 30f, 15f);
            _ui.IconButton("Close", titleRow, Icons.Close, "Close  <color=#687088>Esc</color>", Close, 30f, 15f);

            _subtitle = _ui.Text("Subtitle", column, string.Empty, 12.5f, theme.TextSecondary, TextAlignmentOptions.TopLeft);
            _subtitle.enableWordWrapping = true;
            _subtitle.gameObject.AddComponent<LayoutElement>().preferredHeight = 18f;
        }

        private void BuildSetupBar(RectTransform column)
        {
            VisualTheme theme = _ui.Theme;
            Image bar = _ui.Panel("Setup", column, new Color(1f, 1f, 1f, 0.025f), rounded: true, radius: 10f);
            bar.raycastTarget = false;
            UiFactory.Row(bar.rectTransform, 10f, new RectOffset(12, 12, 6, 6));
            var barLayout = bar.gameObject.AddComponent<LayoutElement>();
            barLayout.preferredHeight = barLayout.minHeight = 46f;
            barLayout.flexibleHeight = 0f;

            Label(bar.transform, "Power-on");
            _powerOn = _ui.Segmented("PowerOn", bar.transform, new[] { "Resolved", "Unknown (X)" }, 0, OnPowerOnChanged,
                new[]
                {
                    "Feedback loops settle to a definite 0 or 1 at power-on (repeatable).",
                    "Feedback loops start unknown (X) until the inputs drive them.",
                });

            // Hidden switch mirroring the segmented control (kept for scripting and tests).
            RectTransform hidden = UiFactory.CreateRect("Hidden", bar.transform);
            hidden.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            _powerOnUnknown = _ui.Toggle("PowerOnUnknown", hidden, "Power on loops as unknown (X)", false, isOn =>
            {
                _powerOn.SetSelectedWithoutNotify(isOn ? 1 : 0);
                Run();
            });
            hidden.gameObject.SetActive(false);

            _clockDivider = Divider(bar.transform);
            _pulseClock = _ui.Toggle("PulseClock", bar.transform, "Pulse clock", true, _ => Run());
            var pulseLayout = _pulseClock.GetComponent<LayoutElement>();
            pulseLayout.preferredWidth = 128f;
            pulseLayout.flexibleWidth = 0f;
            _ui.AddTooltip(_pulseClock.gameObject,
                "On: every step ends with one full clock pulse (rising, then falling edge).\nOff: the clock stays LOW.");

            Divider(bar.transform);
            Label(bar.transform, "Steps");
            _stepsMinus = _ui.IconButton("StepsMinus", bar.transform, Icons.Minus, "One step fewer", () => ChangeSteps(-1),
                28f, 14f);
            _stepsField = _ui.InputField("Steps", bar.transform, "auto");
            _stepsField.contentType = TMP_InputField.ContentType.IntegerNumber;
            _stepsField.characterLimit = 4;
            _stepsField.textComponent.alignment = TextAlignmentOptions.Center;
            ((TextMeshProUGUI)_stepsField.placeholder).alignment = TextAlignmentOptions.Center;
            var stepsLayout = _stepsField.GetComponent<LayoutElement>();
            stepsLayout.flexibleWidth = 0f;
            stepsLayout.preferredWidth = 78f;
            _stepsField.onValueChanged.AddListener(_ => _dirty = true);
            _stepsField.onSubmit.AddListener(_ => Run());
            _ui.AddTooltip(_stepsField.gameObject, "Number of steps. Leave empty to use the longest input pattern.");
            _stepsPlus = _ui.IconButton("StepsPlus", bar.transform, Icons.Plus, "One step more", () => ChangeSteps(1),
                28f, 14f);

            UiFactory.Flexible(bar.transform);
            Label(bar.transform, "Fill inputs");
            _presetCount = SmallButton(bar.transform, "Count up", ApplyCountUp,
                "Binary count over the inputs, like a truth table (first input = most significant bit).");
            _presetLow = SmallButton(bar.transform, "All 0", () => ApplyConstant(Signal.Low), "Every input LOW in every step.");
            _presetHigh = SmallButton(bar.transform, "All 1", () => ApplyConstant(Signal.High), "Every input HIGH in every step.");
        }

        private void BuildViewBar(RectTransform column)
        {
            VisualTheme theme = _ui.Theme;
            RectTransform bar = UiFactory.CreateRect("View Bar", column);
            UiFactory.Row(bar, 8f, new RectOffset(0, 0, 0, 0));
            var barLayout = bar.gameObject.AddComponent<LayoutElement>();
            barLayout.preferredHeight = barLayout.minHeight = 30f;
            barLayout.flexibleHeight = 0f;
            _viewSwitch = _ui.Segmented("View", bar, new[] { "Timing diagram", "Table", "Patterns (text)" }, 0,
                index => ShowView((ResultView)index),
                new[]
                {
                    "Waveforms of every input, clock and output; edit inputs by clicking their lanes.",
                    "One row per step with the values at the end of the step.",
                    "Type or paste 0/1 patterns, one per input.",
                });

            UiFactory.Flexible(bar);
            string high = ColorUtility.ToHtmlStringRGB(theme.SignalColor(Signal.High));
            string unknown = ColorUtility.ToHtmlStringRGB(theme.SignalColor(Signal.Unknown));
            string accent = ColorUtility.ToHtmlStringRGB(theme.Accent);
            // Coloured words rather than block glyphs: the UI font has no box-drawing characters.
            _legend = _ui.Text("Legend", bar,
                "<color=#" + high + "><b>HIGH</b></color>  ·  <color=#" + high + "99>LOW</color>  ·  <color=#" +
                unknown + "><b>X</b> unknown</color>  ·  <color=#" + accent + ">output changed</color>", 11f,
                theme.TextMuted, TextAlignmentOptions.MidlineRight);
            _legend.richText = true;
            _legend.overflowMode = TextOverflowModes.Overflow;
            _legend.gameObject.AddComponent<LayoutElement>().preferredWidth = 300f;

            _zoomGroup = UiFactory.CreateRect("Zoom", bar);
            UiFactory.Row(_zoomGroup, 0f, new RectOffset(0, 0, 0, 0), TextAnchor.MiddleRight);
            _ui.IconButton("ZoomOut", _zoomGroup, Icons.Minus, "Zoom out  <color=#687088>Ctrl+wheel</color>",
                () => _timeline.ZoomOut(), 28f, 14f);
            _ui.IconButton("ZoomIn", _zoomGroup, Icons.Plus, "Zoom in  <color=#687088>Ctrl+wheel</color>",
                () => _timeline.ZoomIn(), 28f, 14f);
            _ui.IconButton("Fit", _zoomGroup, Icons.Frame, "Fit all steps", () => _timeline.FitToView(), 28f, 14f);
        }

        private void BuildPatternsPanel(RectTransform column)
        {
            VisualTheme theme = _ui.Theme;
            Image frame = _ui.Panel("Patterns", column, theme.WorkspaceBackground, rounded: true, radius: 10f);
            var layout = frame.gameObject.AddComponent<LayoutElement>();
            layout.flexibleHeight = 1f;
            layout.minHeight = 200f;
            _patternsPanel = frame.rectTransform;
            RectTransform inner = UiFactory.Column("Inner", frame.rectTransform, 6f, new RectOffset(16, 16, 12, 12));
            UiFactory.Stretch(inner);
            TextMeshProUGUI help = _ui.Text("Help", inner,
                "One pattern per input: 0 and 1 for each step (spaces, commas and _ are ignored). After its pattern " +
                "ends, an input keeps its last value. Changes apply as you type.", 12.5f, theme.TextSecondary,
                TextAlignmentOptions.TopLeft);
            help.enableWordWrapping = true;
            help.gameObject.AddComponent<LayoutElement>().preferredHeight = 36f;
            RectTransform content = _ui.ScrollView("Fields", inner, out _, smooth: true);
            content.GetComponent<VerticalLayoutGroup>().padding = new RectOffset(0, 8, 2, 8);
            content.parent.parent.gameObject.AddComponent<LayoutElement>().flexibleHeight = 1f;
            _patternRoot = content;
        }

        private void BuildSummary(RectTransform column)
        {
            RectTransform row = UiFactory.CreateRect("Summary", column);
            UiFactory.Row(row, 6f, new RectOffset(0, 0, 0, 0));
            row.gameObject.AddComponent<LayoutElement>().preferredHeight = 28f;
            for (int i = 0; i < ChipCount; i++)
            {
                Image chip = _ui.Panel("Chip " + i, row, _ui.Theme.ButtonHover, rounded: true, radius: 8f);
                _ui.AddTooltip(chip.gameObject, string.Empty);
                var chipLayout = chip.gameObject.AddComponent<LayoutElement>();
                chipLayout.preferredHeight = 26f;
                TextMeshProUGUI text = _ui.Text("Text", chip.transform, string.Empty, 12f, _ui.Theme.TextSecondary,
                    TextAlignmentOptions.Center);
                text.richText = true;
                text.overflowMode = TextOverflowModes.Ellipsis;
                UiFactory.Stretch(text.rectTransform, 10f, 10f);
                chip.gameObject.SetActive(false);
                _chips.Add(chip);
                _chipTexts.Add(text);
            }

            _caveat = _ui.Text("Caveat", row, string.Empty, 11.5f, _ui.Theme.TextMuted, TextAlignmentOptions.MidlineRight);
            _caveat.richText = true;
            _caveat.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
        }

        private void BuildFooter(RectTransform column)
        {
            RectTransform buttons = UiFactory.CreateRect("Buttons", column);
            UiFactory.Row(buttons, 8f, new RectOffset(0, 0, 0, 0), TextAnchor.MiddleRight);
            buttons.gameObject.AddComponent<LayoutElement>().preferredHeight = 34f;
            _copyButton = _ui.Button("Copy", buttons, "Copy table", () => CopyToClipboard(), 104f);
            _exportButton = _ui.Button("Export", buttons, "Export CSV", () => ExportCsv(), 110f);
            UiFactory.Flexible(buttons);
            _ui.MakePrimary(_ui.Button("Done", buttons, "Done", Close, 96f));
        }

        private void OnPowerOnChanged(int index)
        {
            _powerOnUnknown.SetIsOnWithoutNotify(index == 1);
            Run();
        }

        private void Label(Transform parent, string text)
        {
            TextMeshProUGUI label = _ui.Text(text, parent, text, 12f, _ui.Theme.TextSecondary);
            label.gameObject.AddComponent<LayoutElement>().preferredWidth = label.GetPreferredValues(text).x + 2f;
        }

        private Button SmallButton(Transform parent, string text, UnityEngine.Events.UnityAction action, string tooltip)
        {
            Button button = _ui.Button(text, parent, text, action, -1f, 12.5f);
            var layout = button.GetComponent<LayoutElement>();
            layout.minHeight = layout.preferredHeight = 30f;
            _ui.AddTooltip(button.gameObject, tooltip);
            return button;
        }

        private RectTransform Divider(Transform parent)
        {
            RectTransform line = _ui.Separator(parent, vertical: true);
            line.GetComponent<LayoutElement>().preferredHeight = 22f;
            return line;
        }
    }
}
