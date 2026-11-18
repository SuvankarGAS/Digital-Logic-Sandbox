using System.Collections.Generic;
using System.Text;
using DigitalLogicSandbox.CircuitEditor.Workspace;
using DigitalLogicSandbox.Core.Validation;
using DigitalLogicSandbox.GateLibrary.Packaging;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace DigitalLogicSandbox.UI.Dialogs
{
    /// <summary>
    /// Modal "Create Custom Gate" dialog. Edits a <see cref="PackagingSpec"/> (metadata, scope, external port names
    /// and order) and validates it live with the real packager, so the user sees errors and warnings before
    /// committing. Packaging goes through <see cref="WorkspaceController.PackageCustomGate"/> and is undoable.
    /// </summary>
    public sealed class PackagingDialog : MonoBehaviour
    {
        private const int MaxListedMessages = 6;

        private readonly StringBuilder _builder = new StringBuilder(256);

        private UiFactory _ui;
        private WorkspaceController _workspace;
        private RectTransform _window;
        private TMP_InputField _name;
        private TMP_InputField _description;
        private TMP_InputField _category;
        private TMP_InputField _icon;
        private Toggle _selectionOnly;
        private TextMeshProUGUI _selectionLabel;
        private Toggle _clearWorkspace;
        private RectTransform _inputsList;
        private RectTransform _outputsList;
        private TextMeshProUGUI _messages;
        private Button _packageButton;
        private PackagingSpec _spec;
        private PackagingResult _preview;

        public bool IsOpen => gameObject.activeSelf;

        public PackagingSpec Spec => _spec;

        public PackagingResult Preview => _preview;

        public TMP_InputField NameField => _name;

        public Button PackageButton => _packageButton;

        public string MessagesText => _messages.text;

        /// <summary>Raised after a gate was packaged successfully.</summary>
        public event System.Action<PackagingResult> Packaged;

        public static PackagingDialog Create(Transform parent, UiFactory ui, WorkspaceController workspace)
        {
            Image blocker = ui.Panel("Packaging Dialog", parent, ui.Theme.Scrim);
            UiFactory.Stretch(blocker.rectTransform);
            var dialog = blocker.gameObject.AddComponent<PackagingDialog>();
            dialog._ui = ui;
            dialog._workspace = workspace;
            dialog.Build(blocker.rectTransform);
            blocker.gameObject.SetActive(false);
            return dialog;
        }

        /// <summary>Opens the dialog for the selection (when <paramref name="selectionOnly"/> and something is selected) or the whole circuit.</summary>
        public void Open(bool selectionOnly)
        {
            if (_workspace.Circuit == null)
            {
                return;
            }

            bool hasSelection = _workspace.Selection.NodeCount > 0;
            _selectionOnly.transform.parent.gameObject.SetActive(true);
            _selectionOnly.interactable = hasSelection;
            _selectionOnly.SetIsOnWithoutNotify(selectionOnly && hasSelection);
            _selectionLabel.text = hasSelection
                ? "Package only the selection (" + _workspace.Selection.NodeCount + " parts)"
                : "Package only the selection (nothing selected)";
            _clearWorkspace.SetIsOnWithoutNotify(false);

            _spec = _workspace.CreatePackagingSpec(_selectionOnly.isOn);
            _name.SetTextWithoutNotify(_spec.Name);
            _description.SetTextWithoutNotify(_spec.Description);
            _category.SetTextWithoutNotify(_spec.Category);
            _icon.SetTextWithoutNotify(_spec.Icon);

            gameObject.SetActive(true);
            transform.SetAsLastSibling();
            _workspace.InputSuspended = true;
            RebuildPorts();
            Revalidate();
            _name.Select();
            _name.ActivateInputField();
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

        /// <summary>Packages with the current settings. Returns the result (null when invalid).</summary>
        public PackagingResult Confirm()
        {
            ReadMetadata();
            PackagingResult result = _workspace.PackageCustomGate(_spec, _clearWorkspace.isOn);
            if (result.Succeeded)
            {
                Close();
                Packaged?.Invoke(result);
                return result;
            }

            _preview = result;
            ShowMessages(result.Report);
            return null;
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
                     _packageButton.interactable)
            {
                Confirm();
            }
        }

        private void Build(RectTransform blocker)
        {
            Image window = _ui.Card("Window", blocker, _ui.Theme.DialogBackground);
            _window = window.rectTransform;
            _window.anchorMin = _window.anchorMax = new Vector2(0.5f, 0.5f);
            _window.pivot = new Vector2(0.5f, 0.5f);
            _window.sizeDelta = new Vector2(560f, 680f);

            RectTransform column = UiFactory.Column("Content", _window, 6f, new RectOffset(20, 20, 16, 16));
            UiFactory.Stretch(column);

            TextMeshProUGUI title = _ui.Text("Title", column, "Create Custom Gate", 18f, _ui.Theme.TextPrimary,
                TextAlignmentOptions.MidlineLeft, FontStyles.Bold);
            title.gameObject.AddComponent<LayoutElement>().preferredHeight = 28f;
            TextMeshProUGUI hint = _ui.Text("Hint", column,
                "Inputs and outputs become the gate's pins. Its behaviour comes from the circuit itself.", 12f,
                _ui.Theme.TextSecondary);
            hint.gameObject.AddComponent<LayoutElement>().preferredHeight = 18f;

            _selectionOnly = _ui.Toggle("Scope", column, "Package only the selection", false, OnScopeChanged);
            _selectionLabel = _selectionOnly.GetComponentInChildren<TextMeshProUGUI>();

            _name = _ui.FieldRow(column, "Name", string.Empty, null);
            _description = _ui.FieldRow(column, "Description", string.Empty, null);
            _category = _ui.FieldRow(column, "Category", string.Empty, null);
            _icon = _ui.FieldRow(column, "Icon", string.Empty, null);
            _icon.characterLimit = 4;
            _name.onValueChanged.AddListener(_ => Revalidate());
            _description.onValueChanged.AddListener(_ => ReadMetadata());
            _category.onValueChanged.AddListener(_ => ReadMetadata());
            _icon.onValueChanged.AddListener(_ => ReadMetadata());

            RectTransform lists = UiFactory.CreateRect("Interface", column);
            UiFactory.Row(lists, 16f, new RectOffset(0, 0, 4, 0), TextAnchor.UpperLeft);
            var listsLayout = lists.gameObject.AddComponent<LayoutElement>();
            listsLayout.preferredHeight = 230f;
            listsLayout.flexibleHeight = 1f;
            _inputsList = PortColumn(lists, "Inputs");
            _outputsList = PortColumn(lists, "Outputs");

            _clearWorkspace = _ui.Toggle("Clear", column, "Remove the packaged parts from the workspace", false, null);

            _messages = _ui.Text("Messages", column, string.Empty, 12f, _ui.Theme.TextSecondary,
                TextAlignmentOptions.TopLeft);
            _messages.enableWordWrapping = true;
            _messages.overflowMode = TextOverflowModes.Ellipsis;
            _messages.gameObject.AddComponent<LayoutElement>().preferredHeight = 96f;

            RectTransform buttons = UiFactory.CreateRect("Buttons", column);
            UiFactory.Row(buttons, 8f, new RectOffset(0, 0, 0, 0), TextAnchor.MiddleRight);
            buttons.gameObject.AddComponent<LayoutElement>().preferredHeight = 34f;
            UiFactory.Flexible(buttons);
            _ui.Button("Cancel", buttons, "Cancel", Close, 100f);
            _packageButton = _ui.Button("Package", buttons, "Create Gate", () => Confirm(), 130f);
            _ui.MakePrimary(_packageButton);
            _packageButton.GetComponent<Image>().color = _ui.Theme.Accent;
        }

        private RectTransform PortColumn(Transform parent, string title)
        {
            RectTransform column = UiFactory.Column(title, parent, 4f, new RectOffset(0, 0, 0, 0));
            var layout = column.gameObject.AddComponent<LayoutElement>();
            layout.flexibleWidth = 1f;
            layout.preferredWidth = 240f;
            _ui.Caption(column, title);
            RectTransform list = UiFactory.Column("List", column, 4f, new RectOffset(0, 0, 0, 0));
            return list;
        }

        private void OnScopeChanged(bool selectionOnly)
        {
            if (!IsOpen)
            {
                return;
            }

            PackagingSpec fresh = _workspace.CreatePackagingSpec(selectionOnly);
            fresh.Name = _name.text;
            fresh.Description = _description.text;
            fresh.Category = _category.text;
            fresh.Icon = _icon.text;
            _spec = fresh;
            RebuildPorts();
            Revalidate();
        }

        private void RebuildPorts()
        {
            BuildPortRows(_inputsList, _spec.Inputs, "No inputs");
            BuildPortRows(_outputsList, _spec.Outputs, "No outputs");
        }

        private void BuildPortRows(RectTransform list, List<PortSpec> ports, string emptyText)
        {
            UiFactory.DestroyChildren(list);
            if (ports.Count == 0)
            {
                TextMeshProUGUI empty = _ui.Text("Empty", list, emptyText, 12f, _ui.Theme.TextSecondary);
                empty.gameObject.AddComponent<LayoutElement>().preferredHeight = 24f;
                return;
            }

            for (int i = 0; i < ports.Count; i++)
            {
                int index = i;
                PortSpec port = ports[i];
                RectTransform row = UiFactory.CreateRect("Port " + i, list);
                UiFactory.Row(row, 4f, new RectOffset(0, 0, 0, 0));
                row.gameObject.AddComponent<LayoutElement>().preferredHeight = 30f;

                TMP_InputField field = _ui.InputField("Name", row, "name");
                field.SetTextWithoutNotify(port.Name);
                field.characterLimit = 16;
                field.onValueChanged.AddListener(value =>
                {
                    port.Name = value;
                    Revalidate();
                });

                Button up = _ui.IconButton("Up", row, Icons.ChevronUp, "Move up", () => MovePort(ports, index, index - 1), 28f, 14f);
                up.interactable = i > 0;
                Button down = _ui.IconButton("Down", row, Icons.ChevronDown, "Move down", () => MovePort(ports, index, index + 1), 28f, 14f);
                down.interactable = i < ports.Count - 1;
            }
        }

        private void MovePort(List<PortSpec> ports, int from, int to)
        {
            if (PackagingSpec.Move(ports, from, to))
            {
                RebuildPorts();
                Revalidate();
            }
        }

        private void ReadMetadata()
        {
            if (_spec == null)
            {
                return;
            }

            _spec.Name = _name.text;
            _spec.Description = _description.text;
            _spec.Category = _category.text;
            _spec.Icon = _icon.text;
        }

        private void Revalidate()
        {
            if (_spec == null || _workspace.Circuit == null)
            {
                return;
            }

            ReadMetadata();
            _preview = _workspace.PreviewPackaging(_spec);
            _packageButton.interactable = _preview.Succeeded;
            ShowMessages(_preview.Report);
        }

        private void ShowMessages(ValidationReport report)
        {
            _builder.Clear();
            int listed = 0;
            IReadOnlyList<Diagnostic> diagnostics = report.Diagnostics;
            for (int pass = 0; pass < 2 && listed < MaxListedMessages; pass++)
            {
                DiagnosticSeverity wanted = pass == 0 ? DiagnosticSeverity.Error : DiagnosticSeverity.Warning;
                string colour = ColorUtility.ToHtmlStringRGB(pass == 0 ? _ui.Theme.Error : _ui.Theme.Warning);
                for (int i = 0; i < diagnostics.Count && listed < MaxListedMessages; i++)
                {
                    if (diagnostics[i].Severity != wanted)
                    {
                        continue;
                    }

                    if (diagnostics[i].Code == DiagnosticCode.MissingName)
                    {
                        // Not a mistake yet, just the next step: shown as a neutral prompt, not a red error (the
                        // Create button stays disabled until there is a name).
                        _builder.Append("<color=#").Append(ColorUtility.ToHtmlStringRGB(_ui.Theme.TextSecondary))
                            .Append('>').Append(diagnostics[i].Message).Append("</color>\n");
                        listed++;
                        continue;
                    }

                    _builder.Append("<color=#").Append(colour).Append('>')
                        .Append(pass == 0 ? "Error: " : "Warning: ")
                        .Append("</color>").Append(diagnostics[i].Message).Append('\n');
                    listed++;
                }
            }

            int total = report.ErrorCount + report.WarningCount;
            if (total > listed)
            {
                _builder.Append("... and ").Append(total - listed).Append(" more");
            }

            if (total == 0)
            {
                _builder.Append("<color=#").Append(ColorUtility.ToHtmlStringRGB(_ui.Theme.SignalColor(Core.Signals.Signal.High)))
                    .Append(">Ready to package.</color>");
            }

            _messages.text = _builder.ToString();
        }
    }
}
