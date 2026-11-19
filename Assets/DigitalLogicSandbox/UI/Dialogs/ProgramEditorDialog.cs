using System.Collections.Generic;
using System.Text;
using DigitalLogicSandbox.CircuitEditor.Workspace;
using DigitalLogicSandbox.Core.Circuit;
using DigitalLogicSandbox.Core.Nodes;
using DigitalLogicSandbox.GateLibrary.Examples;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace DigitalLogicSandbox.UI.Dialogs
{
    /// <summary>
    /// Modal program editor for a gate fed by program words (8-bit constants, e.g. the CPU demo's ROM). Shows the
    /// program as assembly, re-assembles on every edit with line-numbered errors, and applies the whole program as
    /// one undoable step through <see cref="WorkspaceController.ApplyProgram"/>. Read-only while inspecting.
    /// </summary>
    public sealed class ProgramEditorDialog : MonoBehaviour
    {
        private const int MaxErrorsShown = 5;
        private const float PreferredHeight = 640f;

        private readonly StringBuilder _builder = new StringBuilder(256);
        private readonly List<NodeId> _words = new List<NodeId>();

        private UiFactory _ui;
        private WorkspaceController _workspace;
        private TextMeshProUGUI _title;
        private TMP_InputField _source;
        private TextMeshProUGUI _messages;
        private Button _applyButton;
        private RectTransform _window;
        private NodeId _node;
        private AssemblyResult _result;

        public bool IsOpen => gameObject.activeSelf;

        /// <summary>The gate whose program is being edited.</summary>
        public NodeId Node => _node;

        public TMP_InputField SourceField => _source;

        public Button ApplyButton => _applyButton;

        public string MessagesText => _messages.text;

        public string TitleText => _title.text;

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

        /// <summary>Opens the editor for <paramref name="node"/>. Returns false when it has no program words.</summary>
        public bool Open(NodeId node)
        {
            CircuitDefinition circuit = _workspace.Circuit;
            if (circuit == null || !_workspace.TryGetProgramWords(node, _words) ||
                !circuit.TryGetNode(node, out NodeDefinition target))
            {
                _workspace.PostNotification("This gate has no program words (8-bit constants wired to its inputs).");
                return false;
            }

            _node = node;
            var values = new int[_words.Count];
            for (int i = 0; i < _words.Count; i++)
            {
                values[i] = circuit.GetNode(_words[i]).Component.ConstantValue;
            }

            bool editable = _workspace.IsEditable;
            _title.text = (editable ? "Edit program: " : "Program: ") + _workspace.Geometry.TitleOf(target);
            _source.SetTextWithoutNotify(CpuAssembler.Listing(values));
            _source.readOnly = !editable;
            _applyButton.gameObject.SetActive(editable);
            Reassemble(_source.text);

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

        /// <summary>Keeps the window inside small screens (the program field shrinks first).</summary>
        private void FitToScreen()
        {
            Canvas.ForceUpdateCanvases();
            float available = ((RectTransform)transform).rect.height - 24f;
            _window.sizeDelta = new Vector2(_window.sizeDelta.x, Mathf.Clamp(available, 380f, PreferredHeight));
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
            _result = CpuAssembler.Parse(text, _words.Count);
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
                _builder.Append(used).Append(" of ").Append(_words.Count).Append(" words used");
                if (_result.PaddedWords > 0)
                {
                    _builder.Append("; the rest are NOP");
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
            rect.sizeDelta = new Vector2(560f, PreferredHeight);

            RectTransform column = UiFactory.Column("Content", rect, 8f, new RectOffset(20, 20, 16, 16));
            UiFactory.Stretch(column);

            _title = _ui.Text("Title", column, "Edit program", 18f, _ui.Theme.TextPrimary,
                TextAlignmentOptions.MidlineLeft, FontStyles.Bold);
            _title.gameObject.AddComponent<LayoutElement>().preferredHeight = 28f;

            TextMeshProUGUI reference = _ui.Text("Reference", column, CpuAssembler.Reference, 12f,
                _ui.Theme.TextSecondary, TextAlignmentOptions.TopLeft);
            reference.enableWordWrapping = true;
            reference.overflowMode = TextOverflowModes.Overflow;
            reference.gameObject.AddComponent<LayoutElement>().preferredHeight = 52f;

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
