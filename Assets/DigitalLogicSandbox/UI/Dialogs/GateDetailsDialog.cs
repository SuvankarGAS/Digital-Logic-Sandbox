using System.Collections.Generic;
using DigitalLogicSandbox.CircuitEditor.Workspace;
using DigitalLogicSandbox.Core.Circuit;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace DigitalLogicSandbox.UI.Dialogs
{
    /// <summary>
    /// Edits a published custom gate's presentation details (name, icon text, category, description) from the gate
    /// library. Changes apply to every version and are one undoable step; behaviour and references are untouched.
    /// Existing categories are offered as chips so gates are easy to group consistently.
    /// </summary>
    public sealed class GateDetailsDialog : MonoBehaviour
    {
        public const int MaxNameLength = 32;
        public const int MaxIconLength = 4;
        private const int MaxChips = 6;

        private readonly List<Button> _chips = new List<Button>();

        private UiFactory _ui;
        private WorkspaceController _workspace;
        private TextMeshProUGUI _title;
        private TMP_InputField _name;
        private TMP_InputField _icon;
        private TMP_InputField _category;
        private TMP_InputField _description;
        private RectTransform _chipRow;
        private TextMeshProUGUI _message;
        private Button _save;
        private DefinitionId _id;

        public bool IsOpen => gameObject.activeSelf;

        public TMP_InputField NameField => _name;

        public TMP_InputField IconField => _icon;

        public TMP_InputField CategoryField => _category;

        public TMP_InputField DescriptionField => _description;

        public static GateDetailsDialog Create(Transform parent, UiFactory ui, WorkspaceController workspace)
        {
            Image blocker = ui.Panel("Gate Details Dialog", parent, ui.Theme.Scrim);
            UiFactory.Stretch(blocker.rectTransform);
            var dialog = blocker.gameObject.AddComponent<GateDetailsDialog>();
            dialog._ui = ui;
            dialog._workspace = workspace;
            dialog.Build(blocker.rectTransform);
            blocker.gameObject.SetActive(false);
            return dialog;
        }

        /// <summary>
        /// Opens the dialog for <paramref name="id"/>. <paramref name="categories"/> are offered as chips;
        /// <paramref name="focusName"/> selects the name for a quick rename.
        /// </summary>
        public void Open(DefinitionId id, IReadOnlyList<string> categories, bool focusName = true)
        {
            if (!_workspace.Library.TryGetLatest(id, out CircuitDefinition definition))
            {
                return;
            }

            _id = id;
            _title.text = focusName ? "Rename Gate" : "Gate Details";
            _name.SetTextWithoutNotify(definition.Name ?? string.Empty);
            _icon.SetTextWithoutNotify(definition.Icon ?? string.Empty);
            _category.SetTextWithoutNotify(definition.Category ?? string.Empty);
            _description.SetTextWithoutNotify(definition.Description ?? string.Empty);
            BuildChips(categories);
            Validate();
            gameObject.SetActive(true);
            transform.SetAsLastSibling();
            _workspace.InputSuspended = true;
            TMP_InputField focus = focusName ? _name : _description;
            focus.Select();
            focus.ActivateInputField();
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

        /// <summary>Applies the edits (when valid) and closes. Returns false when the name is not usable.</summary>
        public bool Confirm()
        {
            if (!Validate())
            {
                return false;
            }

            _workspace.SetCustomGateDetails(_id, _name.text.Trim(), _description.text.Trim(), _category.text.Trim(),
                _icon.text.Trim());
            Close();
            return true;
        }

        private bool Validate()
        {
            string name = _name.text.Trim();
            string error = null;
            if (name.Length == 0)
            {
                error = "Give the gate a name.";
            }
            else if (_workspace.Library.TryGetLatest(_id, out CircuitDefinition current) &&
                     !string.Equals(current.Name, name, System.StringComparison.OrdinalIgnoreCase) &&
                     _workspace.Library.ContainsName(name))
            {
                error = "Another gate is already called '" + name + "'.";
            }

            _message.text = error ?? "Changes apply to every version. Circuits using this gate keep working.";
            _message.color = error != null ? _ui.Theme.Error : _ui.Theme.TextMuted;
            _save.interactable = error == null;
            return error == null;
        }

        private void BuildChips(IReadOnlyList<string> categories)
        {
            for (int i = 0; i < _chips.Count; i++)
            {
                _chips[i].gameObject.SetActive(false);
                Destroy(_chips[i].gameObject);
            }

            _chips.Clear();
            int count = 0;
            for (int i = 0; categories != null && i < categories.Count && count < MaxChips; i++)
            {
                string category = categories[i];
                if (string.IsNullOrWhiteSpace(category))
                {
                    continue;
                }

                Button chip = _ui.Button("Chip " + category, _chipRow, category, () =>
                {
                    _category.text = category == Panels.LibraryPanel.UncategorizedLabel ? string.Empty : category;
                }, -1f, 11.5f);
                var layout = chip.GetComponent<LayoutElement>();
                layout.preferredHeight = layout.minHeight = 24f;
                layout.preferredWidth = Mathf.Min(layout.preferredWidth, 150f);
                _chips.Add(chip);
                count++;
            }

            _chipRow.gameObject.SetActive(_chips.Count > 0);
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
                     _save.interactable)
            {
                Confirm();
            }
        }

        private void Build(RectTransform blocker)
        {
            Image window = _ui.Card("Window", blocker, _ui.Theme.DialogBackground);
            RectTransform rect = window.rectTransform;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(480f, 330f);

            RectTransform column = UiFactory.Column("Content", rect, 8f, new RectOffset(20, 20, 16, 16));
            UiFactory.Stretch(column);
            _title = _ui.Text("Title", column, "Gate Details", 18f, _ui.Theme.TextPrimary, TextAlignmentOptions.MidlineLeft,
                FontStyles.Bold);
            _title.gameObject.AddComponent<LayoutElement>().preferredHeight = 28f;

            _name = _ui.FieldRow(column, "Name", string.Empty, null);
            _name.characterLimit = MaxNameLength;
            _name.onValueChanged.AddListener(_ => Validate());
            _icon = _ui.FieldRow(column, "Icon text", string.Empty, null);
            _icon.characterLimit = MaxIconLength;
            _category = _ui.FieldRow(column, "Category", string.Empty, null);

            _chipRow = UiFactory.CreateRect("Categories", column);
            UiFactory.Row(_chipRow, 6f, new RectOffset(98, 0, 0, 0));
            _chipRow.gameObject.AddComponent<LayoutElement>().preferredHeight = 24f;

            _description = _ui.FieldRow(column, "Description", string.Empty, null);

            _message = _ui.Text("Message", column, string.Empty, 12f, _ui.Theme.TextMuted, TextAlignmentOptions.TopLeft);
            _message.enableWordWrapping = true;
            var messageLayout = _message.gameObject.AddComponent<LayoutElement>();
            messageLayout.preferredHeight = 34f;
            messageLayout.flexibleHeight = 1f;

            RectTransform buttons = UiFactory.CreateRect("Buttons", column);
            UiFactory.Row(buttons, 8f, new RectOffset(0, 0, 0, 0), TextAnchor.MiddleRight);
            buttons.gameObject.AddComponent<LayoutElement>().preferredHeight = 34f;
            UiFactory.Flexible(buttons);
            _ui.Button("Cancel", buttons, "Cancel", Close, 100f);
            _save = _ui.Button("Save", buttons, "Save", () => Confirm(), 110f);
            _ui.MakePrimary(_save);
        }
    }
}
