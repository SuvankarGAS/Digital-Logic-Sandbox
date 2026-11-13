using System.Collections.Generic;
using DigitalLogicSandbox.CircuitEditor.Navigation;
using DigitalLogicSandbox.CircuitEditor.Workspace;
using DigitalLogicSandbox.UI.Inspector;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DigitalLogicSandbox.UI.Panels
{
    /// <summary>
    /// Breadcrumb bar above the workspace ("Main Circuit > OR > NAND"). Each crumb navigates back to that level.
    /// On the right, a mode badge (Editing / Inspecting / read-only) and the actions of the current level: Edit for
    /// inspected gates; Save and Discard while a draft exists.
    /// </summary>
    public sealed class NavigationBar : MonoBehaviour
    {
        public const float Height = 34f;

        private readonly List<Button> _crumbs = new List<Button>();

        private UiFactory _ui;
        private WorkspaceController _workspace;
        private RectTransform _crumbRow;
        private TextMeshProUGUI _mode;
        private Button _up;
        private Button _edit;
        private Button _save;
        private Button _discard;
        private TextMeshProUGUI _discardLabel;
        private bool _confirmDiscard;

        /// <summary>Raised by the Save button (the shell opens the version dialog).</summary>
        public event System.Action SaveRequested;

        public IReadOnlyList<Button> Crumbs => _crumbs;

        public string ModeText => _mode.text;

        public Button EditButton => _edit;

        public Button SaveButton => _save;

        public Button DiscardButton => _discard;

        public static NavigationBar Create(Transform parent, UiFactory ui, WorkspaceController workspace)
        {
            Image background = ui.Panel("Navigation Bar", parent, ui.Theme.PanelBackground);
            RectTransform rect = background.rectTransform;
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.offsetMin = new Vector2(UiFactory.LibraryWidth, -UiFactory.ToolbarHeight - Height);
            rect.offsetMax = new Vector2(-InspectorPanel.Width, -UiFactory.ToolbarHeight);
            UiFactory.Row(rect, 6f, new RectOffset(10, 10, 3, 3));

            var bar = background.gameObject.AddComponent<NavigationBar>();
            bar._ui = ui;
            bar._workspace = workspace;
            bar._up = ui.Button("Up", rect, "Up", () => workspace.NavigateUp(), 44f, 13f);

            bar._crumbRow = UiFactory.CreateRect("Crumbs", rect);
            UiFactory.Row(bar._crumbRow, 2f, new RectOffset(0, 0, 0, 0));
            bar._crumbRow.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;

            bar._mode = ui.Text("Mode", rect, string.Empty, 12.5f, ui.Theme.TextSecondary, TextAlignmentOptions.MidlineRight,
                FontStyles.Bold);
            bar._mode.gameObject.AddComponent<LayoutElement>().preferredWidth = 200f;
            bar._edit = ui.Button("Edit", rect, "Edit", () => workspace.BeginEdit(), 60f, 13f);
            bar._save = ui.Button("Save", rect, "Save Version...", () => bar.SaveRequested?.Invoke(), 120f, 13f);
            bar._discard = ui.Button("Discard", rect, "Discard", bar.OnDiscardClicked, 110f, 13f);
            bar._discardLabel = bar._discard.GetComponentInChildren<TextMeshProUGUI>();

            workspace.NavigationChanged += bar.Rebuild;
            workspace.StatusChanged += bar.RefreshState;
            bar.Rebuild();
            return bar;
        }

        /// <summary>Recreates the crumbs from the navigator's levels.</summary>
        public void Rebuild()
        {
            _confirmDiscard = false;
            UiFactory.DestroyChildren(_crumbRow);
            _crumbs.Clear();
            if (_workspace == null || !_workspace.IsInitialized)
            {
                return;
            }

            IReadOnlyList<NavigationLevel> levels = _workspace.Navigator.Levels;
            for (int i = 0; i < levels.Count; i++)
            {
                if (i > 0)
                {
                    TextMeshProUGUI separator = _ui.Text("Separator", _crumbRow, ">", 13f, _ui.Theme.TextSecondary,
                        TextAlignmentOptions.Center);
                    separator.gameObject.AddComponent<LayoutElement>().preferredWidth = 14f;
                }

                int index = i;
                bool current = i == levels.Count - 1;
                Button crumb = _ui.Button("Crumb " + i, _crumbRow, levels[i].Title, () => _workspace.NavigateTo(index),
                    -1f, 13f);
                TextMeshProUGUI label = crumb.GetComponentInChildren<TextMeshProUGUI>();
                label.fontStyle = current ? FontStyles.Bold : FontStyles.Normal;
                label.color = levels[i].IsDraft ? _ui.Theme.Warning : current ? _ui.Theme.TextPrimary : _ui.Theme.Accent;
                // Measure after the style is set: the bold current crumb is wider than the default measurement.
                LayoutElement layout = crumb.GetComponent<LayoutElement>();
                layout.preferredWidth = Mathf.Max(40f, label.GetPreferredValues(levels[i].Title).x + 24f);
                crumb.interactable = !current;
                _crumbs.Add(crumb);
            }

            RefreshState();
        }

        /// <summary>Updates the mode badge and button states (cheap; runs on every status change).</summary>
        public void RefreshState()
        {
            if (_workspace == null || !_workspace.IsInitialized || _workspace.CurrentLevel == null)
            {
                return;
            }

            NavigationLevel level = _workspace.CurrentLevel;
            NavigationLevel draft = _workspace.Navigator.Draft;
            switch (level.Kind)
            {
                case NavigationLevelKind.Root:
                    _mode.text = string.Empty;
                    break;
                case NavigationLevelKind.Draft:
                    _mode.text = level.HasUnsavedChanges ? "EDITING (unsaved)" : "EDITING";
                    _mode.color = _ui.Theme.Warning;
                    break;
                default:
                    _mode.text = "INSPECTING - READ-ONLY";
                    _mode.color = _ui.Theme.TextSecondary;
                    break;
            }

            _up.interactable = _workspace.Navigator.CurrentIndex > 0;
            _edit.gameObject.SetActive(level.Kind == NavigationLevelKind.Inspect && draft == null);
            _save.gameObject.SetActive(draft != null);
            _discard.gameObject.SetActive(draft != null);
            _save.interactable = draft != null && draft.HasUnsavedChanges;
            if (!_confirmDiscard)
            {
                _discardLabel.text = "Discard";
            }
        }

        private void OnDiscardClicked()
        {
            NavigationLevel draft = _workspace.Navigator.Draft;
            if (draft == null)
            {
                return;
            }

            // Unsaved edits cannot be undone once discarded, so ask for a second click.
            if (draft.HasUnsavedChanges && !_confirmDiscard)
            {
                _confirmDiscard = true;
                _discardLabel.text = "Really discard?";
                return;
            }

            _confirmDiscard = false;
            _workspace.DiscardDraft();
        }

        private void OnDestroy()
        {
            if (_workspace != null)
            {
                _workspace.NavigationChanged -= Rebuild;
                _workspace.StatusChanged -= RefreshState;
            }
        }
    }
}
