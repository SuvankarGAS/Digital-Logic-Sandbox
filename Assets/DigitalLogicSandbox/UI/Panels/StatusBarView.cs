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
    /// Bottom panel showing simulation status, evaluation count, the first validation problem and live
    /// input/output values. Refreshed only when the workspace reports a change.
    /// </summary>
    public sealed class StatusBarView : MonoBehaviour
    {
        private const int MaxListedPorts = 8;

        /// <summary>How long a notification replaces the validation message.</summary>
        public const float NotificationSeconds = 6f;

        private readonly StringBuilder _builder = new StringBuilder(128);

        private WorkspaceController _workspace;
        private UiFactory _ui;
        private TextMeshProUGUI _status;
        private TextMeshProUGUI _evaluations;
        private TextMeshProUGUI _message;
        private TextMeshProUGUI _values;
        private string _notification;
        private float _notificationUntil;

        public string StatusText => _status.text;

        public string MessageText => _message.text;

        public string ValuesText => _values.text;

        public static StatusBarView Create(Transform parent, UiFactory ui, WorkspaceController workspace)
        {
            Image background = ui.Panel("Status Bar", parent, ui.Theme.PanelHeader);
            RectTransform rect = background.rectTransform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = new Vector2(1f, 0f);
            rect.pivot = new Vector2(0.5f, 0f);
            rect.sizeDelta = new Vector2(0f, UiFactory.StatusBarHeight);
            rect.anchoredPosition = Vector2.zero;
            UiFactory.Row(rect, 18f, new RectOffset(12, 12, 0, 0));

            var view = background.gameObject.AddComponent<StatusBarView>();
            view._workspace = workspace;
            view._ui = ui;
            view._status = view.Cell("Status", rect, 150f);
            view._evaluations = view.Cell("Evaluations", rect, 150f);
            view._values = view.Cell("Values", rect, 360f);
            view._message = view.Cell("Message", rect, -1f);

            workspace.StatusChanged += view.Refresh;
            workspace.Notification += view.ShowNotification;
            view.Refresh();
            return view;
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
                _values.text = string.Empty;
                _message.text = string.Empty;
                return;
            }

            CircuitSession session = _workspace.Session;
            ValidationReport report = session.Report;
            if (!session.IsSimulating)
            {
                SetStatus("Circuit has errors", _ui.Theme.Error);
                _evaluations.text = string.Empty;
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
                else if (runner.State == SimulationState.Paused)
                {
                    SetStatus(runner.HasPendingWork ? "Paused (pending)" : "Paused", _ui.Theme.Warning);
                }
                else
                {
                    SetStatus("Running", _ui.Theme.TextPrimary);
                }

                _evaluations.text = "Evaluations: " + runner.EvaluationCount;
                _values.text = DescribeValues(session);
            }

            if (_workspace.CurrentLevel != null && !_workspace.IsEditable)
            {
                _status.text = "Inspecting - " + _status.text;
            }
            else if (_workspace.CurrentLevel != null && _workspace.CurrentLevel.IsDraft)
            {
                _status.text = "Editing - " + _status.text;
            }

            if (_notification != null)
            {
                _message.text = _notification;
                _message.color = _ui.Theme.Accent;
                return;
            }

            DescribeFirstProblem(report);
        }

        private void SetStatus(string text, Color color)
        {
            _status.text = text;
            _status.color = color;
        }

        private void DescribeFirstProblem(ValidationReport report)
        {
            IReadOnlyList<Diagnostic> diagnostics = report.Diagnostics;
            for (int pass = 0; pass < 2; pass++)
            {
                DiagnosticSeverity wanted = pass == 0 ? DiagnosticSeverity.Error : DiagnosticSeverity.Warning;
                for (int i = 0; i < diagnostics.Count; i++)
                {
                    if (diagnostics[i].Severity != wanted)
                    {
                        continue;
                    }

                    int count = pass == 0 ? report.ErrorCount : report.WarningCount;
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
                    _builder.Append("  ->  ");
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

                Signal value = input ? session.GetInputValue(node) : session.GetOutputValue(node);
                if (i > 0)
                {
                    _builder.Append(' ');
                }

                _builder.Append(ports[i].Name).Append('=').Append(value.ToDisplayChar());
            }

            if (ports.Count > MaxListedPorts)
            {
                _builder.Append(" ...");
            }
        }

        private TextMeshProUGUI Cell(string name, Transform parent, float width)
        {
            TextMeshProUGUI text = _ui.Text(name, parent, string.Empty, 12.5f, _ui.Theme.TextSecondary);
            var layout = text.gameObject.AddComponent<LayoutElement>();
            if (width > 0f)
            {
                layout.preferredWidth = width;
            }
            else
            {
                layout.flexibleWidth = 1f;
            }

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
