using System.Collections.Generic;
using System.Text;
using DigitalLogicSandbox.CircuitEditor.Workspace;
using DigitalLogicSandbox.Core.Circuit;
using DigitalLogicSandbox.Core.Nodes;
using DigitalLogicSandbox.GateLibrary.Programs;
using DigitalLogicSandbox.UI.ContextMenus;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace DigitalLogicSandbox.UI.Dialogs
{
    /// <summary>
    /// Modal program editor for a gate fed by program words (constants of one width, e.g. the CPU demo's ROM). Shows
    /// the program as assembly for the gate's instruction set (chosen here, see <see cref="InstructionSetLibrary"/>),
    /// with line numbers and syntax colouring. It re-assembles on every edit, shows line-numbered errors, and applies
    /// the whole program as one undoable step through
    /// <see cref="WorkspaceController.ApplyProgram(NodeId, IReadOnlyList{int})"/>. Read-only while inspecting.
    /// </summary>
    public sealed class ProgramEditorDialog : MonoBehaviour
    {
        private const int MaxErrorsShown = 5;
        private const float PreferredHeight = 680f;

        private readonly StringBuilder _builder = new StringBuilder(256);
        private readonly List<NodeId> _words = new List<NodeId>();
        private readonly List<MenuItem> _menuItems = new List<MenuItem>();

        private UiFactory _ui;
        private WorkspaceController _workspace;
        private ContextMenuView _menu;
        private InstructionSetDialog _setDialog;
        private TextMeshProUGUI _title;
        private TextMeshProUGUI _setLabel;
        private Button _changeSetButton;
        private Button _editSetButton;
        private TextMeshProUGUI _reference;
        private TMP_InputField _source;
        private CodeFieldDecorator _decorator;
        private TextMeshProUGUI _messages;
        private Button _applyButton;
        private RectTransform _window;
        private NodeId _node;
        private Assembler _assembler;
        private ResolvedInstructionSet _set;
        private AssemblyResult _result;
        private string _generatedListing;

        public bool IsOpen => gameObject.activeSelf;

        /// <summary>The gate whose program is being edited.</summary>
        public NodeId Node => _node;

        public TMP_InputField SourceField => _source;

        public Button ApplyButton => _applyButton;

        public Button ChangeSetButton => _changeSetButton;

        public Button EditSetButton => _editSetButton;

        public string MessagesText => _messages.text;

        public string TitleText => _title.text;

        public string ReferenceText => _reference.text;

        /// <summary>"Instruction set: …" line.</summary>
        public string InstructionSetText => _setLabel.text;

        /// <summary>The instruction set the program is assembled with.</summary>
        public ResolvedInstructionSet ResolvedSet => _set;

        public CodeFieldDecorator Decorator => _decorator;

        public InstructionSetDialog SetDialog => _setDialog;

        public static ProgramEditorDialog Create(Transform parent, UiFactory ui, WorkspaceController workspace)
        {
            Image blocker = ui.Panel("Program Editor Dialog", parent, ui.Theme.Scrim);
            UiFactory.Stretch(blocker.rectTransform);
            var dialog = blocker.gameObject.AddComponent<ProgramEditorDialog>();
            dialog._ui = ui;
            dialog._workspace = workspace;
            dialog.Build(blocker.rectTransform);
            blocker.gameObject.SetActive(false);
            return dialog;
        }

        /// <summary>Connects the instruction set picker (a popup menu) and the instruction set editor.</summary>
        public void Attach(ContextMenuView menu, InstructionSetDialog setDialog)
        {
            if (_setDialog != null)
            {
                _setDialog.Closed -= OnSetDialogClosed;
            }

            _menu = menu;
            _setDialog = setDialog;
            if (_setDialog != null)
            {
                _setDialog.Closed += OnSetDialogClosed;
            }
        }

        /// <summary>Opens the editor for <paramref name="node"/>. Returns false when it has no program words.</summary>
        public bool Open(NodeId node)
        {
            CircuitDefinition circuit = _workspace.Circuit;
            if (circuit == null || !_workspace.TryGetProgramWords(node, _words) ||
                !circuit.TryGetNode(node, out NodeDefinition target))
            {
                _workspace.PostNotification("This gate has no program words (constants wired to its inputs).");
                return false;
            }

            _node = node;
            bool editable = _workspace.IsEditable;
            _title.text = (editable ? "Edit program: " : "Program: ") + _workspace.Geometry.TitleOf(target);
            _source.readOnly = !editable;
            _applyButton.gameObject.SetActive(editable);
            _changeSetButton.gameObject.SetActive(editable);
            ResolveInstructionSet(regenerateListing: true);

            gameObject.SetActive(true);
            transform.SetAsLastSibling();
            FitToScreen();
            _workspace.InputSuspended = true;
            return true;
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

        /// <summary>Replaces the program text (as typing would) and re-assembles it.</summary>
        public void SetSource(string text)
        {
            _source.text = text ?? string.Empty; // raises onValueChanged → Reassemble
        }

        /// <summary>Applies the assembled program. Returns false (dialog stays open) when it has errors or is refused.</summary>
        public bool Apply()
        {
            if (_result == null || _result.HasErrors || !_workspace.IsEditable)
            {
                return false;
            }

            if (_workspace.ApplyProgram(_node, _result.Words) < 0)
            {
                return false;
            }

            Close();
            return true;
        }

        /// <summary>
        /// Uses <paramref name="setId"/> for this gate (a user set id, <see cref="InstructionSetLibrary.ExampleCpuId"/>
        /// or <see cref="InstructionSetLibrary.RawId"/>) as one undoable step. An untouched listing is shown again in
        /// the new set; edited text is kept and re-assembled.
        /// </summary>
        public bool ChooseInstructionSet(string setId)
        {
            if (!IsOpen || !_workspace.BindInstructionSet(_node, setId))
            {
                return false;
            }

            ResolveInstructionSet(regenerateListing: _source.text == _generatedListing);
            return true;
        }

        /// <summary>The picker's rows (also used by tests): built-in, user and raw sets, then "New…" actions.</summary>
        public IReadOnlyList<MenuItem> InstructionSetMenuItems()
        {
            _menuItems.Clear();
            AddSetItem(InstructionSetLibrary.ExampleCpuId, InstructionSetLibrary.ExampleCpuName);
            IReadOnlyList<InstructionSetEntry> sets = _workspace.Programs.Sets;
            for (int i = 0; i < sets.Count; i++)
            {
                AddSetItem(sets[i].Id, sets[i].Name);
            }

            AddSetItem(InstructionSetLibrary.RawId, InstructionSetLibrary.RawName);
            bool canCreate = _setDialog != null && _workspace.IsEditable;
            _menuItems.Add(new MenuItem("New instruction set…", () => OpenNewSet(null), canCreate));
            _menuItems.Add(new MenuItem("New from Example CPU…", () => OpenNewSet(DigitalLogicSandbox.GateLibrary.Programs.InstructionSet.ExampleCpuSource),
                canCreate));
            return _menuItems;
        }

        private void AddSetItem(string id, string name)
        {
            bool current = _set.Id == id;
            _menuItems.Add(new MenuItem(current ? name + " (current)" : name, () => ChooseInstructionSet(id), !current));
        }

        private void ShowInstructionSetMenu()
        {
            if (_menu == null)
            {
                return;
            }

            var rect = (RectTransform)_changeSetButton.transform;
            Vector3[] corners = new Vector3[4];
            rect.GetWorldCorners(corners);
            Canvas canvas = rect.GetComponentInParent<Canvas>();
            Camera eventCamera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay
                ? canvas.worldCamera
                : null;
            Vector2 screen = RectTransformUtility.WorldToScreenPoint(eventCamera, corners[0]);
            _menu.ShowItems(screen, InstructionSetMenuItems());
        }

        private void OpenNewSet(string source)
        {
            string name = source == null ? "My CPU" : "Example CPU copy";
            _setDialog?.OpenNew(name, source, _node, _assembler.WordWidth);
        }

        private void OpenEditSet()
        {
            if (_setDialog != null && _set.IsUserSet)
            {
                _setDialog.OpenEdit(_set.Id, _assembler.WordWidth);
            }
        }

        private void OnSetDialogClosed(string savedId)
        {
            if (!IsOpen)
            {
                return;
            }

            _workspace.InputSuspended = true;
            ResolveInstructionSet(regenerateListing: _source.text == _generatedListing);
        }

        /// <summary>Re-reads the gate's instruction set; optionally shows a fresh listing of the wired words.</summary>
        private void ResolveInstructionSet(bool regenerateListing)
        {
            CircuitDefinition circuit = _workspace.Circuit;
            _workspace.TryGetProgramInstructionSet(_node, out _set);
            _assembler = _workspace.ProgramAssembler(_node) ?? new Assembler(null, 8);
            _setLabel.text = "Instruction set: " + _set.Name + (_set.IsBound ? string.Empty : " (default)");
            _editSetButton.gameObject.SetActive(_set.IsUserSet && _workspace.IsEditable && _setDialog != null);
            _reference.text = _assembler.Reference;
            _decorator.SetTokenizer((text, tokens) => ProgramSyntax.TokenizeProgram(text, _assembler.Problem == null ? _assembler.Set : null, tokens));

            var values = new int[_words.Count];
            for (int i = 0; i < _words.Count; i++)
            {
                values[i] = circuit.GetNode(_words[i]).Component.ConstantValue;
            }

            _generatedListing = _assembler.Listing(values);
            if (regenerateListing)
            {
                _source.SetTextWithoutNotify(_generatedListing);
                _decorator.Refresh();
            }

            Reassemble(_source.text);
        }

        /// <summary>Keeps the window inside small screens (the program field shrinks first).</summary>
        private void FitToScreen()
        {
            Canvas.ForceUpdateCanvases();
            float available = ((RectTransform)transform).rect.height - 24f;
            _window.sizeDelta = new Vector2(_window.sizeDelta.x, Mathf.Clamp(available, 400f, PreferredHeight));
        }

        private void Update()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null || (_menu != null && _menu.gameObject.activeSelf) ||
                (_setDialog != null && _setDialog.IsOpen))
            {
                return;
            }

            if (keyboard.escapeKey.wasPressedThisFrame)
            {
                Close();
                return;
            }

            // Enter inserts a new line in the program; Ctrl+Enter applies.
            bool control = keyboard.leftCtrlKey.isPressed || keyboard.rightCtrlKey.isPressed;
            if (control && (keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame) &&
                _applyButton.gameObject.activeSelf && _applyButton.interactable)
            {
                Apply();
            }
        }

        private void Reassemble(string text)
        {
            if (_assembler == null)
            {
                return;
            }

            _result = _assembler.Parse(text, _words.Count);
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
                int used = _words.Count - _result.PaddedWords;
                _builder.Append(used).Append(" of ").Append(_words.Count).Append(' ').Append(_assembler.WordWidth)
                    .Append("-bit words used");
                if (_result.PaddedWords > 0)
                {
                    _builder.Append("; the rest are ").Append(_assembler.Disassemble(0));
                }

                _builder.Append('.');
                if (_workspace.IsEditable)
                {
                    _builder.Append(" Ctrl+Enter applies; Undo restores the previous program.");
                }
            }

            _messages.text = _builder.ToString();
            _applyButton.interactable = !_result.HasErrors;
        }

        private void Build(RectTransform blocker)
        {
            Image window = _ui.Card("Window", blocker, _ui.Theme.DialogBackground);
            RectTransform rect = window.rectTransform;
            _window = rect;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(580f, PreferredHeight);

            RectTransform column = UiFactory.Column("Content", rect, 8f, new RectOffset(20, 20, 16, 16));
            UiFactory.Stretch(column);

            _title = _ui.Text("Title", column, "Edit program", 18f, _ui.Theme.TextPrimary,
                TextAlignmentOptions.MidlineLeft, FontStyles.Bold);
            _title.gameObject.AddComponent<LayoutElement>().preferredHeight = 28f;

            RectTransform setRow = UiFactory.CreateRect("Instruction Set", column);
            UiFactory.Row(setRow, 8f, new RectOffset(0, 0, 0, 0), TextAnchor.MiddleLeft);
            setRow.gameObject.AddComponent<LayoutElement>().preferredHeight = 30f;
            _setLabel = _ui.Text("Label", setRow, "Instruction set:", 13f, _ui.Theme.TextPrimary);
            _setLabel.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
            _changeSetButton = _ui.Button("Change", setRow, "Change…", ShowInstructionSetMenu, 96f, 12.5f);
            _editSetButton = _ui.Button("Edit Set", setRow, "Edit set…", OpenEditSet, 96f, 12.5f);
            _ui.AddTooltip(_changeSetButton.gameObject, "Choose the instruction set this gate's program uses");
            _ui.AddTooltip(_editSetButton.gameObject, "Edit this instruction set's mnemonics and opcodes");

            _reference = _ui.Text("Reference", column, string.Empty, 12f, _ui.Theme.TextSecondary,
                TextAlignmentOptions.TopLeft);
            _reference.enableWordWrapping = true;
            _reference.overflowMode = TextOverflowModes.Ellipsis;
            _reference.gameObject.AddComponent<LayoutElement>().preferredHeight = 52f;

            _source = _ui.InputField("Source", column, "One instruction per line, e.g. LDI 5");
            LayoutElement sourceLayout = _source.GetComponent<LayoutElement>();
            sourceLayout.preferredHeight = 360f;
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
            _source.onValueChanged.AddListener(Reassemble);
            _decorator = CodeFieldDecorator.Attach(_source, _ui,
                (text, tokens) => ProgramSyntax.TokenizeProgram(text, null, tokens));

            _messages = _ui.Text("Messages", column, string.Empty, 12f, _ui.Theme.TextSecondary,
                TextAlignmentOptions.TopLeft);
            _messages.enableWordWrapping = true;
            _messages.richText = true;
            _messages.overflowMode = TextOverflowModes.Ellipsis;
            _messages.gameObject.AddComponent<LayoutElement>().preferredHeight = 84f;

            RectTransform buttons = UiFactory.CreateRect("Buttons", column);
            UiFactory.Row(buttons, 8f, new RectOffset(0, 0, 0, 0), TextAnchor.MiddleRight);
            buttons.gameObject.AddComponent<LayoutElement>().preferredHeight = 34f;
            UiFactory.Flexible(buttons);
            _ui.Button("Close", buttons, "Close", Close, 100f);
            _applyButton = _ui.Button("Apply", buttons, "Apply program", () => Apply(), 140f);
            _ui.MakePrimary(_applyButton);
        }
    }
}
