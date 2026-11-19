using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using DigitalLogicSandbox.CircuitEditor.Workspace;
using DigitalLogicSandbox.Core.Circuit;
using DigitalLogicSandbox.Core.Nodes;
using DigitalLogicSandbox.Core.Signals;
using DigitalLogicSandbox.Core.Validation;
using DigitalLogicSandbox.TruthTable.Evaluation;
using DigitalLogicSandbox.TruthTable.Export;
using DigitalLogicSandbox.TruthTable.Generation;
using DigitalLogicSandbox.TruthTable.UI;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace DigitalLogicSandbox.UI.Dialogs
{
    /// <summary>
    /// Truth table panel. It plans a table for a target circuit (current circuit, a placed gate, a library
    /// gate), asks for confirmation when the table is large, generates it through a frame-budgeted
    /// <see cref="TruthTableJobDriver"/> with progress and cancellation, and shows it in a virtualized list (only
    /// the visible rows exist as objects). Regenerate, Copy (tab-separated) and Export CSV act on the shown table.
    /// Generation runs in an isolated simulation context, so the live circuit is never affected.
    /// </summary>
    public sealed class TruthTableDialog : MonoBehaviour
    {
        public const float RowHeight = 22f;
        private const int RowPoolSize = 40;
        private const float LeftPadding = 12f;
        private const float GroupGap = 18f;
        private const float MinColumnWidth = 34f;
        private const float MaxColumnWidth = 150f;
        private const float HeaderFontSize = 12.5f;
        private const float CellFontSize = 14f;

        private readonly StringBuilder _builder = new StringBuilder(512);
        private readonly List<TextMeshProUGUI> _headerCells = new List<TextMeshProUGUI>();
        private readonly List<RowView> _rows = new List<RowView>(RowPoolSize);
        private float[] _columnCenters = Array.Empty<float>();
        private float _digitHalfWidth = 4f;

        private UiFactory _ui;
        private WorkspaceController _workspace;
        private TruthTableGenerator _generator;
        private TruthTableJobDriver _driver;

        private RectTransform _layoutRoot;
        private RectTransform _window;

        /// <summary>Window width for narrow tables; wider tables grow the window up to the screen (see <see cref="FitWindow"/>).</summary>
        private const float DefaultWindowWidth = 620f;

        /// <summary>Horizontal padding of the window's content column (left + right).</summary>
        private const float WindowPadding = 40f;
        private TextMeshProUGUI _title;
        private TextMeshProUGUI _summary;
        private TextMeshProUGUI _status;
        private RectTransform _headerContent;
        private ScrollRect _scroll;
        private RectTransform _content;
        private Image _divider;
        private Button _generateButton;
        private Button _cancelButton;
        private Button _regenerateButton;
        private Button _copyButton;
        private Button _exportButton;
        private Button _sequenceButton;

        private CircuitDefinition _target;
        private string _targetTitle;
        private TruthTablePlan _plan;
        private TruthTableData _table;
        private string _hexHigh;
        private string _hexLow;
        private string _hexPending;

        public bool IsOpen => gameObject.activeSelf;

        public TruthTablePlan Plan => _plan;

        /// <summary>The job currently shown (running or finished), or null.</summary>
        public TruthTableJob Job => _driver.Current;

        public TruthTableData Table => _table;

        public TruthTableJobDriver Driver => _driver;

        public TruthTableGenerator Generator => _generator;

        public string TitleText => _title.text;

        public string SummaryText => _summary.text;

        public string StatusText => _status.text;

        public Button GenerateButton => _generateButton;

        public Button CancelButton => _cancelButton;

        public Button RegenerateButton => _regenerateButton;

        public Button CopyButton => _copyButton;

        public Button ExportButton => _exportButton;

        /// <summary>Shown when the target is sequential (truth tables refuse it).</summary>
        public Button SequenceButton => _sequenceButton;

        /// <summary>Raised by "Sequence analysis…" with the refused target and its title.</summary>
        public event Action<CircuitDefinition, string> SequenceAnalysisRequested;

        public ScrollRect Scroll => _scroll;

        /// <summary>Header captions in column order (inputs, then outputs).</summary>
        public IReadOnlyList<TextMeshProUGUI> HeaderCells => _headerCells;

        /// <summary>Folder for CSV exports (defaults to persistentDataPath/Exports).</summary>
        public string ExportDirectory { get; set; }

        /// <summary>Raised after the clipboard text has been produced (tests read it without the OS clipboard).</summary>
        public event Action<string> Copied;

        public static TruthTableDialog Create(Transform parent, UiFactory ui, WorkspaceController workspace,
            TruthTableLimits limits)
        {
            Image blocker = ui.Panel("Truth Table Dialog", parent, ui.Theme.Scrim);
            UiFactory.Stretch(blocker.rectTransform);
            var dialog = blocker.gameObject.AddComponent<TruthTableDialog>();
            dialog._ui = ui;
            dialog._workspace = workspace;
            dialog._generator = new TruthTableGenerator(workspace.Catalog, limits ?? new TruthTableLimits());
            dialog._driver = blocker.gameObject.AddComponent<TruthTableJobDriver>();
            dialog._driver.FrameBudgetMilliseconds = dialog._generator.Limits.EffectiveFrameBudget;
            dialog._driver.Progressed += dialog.OnProgress;
            dialog._driver.Finished += dialog.OnFinished;
            dialog._hexHigh = "#" + ColorUtility.ToHtmlStringRGB(ui.Theme.SignalColor(Signal.High));
            dialog._hexLow = "#" + ColorUtility.ToHtmlStringRGB(ui.Theme.TextSecondary);
            dialog._hexPending = "#" + ColorUtility.ToHtmlStringRGB(ui.Theme.PanelBorder);
            dialog.ExportDirectory = Path.Combine(Application.persistentDataPath, "Exports");
            dialog.Build(blocker.rectTransform);
            blocker.gameObject.SetActive(false);
            return dialog;
        }

        // ------------------------------------------------------------------------------------------------
        // Opening
        // ------------------------------------------------------------------------------------------------

        /// <summary>Truth table of the circuit shown in the workspace (main circuit, inspected gate or draft).</summary>
        public void OpenForCurrentCircuit()
        {
            _workspace.Sync();
            CircuitDefinition circuit = _workspace.Circuit;
            if (circuit != null)
            {
                Open(circuit, DisplayName(circuit));
            }
        }

        /// <summary>Truth table of a placed gate (custom: its definition; primitive: a one-gate harness).</summary>
        public bool OpenForNode(NodeId node)
        {
            CircuitDefinition circuit = _workspace.Circuit;
            if (circuit == null || !circuit.TryGetNode(node, out NodeDefinition definition) || definition.Component.IsUtility)
            {
                return false;
            }

            return OpenForComponent(definition.Component);
        }

        /// <summary>Truth table of a component from the library or the canvas.</summary>
        public bool OpenForComponent(ComponentRef component)
        {
            string title = _workspace.Geometry != null ? _workspace.Geometry.TitleOf(component) : component.ToString();
            CircuitDefinition target = TruthTableTargets.ForComponent(_workspace.Catalog, component, title);
            if (target == null)
            {
                _workspace.PostNotification("No truth table: '" + title + "' cannot be resolved.");
                return false;
            }

            Open(target, title);
            return true;
        }

        /// <summary>Plans a table for <paramref name="target"/>; small tables start generating immediately.</summary>
        public void Open(CircuitDefinition target, string title)
        {
            _target = target ?? throw new ArgumentNullException(nameof(target));
            _targetTitle = string.IsNullOrEmpty(title) ? target.Name : title;
            gameObject.SetActive(true);
            transform.SetAsLastSibling();
            LayoutRebuilder.ForceRebuildLayoutImmediate(_layoutRoot);
            _workspace.InputSuspended = true;
            Regenerate();
        }

        public void Close()
        {
            if (!IsOpen)
            {
                return;
            }

            _driver.Cancel();
            gameObject.SetActive(false);
            _workspace.InputSuspended = false;
        }

        // ------------------------------------------------------------------------------------------------
        // Actions
        // ------------------------------------------------------------------------------------------------

        /// <summary>Re-plans the same target (picking up edits) and regenerates unless confirmation is needed.</summary>
        public void Regenerate()
        {
            if (_target == null)
            {
                return;
            }

            _driver.Cancel();
            _plan = _generator.Plan(_target, _targetTitle);
            _table = null;
            _title.text = "Truth table - " + _plan.Title;
            _summary.text = Summary(_plan);
            BuildColumns(_plan);
            SetRowCount(0);

            if (!_plan.CanGenerate)
            {
                ShowReport(_plan.Report, "Cannot generate this truth table.");
            }
            else if (_plan.RequiresConfirmation)
            {
                ShowReport(_plan.Report, null);
                _status.text += "Press Generate to build all " + TruthTableGenerator.FormatCount(_plan.RowCount) + " rows.";
            }
            else
            {
                Generate();
            }

            RefreshButtons();
        }

        /// <summary>Starts generation for the current plan (the confirmation step for large tables).</summary>
        public void Generate()
        {
            if (_plan == null || !_plan.CanGenerate || _driver.IsRunning)
            {
                return;
            }

            TruthTableJob job = _generator.Start(_plan);
            _table = job.Table;
            SetRowCount(_table.RowCount);
            _driver.Run(job);
            if (_table.RowCount <= _generator.Limits.EffectiveConfirmRows)
            {
                _driver.RunToCompletion(); // small tables are instant
            }

            RefreshButtons();
        }

        /// <summary>Hands the (sequential) target over to sequence analysis.</summary>
        public void OpenSequenceAnalysis()
        {
            if (_target == null)
            {
                return;
            }

            CircuitDefinition target = _target;
            string title = _targetTitle;
            Close();
            SequenceAnalysisRequested?.Invoke(target, title);
        }

        public void CancelGeneration()
        {
            _driver.Cancel();
            RefreshButtons();
        }

        /// <summary>Copies the generated rows as tab-separated text. Returns the text (null when nothing to copy).</summary>
        public string CopyToClipboard()
        {
            if (_table == null || _table.CompletedRows == 0)
            {
                return null;
            }

            string text = TruthTableExporter.ToTabSeparated(_table);
            GUIUtility.systemCopyBuffer = text;
            Copied?.Invoke(text);
            _workspace.PostNotification("Truth table copied (" + _table.CompletedRows + " rows).");
            return text;
        }

        /// <summary>Writes the generated rows to a CSV file. Returns the path, or null on failure.</summary>
        public string ExportCsv()
        {
            if (_table == null || _table.CompletedRows == 0)
            {
                return null;
            }

            try
            {
                Directory.CreateDirectory(ExportDirectory);
                string path = Path.Combine(ExportDirectory, TruthTableExporter.SuggestFileName(_table) + ".csv");
                string temp = path + ".tmp";
                using (var writer = new StreamWriter(temp, false, new UTF8Encoding(false)))
                {
                    TruthTableExporter.WriteCsv(_table, writer);
                }

                if (File.Exists(path))
                {
                    File.Delete(path);
                }

                File.Move(temp, path);
                _workspace.PostNotification("Exported truth table to " + path);
                _status.text = "Exported to " + path;
                return path;
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                _status.text = "<color=#" + ColorUtility.ToHtmlStringRGB(_ui.Theme.Error) + ">Export failed: " +
                               exception.Message + "</color>";
                return null;
            }
        }

        /// <summary>Plain text of a displayed row (tests / accessibility), or null when the row is not visible.</summary>
        public string GetVisibleRowText(int row)
        {
            for (int i = 0; i < _rows.Count; i++)
            {
                if (_rows[i].Row == row && _rows[i].Root.gameObject.activeSelf)
                {
                    _rows[i].Text.ForceMeshUpdate();
                    return _rows[i].Text.GetParsedText();
                }
            }

            return null;
        }

        /// <summary>Scrolls so that <paramref name="row"/> is the first visible row.</summary>
        public void ScrollToRow(int row)
        {
            Vector2 position = _content.anchoredPosition;
            position.y = Mathf.Max(0f, row * RowHeight);
            _content.anchoredPosition = position;
            RefreshRows();
        }

        // ------------------------------------------------------------------------------------------------
        // Generation feedback
        // ------------------------------------------------------------------------------------------------

        private void OnProgress(TruthTableJob job)
        {
            if (!job.IsFinished)
            {
                _status.text = "Generating... " + TruthTableGenerator.FormatCount(job.CompletedRows) + " / " +
                               TruthTableGenerator.FormatCount(job.TotalRows) + " rows (" +
                               Mathf.FloorToInt(job.Progress * 100f) + "%)";
            }

            RefreshRows();
        }

        private void OnFinished(TruthTableJob job)
        {
            switch (job.Status)
            {
                case TruthTableStatus.Completed:
                    _builder.Clear();
                    _builder.Append("Complete: ").Append(TruthTableGenerator.FormatCount(job.TotalRows))
                        .Append(job.TotalRows == 1 ? " combination" : " combinations").Append(" evaluated by the simulation engine (")
                        .Append(job.EvaluationCount.ToString(CultureInfo.InvariantCulture)).Append(" gate evaluations, ")
                        .Append(job.Elapsed.TotalMilliseconds.ToString("0.#", CultureInfo.InvariantCulture)).Append(" ms).");
                    AppendWarnings(_plan.Report);
                    _status.text = _builder.ToString();
                    break;
                case TruthTableStatus.Cancelled:
                    _status.text = "Cancelled after " + TruthTableGenerator.FormatCount(job.CompletedRows) + " of " +
                                   TruthTableGenerator.FormatCount(job.TotalRows) + " rows. Generated rows can still be copied or exported.";
                    break;
                case TruthTableStatus.Failed:
                    _status.text = "<color=#" + ColorUtility.ToHtmlStringRGB(_ui.Theme.Error) + ">" + job.Error + "</color>";
                    break;
            }

            RefreshRows();
            RefreshButtons();
        }

        private void RefreshButtons()
        {
            bool running = _driver.IsRunning;
            bool hasRows = _table != null && _table.CompletedRows > 0;
            _generateButton.gameObject.SetActive(_plan != null && _plan.CanGenerate && _plan.RequiresConfirmation && _table == null);
            _cancelButton.gameObject.SetActive(running);
            _regenerateButton.interactable = !running && _target != null;
            _sequenceButton.gameObject.SetActive(_plan != null && !_plan.CanGenerate &&
                _plan.Report.Contains(DiagnosticCode.SequentialCircuit));
            _copyButton.interactable = !running && hasRows;
            _exportButton.interactable = !running && hasRows;
        }

        // ------------------------------------------------------------------------------------------------
        // Table rendering
        // ------------------------------------------------------------------------------------------------

        private void BuildColumns(TruthTablePlan plan)
        {
            int inputs = plan.Inputs.Count;
            int outputs = plan.Outputs.Count;
            int columns = inputs + outputs;
            while (_headerCells.Count < columns)
            {
                TextMeshProUGUI cell = _ui.Text("Header " + _headerCells.Count, _headerContent, string.Empty,
                    HeaderFontSize, _ui.Theme.TextPrimary, TextAlignmentOptions.Center, FontStyles.Bold);
                RectTransform rect = cell.rectTransform;
                rect.anchorMin = new Vector2(0f, 0f);
                rect.anchorMax = new Vector2(0f, 1f);
                rect.pivot = new Vector2(0.5f, 0.5f);
                _headerCells.Add(cell);
            }

            _columnCenters = columns == _columnCenters.Length ? _columnCenters : new float[columns];
            float x = LeftPadding;
            for (int c = 0; c < _headerCells.Count; c++)
            {
                TextMeshProUGUI cell = _headerCells[c];
                bool used = c < columns;
                cell.gameObject.SetActive(used);
                if (!used)
                {
                    continue;
                }

                if (c == inputs && inputs > 0)
                {
                    x += GroupGap;
                }

                bool isInput = c < inputs;
                string name = isInput ? plan.Inputs[c].Name : plan.Outputs[c - inputs].Name;
                cell.text = name;
                cell.color = isInput ? _ui.Theme.TextPrimary : _ui.Theme.Accent;
                float width = Mathf.Clamp(cell.GetPreferredValues(name).x + 16f, MinColumnWidth, MaxColumnWidth);
                cell.rectTransform.sizeDelta = new Vector2(width, 0f);
                cell.rectTransform.anchoredPosition = new Vector2(x + width * 0.5f, 0f);
                _columnCenters[c] = x + width * 0.5f;
                x += width;
            }

            // Stripes span at least the visible width.
            float viewportWidth = FitWindow(x + LeftPadding);
            float contentWidth = Mathf.Max(x + LeftPadding, viewportWidth);
            _headerContent.sizeDelta = new Vector2(contentWidth, _headerContent.sizeDelta.y);
            _content.sizeDelta = new Vector2(contentWidth, _content.sizeDelta.y);

            float dividerX = inputs > 0 && outputs > 0
                ? (_columnCenters[inputs - 1] + _columnCenters[inputs]) * 0.5f
                : -10f;
            _divider.rectTransform.anchoredPosition = new Vector2(dividerX, 0f);
            _divider.gameObject.SetActive(inputs > 0 && outputs > 0);

            if (_rows.Count > 0)
            {
                _digitHalfWidth = _rows[0].Text.GetPreferredValues("0").x * 0.5f;
            }
        }

        private void SetRowCount(int rows)
        {
            _content.sizeDelta = new Vector2(_content.sizeDelta.x, rows * RowHeight);
            _content.anchoredPosition = Vector2.zero;
            RefreshRows();
        }

        private void RefreshRows()
        {
            int total = _table?.RowCount ?? 0;
            int first = Mathf.Max(0, Mathf.FloorToInt(_content.anchoredPosition.y / RowHeight));
            for (int i = 0; i < _rows.Count; i++)
            {
                RowView view = _rows[i];
                int row = first + i;
                bool visible = row < total;
                view.Root.gameObject.SetActive(visible);
                if (!visible)
                {
                    view.Row = -1;
                    continue;
                }

                view.Row = row;
                view.Root.anchoredPosition = new Vector2(0f, -row * RowHeight);
                view.Stripe.color = row % 2 == 0 ? Color.clear : new Color(1f, 1f, 1f, 0.018f);
                FormatRow(row);
                view.Text.SetText(_builder);
            }

            _headerContent.anchoredPosition = new Vector2(_content.anchoredPosition.x, _headerContent.anchoredPosition.y);
        }

        private void FormatRow(int row)
        {
            _builder.Clear();
            int inputs = _table.InputCount;
            bool done = row < _table.CompletedRows;
            for (int c = 0; c < inputs; c++)
            {
                AppendCell(c, _table.GetInput(row, c), true);
            }

            for (int o = 0; o < _table.OutputCount; o++)
            {
                if (done)
                {
                    AppendCell(inputs + o, _table.GetOutput(row, o), true);
                }
                else
                {
                    AppendPosition(inputs + o);
                    _builder.Append("<color=").Append(_hexPending).Append(">.</color>");
                }
            }
        }

        private void AppendCell(int column, Signal value, bool bold)
        {
            AppendPosition(column);
            _builder.Append("<color=").Append(value == Signal.High ? _hexHigh : _hexLow).Append('>');
            if (bold && value == Signal.High)
            {
                _builder.Append("<b>").Append(TruthTableData.Symbol(value)).Append("</b>");
            }
            else
            {
                _builder.Append(TruthTableData.Symbol(value));
            }

            _builder.Append("</color>");
        }

        private void AppendPosition(int column)
        {
            float x = _columnCenters[column] - _digitHalfWidth;
            _builder.Append("<pos=").Append(Mathf.RoundToInt(x)).Append('>');
        }

        // ------------------------------------------------------------------------------------------------
        // Text helpers
        // ------------------------------------------------------------------------------------------------

        private static string Summary(TruthTablePlan plan)
        {
            int inputs = plan.Inputs.Count;
            int outputs = plan.Outputs.Count;
            return inputs + (inputs == 1 ? " input, " : " inputs, ") + outputs + (outputs == 1 ? " output" : " outputs") +
                   "  -  2^" + inputs + " = " + TruthTableGenerator.FormatCount(plan.RowCount) +
                   (plan.RowCount == 1 ? " combination" : " combinations");
        }

        private void ShowReport(ValidationReport report, string headline)
        {
            _builder.Clear();
            if (!string.IsNullOrEmpty(headline))
            {
                _builder.Append("<color=#").Append(ColorUtility.ToHtmlStringRGB(_ui.Theme.Error)).Append('>')
                    .Append(headline).Append("</color>\n");
            }

            int shown = 0;
            IReadOnlyList<Diagnostic> diagnostics = report.Diagnostics;
            for (int i = 0; i < diagnostics.Count && shown < 4; i++)
            {
                if (diagnostics[i].Severity != DiagnosticSeverity.Error)
                {
                    continue;
                }

                _builder.Append("<color=#").Append(ColorUtility.ToHtmlStringRGB(_ui.Theme.Error)).Append(">Error: ")
                    .Append(diagnostics[i].Message).Append("</color>\n");
                shown++;
            }

            AppendWarnings(report);
            _status.text = _builder.ToString();
        }

        private void AppendWarnings(ValidationReport report)
        {
            IReadOnlyList<Diagnostic> diagnostics = report.Diagnostics;
            int shown = 0;
            for (int i = 0; i < diagnostics.Count && shown < 2; i++)
            {
                if (diagnostics[i].Severity != DiagnosticSeverity.Warning)
                {
                    continue;
                }

                _builder.Append(_builder.Length > 0 ? "\n" : string.Empty).Append("<color=#")
                    .Append(ColorUtility.ToHtmlStringRGB(_ui.Theme.Warning)).Append(">Warning: ")
                    .Append(diagnostics[i].Message).Append("</color>");
                shown++;
            }

            if (shown > 0)
            {
                _builder.Append('\n');
            }
        }

        private string DisplayName(CircuitDefinition circuit)
        {
            return _workspace.Geometry != null ? _workspace.Geometry.NameOf(circuit) : circuit.Name;
        }

        private void Update()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
            {
                if (_driver.IsRunning)
                {
                    CancelGeneration();
                }
                else
                {
                    Close();
                }
            }
        }

        /// <summary>
        /// Widens the window so a table of <paramref name="tableWidth"/> fits without scrolling sideways (wide bus
        /// tables such as an ALU used to hide their last output columns), up to the screen width minus a margin.
        /// Returns the resulting viewport width. Anything wider still scrolls horizontally.
        /// </summary>
        private float FitWindow(float tableWidth)
        {
            var parent = (RectTransform)_window.parent;
            float max = Mathf.Max(DefaultWindowWidth, parent.rect.width - 80f);
            float width = Mathf.Clamp(tableWidth + WindowPadding, DefaultWindowWidth, max);
            _window.sizeDelta = new Vector2(width, _window.sizeDelta.y);
            return width - WindowPadding;
        }

        /// <summary>Current window width (tests).</summary>
        public float WindowWidth => _window.sizeDelta.x;

        // ------------------------------------------------------------------------------------------------
        // Construction
        // ------------------------------------------------------------------------------------------------

        private void Build(RectTransform blocker)
        {
            Image window = _ui.Card("Window", blocker, _ui.Theme.DialogBackground);
            RectTransform rect = window.rectTransform;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(DefaultWindowWidth, 620f);
            _window = rect;

            RectTransform column = UiFactory.Column("Content", rect, 6f, new RectOffset(20, 20, 14, 16));
            UiFactory.Stretch(column);
            _layoutRoot = column;

            RectTransform titleRow = UiFactory.CreateRect("Title Row", column);
            UiFactory.Row(titleRow, 8f, new RectOffset(0, 0, 0, 0));
            titleRow.gameObject.AddComponent<LayoutElement>().preferredHeight = 30f;
            _title = _ui.Text("Title", titleRow, "Truth table", 18f, _ui.Theme.TextPrimary,
                TextAlignmentOptions.MidlineLeft, FontStyles.Bold);
            _title.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
            _ui.IconButton("Close", titleRow, Icons.Close, "Close", Close, 30f, 15f);

            _summary = _ui.Text("Summary", column, string.Empty, 12.5f, _ui.Theme.TextSecondary);
            _summary.gameObject.AddComponent<LayoutElement>().preferredHeight = 18f;

            // Table: fixed header strip + virtualized body.
            Image tableFrame = _ui.Panel("Table", column, _ui.Theme.WorkspaceBackground, rounded: true, radius: 10f);
            var tableLayout = tableFrame.gameObject.AddComponent<LayoutElement>();
            tableLayout.flexibleHeight = 1f;
            tableLayout.minHeight = 200f;
            RectTransform table = tableFrame.rectTransform;

            RectTransform header = UiFactory.CreateRect("Header", table);
            header.anchorMin = new Vector2(0f, 1f);
            header.anchorMax = new Vector2(1f, 1f);
            header.pivot = new Vector2(0f, 1f);
            header.offsetMin = new Vector2(0f, -28f);
            header.offsetMax = Vector2.zero;
            header.gameObject.AddComponent<RectMask2D>();
            Image headerLine = _ui.Panel("Header Line", header, _ui.Theme.PanelBorder);
            headerLine.raycastTarget = false;
            RectTransform lineRect = headerLine.rectTransform;
            lineRect.anchorMin = new Vector2(0f, 0f);
            lineRect.anchorMax = new Vector2(1f, 0f);
            lineRect.pivot = new Vector2(0.5f, 0f);
            lineRect.sizeDelta = new Vector2(0f, 1f);
            _headerContent = UiFactory.CreateRect("Header Content", header);
            _headerContent.anchorMin = new Vector2(0f, 0f);
            _headerContent.anchorMax = new Vector2(0f, 1f);
            _headerContent.pivot = new Vector2(0f, 0.5f);
            _headerContent.sizeDelta = new Vector2(0f, 0f);

            RectTransform body = UiFactory.CreateRect("Body", table);
            UiFactory.Stretch(body, 0f, 0f, 28f, 0f);
            _scroll = body.gameObject.AddComponent<ScrollRect>();
            _scroll.horizontal = true;
            _scroll.vertical = true;
            _scroll.movementType = ScrollRect.MovementType.Clamped;
            _scroll.scrollSensitivity = RowHeight * 1.5f;
            _scroll.inertia = false;
            RectTransform viewport = UiFactory.CreateRect("Viewport", body);
            UiFactory.Stretch(viewport);
            viewport.gameObject.AddComponent<RectMask2D>();
            viewport.gameObject.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0f);
            _content = UiFactory.CreateRect("Rows", viewport);
            _content.anchorMin = _content.anchorMax = new Vector2(0f, 1f);
            _content.pivot = new Vector2(0f, 1f);
            _content.sizeDelta = Vector2.zero;
            _scroll.viewport = viewport;
            _scroll.content = _content;
            _scroll.onValueChanged.AddListener(_ => RefreshRows());

            for (int i = 0; i < RowPoolSize; i++)
            {
                Image stripe = _ui.Panel("Row " + i, _content, Color.clear);
                stripe.raycastTarget = false;
                RectTransform rowRect = stripe.rectTransform;
                rowRect.anchorMin = new Vector2(0f, 1f);
                rowRect.anchorMax = new Vector2(1f, 1f);
                rowRect.pivot = new Vector2(0f, 1f);
                rowRect.sizeDelta = new Vector2(0f, RowHeight);
                TextMeshProUGUI text = _ui.Text("Cells", rowRect, string.Empty, CellFontSize, _ui.Theme.TextPrimary);
                text.richText = true;
                text.overflowMode = TextOverflowModes.Overflow;
                UiFactory.Stretch(text.rectTransform);
                stripe.gameObject.SetActive(false);
                _rows.Add(new RowView { Root = rowRect, Stripe = stripe, Text = text, Row = -1 });
            }

            _divider = _ui.Panel("Divider", _content, _ui.Theme.PanelBorder);
            _divider.raycastTarget = false;
            RectTransform dividerRect = _divider.rectTransform;
            dividerRect.anchorMin = new Vector2(0f, 0f);
            dividerRect.anchorMax = new Vector2(0f, 1f);
            dividerRect.pivot = new Vector2(0.5f, 0.5f);
            dividerRect.sizeDelta = new Vector2(1f, 0f);

            _status = _ui.Text("Status", column, string.Empty, 12f, _ui.Theme.TextSecondary, TextAlignmentOptions.TopLeft);
            _status.enableWordWrapping = true;
            _status.richText = true;
            _status.overflowMode = TextOverflowModes.Ellipsis;
            _status.gameObject.AddComponent<LayoutElement>().preferredHeight = 64f;

            RectTransform buttons = UiFactory.CreateRect("Buttons", column);
            UiFactory.Row(buttons, 8f, new RectOffset(0, 0, 0, 0), TextAnchor.MiddleRight);
            buttons.gameObject.AddComponent<LayoutElement>().preferredHeight = 34f;
            _regenerateButton = _ui.Button("Regenerate", buttons, "Regenerate", Regenerate, 110f);
            _copyButton = _ui.Button("Copy", buttons, "Copy", () => CopyToClipboard(), 80f);
            _exportButton = _ui.Button("Export", buttons, "Export CSV", () => ExportCsv(), 110f);
            UiFactory.Flexible(buttons);
            _sequenceButton = _ui.Button("Sequence", buttons, "Sequence analysis…", OpenSequenceAnalysis, 170f);
            _cancelButton = _ui.Button("Cancel", buttons, "Cancel", CancelGeneration, 90f);
            _generateButton = _ui.Button("Generate", buttons, "Generate", Generate, 100f);
            _ui.MakePrimary(_generateButton);
            _ui.Button("Done", buttons, "Close", Close, 80f);

            _digitHalfWidth = _rows[0].Text.GetPreferredValues("0").x * 0.5f;
            RefreshButtons();
        }

        private sealed class RowView
        {
            public RectTransform Root;
            public Image Stripe;
            public TextMeshProUGUI Text;
            public int Row;
        }
    }
}
