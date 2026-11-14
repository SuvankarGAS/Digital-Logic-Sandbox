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
    /// and supports search by name, category and description. Right-clicking an entry opens its context menu.
    /// </summary>
    public sealed class LibraryPanel : MonoBehaviour
    {
        public const string UncategorizedLabel = "General";

        private readonly List<LibraryItemView> _items = new List<LibraryItemView>();
        private readonly List<LibraryItemView> _customItems = new List<LibraryItemView>();
        private readonly List<GameObject> _customHeaders = new List<GameObject>();
        private readonly List<CircuitDefinition> _scratchDefinitions = new List<CircuitDefinition>();
        private readonly List<LibraryItemView> _busItems = new List<LibraryItemView>();

        /// <summary>Default bit width of bus components placed from the library.</summary>
        public const int DefaultBusWidth = 4;

        /// <summary>Default number of drivers of a bus join placed from the library.</summary>
        public const int DefaultJoinInputs = 2;

        private RectTransform _busRoot;
        private TMP_InputField _busWidthField;
        private TMP_InputField _joinInputsField;
        private int _busWidth = DefaultBusWidth;
        private int _joinInputs = DefaultJoinInputs;

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

        /// <summary>Bit width used by the bus entries (Bus Input/Output, Splitter, Merger, Bus Join, Tri-state).</summary>
        public int BusWidth => _busWidth;

        public int JoinInputs => _joinInputs;

        public TMP_InputField BusWidthField => _busWidthField;

        /// <summary>
        /// Sets the width (and bus join input count) of the bus entries; values are clamped to the supported range
        /// and the entries are rebuilt so dragging places the new size.
        /// </summary>
        public void SetBusParameters(int width, int joinInputs)
        {
            _busWidth = Mathf.Clamp(width, 2, PortDescriptor.MaxWidth);
            _joinInputs = Mathf.Clamp(joinInputs, 2, ComponentRef.MaxBusJoinInputs);
            _busWidthField?.SetTextWithoutNotify(_busWidth.ToString(System.Globalization.CultureInfo.InvariantCulture));
            _joinInputsField?.SetTextWithoutNotify(_joinInputs.ToString(System.Globalization.CultureInfo.InvariantCulture));
            RebuildBusItems();
        }

        /// <summary>Right-click on an entry (screen position). The shell shows the entry context menu.</summary>
        public event Action<PlacementRequest, Vector2> ItemContextRequested;

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
            library.DefinitionUpdated += panel.OnDefinitionAdded;
            return panel;
        }

        /// <summary>Applies a search filter (case-insensitive substring on name, and category/description of custom gates).</summary>
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
                LibraryItemView item = AddItem(_customRoot,
                    new PlacementRequest(ComponentRef.Custom(definition.Reference), definition.Name),
                    definition.Name, Describe(signature) + (definition.Version > 1 ? "  -  v" + definition.Version : string.Empty),
                    definition.Icon, category + " " + definition.Description);
                _customItems.Add(item);
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
                AddItem(_content, new PlacementRequest(ComponentRef.Primitive(primitive.Id), primitive.DisplayName),
                    primitive.DisplayName, Describe(primitive.Signature));
            }

            Section("Circuit I/O");
            AddItem(_content, new PlacementRequest(ComponentRef.InputBoundary, "Input"), "Input", "switch");
            AddItem(_content, new PlacementRequest(ComponentRef.OutputBoundary, "Output"), "Output", "indicator");
            AddItem(_content, new PlacementRequest(ComponentRef.Clock, "Clock"), "Clock", "global clock signal");

            Section("Buses");
            _busWidthField = _ui.FieldRow(_content, "Bus width",
                _busWidth.ToString(System.Globalization.CultureInfo.InvariantCulture),
                text => SetBusParameters(ParseOr(text, _busWidth), _joinInputs));
            _busWidthField.contentType = TMP_InputField.ContentType.IntegerNumber;
            _joinInputsField = _ui.FieldRow(_content, "Join inputs",
                _joinInputs.ToString(System.Globalization.CultureInfo.InvariantCulture),
                text => SetBusParameters(_busWidth, ParseOr(text, _joinInputs)));
            _joinInputsField.contentType = TMP_InputField.ContentType.IntegerNumber;
            _busRoot = UiFactory.CreateRect("Bus Components", _content);
            var busLayout = _busRoot.gameObject.AddComponent<VerticalLayoutGroup>();
            busLayout.spacing = 4f;
            busLayout.childControlWidth = true;
            busLayout.childControlHeight = true;
            busLayout.childForceExpandHeight = false;
            RebuildBusItems();

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

        private void RebuildBusItems()
        {
            if (_busRoot == null)
            {
                return;
            }

            for (int i = 0; i < _busItems.Count; i++)
            {
                _items.Remove(_busItems[i]);
                Destroy(_busItems[i].gameObject);
            }

            _busItems.Clear();
            string bits = _busWidth.ToString(System.Globalization.CultureInfo.InvariantCulture) + "-bit";
            AddBusItem(ComponentRef.InputBus(_busWidth), "Bus Input", bits + " switches");
            AddBusItem(ComponentRef.OutputBus(_busWidth), "Bus Output", bits + " display");
            AddBusItem(ComponentRef.Splitter(_busWidth), "Splitter", bits + " bus to single bits");
            AddBusItem(ComponentRef.Merger(_busWidth), "Merger", "single bits to " + bits + " bus");
            AddBusItem(ComponentRef.BusJoin(_busWidth, _joinInputs), "Bus Join",
                _joinInputs + " drivers onto one " + bits + " bus");
            AddBusItem(ComponentRef.TriState(_busWidth), "Tri-state", bits + " driver with enable");
            AddBusItem(ComponentRef.TriState(1), "Tri-state (1-bit)", "driver with enable");
            SetFilter(_filter);
        }

        private void AddBusItem(ComponentRef component, string title, string detail)
        {
            LibraryItemView item = AddItem(_busRoot, new PlacementRequest(component, title), title, detail, null,
                "bus wiring");
            _busItems.Add(item);
        }

        private static int ParseOr(string text, int fallback)
        {
            return int.TryParse(text, System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out int value) ? value : fallback;
        }

        private LibraryItemView AddItem(Transform parent, PlacementRequest request, string title, string detail,
            string icon = null, string searchExtra = null)
        {
            LibraryItemView item = LibraryItemView.Create(parent, _ui, _workspace, request, title, detail, icon,
                searchExtra);
            item.ContextRequested += OnItemContext;
            _items.Add(item);
            return item;
        }

        private void OnItemContext(LibraryItemView item, Vector2 screen)
        {
            ItemContextRequested?.Invoke(item.Request, screen);
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
                _library.DefinitionUpdated -= OnDefinitionAdded;
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
