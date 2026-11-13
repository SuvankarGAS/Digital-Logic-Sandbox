using System;
using System.Collections.Generic;
using DigitalLogicSandbox.CircuitEditor.Placement;
using DigitalLogicSandbox.CircuitEditor.Workspace;
using DigitalLogicSandbox.Components.Abstractions;
using DigitalLogicSandbox.Core.Circuit;
using DigitalLogicSandbox.Core.Nodes;
using DigitalLogicSandbox.Core.Ports;
using DigitalLogicSandbox.GateLibrary.Custom;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DigitalLogicSandbox.UI.Panels
{
    /// <summary>
    /// Left panel: built-in gates (listed from the primitive registry, never hardcoded), circuit I/O boundaries,
    /// and the user's custom gates grouped by category. It refreshes automatically when a definition is published
    /// and supports name search.
    /// </summary>
    public sealed class LibraryPanel : MonoBehaviour
    {
        public const string UncategorizedLabel = "General";

        private readonly List<LibraryItemView> _items = new List<LibraryItemView>();
        private readonly List<LibraryItemView> _customItems = new List<LibraryItemView>();
        private readonly List<GameObject> _customHeaders = new List<GameObject>();
        private readonly List<CircuitDefinition> _scratchDefinitions = new List<CircuitDefinition>();

        private UiFactory _ui;
        private WorkspaceController _workspace;
        private ComponentCatalog _catalog;
        private DefinitionLibrary _library;
        private RectTransform _content;
        private RectTransform _customRoot;
        private TextMeshProUGUI _customEmpty;
        private TMP_InputField _search;
        private string _filter = string.Empty;

        public IReadOnlyList<LibraryItemView> Items => _items;

        public TMP_InputField SearchField => _search;

        public static LibraryPanel Create(Transform parent, UiFactory ui, WorkspaceController workspace,
            ComponentCatalog catalog, DefinitionLibrary library)
        {
            Image background = ui.Panel("Gate Library", parent, ui.Theme.PanelBackground);
            RectTransform rect = background.rectTransform;
            rect.anchorMin = new Vector2(0f, 0f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 0.5f);
            rect.offsetMin = new Vector2(0f, UiFactory.StatusBarHeight);
            rect.offsetMax = new Vector2(UiFactory.LibraryWidth, -UiFactory.ToolbarHeight);

            Image border = ui.Panel("Border", rect, ui.Theme.PanelBorder);
            border.raycastTarget = false;
            border.rectTransform.anchorMin = new Vector2(1f, 0f);
            border.rectTransform.anchorMax = new Vector2(1f, 1f);
            border.rectTransform.sizeDelta = new Vector2(1f, 0f);
            border.rectTransform.anchoredPosition = Vector2.zero;

            var panel = background.gameObject.AddComponent<LibraryPanel>();
            panel._ui = ui;
            panel._workspace = workspace;
            panel._catalog = catalog;
            panel._library = library;
            panel.Build(rect);
            library.DefinitionAdded += panel.OnDefinitionAdded;
            library.DefinitionRemoved += panel.OnDefinitionAdded;
            return panel;
        }

        /// <summary>Applies a search filter (case-insensitive substring on the gate name).</summary>
        public void SetFilter(string filter)
        {
            _filter = filter?.Trim() ?? string.Empty;
            for (int i = 0; i < _items.Count; i++)
            {
                _items[i].gameObject.SetActive(_items[i].Matches(_filter));
            }

            UpdateCustomEmptyState();
        }

        /// <summary>Rebuilds the custom gate list from the library (latest version of each definition).</summary>
        public void RefreshCustomGates()
        {
            for (int i = 0; i < _customItems.Count; i++)
            {
                _items.Remove(_customItems[i]);
                Destroy(_customItems[i].gameObject);
            }

            for (int i = 0; i < _customHeaders.Count; i++)
            {
                Destroy(_customHeaders[i]);
            }

            _customItems.Clear();
            _customHeaders.Clear();

            _scratchDefinitions.Clear();
            _library.GetLatestDefinitions(_scratchDefinitions);
            _scratchDefinitions.Sort(CompareByCategoryThenName);

            string currentCategory = null;
            bool multipleCategories = HasMultipleCategories(_scratchDefinitions);
            for (int i = 0; i < _scratchDefinitions.Count; i++)
            {
                CircuitDefinition definition = _scratchDefinitions[i];
                string category = CategoryOf(definition);
                if (multipleCategories && !string.Equals(category, currentCategory, StringComparison.Ordinal))
                {
                    currentCategory = category;
                    TextMeshProUGUI header = _ui.Text("Category " + category, _customRoot, category.ToUpperInvariant(),
                        11f, _ui.Theme.TextSecondary);
                    header.gameObject.AddComponent<LayoutElement>().preferredHeight = 20f;
                    _customHeaders.Add(header.gameObject);
                }

                ComponentSignature signature = definition.GetSignature();
                LibraryItemView item = LibraryItemView.Create(_customRoot, _ui, _workspace,
                    new PlacementRequest(ComponentRef.Custom(definition.Reference), definition.Name),
                    definition.Name, Describe(signature) + (definition.Version > 1 ? "  -  v" + definition.Version : string.Empty));
                _customItems.Add(item);
                _items.Add(item);
            }

            SetFilter(_filter);
        }

        private void Build(RectTransform rect)
        {
            RectTransform header = UiFactory.CreateRect("Header", rect);
            header.anchorMin = new Vector2(0f, 1f);
            header.anchorMax = new Vector2(1f, 1f);
            header.pivot = new Vector2(0.5f, 1f);
            header.sizeDelta = new Vector2(0f, 76f);
            header.anchoredPosition = Vector2.zero;
            var headerLayout = header.gameObject.AddComponent<VerticalLayoutGroup>();
            headerLayout.padding = new RectOffset(12, 12, 10, 6);
            headerLayout.spacing = 6f;
            headerLayout.childControlWidth = true;
            headerLayout.childControlHeight = true;
            headerLayout.childForceExpandHeight = false;

            TextMeshProUGUI title = _ui.Text("Title", header, "Gate Library", 15f, _ui.Theme.TextPrimary,
                TextAlignmentOptions.MidlineLeft, FontStyles.Bold);
            title.gameObject.AddComponent<LayoutElement>().preferredHeight = 20f;
            _search = _ui.InputField("Search", header, "Search gates...");
            _search.onValueChanged.AddListener(SetFilter);

            RectTransform scroll = UiFactory.CreateRect("Scroll", rect);
            UiFactory.Stretch(scroll, 0f, 1f, 76f, 0f);
            _content = _ui.ScrollView("List", scroll, out _);
            UiFactory.Stretch((RectTransform)_content.parent.parent);

            Section("Built-in Gates");
            IReadOnlyList<IPrimitiveComponent> primitives = _catalog.Primitives.All;
            for (int i = 0; i < primitives.Count; i++)
            {
                IPrimitiveComponent primitive = primitives[i];
                _items.Add(LibraryItemView.Create(_content, _ui, _workspace,
                    new PlacementRequest(ComponentRef.Primitive(primitive.Id), primitive.DisplayName),
                    primitive.DisplayName, Describe(primitive.Signature)));
            }

            Section("Circuit I/O");
            _items.Add(LibraryItemView.Create(_content, _ui, _workspace,
                new PlacementRequest(ComponentRef.InputBoundary, "Input"), "Input", "switch"));
            _items.Add(LibraryItemView.Create(_content, _ui, _workspace,
                new PlacementRequest(ComponentRef.OutputBoundary, "Output"), "Output", "indicator"));

            Section("My Custom Gates");
            _customRoot = UiFactory.CreateRect("Custom Gates", _content);
            var customLayout = _customRoot.gameObject.AddComponent<VerticalLayoutGroup>();
            customLayout.spacing = 4f;
            customLayout.childControlWidth = true;
            customLayout.childControlHeight = true;
            customLayout.childForceExpandHeight = false;
            _customEmpty = _ui.Text("Empty", _content,
                "No custom gates yet.\nBuild a circuit and package it.", 12f, _ui.Theme.TextSecondary,
                TextAlignmentOptions.TopLeft);
            _customEmpty.enableWordWrapping = true;
            _customEmpty.gameObject.AddComponent<LayoutElement>().preferredHeight = 40f;

            RefreshCustomGates();
        }

        private void Section(string title)
        {
            TextMeshProUGUI label = _ui.Text("Section " + title, _content, title, 12f, _ui.Theme.Accent,
                TextAlignmentOptions.BottomLeft, FontStyles.Bold);
            label.gameObject.AddComponent<LayoutElement>().preferredHeight = 26f;
        }

        private void UpdateCustomEmptyState()
        {
            if (_customEmpty == null)
            {
                return;
            }

            bool anyVisible = false;
            for (int i = 0; i < _customItems.Count; i++)
            {
                anyVisible |= _customItems[i].gameObject.activeSelf;
            }

            _customEmpty.text = _customItems.Count == 0
                ? "No custom gates yet.\nBuild a circuit and package it."
                : "No custom gates match the search.";
            _customEmpty.gameObject.SetActive(!anyVisible);
        }

        private void OnDefinitionAdded(CircuitDefinition definition)
        {
            RefreshCustomGates();
        }

        private void OnDestroy()
        {
            if (_library != null)
            {
                _library.DefinitionAdded -= OnDefinitionAdded;
                _library.DefinitionRemoved -= OnDefinitionAdded;
            }
        }

        private static string Describe(ComponentSignature signature)
        {
            return signature.Inputs.Count + " in · " + signature.Outputs.Count + " out";
        }

        private static string CategoryOf(CircuitDefinition definition)
        {
            return string.IsNullOrWhiteSpace(definition.Category) ? UncategorizedLabel : definition.Category.Trim();
        }

        private static bool HasMultipleCategories(List<CircuitDefinition> definitions)
        {
            for (int i = 1; i < definitions.Count; i++)
            {
                if (!string.Equals(CategoryOf(definitions[i]), CategoryOf(definitions[0]), StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static int CompareByCategoryThenName(CircuitDefinition a, CircuitDefinition b)
        {
            int category = string.Compare(CategoryOf(a), CategoryOf(b), StringComparison.OrdinalIgnoreCase);
            return category != 0 ? category : string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
        }
    }
}
