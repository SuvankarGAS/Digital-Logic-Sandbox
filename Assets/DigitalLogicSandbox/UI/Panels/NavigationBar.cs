using System.Collections.Generic;
using DigitalLogicSandbox.CircuitEditor.Navigation;
using DigitalLogicSandbox.CircuitEditor.Workspace;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DigitalLogicSandbox.UI.Panels
{
    /// <summary>
    /// Floating breadcrumb pill under the toolbar ("Main Circuit › OR › NAND"). Each crumb navigates back to that
    /// level. Next to the crumbs: a mode badge (Editing / Inspecting · read-only) and the actions of the current
    /// level: Edit for inspected gates; Save and Discard while a draft exists. At the top level with nothing to
    /// navigate the pill hides itself, so the main circuit starts clean.
    /// </summary>
    public sealed class NavigationBar : MonoBehaviour
    {
        public const float Height = 40f;

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
            Image background = ui.Card("Navigation Bar", parent, ui.Theme.PanelBackground, radius: 12f);
            RectTransform rect = background.rectTransform;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = new Vector2(0f, -UiFactory.TopInset);
            rect.sizeDelta = new Vector2(0f, Height);
            UiFactory.Row(rect, 6f, new RectOffset(4, 6, 4, 4));
            rect.gameObject.AddComponent<ContentSizeFitter>().horizontalFit = ContentSizeFitter.FitMode.PreferredSize;

            var bar = background.gameObject.AddComponent<NavigationBar>();
            bar._ui = ui;
            bar._workspace = workspace;
            bar._up = ui.IconButton("Up", rect, Icons.Up, "Back to parent circuit", () => workspace.NavigateUp(), 32f, 15f);

            bar._crumbRow = UiFactory.CreateRect("Crumbs", rect);
            UiFactory.Row(bar._crumbRow, 0f, new RectOffset(0, 0, 0, 0));

            bar._mode = ui.Text("Mode", rect, string.Empty, 10.5f, ui.Theme.TextMuted, TextAlignmentOptions.Center,
                FontStyles.Bold);
            bar._mode.characterSpacing = 6f;
            bar._mode.gameObject.AddComponent<LayoutElement>();
            bar._edit = ui.Button("Edit", rect, "Edit", () => workspace.BeginEdit(), 64f, 13f);
            ui.MakePrimary(bar._edit);
            bar._discard = ui.Button("Discard", rect, "Discard", bar.OnDiscardClicked, 104f, 13f);
            bar._save = ui.Button("Save", rect, "Save version…", () => bar.SaveRequested?.Invoke(), 120f, 13f);
            ui.MakePrimary(bar._save);
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
            // Show the bar before measuring: text created under an inactive bar (it is hidden at the top level)
            // measures too narrow, which squeezed the first crumbs to "…" when opening a gate.
            gameObject.SetActive(levels.Count > 1 || _workspace.Navigator.Draft != null);
            for (int i = 0; i < levels.Count; i++)
            {
                if (i > 0)
                {
                    TextMeshProUGUI separator = _ui.Icon("Separator", _crumbRow, Icons.ChevronRight, 12f, _ui.Theme.TextMuted);
                    separator.gameObject.AddComponent<LayoutElement>().preferredWidth = 14f;
                }

                int index = i;
                bool current = i == levels.Count - 1;
                Button crumb = _ui.Button("Crumb " + i, _crumbRow, levels[i].Title, () => _workspace.NavigateTo(index),
                    -1f, 13f);
                // Crumbs are text links: no resting background, a soft tile on hover.
                ColorBlock colors = crumb.colors;
                Color hover = _ui.Theme.ButtonHover;
                colors.normalColor = new Color(hover.r, hover.g, hover.b, 0f);
                colors.selectedColor = colors.normalColor;
                colors.disabledColor = colors.normalColor;
                UiFactory.SetColorsInstant(crumb, colors);
                TextMeshProUGUI label = crumb.GetComponentInChildren<TextMeshProUGUI>();
                label.fontWeight = current ? FontWeight.SemiBold : FontWeight.Regular;
                Color color = levels[i].IsDraft ? _ui.Theme.Warning : current ? _ui.Theme.TextPrimary : _ui.Theme.TextSecondary;
                crumb.GetComponent<ButtonVisual>().SetLabelColor(color);
                crumb.interactable = !current;
                if (current)
                {
                    // The current crumb is not a link, but it should not look disabled either.
                    crumb.GetComponent<ButtonVisual>().enabled = false;
                    label.color = color;
                }

                // Measure after the style is set: the semibold current crumb is wider than the default measurement.
                LayoutElement layout = crumb.GetComponent<LayoutElement>();
                // Long gate names are capped (ellipsis) so the trail stays on one line; the full name is a tooltip.
                float natural = label.GetPreferredValues(levels[i].Title).x + 20f;
                layout.preferredWidth = Mathf.Clamp(natural, 40f, MaxCrumbWidth);
                if (natural > MaxCrumbWidth)
                {
                    _ui.AddTooltip(crumb.gameObject, levels[i].Title);
                }

                _crumbs.Add(crumb);
            }

            RefreshState();
        }

        /// <summary>Widest a single breadcrumb may grow before its name is truncated.</summary>
        private const float MaxCrumbWidth = 200f;

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
                    _mode.text = level.HasUnsavedChanges ? "EDITING · UNSAVED" : "EDITING";
                    _mode.color = _ui.Theme.Warning;
                    break;
                case NavigationLevelKind.Preview:
                    _mode.text = "PREVIEW · READ-ONLY";
                    _mode.color = _ui.Theme.TextMuted;
                    break;
                default:
                    _mode.text = "INSPECTING · READ-ONLY";
                    _mode.color = _ui.Theme.TextMuted;
                    break;
            }

            _mode.gameObject.SetActive(_mode.text.Length > 0);
            _mode.GetComponent<LayoutElement>().preferredWidth = _mode.GetPreferredValues(_mode.text).x + 16f;
            // Nothing to navigate at the top level: keep the workspace clean.
            gameObject.SetActive(_workspace.Navigator.Levels.Count > 1 || draft != null);

            _up.interactable = _workspace.Navigator.CurrentIndex > 0;
            _edit.gameObject.SetActive((level.Kind == NavigationLevelKind.Inspect || level.IsPreview) && draft == null);
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
                _discardLabel.text = "Discard edits?";
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
