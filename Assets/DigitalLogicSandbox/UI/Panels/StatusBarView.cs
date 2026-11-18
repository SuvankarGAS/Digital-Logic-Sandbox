using System.Collections.Generic;
using System.Text;
using DigitalLogicSandbox.CircuitEditor.Workspace;
using DigitalLogicSandbox.Core.Circuit;
using DigitalLogicSandbox.Core.Nodes;
using DigitalLogicSandbox.Core.Ports;
using DigitalLogicSandbox.Core.Signals;
using DigitalLogicSandbox.Core.Validation;
using DigitalLogicSandbox.Simulation.Engine;
using DigitalLogicSandbox.Simulation.Propagation;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DigitalLogicSandbox.UI.Panels
{
    /// <summary>
    /// Floating status pill at the bottom of the workspace. At rest it shows only a coloured state dot and one word
    /// ("Running", "Paused", …), plus a message when there is something to say (a notification, the first
    /// validation problem). Clicking it expands a small details card with the evaluation count, propagation
    /// status, clock and live input/output values. Refreshed only when the workspace reports a change.
    /// </summary>
    public sealed class StatusBarView : MonoBehaviour
    {
        private const int MaxListedPorts = 8;
        private const float MaxMessageWidth = 460f;

        /// <summary>How long a notification replaces the validation message.</summary>
        public const float NotificationSeconds = 6f;

        private readonly StringBuilder _builder = new StringBuilder(128);

        private WorkspaceController _workspace;
        private UiFactory _ui;
        private TextMeshProUGUI _status;
        private TextMeshProUGUI _clock;
        private TextMeshProUGUI _evaluations;
        private TextMeshProUGUI _message;
        private TextMeshProUGUI _values;
        private Image _dot;
        private TextMeshProUGUI _chevron;
        private RectTransform _details;
        private string _notification;
        private float _notificationUntil;

        /// <summary>True while the details card is expanded.</summary>
        public bool DetailsOpen => _details != null && _details.gameObject.activeSelf;

        public string StatusText => _status.text;

        public string MessageText => _message.text;

        public string ValuesText => _values.text;

        public string ClockText => _clock.text;

        public static StatusBarView Create(Transform parent, UiFactory ui, WorkspaceController workspace)
        {
            Image background = ui.Card("Status Bar", parent, ui.Theme.PanelBackground, radius: UiFactory.StatusBarHeight * 0.5f);
            RectTransform rect = background.rectTransform;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0f);
            rect.pivot = new Vector2(0.5f, 0f);
            rect.sizeDelta = new Vector2(0f, UiFactory.StatusBarHeight);
            rect.anchoredPosition = new Vector2(0f, UiFactory.Margin + 2f);
            UiFactory.Row(rect, 9f, new RectOffset(14, 10, 0, 0));
            rect.gameObject.AddComponent<ContentSizeFitter>().horizontalFit = ContentSizeFitter.FitMode.PreferredSize;

            var view = background.gameObject.AddComponent<StatusBarView>();
            view._workspace = workspace;
            view._ui = ui;

            var toggle = background.gameObject.AddComponent<Button>();
            toggle.transition = Selectable.Transition.None;
            toggle.navigation = new Navigation { mode = Navigation.Mode.None };
            toggle.onClick.AddListener(view.ToggleDetails);
            ui.AddTooltip(background.gameObject, "Simulation details");

            view._dot = ui.Panel("Dot", rect, ui.Theme.SignalColor(Signal.High));
            view._dot.sprite = DigitalLogicSandbox.Rendering.RenderAssets.Circle;
            view._dot.raycastTarget = false;
            var dotLayout = view._dot.gameObject.AddComponent<LayoutElement>();
            dotLayout.preferredWidth = dotLayout.minWidth = 8f;
            dotLayout.preferredHeight = dotLayout.minHeight = 8f;
            view._status = view.Cell("Status", rect, ui.Theme.TextPrimary);
            view._clock = view.Cell("Clock", rect, ui.Theme.TextSecondary);
            view._message = view.Cell("Message", rect, ui.Theme.TextSecondary);
            view._chevron = ui.Icon("Chevron", rect, Icons.ChevronUp, 13f, ui.Theme.TextMuted);
            view._chevron.gameObject.AddComponent<LayoutElement>().preferredWidth = 16f;

            // Details card above the pill (collapsed by default).
            Image details = ui.Card("Status Details", parent, ui.Theme.PanelBackground, radius: 12f);
            view._details = details.rectTransform;
            view._details.anchorMin = view._details.anchorMax = new Vector2(0.5f, 0f);
            view._details.pivot = new Vector2(0.5f, 0f);
            view._details.anchoredPosition = new Vector2(0f, UiFactory.Margin + UiFactory.StatusBarHeight + 10f);
            view._details.sizeDelta = new Vector2(340f, 0f);
            var column = details.gameObject.AddComponent<VerticalLayoutGroup>();
            column.padding = new RectOffset(16, 16, 10, 12);
            column.spacing = 2f;
            column.childControlWidth = true;
            column.childControlHeight = true;
            column.childForceExpandWidth = true;
            column.childForceExpandHeight = false;
            details.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            ui.Caption(details.transform, "Simulation");
            view._evaluations = view.DetailLine("Evaluations", details.transform);
            view._values = view.DetailLine("Values", details.transform);
            view._values.enableWordWrapping = true;
            details.gameObject.SetActive(false);

            workspace.StatusChanged += view.Refresh;
            workspace.Notification += view.ShowNotification;
            view.Refresh();
            return view;
        }

        /// <summary>Expands or collapses the details card.</summary>
        public void ToggleDetails()
        {
            _details.gameObject.SetActive(!_details.gameObject.activeSelf);
            Refresh();
        }

        /// <summary>Shows a transient message (e.g. a refused edit or a saved version).</summary>
        public void ShowNotification(string message)
        {
            _notification = message;
            _notificationUntil = Time.unscaledTime + NotificationSeconds;
            Refresh();
        }

        private void Update()
        {
            if (_notification != null && Time.unscaledTime > _notificationUntil)
            {
                _notification = null;
                Refresh();
            }
        }

        public void Refresh()
        {
            if (_workspace == null || !_workspace.IsInitialized || _workspace.Circuit == null)
            {
                _status.text = "No circuit";
                _evaluations.text = string.Empty;
                _clock.text = string.Empty;
                _values.text = string.Empty;
                _message.text = string.Empty;
                Layout();
                return;
            }

            RefreshContent();
            Layout();
        }

        private void RefreshContent()
        {

            CircuitSession session = _workspace.Session;
            ValidationReport report = session.Report;
            if (!session.IsSimulating)
            {
                SetStatus("Circuit has errors", _ui.Theme.Error);
                _evaluations.text = string.Empty;
                _clock.text = string.Empty;
                _values.text = string.Empty;
            }
            else
            {
                SimulationRunner runner = session.Runner;
                SettleStatus settle = runner.LastResult.Status;
                if (settle == SettleStatus.Oscillating)
                {
                    SetStatus("Oscillating", _ui.Theme.Warning);
                }
                else if (runner.Context.ContentionCount > 0)
                {
                    SetStatus("Bus contention", _ui.Theme.Warning);
                }
                else if (runner.State == SimulationState.Paused)
                {
                    SetStatus(runner.HasPendingWork ? "Paused (pending)" : "Paused", _ui.Theme.Warning);
                }
                else
                {
                    SetStatus("Running", _ui.Theme.SignalColor(Signal.High));
                }

                _clock.text = runner.HasClock
                    ? "CLK " + runner.Context.Clock.ToDisplayChar() + "  ·  " +
                      (runner.ClockEnabled && runner.State == SimulationState.Running
                          ? runner.ClockFrequency.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + " Hz"
                          : "manual") +
                      "  ·  cycle " + runner.ClockCycles +
                      (runner.Circuit.ClockDomainCount > 1 ? "  ·  " + runner.Circuit.ClockDomainCount + " clocks" : string.Empty)
                    : string.Empty;

                _evaluations.text = "Evaluations  <color=#" + ColorUtility.ToHtmlStringRGB(_ui.Theme.TextPrimary) + ">" +
                    runner.EvaluationCount + "</color>" +
                    (_workspace.SignalAnimation != null && _workspace.SignalAnimation.IsAnimating ? "   · propagating" : string.Empty);
                _values.text = DescribeValues(session);
            }

            if (_notification != null)
            {
                _message.text = _notification;
                _message.color = _ui.Theme.Accent;
                return;
            }

            if (session.IsSimulating && !report.HasErrors && session.Runner.Context.ContentionCount > 0)
            {
                // Runtime problem (not a structural one): explain it until the drivers stop conflicting.
                var runtime = new ValidationReport();
                DigitalLogicSandbox.Simulation.Diagnostics.SimulationDiagnostics.ReportContention(session.Runner.Context, runtime);
                if (runtime.Diagnostics.Count > 0)
                {
                    _message.text = runtime.Diagnostics[0].Message;
                    _message.color = _ui.Theme.Warning;
                    return;
                }
            }

            DescribeFirstProblem(report);
        }

        /// <summary>Sets the state word; the dot carries the colour, the word stays calm and readable.</summary>
        private void SetStatus(string text, Color color)
        {
            _status.text = text;
            _status.color = _ui.Theme.TextPrimary;
            _dot.color = color;
        }

        /// <summary>Hides empty cells and sizes the pill to its content.</summary>
        private void Layout()
        {
            _clock.gameObject.SetActive(!string.IsNullOrEmpty(_clock.text));
            _message.gameObject.SetActive(!string.IsNullOrEmpty(_message.text));
            Fit(_status, 240f);
            Fit(_clock, 220f);
            Fit(_message, MaxMessageWidth);
            _chevron.text = DetailsOpen ? Icons.ChevronDown : Icons.ChevronUp;
            _values.gameObject.SetActive(!string.IsNullOrEmpty(_values.text));
        }

        private static void Fit(TextMeshProUGUI text, float max)
        {
            if (text.gameObject.activeSelf)
            {
                text.GetComponent<LayoutElement>().preferredWidth = Mathf.Min(max, text.GetPreferredValues(text.text).x + 2f);
            }
        }

        /// <summary>
        /// Shows the first error, else the first warning. Feedback-loop warnings are left out: every latch, flip-flop
        /// and memory is a loop on purpose, so in a sequential circuit that warning was permanently on. Loops that
        /// actually misbehave still surface as oscillation or contention.
        /// </summary>
        private void DescribeFirstProblem(ValidationReport report)
        {
            IReadOnlyList<Diagnostic> diagnostics = report.Diagnostics;
            int feedback = 0;
            for (int i = 0; i < diagnostics.Count; i++)
            {
                feedback += diagnostics[i].Code == DiagnosticCode.FeedbackLoop ? 1 : 0;
            }

            for (int pass = 0; pass < 2; pass++)
            {
                DiagnosticSeverity wanted = pass == 0 ? DiagnosticSeverity.Error : DiagnosticSeverity.Warning;
                for (int i = 0; i < diagnostics.Count; i++)
                {
                    if (diagnostics[i].Severity != wanted || diagnostics[i].Code == DiagnosticCode.FeedbackLoop)
                    {
                        continue;
                    }

                    int count = pass == 0 ? report.ErrorCount : report.WarningCount - feedback;
                    string suffix = count > 1 ? "  (+" + (count - 1) + " more)" : string.Empty;
                    _message.text = (pass == 0 ? "Error: " : "Warning: ") + diagnostics[i].Message + suffix;
                    _message.color = pass == 0 ? _ui.Theme.Error : _ui.Theme.Warning;
                    return;
                }
            }

            _message.text = string.Empty;
        }

        private string DescribeValues(CircuitSession session)
        {
            CircuitDefinition circuit = session.Circuit;
            _builder.Clear();
            AppendPorts(session, circuit, circuit.Inputs, input: true);
            if (circuit.Outputs.Count > 0)
            {
                if (_builder.Length > 0)
                {
                    _builder.Append("   \u2192   ");
                }

                AppendPorts(session, circuit, circuit.Outputs, input: false);
            }

            return _builder.ToString();
        }

        private void AppendPorts(CircuitSession session, CircuitDefinition circuit, IReadOnlyList<InterfacePort> ports,
            bool input)
        {
            int count = Mathf.Min(ports.Count, MaxListedPorts);
            for (int i = 0; i < count; i++)
            {
                if (!circuit.TryGetBoundaryNode(ports[i].Id, out NodeId node))
                {
                    continue;
                }

                if (i > 0)
                {
                    _builder.Append(' ');
                }

                _builder.Append(ports[i].Name).Append('=');
                if (ports[i].Width > 1)
                {
                    _builder.Append(session.FormatBoundaryValue(node));
                    continue;
                }

                Signal value = input ? session.GetInputValue(node) : session.GetOutputValue(node);
                _builder.Append(value.ToDisplayChar());
            }

            if (ports.Count > MaxListedPorts)
            {
                _builder.Append(" ...");
            }
        }

        private TextMeshProUGUI Cell(string name, Transform parent, Color color)
        {
            TextMeshProUGUI text = _ui.Text(name, parent, string.Empty, 12.5f, color);
            text.richText = true;
            text.gameObject.AddComponent<LayoutElement>();
            return text;
        }

        private TextMeshProUGUI DetailLine(string name, Transform parent)
        {
            TextMeshProUGUI text = _ui.Text(name, parent, string.Empty, 12.5f, _ui.Theme.TextSecondary,
                TextAlignmentOptions.TopLeft);
            text.richText = true;
            text.overflowMode = TextOverflowModes.Overflow;
            text.gameObject.AddComponent<LayoutElement>().minHeight = 20f;
            return text;
        }

        private void OnDestroy()
        {
            if (_workspace != null)
            {
                _workspace.StatusChanged -= Refresh;
                _workspace.Notification -= ShowNotification;
            }
        }
    }
}
