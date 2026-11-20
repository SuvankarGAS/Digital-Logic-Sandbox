using System;
using System.Text;
using DigitalLogicSandbox.CircuitEditor.Workspace;
using DigitalLogicSandbox.Core.Nodes;
using DigitalLogicSandbox.GateLibrary.Programs;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace DigitalLogicSandbox.UI.Dialogs
{
    /// <summary>
    /// Modal editor for a user instruction set: its name and spec text, parsed live, with line-numbered errors, a
    /// preview of the reference the program editor will show, and save and delete as undoable steps. A new set can be
    /// bound to the program gate it was created from.
    /// </summary>
    public sealed class InstructionSetDialog : MonoBehaviour
    {
        private const int MaxErrorsShown = 4;
        private const float PreferredHeight = 600f;

        private readonly StringBuilder _builder = new StringBuilder(256);

        private UiFactory _ui;
        private WorkspaceController _workspace;
        private ConfirmDialog _confirm;
        private RectTransform _window;
        private TextMeshProUGUI _title;
        private TMP_InputField _name;
        private TMP_InputField _source;
        private CodeFieldDecorator _decorator;
        private TextMeshProUGUI _messages;
        private Button _saveButton;
        private Button _deleteButton;
        private string _editingId;
        private NodeId _bindTo;
        private int _wordWidth;
        private InstructionSetParseResult _result;
        private bool _suspendedBefore;

        /// <summary>Raised when the dialog closes, with the saved set's id (null when nothing was saved).</summary>
        public event Action<string> Closed;

        public bool IsOpen => gameObject.activeSelf;

        public TMP_InputField NameField => _name;

        public TMP_InputField SourceField => _source;

        public Button SaveButton => _saveButton;

        public Button DeleteButton => _deleteButton;

        public string MessagesText => _messages.text;

        public string TitleText => _title.text;

        public CodeFieldDecorator Decorator => _decorator;

        public static InstructionSetDialog Create(Transform parent, UiFactory ui, WorkspaceController workspace,
            ConfirmDialog confirm)
        {
            Image blocker = ui.Panel("Instruction Set Dialog", parent, ui.Theme.Scrim);
            UiFactory.Stretch(blocker.rectTransform);
            var dialog = blocker.gameObject.AddComponent<InstructionSetDialog>();
            dialog._ui = ui;
            dialog._workspace = workspace;
            dialog._confirm = confirm;
            dialog.Build(blocker.rectTransform);
            blocker.gameObject.SetActive(false);
            return dialog;
        }

        /// <summary>Sets the confirmation dialog used by Delete (created after this dialog so it draws on top).</summary>
        public void AttachConfirm(ConfirmDialog confirm)
        {
            _confirm = confirm;
        }

        /// <summary>
        /// Opens a new set starting from <paramref name="source"/> (the template when null). When saved it is bound
        /// to <paramref name="bindTo"/>'s program gate, if that has a program. <paramref name="wordWidth"/> is used for
        /// the reference preview.
        /// </summary>
        public void OpenNew(string name, string source, NodeId bindTo, int wordWidth)
        {
            _editingId = null;
            _bindTo = bindTo;
            Show("New instruction set", name ?? "My CPU", source ?? InstructionSet.Template, wordWidth);
        }

        /// <summary>Opens an existing user set. Returns false when there is no such set.</summary>
        public bool OpenEdit(string setId, int wordWidth)
        {
            if (!_workspace.Programs.TryGet(setId, out InstructionSetEntry entry))
            {
                return false;
            }

            _editingId = setId;
            _bindTo = NodeId.None;
            Show("Edit instruction set", entry.Name, entry.Source, wordWidth);
            return true;
        }

        /// <summary>Replaces the spec text (as typing would) and re-parses it.</summary>
        public void SetSource(string text)
        {
            _source.text = text ?? string.Empty;
        }

        /// <summary>Saves (adds or edits) the set and closes. Returns false (dialog stays open) when refused.</summary>
        public bool Save()
        {
            if (_result == null || _result.HasErrors)
            {
                return false;
            }

            string id;
            if (_editingId == null)
            {
                id = _workspace.AddInstructionSet(_name.text, _source.text, _bindTo);
                if (id == null)
                {
                    return false;
                }
            }
            else
            {
                if (!_workspace.EditInstructionSet(_editingId, _name.text, _source.text))
                {
                    return false;
                }

                id = _editingId;
            }

            CloseWith(id);
            return true;
        }

        /// <summary>Asks for confirmation, then deletes the set being edited.</summary>
        public void RequestDelete()
        {
            if (_editingId == null)
            {
                return;
            }

            string id = _editingId;
            if (_confirm == null)
            {
                if (_workspace.RemoveInstructionSet(id))
                {
                    CloseWith(null);
                }

                return;
            }

            _confirm.Show("Delete instruction set?",
                "Gates using '" + _name.text + "' go back to their default instruction set. Undo restores it.",
                "Delete", () =>
                {
                    if (_workspace.RemoveInstructionSet(id))
                    {
                        CloseWith(null);
                    }
                    else
                    {
                        _workspace.InputSuspended = true;
                    }
                }, onCancel: () => _workspace.InputSuspended = true);
        }

        public void Close()
        {
            CloseWith(null);
        }

        private void CloseWith(string savedId)
        {
            if (!IsOpen)
            {
                return;
            }

            gameObject.SetActive(false);
            _workspace.InputSuspended = _suspendedBefore;
            Closed?.Invoke(savedId);
        }

        private void Show(string title, string name, string source, int wordWidth)
        {
            _wordWidth = Mathf.Clamp(wordWidth, 1, Assembler.MaxWordWidth);
            _title.text = title;
            _name.SetTextWithoutNotify(name);
            _source.SetTextWithoutNotify(source);
            _deleteButton.gameObject.SetActive(_editingId != null);
            _decorator.Refresh();
            Reparse();

            _suspendedBefore = _workspace.InputSuspended;
            gameObject.SetActive(true);
            transform.SetAsLastSibling();
            Canvas.ForceUpdateCanvases();
            float available = ((RectTransform)transform).rect.height - 24f;
            _window.sizeDelta = new Vector2(_window.sizeDelta.x, Mathf.Clamp(available, 360f, PreferredHeight));
            _workspace.InputSuspended = true;
        }

        private void Update()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null || (_confirm != null && _confirm.IsOpen))
            {
                return;
            }

            if (keyboard.escapeKey.wasPressedThisFrame)
            {
                Close();
                return;
            }

            bool control = keyboard.leftCtrlKey.isPressed || keyboard.rightCtrlKey.isPressed;
            if (control && (keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame) &&
                _saveButton.interactable)
            {
                Save();
            }
        }

        private void Reparse()
        {
            _result = InstructionSet.Parse(_source.text, _name.text);
            _decorator.SetErrors(_result.Errors);
            _builder.Clear();
            if (_result.HasErrors)
            {
                string color = ColorUtility.ToHtmlStringRGB(_ui.Theme.Error);
                int shown = Mathf.Min(MaxErrorsShown, _result.Errors.Count);
                for (int i = 0; i < shown; i++)
                {
                    _builder.Append("<color=#").Append(color).Append('>').Append(_result.Errors[i]).Append("</color>\n");
                }

                if (_result.Errors.Count > shown)
                {
                    _builder.Append("… and ").Append(_result.Errors.Count - shown).Append(" more.");
                }
            }
            else
            {
                InstructionSet set = _result.Set;
                string problem = set.CheckWordWidth(_wordWidth);
                if (problem != null)
                {
                    _builder.Append("<color=#").Append(ColorUtility.ToHtmlStringRGB(_ui.Theme.Warning)).Append('>')
                        .Append(problem).Append("</color>\n");
                }

                _builder.Append(set.Instructions.Count).Append(" instruction")
                    .Append(set.Instructions.Count == 1 ? "" : "s").Append(", ").Append(set.OpcodeBits)
                    .Append("-bit opcode. ");
                if (problem == null)
                {
                    _builder.Append(set.Reference(_wordWidth));
                }
            }

            _messages.text = _builder.ToString();
            _saveButton.interactable = !_result.HasErrors && _workspace.IsEditable;
        }

        private void Build(RectTransform blocker)
        {
            Image window = _ui.Card("Window", blocker, _ui.Theme.DialogBackground);
            _window = window.rectTransform;
            _window.anchorMin = _window.anchorMax = new Vector2(0.5f, 0.5f);
            _window.pivot = new Vector2(0.5f, 0.5f);
            _window.sizeDelta = new Vector2(560f, PreferredHeight);

            RectTransform column = UiFactory.Column("Content", _window, 8f, new RectOffset(20, 20, 16, 16));
            UiFactory.Stretch(column);

            _title = _ui.Text("Title", column, "Instruction set", 18f, _ui.Theme.TextPrimary,
                TextAlignmentOptions.MidlineLeft, FontStyles.Bold);
            _title.gameObject.AddComponent<LayoutElement>().preferredHeight = 28f;

            _name = _ui.InputField("Name", column, "Name, e.g. My CPU");
            _name.characterLimit = 40;
            _name.onValueChanged.AddListener(_ => Reparse());

            TextMeshProUGUI help = _ui.Text("Help", column,
                "Start with 'opcode N' (bits for the opcode, at the top of each word). Then one line per instruction: " +
                "MNEMONIC opcode, plus an operand name such as n if it takes one. A ';' comment describes it.",
                12f, _ui.Theme.TextSecondary, TextAlignmentOptions.TopLeft);
            help.enableWordWrapping = true;
            help.gameObject.AddComponent<LayoutElement>().preferredHeight = 34f;

            _source = _ui.InputField("Spec", column, "opcode 4");
            LayoutElement sourceLayout = _source.GetComponent<LayoutElement>();
            sourceLayout.preferredHeight = 300f;
            sourceLayout.minHeight = 120f;
            sourceLayout.flexibleHeight = 1f;
            _source.lineType = TMP_InputField.LineType.MultiLineNewline;
            _source.richText = false;
            _source.restoreOriginalTextOnEscape = false;
            _source.resetOnDeActivation = false;
            _source.textComponent.alignment = TextAlignmentOptions.TopLeft;
            _source.textComponent.enableWordWrapping = false;
            _source.textComponent.fontSize = 14f;
            ((TextMeshProUGUI)_source.placeholder).alignment = TextAlignmentOptions.TopLeft;
            _source.onValueChanged.AddListener(_ => Reparse());
            _decorator = CodeFieldDecorator.Attach(_source, _ui, ProgramSyntax.TokenizeSpec);

            _messages = _ui.Text("Messages", column, string.Empty, 12f, _ui.Theme.TextSecondary,
                TextAlignmentOptions.TopLeft);
            _messages.enableWordWrapping = true;
            _messages.richText = true;
            _messages.overflowMode = TextOverflowModes.Ellipsis;
            _messages.gameObject.AddComponent<LayoutElement>().preferredHeight = 84f;

            RectTransform buttons = UiFactory.CreateRect("Buttons", column);
            UiFactory.Row(buttons, 8f, new RectOffset(0, 0, 0, 0), TextAnchor.MiddleRight);
            buttons.gameObject.AddComponent<LayoutElement>().preferredHeight = 34f;
            _deleteButton = _ui.Button("Delete", buttons, "Delete", RequestDelete, 90f);
            UiFactory.Flexible(buttons);
            _ui.Button("Close", buttons, "Close", Close, 100f);
            _saveButton = _ui.Button("Save", buttons, "Save", () => Save(), 120f);
            _ui.MakePrimary(_saveButton);
        }
    }
}
