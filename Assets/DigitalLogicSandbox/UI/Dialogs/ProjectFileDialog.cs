using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using DigitalLogicSandbox.CircuitEditor.Workspace;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace DigitalLogicSandbox.UI.Dialogs
{
    public enum ProjectFileDialogMode : byte
    {
        Open = 0,
        SaveAs = 1,
    }

    /// <summary>
    /// In-app project picker for Open and Save As (the Unity runtime has no native file dialogs). It lists the
    /// projects in the default folder (newest first). The name field accepts a bare name, which goes into the
    /// default folder, or a full path. Saving over an existing file asks for a second click.
    /// </summary>
    public sealed class ProjectFileDialog : MonoBehaviour
    {
        private readonly List<Button> _items = new List<Button>();

        private UiFactory _ui;
        private WorkspaceController _workspace;
        private IProjectService _projects;
        private TextMeshProUGUI _title;
        private TextMeshProUGUI _folder;
        private RectTransform _list;
        private TMP_InputField _nameField;
        private TextMeshProUGUI _message;
        private Button _confirm;
        private TextMeshProUGUI _confirmLabel;
        private string _pendingOverwrite;
        private Action _afterSuccess;

        public bool IsOpen => gameObject.activeSelf;

        public ProjectFileDialogMode Mode { get; private set; }

        public TMP_InputField NameField => _nameField;

        public Button ConfirmButton => _confirm;

        public string MessageText => _message.text;

        /// <summary>Displayed file names, in list order.</summary>
        public IReadOnlyList<Button> Items => _items;

        public static ProjectFileDialog Create(Transform parent, UiFactory ui, WorkspaceController workspace,
            IProjectService projects)
        {
            Image blocker = ui.Panel("Project File Dialog", parent, ui.Theme.Scrim);
            UiFactory.Stretch(blocker.rectTransform);
            var dialog = blocker.gameObject.AddComponent<ProjectFileDialog>();
            dialog._ui = ui;
            dialog._workspace = workspace;
            dialog._projects = projects;
            dialog.Build(blocker.rectTransform);
            blocker.gameObject.SetActive(false);
            return dialog;
        }

        /// <summary>Shows the dialog. <paramref name="afterSuccess"/> runs after a successful open or save.</summary>
        public void Show(ProjectFileDialogMode mode, Action afterSuccess = null)
        {
            Mode = mode;
            _afterSuccess = afterSuccess;
            _pendingOverwrite = null;
            bool save = mode == ProjectFileDialogMode.SaveAs;
            _title.text = save ? "Save project as" : "Open project";
            _confirmLabel.text = save ? "Save" : "Open";
            _folder.text = "Folder: " + _projects.ProjectsDirectory;
            _nameField.SetTextWithoutNotify(save ? _projects.DisplayName : string.Empty);
            _message.text = string.Empty;
            gameObject.SetActive(true); // before populating: TMP cannot measure text on inactive objects
            transform.SetAsLastSibling();
            PopulateList();
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

        /// <summary>Opens or saves using the name field. Returns true on success (the dialog closes).</summary>
        public bool Confirm()
        {
            string text = _nameField.text.Trim();
            if (text.Length == 0)
            {
                ShowError("Enter a project name.");
                return false;
            }

            string path;
            try
            {
                path = _projects.ResolvePath(text);
            }
            catch (ArgumentException exception)
            {
                ShowError(exception.Message);
                return false;
            }

            string message;
            bool succeeded;
            if (Mode == ProjectFileDialogMode.Open)
            {
                succeeded = _projects.Open(path, out message);
            }
            else
            {
                bool overwriting = File.Exists(path) && !string.Equals(path, _projects.CurrentPath, StringComparison.OrdinalIgnoreCase);
                if (overwriting && !string.Equals(_pendingOverwrite, path, StringComparison.OrdinalIgnoreCase))
                {
                    _pendingOverwrite = path;
                    ShowWarning("'" + Path.GetFileName(path) + "' already exists. Click Save again to replace it.");
                    return false;
                }

                succeeded = _projects.SaveAs(path, out message);
            }

            if (!succeeded)
            {
                ShowError(message);
                return false;
            }

            Close();
            if (!string.IsNullOrEmpty(message))
            {
                _workspace.PostNotification(message);
            }

            Action after = _afterSuccess;
            _afterSuccess = null;
            after?.Invoke();
            return true;
        }

        private void PopulateList()
        {
            UiFactory.DestroyChildren(_list);
            _items.Clear();
            IReadOnlyList<string> files = _projects.ListProjects();
            if (files.Count == 0)
            {
                TextMeshProUGUI empty = _ui.Text("Empty", _list, "No saved projects in this folder yet.", 12.5f,
                    _ui.Theme.TextSecondary);
                empty.gameObject.AddComponent<LayoutElement>().preferredHeight = 24f;
                return;
            }

            for (int i = 0; i < files.Count; i++)
            {
                string file = files[i];
                string name = Path.GetFileNameWithoutExtension(file);
                string stamp = File.GetLastWriteTime(file).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
                Button item = _ui.Button("Item " + name, _list, name + "    " + stamp, () => Select(name, file), -1f, 13f);
                TextMeshProUGUI label = item.GetComponentInChildren<TextMeshProUGUI>();
                label.alignment = TextAlignmentOptions.MidlineLeft;
                item.GetComponent<LayoutElement>().preferredWidth = -1f;
                _items.Add(item);
            }
        }

        private void Select(string name, string path)
        {
            bool sameAsField = string.Equals(_nameField.text.Trim(), name, StringComparison.Ordinal);
            _nameField.SetTextWithoutNotify(name);
            _message.text = string.Empty;
            if (sameAsField && Mode == ProjectFileDialogMode.Open)
            {
                Confirm(); // second click on the same entry opens it
            }
        }

        private void ShowError(string message)
        {
            _message.text = "<color=#" + ColorUtility.ToHtmlStringRGB(_ui.Theme.Error) + ">" + message + "</color>";
        }

        private void ShowWarning(string message)
        {
            _message.text = "<color=#" + ColorUtility.ToHtmlStringRGB(_ui.Theme.Warning) + ">" + message + "</color>";
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
            else if (keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame)
            {
                Confirm();
            }
        }

        private void Build(RectTransform blocker)
        {
            Image window = _ui.Card("Window", blocker, _ui.Theme.PanelBackground);
            RectTransform rect = window.rectTransform;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(560f, 480f);

            RectTransform column = UiFactory.Column("Content", rect, 8f, new RectOffset(20, 20, 16, 16));
            UiFactory.Stretch(column);
            _title = _ui.Text("Title", column, "Open project", 18f, _ui.Theme.TextPrimary,
                TextAlignmentOptions.MidlineLeft, FontStyles.Bold);
            _title.gameObject.AddComponent<LayoutElement>().preferredHeight = 28f;
            _folder = _ui.Text("Folder", column, string.Empty, 11.5f, _ui.Theme.TextSecondary);
            _folder.overflowMode = TextOverflowModes.Ellipsis;
            _folder.gameObject.AddComponent<LayoutElement>().preferredHeight = 18f;

            Image frame = _ui.Panel("List Frame", column, _ui.Theme.WorkspaceBackground, rounded: true, radius: 10f);
            var frameLayout = frame.gameObject.AddComponent<LayoutElement>();
            frameLayout.flexibleHeight = 1f;
            frameLayout.minHeight = 160f;
            _list = _ui.ScrollView("Projects", frame.rectTransform, out ScrollRect _);
            UiFactory.Stretch((RectTransform)_list.parent.parent);

            _nameField = _ui.FieldRow(column, "Name", string.Empty, null, 60f);
            _message = _ui.Text("Message", column, string.Empty, 12f, _ui.Theme.TextSecondary, TextAlignmentOptions.TopLeft);
            _message.enableWordWrapping = true;
            _message.richText = true;
            _message.gameObject.AddComponent<LayoutElement>().preferredHeight = 36f;

            RectTransform buttons = UiFactory.CreateRect("Buttons", column);
            UiFactory.Row(buttons, 8f, new RectOffset(0, 0, 0, 0), TextAnchor.MiddleRight);
            buttons.gameObject.AddComponent<LayoutElement>().preferredHeight = 34f;
            UiFactory.Flexible(buttons);
            _ui.Button("Cancel", buttons, "Cancel", Close, 100f);
            _confirm = _ui.Button("Confirm", buttons, "Open", () => Confirm(), 110f);
            _ui.MakePrimary(_confirm);
            _confirmLabel = _confirm.GetComponentInChildren<TextMeshProUGUI>();
        }
    }
}
