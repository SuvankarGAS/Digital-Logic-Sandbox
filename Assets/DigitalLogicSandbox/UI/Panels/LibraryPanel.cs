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
    /// Floating gate library on the left. It opens with only what a newcomer needs: the built-in gates (listed
    /// from the primitive registry, never hardcoded), inputs and outputs, and the user's own gates. Advanced parts
    /// (clock, buses, displays) live in collapsed sections. Search looks through everything, expanding whatever
    /// matches. Custom gates are grouped by category and refresh automatically when a definition is published.
    /// Right-clicking an entry opens its context menu.
    /// </summary>
    public sealed class LibraryPanel : MonoBehaviour
    {
        public const string UncategorizedLabel = "General";

        /// <summary>Default bit width of bus components placed from the library.</summary>
        public const int DefaultBusWidth = 4;

        /// <summary>Default number of drivers of a bus join placed from the library.</summary>
        public const int DefaultJoinInputs = 2;

        private const float HeaderHeight = 92f;

        private readonly List<LibraryItemView> _items = new List<LibraryItemView>();
        private readonly List<LibraryItemView> _customItems = new List<LibraryItemView>();
        private readonly List<GameObject> _customHeaders = new List<GameObject>();
        private readonly List<CircuitDefinition> _scratchDefinitions = new List<CircuitDefinition>();
        private readonly List<LibraryItemView> _busItems = new List<LibraryItemView>();
        private readonly List<Section> _sections = new List<Section>();

        private RectTransform _busRoot;
        private RectTransform _displayRoot;
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
        private Section _customSection;
        private Section _busSection;
        private Section _displaySection;
        private GameObject _customEmpty;
        private TextMeshProUGUI _customEmptyText;
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
            Image background = ui.Card("Gate Library", parent, ui.Theme.PanelBackground);
            RectTransform rect = background.rectTransform;
            rect.anchorMin = new Vector2(0f, 0f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 0.5f);
            rect.offsetMin = new Vector2(UiFactory.Margin, UiFactory.Margin);
            rect.offsetMax = new Vector2(UiFactory.Margin + UiFactory.LibraryWidth, -UiFactory.TopInset);

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

        /// <summary>
        /// Applies a search filter (case-insensitive substring on name, and category/description of custom gates).
        /// While searching, every section with a match is shown expanded and sections without matches are hidden.
        /// </summary>
        public void SetFilter(string filter)
        {
            _filter = filter?.Trim() ?? string.Empty;
            for (int i = 0; i < _items.Count; i++)
            {
                _items[i].gameObject.SetActive(_items[i].Matches(_filter));
            }

            bool searching = _filter.Length > 0;
            for (int i = 0; i < _sections.Count; i++)
            {
                _sections[i].ApplyFilter(searching);
            }

            UpdateCustomEmptyState();
        }

        /// <summary>Rebuilds the custom gate list from the library (latest version of each definition).</summary>
        public void RefreshCustomGates()
        {
            for (int i = 0; i < _customItems.Count; i++)
            {
                _items.Remove(_customItems[i]);
                _customSection.Items.Remove(_customItems[i]);
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
                    TextMeshProUGUI header = _ui.Text("Category " + category, _customRoot, category, 11.5f,
                        _ui.Theme.TextMuted, TextAlignmentOptions.BottomLeft);
                    header.margin = new Vector4(8f, 0f, 0f, 2f);
                    header.gameObject.AddComponent<LayoutElement>().preferredHeight = 24f;
                    _customHeaders.Add(header.gameObject);
                }

                ComponentSignature signature = definition.GetSignature();
                string tile = string.IsNullOrWhiteSpace(definition.Icon) ? Initial(definition.Name) : definition.Icon;
                string tooltip = string.IsNullOrWhiteSpace(definition.Description)
                    ? definition.Name + (definition.Version > 1 ? "  ·  version " + definition.Version : string.Empty)
                    : definition.Description;
                LibraryItemView item = AddItem(_customSection, _customRoot,
                    new PlacementRequest(ComponentRef.Custom(definition.Reference), definition.Name),
                    definition.Name, Describe(signature), tile, LibraryTileStyle.Accent, showDetail: true,
                    category + " " + definition.Description, tooltip);
                _customItems.Add(item);
            }

            _customSection.SetCount(_customItems.Count);
            SetFilter(_filter);
        }

        private void Build(RectTransform rect)
        {
            RectTransform header = UiFactory.CreateRect("Header", rect);
            header.anchorMin = new Vector2(0f, 1f);
            header.anchorMax = new Vector2(1f, 1f);
            header.pivot = new Vector2(0.5f, 1f);
            header.sizeDelta = new Vector2(0f, HeaderHeight);
            header.anchoredPosition = Vector2.zero;
            var headerLayout = header.gameObject.AddComponent<VerticalLayoutGroup>();
            headerLayout.padding = new RectOffset(14, 14, 14, 8);
            headerLayout.spacing = 10f;
            headerLayout.childControlWidth = true;
            headerLayout.childControlHeight = true;
            headerLayout.childForceExpandHeight = false;

            TextMeshProUGUI title = _ui.Text("Title", header, "Library", 14.5f, _ui.Theme.TextPrimary,
                TextAlignmentOptions.MidlineLeft, FontStyles.Bold);
            title.gameObject.AddComponent<LayoutElement>().preferredHeight = 20f;
            _search = _ui.InputField("Search", header, "Search parts", Icons.Search);
            _search.onValueChanged.AddListener(SetFilter);

            RectTransform scroll = UiFactory.CreateRect("Scroll", rect);
            UiFactory.Stretch(scroll, 2f, 2f, HeaderHeight, 4f);
            _content = _ui.ScrollView("List", scroll, out _);
            UiFactory.Stretch((RectTransform)_content.parent.parent);
            var listLayout = _content.GetComponent<VerticalLayoutGroup>();
            listLayout.padding = new RectOffset(8, 8, 0, 14);
            listLayout.spacing = 1f;

            Section gates = AddSection("Gates", expanded: true);
            IReadOnlyList<IPrimitiveComponent> primitives = _catalog.Primitives.All;
            for (int i = 0; i < primitives.Count; i++)
            {
                IPrimitiveComponent primitive = primitives[i];
                AddItem(gates, gates.Body, new PlacementRequest(ComponentRef.Primitive(primitive.Id), primitive.DisplayName),
                    primitive.DisplayName, "Built-in gate  ·  " + Describe(primitive.Signature),
                    Initial(primitive.DisplayName), LibraryTileStyle.Neutral, showDetail: false);
            }

            gates.SetCount(primitives.Count);

            Section io = AddSection("Inputs & Outputs", expanded: true);
            AddItem(io, io.Body, new PlacementRequest(ComponentRef.InputBoundary, "Input"), "Input",
                "A switch you can flip between 0 and 1", Icons.Switch, LibraryTileStyle.Icon, false, "switch");
            AddItem(io, io.Body, new PlacementRequest(ComponentRef.OutputBoundary, "Output"), "Output",
                "A lamp that lights up on 1", Icons.Lamp, LibraryTileStyle.Icon, false, "indicator lamp");
            AddItem(io, io.Body, new PlacementRequest(ComponentRef.Constant(1, 1), "Constant"), "Constant",
                "A fixed value of 1 to 8 bits, set in the inspector", Icons.Binary, LibraryTileStyle.Icon, false,
                "fixed value rom word");
            io.SetCount(3);

            _customSection = AddSection("My Gates", expanded: true);
            _customRoot = _customSection.Body;
            _customEmpty = BuildCustomEmptyState(_customSection.Body);

            Section timing = AddSection("Timing", expanded: false);
            AddItem(timing, timing.Body, new PlacementRequest(ComponentRef.Clock, "Clock"), "Clock",
                "Global clock signal for sequential circuits", Icons.Clock, LibraryTileStyle.Icon, false,
                "global clock signal");
            timing.SetCount(1);

            _busSection = AddSection("Buses", expanded: false);
            _busWidthField = _ui.FieldRow(_busSection.Body, "Bus width",
                _busWidth.ToString(System.Globalization.CultureInfo.InvariantCulture),
                text => SetBusParameters(ParseOr(text, _busWidth), _joinInputs));
            _busWidthField.contentType = TMP_InputField.ContentType.IntegerNumber;
            _joinInputsField = _ui.FieldRow(_busSection.Body, "Join inputs",
                _joinInputs.ToString(System.Globalization.CultureInfo.InvariantCulture),
                text => SetBusParameters(_busWidth, ParseOr(text, _joinInputs)));
            _joinInputsField.contentType = TMP_InputField.ContentType.IntegerNumber;
            _busRoot = UiFactory.Column("Bus Components", _busSection.Body, 1f, new RectOffset(0, 0, 4, 0));

            _displaySection = AddSection("Displays", expanded: false);
            _displayRoot = UiFactory.Column("Display Components", _displaySection.Body, 1f, new RectOffset(0, 0, 0, 0));
            RebuildBusItems();

            RefreshCustomGates();
        }

        private GameObject BuildCustomEmptyState(Transform parent)
        {
            RectTransform box = UiFactory.Column("Empty", parent, 4f, new RectOffset(10, 10, 6, 8));
            TextMeshProUGUI icon = _ui.Icon("Icon", box, Icons.PackagePlus, 18f, _ui.Theme.TextMuted);
            icon.alignment = TextAlignmentOptions.Left;
            icon.gameObject.AddComponent<LayoutElement>().preferredHeight = 24f;
            _customEmptyText = _ui.Text("Text", box, string.Empty, 12f, _ui.Theme.TextSecondary, TextAlignmentOptions.TopLeft);
            _customEmptyText.enableWordWrapping = true;
            _customEmptyText.gameObject.AddComponent<LayoutElement>().preferredHeight = 34f;
            return box.gameObject;
        }

        private Section AddSection(string title, bool expanded)
        {
            var section = new Section(this, title, expanded);
            _sections.Add(section);
            return section;
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
                _busSection.Items.Remove(_busItems[i]);
                _displaySection.Items.Remove(_busItems[i]);
                Destroy(_busItems[i].gameObject);
            }

            _busItems.Clear();
            string bits = _busWidth.ToString(System.Globalization.CultureInfo.InvariantCulture) + "-bit";
            AddBusItem(ComponentRef.InputBus(_busWidth), "Bus Input", bits + " switches", Icons.Switch);
            AddBusItem(ComponentRef.OutputBus(_busWidth), "Bus Output", bits + " display", Icons.Lamp);
            AddBusItem(ComponentRef.Splitter(_busWidth), "Splitter", bits + " bus to single bits", Icons.Bus);
            AddBusItem(ComponentRef.Merger(_busWidth), "Merger", "single bits to " + bits + " bus", Icons.Bus);
            AddBusItem(ComponentRef.BusJoin(_busWidth, _joinInputs), "Bus Join",
                _joinInputs + " drivers onto one " + bits + " bus", Icons.Bus);
            AddBusItem(ComponentRef.TriState(_busWidth), "Tri-state", bits + " driver with enable", Icons.Zap);
            AddBusItem(ComponentRef.TriState(1), "Tri-state (1-bit)", "driver with enable", Icons.Zap);

            // Displays: LED and seven-segment have fixed single-bit inputs; the others follow the bus width.
            AddDisplayItem(ComponentRef.Display(DisplayStyle.Led), "LED", "lamp, lit while HIGH", Icons.Lamp);
            AddDisplayItem(ComponentRef.Display(DisplayStyle.SevenSegment), "7-Segment", "one digit, inputs a-g and dp",
                Icons.Hash);
            AddDisplayItem(ComponentRef.Display(DisplayStyle.Binary, _busWidth), "Binary Display", bits + " as lamps",
                Icons.Binary);
            AddDisplayItem(ComponentRef.Display(DisplayStyle.Hex, _busWidth), "Hex Display", bits + " as hex digits",
                Icons.Hash);
            AddDisplayItem(ComponentRef.Display(DisplayStyle.Decimal, _busWidth), "Decimal Display",
                bits + " unsigned number", Icons.Monitor);
            AddDisplayItem(ComponentRef.Display(DisplayStyle.SignedDecimal, _busWidth), "Signed Display",
                bits + " two's complement number", Icons.Monitor);
            _busSection.SetCount(7);
            _displaySection.SetCount(6);
            SetFilter(_filter);
        }

        private void AddBusItem(ComponentRef component, string title, string detail, string glyph)
        {
            LibraryItemView item = AddItem(_busSection, _busRoot, new PlacementRequest(component, title), title, detail,
                glyph, LibraryTileStyle.Icon, false, "bus wiring");
            _busItems.Add(item);
        }

        private void AddDisplayItem(ComponentRef component, string title, string detail, string glyph)
        {
            LibraryItemView item = AddItem(_displaySection, _displayRoot ? _displayRoot : _busRoot,
                new PlacementRequest(component, title), title, detail, glyph, LibraryTileStyle.Icon, false,
                "display output led seven segment hex decimal binary");
            _busItems.Add(item);
        }

        private static int ParseOr(string text, int fallback)
        {
            return int.TryParse(text, System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out int value) ? value : fallback;
        }

        private LibraryItemView AddItem(Section section, Transform parent, PlacementRequest request, string title,
            string detail, string tile, LibraryTileStyle style, bool showDetail, string searchExtra = null,
            string tooltip = null)
        {
            LibraryItemView item = LibraryItemView.Create(parent, _ui, _workspace, request, title, detail, tile, style,
                showDetail, searchExtra, tooltip);
            item.ContextRequested += OnItemContext;
            _items.Add(item);
            section.Items.Add(item);
            return item;
        }

        private void OnItemContext(LibraryItemView item, Vector2 screen)
        {
            ItemContextRequested?.Invoke(item.Request, screen);
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

            bool searching = _filter.Length > 0;
            _customEmptyText.text = "Build a circuit, then package it as a gate. It will appear here, ready to reuse.";
            // While searching, an empty "My Gates" simply disappears with the other sections that have no match.
            _customEmpty.SetActive(!anyVisible && !searching);
            if (!searching)
            {
                _customSection.SetHeaderVisible(true);
            }
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

        /// <summary>Tile letter for a gate without an icon: the first letter or digit of its name.</summary>
        private static string Initial(string name)
        {
            if (!string.IsNullOrEmpty(name))
            {
                for (int i = 0; i < name.Length; i++)
                {
                    if (char.IsLetterOrDigit(name[i]))
                    {
                        return char.ToUpperInvariant(name[i]).ToString();
                    }
                }
            }

            return "?";
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

        /// <summary>
        /// Collapsible group: a header row (chevron, title, item count) and a body holding the entries. The
        /// user's expanded/collapsed choice is kept while searching temporarily overrides it.
        /// </summary>
        private sealed class Section
        {
            private readonly RectTransform _header;
            private readonly TextMeshProUGUI _chevron;
            private readonly TextMeshProUGUI _count;
            private bool _expanded;
            private bool _searching;

            public Section(LibraryPanel panel, string title, bool expanded)
            {
                UiFactory ui = panel._ui;
                _expanded = expanded;
                Image header = ui.Panel("Section " + title, panel._content, Color.clear);
                _header = header.rectTransform;
                header.gameObject.AddComponent<LayoutElement>().preferredHeight = 34f;
                var button = header.gameObject.AddComponent<Button>();
                button.transition = Selectable.Transition.None;
                button.navigation = new Navigation { mode = Navigation.Mode.None };
                button.onClick.AddListener(Toggle);

                _chevron = ui.Icon("Chevron", _header, Icons.ChevronDown, 12f, ui.Theme.TextMuted);
                _chevron.rectTransform.anchorMin = new Vector2(0f, 0f);
                _chevron.rectTransform.anchorMax = new Vector2(0f, 1f);
                _chevron.rectTransform.pivot = new Vector2(0f, 0.5f);
                _chevron.rectTransform.sizeDelta = new Vector2(20f, 0f);
                _chevron.rectTransform.anchoredPosition = new Vector2(4f, -3f);

                TextMeshProUGUI label = ui.Text("Title", _header, title.ToUpperInvariant(), 10.5f, ui.Theme.TextMuted,
                    TextAlignmentOptions.MidlineLeft, FontStyles.Bold);
                label.characterSpacing = 8f;
                UiFactory.Stretch(label.rectTransform, 26f, 36f, 6f, 0f);

                _count = ui.Text("Count", _header, string.Empty, 10.5f, ui.Theme.TextMuted, TextAlignmentOptions.MidlineRight);
                UiFactory.Stretch(_count.rectTransform, 150f, 10f, 6f, 0f);

                Body = UiFactory.Column("Body " + title, panel._content, 1f, new RectOffset(0, 0, 0, 6));
                Apply();
            }

            public RectTransform Body { get; }

            public List<LibraryItemView> Items { get; } = new List<LibraryItemView>();

            public void SetCount(int count)
            {
                _count.text = count > 0 ? count.ToString(System.Globalization.CultureInfo.InvariantCulture) : string.Empty;
            }

            public void SetHeaderVisible(bool visible)
            {
                _header.gameObject.SetActive(visible);
            }

            /// <summary>While searching, show the section expanded only if it has a matching entry.</summary>
            public void ApplyFilter(bool searching)
            {
                _searching = searching;
                Apply();
            }

            private void Toggle()
            {
                if (_searching)
                {
                    return;
                }

                _expanded = !_expanded;
                Apply();
            }

            private void Apply()
            {
                if (_searching)
                {
                    bool any = false;
                    for (int i = 0; i < Items.Count && !any; i++)
                    {
                        any = Items[i].gameObject.activeSelf;
                    }

                    _header.gameObject.SetActive(any);
                    Body.gameObject.SetActive(any);
                    _chevron.text = Icons.ChevronDown;
                    return;
                }

                _header.gameObject.SetActive(true);
                Body.gameObject.SetActive(_expanded);
                _chevron.text = _expanded ? Icons.ChevronDown : Icons.ChevronRight;
            }
        }
    }
}
