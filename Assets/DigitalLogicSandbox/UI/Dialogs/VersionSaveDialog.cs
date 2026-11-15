using System.Collections.Generic;
using System.Text;
using DigitalLogicSandbox.CircuitEditor.Navigation;
using DigitalLogicSandbox.CircuitEditor.Workspace;
using DigitalLogicSandbox.Core.Circuit;
using DigitalLogicSandbox.Core.Validation;
using DigitalLogicSandbox.GateLibrary.Custom;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace DigitalLogicSandbox.UI.Dialogs
{
    /// <summary>
    /// Modal "Save new version" dialog for an edited custom gate. It explains what each update mode would affect
    /// (instances in the main circuit, dependent gates, connections that would be lost because pins were removed),
    /// shows the draft's validation and saves through <see cref="WorkspaceController.SaveDraft"/> (undoable).
    /// </summary>
    public sealed class VersionSaveDialog : MonoBehaviour
    {
        private readonly StringBuilder _builder = new StringBuilder(256);

        private UiFactory _ui;
        private WorkspaceController _workspace;
        private TextMeshProUGUI _title;
        private TextMeshProUGUI _impact;
        private TextMeshProUGUI _messages;
        private Toggle _keep;
        private Toggle _updateMain;
        private Toggle _updateEverywhere;
        private Button _saveButton;

        public bool IsOpen => gameObject.activeSelf;

        public Button SaveButton => _saveButton;

        public string ImpactText => _impact.text;

        public string MessagesText => _messages.text;

        public VersionUpdateMode SelectedMode =>
            _updateEverywhere.isOn ? VersionUpdateMode.UpdateEverywhere
            : _updateMain.isOn ? VersionUpdateMode.UpdateMainCircuit
            : VersionUpdateMode.KeepReferences;

        /// <summary>Raised after a successful save.</summary>
        public event System.Action<DraftSaveResult> Saved;

        public static VersionSaveDialog Create(Transform parent, UiFactory ui, WorkspaceController workspace)
        {
            Image blocker = ui.Panel("Version Save Dialog", parent, ui.Theme.Scrim);
            UiFactory.Stretch(blocker.rectTransform);
            var dialog = blocker.gameObject.AddComponent<VersionSaveDialog>();
            dialog._ui = ui;
            dialog._workspace = workspace;
            dialog.Build(blocker.rectTransform);
            blocker.gameObject.SetActive(false);
            return dialog;
        }

        public void Open()
        {
            NavigationLevel draft = _workspace.Navigator.Draft;
            if (draft == null)
            {
                return;
            }

            _title.text = "Save '" + draft.Circuit.Name + "' as version " + draft.Circuit.Version;
            VersionImpact impact = _workspace.AnalyzeDraftSave();
            DescribeImpact(draft, impact);

            // Default: update the main circuit when it uses the gate, otherwise just publish.
            SelectMode(impact.MainCircuitInstances > 0 ? VersionUpdateMode.UpdateMainCircuit : VersionUpdateMode.KeepReferences);
            _updateEverywhere.interactable = impact.CascadeCount > 0;

            ValidationReport report = _workspace.ValidateDraft();
            ShowReport(report, null);
            _saveButton.interactable = report != null && !report.HasErrors && draft.HasUnsavedChanges;

            gameObject.SetActive(true);
            transform.SetAsLastSibling();
            _workspace.InputSuspended = true;
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

        public void SelectMode(VersionUpdateMode mode)
        {
            _keep.SetIsOnWithoutNotify(mode == VersionUpdateMode.KeepReferences);
            _updateMain.SetIsOnWithoutNotify(mode == VersionUpdateMode.UpdateMainCircuit);
            _updateEverywhere.SetIsOnWithoutNotify(mode == VersionUpdateMode.UpdateEverywhere);
        }

        /// <summary>Saves with the selected mode. Returns the result (the dialog stays open on failure).</summary>
        public DraftSaveResult Confirm()
        {
            DraftSaveResult result = _workspace.SaveDraft(SelectedMode);
            if (result.Succeeded)
            {
                Close();
                Saved?.Invoke(result);
            }
            else
            {
                ShowReport(result.Report, result.Error);
            }

            return result;
        }

        private void Update()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null)
            {
                return;
            }

            if (keyboard.escapeKey.wasPressedThisFrame)
            {
                Close();
            }
            else if ((keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame) &&
                     _saveButton.interactable)
            {
                Confirm();
            }
        }

        private void DescribeImpact(NavigationLevel draft, VersionImpact impact)
        {
            _builder.Clear();
            _builder.Append("Version ").Append(draft.DraftSource.Version)
                .Append(" stays in the library, so nothing that uses it breaks.\n\n");
            _builder.Append("Main circuit: ").Append(impact.MainCircuitInstances).Append(" instance")
                .Append(impact.MainCircuitInstances == 1 ? "" : "s").Append(" of v").Append(draft.DraftSource.Version).Append('.');
            if (impact.MainCircuitDroppedConnections > 0)
            {
                _builder.Append(" Updating them removes ").Append(impact.MainCircuitDroppedConnections)
                    .Append(" connection(s) to pins that no longer exist.");
            }

            List<CircuitDefinition> dependents = impact.DirectDependents;
            _builder.Append("\nOther gates using v").Append(draft.DraftSource.Version).Append(": ");
            if (dependents.Count == 0)
            {
                _builder.Append("none.");
            }
            else
            {
                for (int i = 0; i < dependents.Count; i++)
                {
                    _builder.Append(i > 0 ? ", " : string.Empty).Append(dependents[i].Name);
                }

                _builder.Append(". \"Update everywhere\" creates new versions of ").Append(impact.CascadeCount)
                    .Append(" gate(s)");
                if (impact.CascadeDroppedConnections > 0)
                {
                    _builder.Append(" and removes ").Append(impact.CascadeDroppedConnections).Append(" internal connection(s)");
                }

                _builder.Append('.');
            }

            _impact.text = _builder.ToString();
        }

        private void ShowReport(ValidationReport report, string error)
        {
            _builder.Clear();
            if (!string.IsNullOrEmpty(error))
            {
                _builder.Append("<color=#").Append(ColorUtility.ToHtmlStringRGB(_ui.Theme.Error)).Append('>')
                    .Append(error).Append("</color>\n");
            }

            if (report != null)
            {
                IReadOnlyList<Diagnostic> diagnostics = report.Diagnostics;
                int shown = 0;
                for (int i = 0; i < diagnostics.Count && shown < 5; i++)
                {
                    bool isError = diagnostics[i].Severity == DiagnosticSeverity.Error;
                    if (!isError && diagnostics[i].Severity != DiagnosticSeverity.Warning)
                    {
                        continue;
                    }

                    Color color = isError ? _ui.Theme.Error : _ui.Theme.Warning;
                    _builder.Append("<color=#").Append(ColorUtility.ToHtmlStringRGB(color)).Append('>')
                        .Append(isError ? "Error: " : "Warning: ").Append(diagnostics[i].Message).Append("</color>\n");
                    shown++;
                }

                if (shown == 0 && string.IsNullOrEmpty(error))
                {
                    _builder.Append("The edited gate is valid.");
                }
            }

            _messages.text = _builder.ToString();
        }

        private void Build(RectTransform blocker)
        {
            Image window = _ui.Card("Window", blocker, _ui.Theme.PanelBackground);
            RectTransform rect = window.rectTransform;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(560f, 470f);

            RectTransform column = UiFactory.Column("Content", rect, 8f, new RectOffset(20, 20, 16, 16));
            UiFactory.Stretch(column);

            _title = _ui.Text("Title", column, "Save new version", 18f, _ui.Theme.TextPrimary,
                TextAlignmentOptions.MidlineLeft, FontStyles.Bold);
            _title.gameObject.AddComponent<LayoutElement>().preferredHeight = 28f;

            _impact = _ui.Text("Impact", column, string.Empty, 12.5f, _ui.Theme.TextSecondary, TextAlignmentOptions.TopLeft);
            _impact.enableWordWrapping = true;
            _impact.gameObject.AddComponent<LayoutElement>().preferredHeight = 110f;

            _ui.Caption(column, "Existing references");
            var group = column.gameObject.AddComponent<ToggleGroup>();
            group.allowSwitchOff = false;
            _keep = _ui.Toggle("Keep", column, "Keep existing references (publish only)", true, null);
            _updateMain = _ui.Toggle("UpdateMain", column, "Update instances in the main circuit", false, null);
            _updateEverywhere = _ui.Toggle("UpdateEverywhere", column,
                "Update everywhere (also new versions of gates that use it)", false, null);
            _keep.group = group;
            _updateMain.group = group;
            _updateEverywhere.group = group;

            _messages = _ui.Text("Messages", column, string.Empty, 12f, _ui.Theme.TextSecondary, TextAlignmentOptions.TopLeft);
            _messages.enableWordWrapping = true;
            _messages.richText = true;
            _messages.overflowMode = TextOverflowModes.Ellipsis;
            var messagesLayout = _messages.gameObject.AddComponent<LayoutElement>();
            messagesLayout.preferredHeight = 80f;
            messagesLayout.flexibleHeight = 1f;

            RectTransform buttons = UiFactory.CreateRect("Buttons", column);
            UiFactory.Row(buttons, 8f, new RectOffset(0, 0, 0, 0), TextAnchor.MiddleRight);
            buttons.gameObject.AddComponent<LayoutElement>().preferredHeight = 34f;
            UiFactory.Flexible(buttons);
            _ui.Button("Cancel", buttons, "Cancel", Close, 100f);
            _saveButton = _ui.Button("Save", buttons, "Save Version", () => Confirm(), 130f);
            _ui.MakePrimary(_saveButton);
        }
    }
}
