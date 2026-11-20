using System;
using System.Collections.Generic;
using System.Globalization;
using DG.Tweening;
using DigitalLogicSandbox.CircuitEditor.Placement;
using DigitalLogicSandbox.CircuitEditor.Workspace;
using DigitalLogicSandbox.Components.Abstractions;
using DigitalLogicSandbox.Core.Circuit;
using DigitalLogicSandbox.Core.Nodes;
using DigitalLogicSandbox.Core.Ports;
using DigitalLogicSandbox.GateLibrary.Custom;
using DigitalLogicSandbox.Rendering;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace DigitalLogicSandbox.UI.Panels
{
    /// <summary>Tabs of the gate library.</summary>
    public enum LibraryTab
    {
        /// <summary>Every part, in sections.</summary>
        All = 0,

        /// <summary>Starred parts.</summary>
        Favorites = 1,

        /// <summary>Recently placed parts.</summary>
        Recent = 2,
    }

    /// <summary>
    /// Floating gate library on the left. It opens with what a newcomer needs: the built-in gates (listed from the
    /// primitive registry, never hardcoded), inputs and outputs, and the user's own gates grouped in collapsible
    /// categories. Advanced parts (clock, buses, displays) live in collapsed sections. Starred and recently placed
    /// parts have their own tabs (All · Favorites · Recent) so the main list stays uncluttered.
    /// <para>
    /// Search ranks matches (name prefix first), highlights the matching text and expands whatever matches; the
    /// arrow keys move through the results and Enter arms placement. The panel collapses to an icon rail and can be
    /// resized by dragging its edge. Width, collapse, section state, pins and recents are per-user
    /// <see cref="UiPreferences"/>, never project data. Hovering an entry shows the <see cref="GatePreviewCard"/>.
    /// </para>
    /// </summary>
    public sealed class LibraryPanel : MonoBehaviour
    {
        public const string UncategorizedLabel = "General";

        /// <summary>Default bit width of bus components placed from the library.</summary>
        public const int DefaultBusWidth = 4;

        /// <summary>Default number of drivers of a bus join placed from the library.</summary>
        public const int DefaultJoinInputs = 2;

        /// <summary>Width of the collapsed icon rail.</summary>
        public const float CollapsedWidth = 52f;

        private const float HeaderHeight = 128f;
        private const float TabHeight = 28f;

        /// <summary>Row indent under a top-level section title.</summary>
        private const float SectionIndent = LibraryItemView.IndentStep;

        /// <summary>Row indent under a category title.</summary>
        private const float CategoryIndent = LibraryItemView.IndentStep * 2f + 2f;

        /// <summary>Left padding of blocks that line up with top-level rows (settings, empty states).</summary>
        private static readonly int BlockPadding = (int)(LibraryItemView.RowPadding + SectionIndent);
        private const float FooterHeight = 24f;
        private const float AnimationTime = 0.22f;

        private readonly List<LibraryItemView> _items = new List<LibraryItemView>();
        private readonly List<LibraryItemView> _customItems = new List<LibraryItemView>();
        private readonly List<LibraryItemView> _busItems = new List<LibraryItemView>();
        private readonly List<LibraryItemView> _pinnedItems = new List<LibraryItemView>();
        private readonly List<LibraryItemView> _recentItems = new List<LibraryItemView>();
        private readonly List<LibraryItemView> _navigable = new List<LibraryItemView>();
        private readonly List<Section> _sections = new List<Section>();
        private readonly List<Section> _categorySections = new List<Section>();
        private readonly List<CircuitDefinition> _scratchDefinitions = new List<CircuitDefinition>();
        private readonly List<LibraryItemView> _scratchItems = new List<LibraryItemView>();
        private readonly Dictionary<string, LibraryItemView> _byKey = new Dictionary<string, LibraryItemView>(StringComparer.Ordinal);
        private readonly List<GameObject> _railSectionButtons = new List<GameObject>();

        private RectTransform _rect;
        private RectTransform _full;
        private CanvasGroup _fullGroup;
        private RectTransform _rail;
        private CanvasGroup _railGroup;
        private RectTransform _scrollRect;
        private ScrollRect _scroll;
        private TextMeshProUGUI _footer;
        private LibraryResizeHandle _resize;
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
        private UiPreferences _preferences;
        private GatePreviewCard _preview;
        private RectTransform _content;
        private Section _pinnedSection;
        private Section _recentSection;
        private Section _gatesSection;
        private Section _customSection;
        private Section _busSection;
        private Section _displaySection;
        private GameObject _customEmpty;
        private GameObject _noResults;
        private TextMeshProUGUI _noResultsText;
        private TMP_InputField _search;
        private Button _clearSearch;
        private string _filter = string.Empty;
        private bool _searchWasFocused;

        /// <summary>The preview card is showing because of the keyboard highlight (not a mouse hover).</summary>
        private bool _keyboardPreview;
        private int _highlight = -1;
        private float _width = UiPreferences.DefaultLibraryWidth;
        private float _currentWidth = UiPreferences.DefaultLibraryWidth;
        private bool _collapsed;
        private bool _animate = true;
        private float _lastScrollOffset;
        private LibraryTab _tab;
        private readonly Button[] _tabButtons = new Button[3];
        private readonly TextMeshProUGUI[] _tabLabels = new TextMeshProUGUI[3];
        private readonly LibraryTabView[] _tabViews = new LibraryTabView[3];
        private GameObject _favoritesEmpty;
        private GameObject _recentEmpty;
        private GameObject _recentBar;
        private TextMeshProUGUI _recentCount;

        /// <summary>Version display setting the custom gate entries were built with.</summary>
        private bool _builtWithVersions = true;

        /// <summary>Main entries, one per component (excludes Pinned / Recent shortcut rows).</summary>
        public IReadOnlyList<LibraryItemView> Items => _items;

        /// <summary>Rows of the Pinned section (in pin order).</summary>
        public IReadOnlyList<LibraryItemView> PinnedItems => _pinnedItems;

        /// <summary>Rows of the Recent section (most recent first).</summary>
        public IReadOnlyList<LibraryItemView> RecentItems => _recentItems;

        /// <summary>Entries the arrow keys move through, in visual order.</summary>
        public IReadOnlyList<LibraryItemView> NavigableItems => _navigable;

        /// <summary>The keyboard-highlighted entry, or null.</summary>
        public LibraryItemView HighlightedItem => _highlight >= 0 && _highlight < _navigable.Count ? _navigable[_highlight] : null;

        public TMP_InputField SearchField => _search;

        public Button ClearSearchButton => _clearSearch;

        /// <summary>The "no parts match" message while a search finds nothing.</summary>
        public bool IsShowingNoResults => _noResults != null && _noResults.activeSelf;

        public UiPreferences Preferences => _preferences;

        public GatePreviewCard PreviewCard => _preview;

        public LibraryResizeHandle ResizeHandle => _resize;

        /// <summary>Animate collapse, width, scrolling and hover changes (tests turn this off).</summary>
        public bool Animate
        {
            get => _animate;
            set
            {
                _animate = value;
                if (_scroll is SmoothScrollRect smooth)
                {
                    smooth.Animate = value;
                }
            }
        }

        /// <summary>The shown tab.</summary>
        public LibraryTab CurrentTab => _tab;

        /// <summary>True while the Favorites or Recent tab shows its "nothing here yet" message.</summary>
        public bool IsShowingEmptyTab =>
            (_tab == LibraryTab.Favorites && _favoritesEmpty != null && _favoritesEmpty.activeInHierarchy) ||
            (_tab == LibraryTab.Recent && _recentEmpty != null && _recentEmpty.activeInHierarchy);

        /// <summary>The list's scroll view (eased wheel scrolling).</summary>
        public ScrollRect Scroll => _scroll;

        /// <summary>Raised after the shown tab changed.</summary>
        public event Action<LibraryTab> TabChanged;

        public bool IsCollapsed => _collapsed;

        /// <summary>Width of the expanded panel (the user's chosen width).</summary>
        public float ExpandedWidth => _width;

        /// <summary>Width the panel currently has (animating between rail and expanded width).</summary>
        public float CurrentWidth => _currentWidth;

        /// <summary>Bit width used by the bus entries (Bus Input/Output, Splitter, Merger, Bus Join, Tri-state).</summary>
        public int BusWidth => _busWidth;

        public int JoinInputs => _joinInputs;

        public TMP_InputField BusWidthField => _busWidthField;

        /// <summary>Right-click on an entry (screen position). The shell shows the entry context menu.</summary>
        public event Action<PlacementRequest, Vector2> ItemContextRequested;

        /// <summary>Right-click on a custom gate category header: the category name and its gates.</summary>
        public event Action<string, IReadOnlyList<DefinitionId>, Vector2> CategoryContextRequested;

        public static LibraryPanel Create(Transform parent, UiFactory ui, WorkspaceController workspace,
            ComponentCatalog catalog, DefinitionLibrary library, UiPreferences preferences = null)
        {
            Image background = ui.Card("Gate Library", parent, ui.Theme.PanelBackground);
            RectTransform rect = background.rectTransform;
            rect.anchorMin = new Vector2(0f, 0f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 0.5f);

            var panel = background.gameObject.AddComponent<LibraryPanel>();
            panel._ui = ui;
            panel._rect = rect;
            panel._workspace = workspace;
            panel._catalog = catalog;
            panel._library = library;
            panel._preferences = preferences ?? new UiPreferences(new MemoryPreferenceStore());
            panel._width = panel._preferences.LibraryWidth;
            panel.Build(rect);
            library.DefinitionAdded += panel.OnLibraryChanged;
            library.DefinitionRemoved += panel.OnLibraryChanged;
            library.DefinitionUpdated += panel.OnLibraryChanged;
            workspace.SettingsChanged += panel.OnWorkspaceSettingsChanged;
            workspace.PlacementChanged += panel.RefreshArmed;
            workspace.PlacementCommitted += panel.OnPlacementCommitted;
            panel._preferences.EntriesChanged += panel.RebuildShortcuts;
            panel.SetCollapsed(panel._preferences.LibraryCollapsed, animate: false);
            panel.SetTab((LibraryTab)panel._preferences.LibraryTab);
            return panel;
        }

        /// <summary>Attaches the shared hover card (created by the shell above the other panels).</summary>
        public void AttachPreview(GatePreviewCard preview)
        {
            _preview = preview;
        }

        /// <summary>Stable preferences key of a library entry: primitive id, custom gate id, or utility title.</summary>
        public static string KeyOf(PlacementRequest request)
        {
            ComponentRef component = request.Component;
            switch (component.Kind)
            {
                case ComponentKind.Primitive:
                    return "p:" + component.PrimitiveId;
                case ComponentKind.Custom:
                    return "c:" + component.Definition.Id;
                default:
                    return "u:" + (request.DisplayName ?? component.Kind.ToString());
            }
        }

        /// <summary>
        /// Sets the width (and bus join input count) of the bus entries; values are clamped to the supported range
        /// and the entries are rebuilt so dragging places the new size.
        /// </summary>
        public void SetBusParameters(int width, int joinInputs)
        {
            _busWidth = Mathf.Clamp(width, 2, PortDescriptor.MaxWidth);
            _joinInputs = Mathf.Clamp(joinInputs, 2, ComponentRef.MaxBusJoinInputs);
            _busWidthField?.SetTextWithoutNotify(_busWidth.ToString(CultureInfo.InvariantCulture));
            _joinInputsField?.SetTextWithoutNotify(_joinInputs.ToString(CultureInfo.InvariantCulture));
            RebuildBusItems();
        }

        // ------------------------------------------------------------------------------------------------
        // Search
        // ------------------------------------------------------------------------------------------------

        /// <summary>
        /// Applies a search filter (case-insensitive; names first, then category, description and pin names). While
        /// searching, sections with a match are shown expanded, matches are ranked within their section and the first
        /// best result is highlighted. The search applies to the shown tab.
        /// </summary>
        public void SetFilter(string filter)
        {
            string trimmed = filter?.Trim() ?? string.Empty;
            bool changed = !string.Equals(trimmed, _filter, StringComparison.Ordinal);
            _filter = trimmed;
            bool searching = _filter.Length > 0;
            bool anyMain = FilterRows(_items);
            bool anyFavorite = FilterRows(_pinnedItems);
            bool anyRecent = FilterRows(_recentItems);
            bool any = _tab == LibraryTab.Favorites ? anyFavorite : _tab == LibraryTab.Recent ? anyRecent : anyMain;
            bool tabEmpty = _tab == LibraryTab.Favorites ? _pinnedItems.Count == 0
                : _tab == LibraryTab.Recent && _recentItems.Count == 0;

            for (int i = 0; i < _sections.Count; i++)
            {
                _sections[i].Reorder(searching, _scratchItems);
            }

            for (int i = 0; i < _sections.Count; i++)
            {
                _sections[i].ApplyFilter(searching);
            }

            if (_search != null && _search.text.Trim() != _filter)
            {
                _search.SetTextWithoutNotify(filter ?? string.Empty);
            }

            if (_clearSearch != null)
            {
                _clearSearch.gameObject.SetActive(searching);
            }

            bool noResults = searching && !any && !tabEmpty;
            _noResults.SetActive(noResults);
            if (noResults)
            {
                string what = _tab == LibraryTab.Favorites ? "No favorites match" : _tab == LibraryTab.Recent
                    ? "No recent parts match" : "No parts match";
                _noResultsText.text = what + " \u201c" + Escape(_filter) + "\u201d";
            }

            UpdateEmptyStates();
            RebuildNavigation();
            if (searching && changed)
            {
                ScrollToOffset(0f, animate: false);
            }

            SetHighlight(searching ? BestMatch() : -1, scroll: searching);
        }

        private bool FilterRows(List<LibraryItemView> rows)
        {
            bool any = false;
            for (int i = 0; i < rows.Count; i++)
            {
                LibraryItemView item = rows[i];
                bool match = item.Matches(_filter);
                item.gameObject.SetActive(match);
                item.SetHighlightedText(_filter);
                any |= match;
            }

            return any;
        }

        // ------------------------------------------------------------------------------------------------
        // Tabs
        // ------------------------------------------------------------------------------------------------

        /// <summary>Shows a tab (All parts, Favorites or Recent) and remembers it.</summary>
        public void SetTab(LibraryTab tab)
        {
            bool changed = tab != _tab;
            _tab = tab;
            _preferences.LibraryTab = (int)tab;
            _preview?.Release(null);
            RefreshTabButtons();
            SetFilter(_filter);
            if (changed)
            {
                ScrollToOffset(0f, animate: false);
                TabChanged?.Invoke(tab);
            }
        }

        private void OpenTab(LibraryTab tab)
        {
            if (_collapsed)
            {
                SetCollapsed(false, Animate);
            }

            SetTab(tab);
        }

        private void RefreshTabButtons()
        {
            for (int i = 0; i < _tabButtons.Length; i++)
            {
                if (_tabButtons[i] == null)
                {
                    continue;
                }

                bool active = (int)_tab == i;
                _tabLabels[i].text = TabText((LibraryTab)i, active);
                _tabViews[i].SetActive(active, instant: !Animate);
            }
        }

        private string TabText(LibraryTab tab, bool active)
        {
            string accent = "<color=#" + ColorUtility.ToHtmlStringRGB(active ? _ui.Theme.Accent : _ui.Theme.TextMuted) + ">";
            switch (tab)
            {
                case LibraryTab.Favorites:
                    return accent + Icons.Star + "</color> Favorites";
                case LibraryTab.Recent:
                    return accent + Icons.History + "</color> Recent";
                default:
                    return "All";
            }
        }

        /// <summary>
        /// Index of the best-ranked visible result (the first one among equals). Sections keep their order, so the best
        /// match may sit below weaker matches of an earlier section ("in" → Input, not AND's "built-in").
        /// </summary>
        private int BestMatch()
        {
            int best = -1;
            for (int i = 0; i < _navigable.Count; i++)
            {
                if (best < 0 || _navigable[i].Rank < _navigable[best].Rank)
                {
                    best = i;
                }
            }

            return best;
        }

        /// <summary>Clears the search text and filter.</summary>
        public void ClearSearch()
        {
            _search.text = string.Empty;
            SetFilter(string.Empty);
        }

        /// <summary>Focuses the search field (Ctrl+F), expanding a collapsed panel first.</summary>
        public void FocusSearch()
        {
            if (_collapsed)
            {
                SetCollapsed(false, Animate);
            }

            _search.Select();
            _search.ActivateInputField();
        }

        /// <summary>Moves the keyboard highlight by <paramref name="delta"/> entries (clamped).</summary>
        public void MoveHighlight(int delta)
        {
            if (_navigable.Count == 0)
            {
                SetHighlight(-1, false);
                return;
            }

            int next = _highlight < 0 ? (delta > 0 ? 0 : _navigable.Count - 1) : _highlight + delta;
            SetHighlight(Mathf.Clamp(next, 0, _navigable.Count - 1), scroll: true);
            if (_preview != null && HighlightedItem != null)
            {
                _preview.ShowNow(HighlightedItem, _rect);
                _keyboardPreview = true;
            }
        }

        /// <summary>Arms placement of the highlighted entry (the first result when nothing is highlighted).</summary>
        public bool PlaceHighlighted()
        {
            LibraryItemView item = HighlightedItem ?? (_navigable.Count > 0 ? _navigable[0] : null);
            if (item == null || _workspace.Circuit == null)
            {
                return false;
            }

            _preview?.Release(null);
            _workspace.BeginPlacement(item.Request, fromDrag: false);
            if (EventSystem.current != null)
            {
                EventSystem.current.SetSelectedGameObject(null);
            }

            return _workspace.IsPlacing;
        }

        private void SetHighlight(int index, bool scroll)
        {
            LibraryItemView previous = HighlightedItem;
            _highlight = index;
            LibraryItemView current = HighlightedItem;
            if (current == null || (_preview != null && _preview.Source != current))
            {
                HideKeyboardPreview();
            }

            if (previous != null && previous != current)
            {
                previous.SetHighlighted(false);
            }

            if (current != null)
            {
                current.SetHighlighted(true);
                if (scroll)
                {
                    ScrollIntoView((RectTransform)current.transform);
                }
            }
        }

        /// <summary>Hides the preview card if the keyboard highlight opened it.</summary>
        private void HideKeyboardPreview()
        {
            if (!_keyboardPreview)
            {
                return;
            }

            _keyboardPreview = false;
            _preview?.Release(null);
        }

        /// <summary>
        /// Escape while the search had focus: always hides a keyboard preview; with text, clears the search and keeps
        /// typing (returns true); without text the field simply loses focus (returns false).
        /// </summary>
        public bool HandleEscape()
        {
            HideKeyboardPreview();
            SetHighlight(-1, false);
            if (_search.text.Length == 0)
            {
                return false;
            }

            ClearSearch();
            _search.ActivateInputField();
            return true;
        }

        private void RebuildNavigation()
        {
            HideKeyboardPreview();
            LibraryItemView highlighted = HighlightedItem;
            if (highlighted != null)
            {
                highlighted.SetHighlighted(false);
            }

            _highlight = -1;
            _navigable.Clear();
            for (int i = 0; i < _sections.Count; i++)
            {
                _sections[i].CollectNavigable(_navigable);
            }
        }

        // ------------------------------------------------------------------------------------------------
        // Custom gates
        // ------------------------------------------------------------------------------------------------

        /// <summary>Rebuilds the custom gate list from the library (latest version of each definition).</summary>
        public void RefreshCustomGates()
        {
            for (int i = 0; i < _customItems.Count; i++)
            {
                RemoveItem(_customItems[i]);
            }

            _customItems.Clear();
            _customSection.Items.Clear();
            for (int i = 0; i < _categorySections.Count; i++)
            {
                _categorySections[i].Destroy();
                _customSection.Children.Remove(_categorySections[i]);
            }

            _categorySections.Clear();
            _builtWithVersions = _workspace.ShowGateVersions;
            _scratchDefinitions.Clear();
            _library.GetLatestDefinitions(_scratchDefinitions);
            _scratchDefinitions.Sort(CompareByCategoryThenName);

            bool multipleCategories = HasMultipleCategories(_scratchDefinitions);
            Section category = null;
            for (int i = 0; i < _scratchDefinitions.Count; i++)
            {
                CircuitDefinition definition = _scratchDefinitions[i];
                string name = CategoryOf(definition);
                Section target = _customSection;
                if (multipleCategories)
                {
                    if (category == null || !string.Equals(category.Title, name, StringComparison.Ordinal))
                    {
                        category = new Section(this, _customSection.Body, name, "category:" + name, null,
                            expanded: true, isCategory: true);
                        category.Parent = _customSection;
                        category.HeaderRightClicked = OnCategoryRightClicked;
                        _customSection.Children.Add(category);
                        _categorySections.Add(category);
                    }

                    target = category;
                }

                ComponentSignature signature = definition.GetSignature();
                var spec = new LibraryEntrySpec
                {
                    Request = new PlacementRequest(ComponentRef.Custom(definition.Reference), definition.Name),
                    Title = definition.Name,
                    Detail = signature.Inputs.Count + " → " + signature.Outputs.Count,
                    Description = definition.Description,
                    Category = name,
                    Tile = string.IsNullOrWhiteSpace(definition.Icon) ? Initial(definition.Name) : definition.Icon,
                    Style = LibraryTileStyle.Accent,
                    ShowDetail = true,
                    SearchExtra = name + " " + definition.Description + " " + PinNames(signature),
                    Inputs = signature.Inputs.Count,
                    Outputs = signature.Outputs.Count,
                };
                spec.Key = KeyOf(spec.Request);
                LibraryItemView item = AddItem(target, target.Body, spec);
                _customItems.Add(item);
            }

            for (int i = 0; i < _categorySections.Count; i++)
            {
                _categorySections[i].SetCount(_categorySections[i].Items.Count);
            }

            _customSection.SetCount(_customItems.Count);
            RebuildShortcuts();
            RefreshArmed();
            SetFilter(_filter);
        }

        /// <summary>The latest definitions in <paramref name="category"/> (as shown in the library).</summary>
        public void GetCategoryGates(string category, List<DefinitionId> results)
        {
            results.Clear();
            for (int i = 0; i < _customItems.Count; i++)
            {
                if (string.Equals(_customItems[i].Spec.Category, category, StringComparison.Ordinal))
                {
                    results.Add(_customItems[i].Request.Component.Definition.Id);
                }
            }
        }

        /// <summary>Every category currently used by a custom gate, sorted.</summary>
        public void GetCategories(List<string> results)
        {
            results.Clear();
            for (int i = 0; i < _customItems.Count; i++)
            {
                string category = _customItems[i].Spec.Category;
                if (!results.Contains(category))
                {
                    results.Add(category);
                }
            }

            results.Sort(StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>Remembers a renamed category's open/closed state under its new name.</summary>
        public void RenameCategoryState(string oldName, string newName)
        {
            _preferences.RenameSection("category:" + oldName, "category:" + newName);
        }

        private void OnCategoryRightClicked(Section section, Vector2 screen)
        {
            var ids = new List<DefinitionId>();
            GetCategoryGates(section.Title, ids);
            CategoryContextRequested?.Invoke(section.Title, ids, screen);
        }

        // ------------------------------------------------------------------------------------------------
        // Pinned and recent
        // ------------------------------------------------------------------------------------------------

        public bool IsPinned(PlacementRequest request)
        {
            return _preferences.IsPinned(KeyOf(request));
        }

        public void SetPinned(PlacementRequest request, bool pinned)
        {
            _preferences.SetPinned(KeyOf(request), pinned);
        }

        private void TogglePin(LibraryItemView item)
        {
            _preferences.SetPinned(item.Key, !_preferences.IsPinned(item.Key));
        }

        private void OnPlacementCommitted(PlacementRequest request)
        {
            string key = KeyOf(request);
            if (_byKey.ContainsKey(key))
            {
                _preferences.AddRecent(key);
            }
        }

        /// <summary>Rebuilds the Pinned and Recent rows from preferences (keys without an entry are skipped).</summary>
        private void RebuildShortcuts()
        {
            if (_pinnedSection == null)
            {
                return;
            }

            BuildShortcuts(_pinnedSection, _pinnedItems, _preferences.Pinned);
            BuildShortcuts(_recentSection, _recentItems, _preferences.Recent);
            for (int i = 0; i < _items.Count; i++)
            {
                _items[i].SetPinned(_preferences.IsPinned(_items[i].Key));
            }

            RefreshArmed();
            RefreshTabButtons();
            if (_recentCount != null)
            {
                _recentCount.text = _recentItems.Count == 1 ? "1 recently placed part"
                    : _recentItems.Count.ToString(CultureInfo.InvariantCulture) + " recently placed parts";
            }

            if (_noResults != null)
            {
                SetFilter(_filter);
            }
        }

        private void BuildShortcuts(Section section, List<LibraryItemView> rows, IReadOnlyList<string> keys)
        {
            for (int i = 0; i < rows.Count; i++)
            {
                DestroyRow(rows[i]);
            }

            rows.Clear();
            section.Items.Clear();
            for (int i = 0; i < keys.Count; i++)
            {
                if (!_byKey.TryGetValue(keys[i], out LibraryItemView source))
                {
                    continue;
                }

                LibraryItemView row = LibraryItemView.Create(section.Body, _ui, _workspace, source.Spec, shortcut: true,
                    indent: section.RowIndent);
                Wire(row);
                row.SetPinned(_preferences.IsPinned(row.Key));
                row.HomeIndex = row.transform.GetSiblingIndex();
                rows.Add(row);
                section.Items.Add(row);
            }

            section.SetCount(rows.Count);
        }

        // ------------------------------------------------------------------------------------------------
        // Collapse and resize
        // ------------------------------------------------------------------------------------------------

        /// <summary>Collapses the panel to an icon rail (or expands it again).</summary>
        public void SetCollapsed(bool collapsed, bool animate = true)
        {
            _collapsed = collapsed;
            _preferences.LibraryCollapsed = collapsed;
            _preview?.Release(null);
            DOTween.Kill(this);
            _full.gameObject.SetActive(!collapsed);
            _rail.gameObject.SetActive(collapsed);
            _resize.gameObject.SetActive(!collapsed);
            float target = collapsed ? CollapsedWidth : _width;
            if (!animate || !Animate)
            {
                ApplyWidth(target);
                return;
            }

            if (collapsed)
            {
                _railGroup.alpha = 0f;
            }

            DOTween.To(() => _currentWidth, ApplyWidth, target, AnimationTime).SetEase(Ease.OutCubic).SetUpdate(true)
                .SetTarget(this);
        }

        /// <summary>Sets the expanded width (clamped to the supported range) and stores it.</summary>
        public void SetWidth(float width)
        {
            _width = Mathf.Clamp(width, UiPreferences.MinLibraryWidth, UiPreferences.MaxLibraryWidth);
            _preferences.LibraryWidth = _width;
            if (!_collapsed)
            {
                DOTween.Kill(this);
                ApplyWidth(_width);
            }
        }

        private void ApplyWidth(float width)
        {
            _currentWidth = width;
            _rect.offsetMin = new Vector2(UiFactory.Margin, UiFactory.Margin);
            _rect.offsetMax = new Vector2(UiFactory.Margin + width, -UiFactory.TopInset);
            float span = Mathf.Max(1f, _width - CollapsedWidth);
            float t = Mathf.Clamp01((width - CollapsedWidth) / span);
            _fullGroup.alpha = _collapsed ? 0f : t * t;
            _railGroup.alpha = _collapsed ? 1f - t : 0f;
        }

        private void OnResizeDragged(float delta)
        {
            _width = Mathf.Clamp(_width + delta, UiPreferences.MinLibraryWidth, UiPreferences.MaxLibraryWidth);
            ApplyWidth(_width);
        }

        private void OnResizeEnded()
        {
            _preferences.LibraryWidth = _width;
        }

        private void ExpandTo(Section section)
        {
            if (_tab != LibraryTab.All)
            {
                SetTab(LibraryTab.All);
            }

            section?.SetExpanded(true);
            if (!_collapsed)
            {
                ScrollToSection(section);
                return;
            }

            SetCollapsed(false, Animate);
            if (Animate)
            {
                DOTween.Sequence().AppendInterval(AnimationTime).AppendCallback(() => ScrollToSection(section))
                    .SetUpdate(true).SetTarget(this);
            }
            else
            {
                ScrollToSection(section);
            }
        }

        private void ScrollToSection(Section section)
        {
            if (section?.Header != null)
            {
                ScrollIntoView(section.Header, alignTop: true, animate: Animate);
            }
        }

        private static readonly Vector3[] Corners = new Vector3[4];

        private void ScrollIntoView(RectTransform target, bool alignTop = false, bool animate = false)
        {
            if (target == null || !target.gameObject.activeInHierarchy)
            {
                return;
            }

            LayoutRebuilder.ForceRebuildLayoutImmediate(_content);
            RectTransform viewport = _scroll.viewport;
            float viewHeight = viewport.rect.height;
            float contentHeight = _content.rect.height;
            target.GetWorldCorners(Corners);
            float top = _content.InverseTransformPoint(Corners[1]).y;
            float bottom = _content.InverseTransformPoint(Corners[0]).y;
            float scroll = _content.anchoredPosition.y;
            const float pad = 6f;
            if (alignTop || top > -scroll - pad)
            {
                scroll = -top - pad;
            }
            else if (bottom < -scroll - viewHeight + pad)
            {
                scroll = -bottom - viewHeight + pad;
            }

            scroll = Mathf.Clamp(scroll, 0f, Mathf.Max(0f, contentHeight - viewHeight));
            ScrollToOffset(scroll, animate);
        }

        private void ScrollToOffset(float offset, bool animate)
        {
            _scroll.velocity = Vector2.zero;
            if (_scroll is SmoothScrollRect smooth)
            {
                smooth.ScrollTo(offset, animate && Animate);
                return;
            }

            _content.anchoredPosition = new Vector2(_content.anchoredPosition.x, Mathf.Max(0f, offset));
        }

        /// <summary>Scrolling hides the hover card (it would point at a row that moved away).</summary>
        private void OnScrolled(Vector2 position)
        {
            float offset = _content.anchoredPosition.y;
            if (Mathf.Abs(offset - _lastScrollOffset) > 0.5f)
            {
                _lastScrollOffset = offset;
                if (_preview != null && _preview.Source != null && !_preview.Source.IsHighlighted)
                {
                    _preview.Release(null);
                }
            }
        }

        // ------------------------------------------------------------------------------------------------
        // Building
        // ------------------------------------------------------------------------------------------------

        private void Build(RectTransform rect)
        {
            _full = UiFactory.CreateRect("Full", rect);
            UiFactory.Stretch(_full);
            _fullGroup = _full.gameObject.AddComponent<CanvasGroup>();
            _rail = UiFactory.CreateRect("Rail", rect);
            UiFactory.Stretch(_rail);
            _railGroup = _rail.gameObject.AddComponent<CanvasGroup>();

            BuildHeader(_full);

            _scrollRect = UiFactory.CreateRect("Scroll", _full);
            UiFactory.Stretch(_scrollRect, 2f, 2f, HeaderHeight, 4f);
            _content = _ui.ScrollView("List", _scrollRect, out _scroll, smooth: true);
            _scroll.onValueChanged.AddListener(OnScrolled);
            UiFactory.Stretch((RectTransform)_content.parent.parent);
            var listLayout = _content.GetComponent<VerticalLayoutGroup>();
            listLayout.padding = new RectOffset(8, 8, 0, 14);
            listLayout.spacing = 1f;

            _footer = _ui.Text("Footer", _full, string.Empty, 10.5f, _ui.Theme.TextMuted, TextAlignmentOptions.Center);
            _footer.richText = true;
            RectTransform footer = _footer.rectTransform;
            footer.anchorMin = new Vector2(0f, 0f);
            footer.anchorMax = new Vector2(1f, 0f);
            footer.pivot = new Vector2(0.5f, 0f);
            footer.sizeDelta = new Vector2(-16f, FooterHeight);
            footer.anchoredPosition = new Vector2(0f, 4f);
            string key = "<color=#" + ColorUtility.ToHtmlStringRGB(_ui.Theme.TextSecondary) + ">";
            _footer.text = key + "↑↓</color> move   " + key + "Enter</color> place   " + key + "Esc</color> clear";
            _footer.gameObject.SetActive(false);

            BuildNoResults();

            _pinnedSection = AddSection("Favorites", "pinned", null, expanded: true, LibraryTab.Favorites);
            _favoritesEmpty = BuildTabEmptyState(_pinnedSection.Body, Icons.Star, "No favorites yet",
                "Hover a part and click " + Icons.Star + ", or right-click it and choose Add to favorites.");
            _recentSection = AddSection("Recent", "recent", null, expanded: true, LibraryTab.Recent);
            BuildRecentBar(_recentSection.Body);
            _recentEmpty = BuildTabEmptyState(_recentSection.Body, Icons.History, "Nothing placed yet",
                "Parts you place in the workspace appear here, most recent first.");

            _gatesSection = AddSection("Gates", "gates", Icons.Cpu, expanded: true);
            IReadOnlyList<IPrimitiveComponent> primitives = _catalog.Primitives.All;
            for (int i = 0; i < primitives.Count; i++)
            {
                IPrimitiveComponent primitive = primitives[i];
                AddEntry(_gatesSection, _gatesSection.Body,
                    new PlacementRequest(ComponentRef.Primitive(primitive.Id), primitive.DisplayName), primitive.DisplayName,
                    "Built-in gate", Initial(primitive.DisplayName), LibraryTileStyle.Neutral, primitive.Signature,
                    "built-in primitive logic");
            }

            _gatesSection.SetCount(primitives.Count);

            Section io = AddSection("Inputs & Outputs", "io", Icons.Switch, expanded: true);
            AddEntry(io, io.Body, new PlacementRequest(ComponentRef.InputBoundary, "Input"), "Input",
                "A switch you can flip between 0 and 1", Icons.Switch, LibraryTileStyle.Icon, null, "switch");
            AddEntry(io, io.Body, new PlacementRequest(ComponentRef.OutputBoundary, "Output"), "Output",
                "A lamp that lights up on 1", Icons.Lamp, LibraryTileStyle.Icon, null, "indicator lamp");
            AddEntry(io, io.Body, new PlacementRequest(ComponentRef.Constant(1, 1), "Constant"), "Constant",
                "A fixed value of 1 to 8 bits, set in the inspector", Icons.Binary, LibraryTileStyle.Icon, null,
                "fixed value rom word");
            io.SetCount(3);

            _customSection = AddSection("My Gates", "custom", Icons.Boxes, expanded: true);
            _customEmpty = BuildCustomEmptyState(_customSection.Body);

            Section timing = AddSection("Timing", "timing", Icons.Clock, expanded: false);
            AddEntry(timing, timing.Body, new PlacementRequest(ComponentRef.Clock, "Clock"), "Clock",
                "Global clock signal for sequential circuits", Icons.Clock, LibraryTileStyle.Icon, null,
                "global clock signal");
            timing.SetCount(1);

            _busSection = AddSection("Buses", "buses", Icons.Bus, expanded: false);
            RectTransform settings = UiFactory.Column("Bus Settings", _busSection.Body, 4f,
                new RectOffset(BlockPadding, 6, 2, 4));
            _busWidthField = _ui.FieldRow(settings, "Bus width", _busWidth.ToString(CultureInfo.InvariantCulture),
                text => SetBusParameters(ParseOr(text, _busWidth), _joinInputs));
            _busWidthField.contentType = TMP_InputField.ContentType.IntegerNumber;
            _joinInputsField = _ui.FieldRow(settings, "Join inputs", _joinInputs.ToString(CultureInfo.InvariantCulture),
                text => SetBusParameters(_busWidth, ParseOr(text, _joinInputs)));
            _joinInputsField.contentType = TMP_InputField.ContentType.IntegerNumber;
            // The fields handle the wheel themselves; pass it on so the list keeps scrolling under the pointer.
            _busWidthField.gameObject.AddComponent<ScrollForwarder>();
            _joinInputsField.gameObject.AddComponent<ScrollForwarder>();
            _busRoot = UiFactory.Column("Bus Components", _busSection.Body, 1f, new RectOffset(0, 0, 2, 0));

            _displaySection = AddSection("Displays", "displays", Icons.Monitor, expanded: false);
            _displayRoot = UiFactory.Column("Display Components", _displaySection.Body, 1f, new RectOffset(0, 0, 0, 0));

            BuildRail();
            RebuildRail();
            _resize = LibraryResizeHandle.Create(_rect, _ui);
            _resize.Dragged += OnResizeDragged;
            _resize.DragEnded += OnResizeEnded;
            _resize.ResetRequested += () => SetWidth(UiPreferences.DefaultLibraryWidth);

            RebuildBusItems();
            RefreshCustomGates();
        }

        private void BuildHeader(RectTransform parent)
        {
            RectTransform header = UiFactory.CreateRect("Header", parent);
            header.anchorMin = new Vector2(0f, 1f);
            header.anchorMax = new Vector2(1f, 1f);
            header.pivot = new Vector2(0.5f, 1f);
            header.sizeDelta = new Vector2(0f, HeaderHeight);
            header.anchoredPosition = Vector2.zero;
            var headerLayout = header.gameObject.AddComponent<VerticalLayoutGroup>();
            headerLayout.padding = new RectOffset(14, 10, 10, 8);
            headerLayout.spacing = 8f;
            headerLayout.childControlWidth = true;
            headerLayout.childControlHeight = true;
            headerLayout.childForceExpandHeight = false;

            RectTransform titleRow = UiFactory.CreateRect("Title Row", header);
            UiFactory.Row(titleRow, 4f, new RectOffset(0, 0, 0, 0));
            titleRow.gameObject.AddComponent<LayoutElement>().preferredHeight = 28f;
            TextMeshProUGUI title = _ui.Text("Title", titleRow, "Library", 14.5f, _ui.Theme.TextPrimary,
                TextAlignmentOptions.MidlineLeft, FontStyles.Bold);
            title.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
            _ui.IconButton("Collapse", titleRow, Icons.PanelLeftClose, "Collapse library", () => SetCollapsed(true, Animate),
                28f, 15f);

            _search = _ui.InputField("Search", header, "Search parts   Ctrl+F", Icons.Search);
            _search.restoreOriginalTextOnEscape = false;
            _search.onValueChanged.AddListener(SetFilter);
            _search.onSubmit.AddListener(_ => PlaceHighlighted());
            UiFactory.Stretch(_search.textViewport, 31f, 32f, 4f, 4f);
            _clearSearch = _ui.IconButton("Clear", _search.transform, Icons.Close, "Clear search", ClearSearch, 24f, 13f);
            RectTransform clear = (RectTransform)_clearSearch.transform;
            clear.anchorMin = clear.anchorMax = new Vector2(1f, 0.5f);
            clear.pivot = new Vector2(1f, 0.5f);
            clear.sizeDelta = new Vector2(24f, 24f);
            clear.anchoredPosition = new Vector2(-4f, 0f);
            Destroy(_clearSearch.GetComponent<LayoutElement>());
            _clearSearch.gameObject.SetActive(false);
            BuildTabs(header);
        }

        /// <summary>Segmented control: All · Favorites · Recent.</summary>
        private void BuildTabs(RectTransform header)
        {
            Image track = _ui.Panel("Tabs", header, _ui.Theme.WorkspaceBackground, rounded: true, radius: 8f);
            track.raycastTarget = false;
            var trackLayout = track.gameObject.AddComponent<LayoutElement>();
            trackLayout.preferredHeight = trackLayout.minHeight = TabHeight;
            var row = track.gameObject.AddComponent<HorizontalLayoutGroup>();
            row.padding = new RectOffset(3, 3, 3, 3);
            row.spacing = 2f;
            row.childControlWidth = true;
            row.childControlHeight = true;
            row.childForceExpandWidth = true;
            row.childForceExpandHeight = true;
            for (int i = 0; i < _tabButtons.Length; i++)
            {
                LibraryTab tab = (LibraryTab)i;
                Image tile = _ui.Panel("Tab " + tab, track.transform, Color.clear, rounded: true, radius: 6f);
                var layout = tile.gameObject.AddComponent<LayoutElement>();
                layout.flexibleWidth = tab == LibraryTab.All ? 0.6f : tab == LibraryTab.Favorites ? 1.25f : 1.05f;
                var button = tile.gameObject.AddComponent<Button>();
                button.transition = Selectable.Transition.None;
                button.navigation = new Navigation { mode = Navigation.Mode.None };
                button.onClick.AddListener(() => SetTab(tab));
                TextMeshProUGUI label = _ui.Text("Label", tile.transform, string.Empty, 11f, _ui.Theme.TextMuted,
                    TextAlignmentOptions.Center, FontStyles.Bold);
                label.richText = true;
                label.overflowMode = TextOverflowModes.Overflow;
                UiFactory.Stretch(label.rectTransform, 2f, 2f);
                var view = tile.gameObject.AddComponent<LibraryTabView>();
                view.Initialize(_ui.Theme, tile, label);
                _ui.AddTooltip(tile.gameObject, tab == LibraryTab.All ? "All parts"
                    : tab == LibraryTab.Favorites ? "Favorites: your starred parts" : "Recently placed parts");
                _tabButtons[i] = button;
                _tabLabels[i] = label;
                _tabViews[i] = view;
            }

            RefreshTabButtons();
        }

        private GameObject BuildTabEmptyState(Transform parent, string glyph, string title, string text)
        {
            RectTransform box = UiFactory.Column("Empty", parent, 4f, new RectOffset(12, 12, 18, 10));
            box.GetComponent<VerticalLayoutGroup>().childAlignment = TextAnchor.UpperCenter;
            TextMeshProUGUI icon = _ui.Icon("Icon", box, glyph, 20f, _ui.Theme.TextMuted);
            icon.gameObject.AddComponent<LayoutElement>().preferredHeight = 28f;
            TextMeshProUGUI heading = _ui.Text("Title", box, title, 13f, _ui.Theme.TextPrimary, TextAlignmentOptions.Center,
                FontStyles.Bold);
            heading.gameObject.AddComponent<LayoutElement>().preferredHeight = 20f;
            TextMeshProUGUI body = _ui.Text("Text", box, text, 12f, _ui.Theme.TextSecondary, TextAlignmentOptions.Top);
            body.enableWordWrapping = true;
            body.gameObject.AddComponent<LayoutElement>().preferredHeight = 52f;
            return box.gameObject;
        }

        private void BuildRecentBar(Transform parent)
        {
            RectTransform bar = UiFactory.CreateRect("Recent Bar", parent);
            UiFactory.Row(bar, 4f, new RectOffset((int)LibraryItemView.RowPadding, 2, 6, 4));
            bar.gameObject.AddComponent<LayoutElement>().preferredHeight = 30f;
            _recentCount = _ui.Text("Count", bar, string.Empty, 11.5f, _ui.Theme.TextMuted);
            _recentCount.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
            Button clear = _ui.Button("Clear", bar, "Clear", () => _preferences.ClearRecent(), 64f, 11.5f);
            var layout = clear.GetComponent<LayoutElement>();
            layout.preferredHeight = layout.minHeight = 24f;
            _recentBar = bar.gameObject;
        }

        private void BuildNoResults()
        {
            RectTransform box = UiFactory.Column("No Results", _content, 6f, new RectOffset(10, 10, 14, 10));
            box.GetComponent<VerticalLayoutGroup>().childAlignment = TextAnchor.UpperCenter;
            TextMeshProUGUI icon = _ui.Icon("Icon", box, Icons.Search, 20f, _ui.Theme.TextMuted);
            icon.gameObject.AddComponent<LayoutElement>().preferredHeight = 26f;
            _noResultsText = _ui.Text("Text", box, string.Empty, 12.5f, _ui.Theme.TextSecondary, TextAlignmentOptions.Center);
            _noResultsText.enableWordWrapping = true;
            _noResultsText.gameObject.AddComponent<LayoutElement>().preferredHeight = 36f;
            Button clear = _ui.Button("Clear Search", box, "Clear search", ClearSearch, 120f, 12.5f);
            clear.GetComponent<LayoutElement>().flexibleWidth = 0f;
            var wrap = UiFactory.CreateRect("Center", box);
            UiFactory.Row(wrap, 0f, new RectOffset(0, 0, 0, 0), TextAnchor.MiddleCenter);
            wrap.gameObject.AddComponent<LayoutElement>().preferredHeight = 32f;
            clear.transform.SetParent(wrap, false);
            _noResults = box.gameObject;
            _noResults.SetActive(false);
        }

        private GameObject BuildCustomEmptyState(Transform parent)
        {
            RectTransform box = UiFactory.Column("Empty", parent, 4f, new RectOffset(BlockPadding, 10, 6, 8));
            TextMeshProUGUI icon = _ui.Icon("Icon", box, Icons.PackagePlus, 18f, _ui.Theme.TextMuted);
            icon.alignment = TextAlignmentOptions.Left;
            icon.gameObject.AddComponent<LayoutElement>().preferredHeight = 24f;
            TextMeshProUGUI text = _ui.Text("Text", box,
                "Build a circuit, then package it as a gate. It will appear here, ready to reuse.", 12f,
                _ui.Theme.TextSecondary, TextAlignmentOptions.TopLeft);
            text.enableWordWrapping = true;
            text.gameObject.AddComponent<LayoutElement>().preferredHeight = 52f; // wraps in the panel width
            return box.gameObject;
        }

        private void BuildRail()
        {
            var layout = _rail.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(9, 9, 10, 10);
            layout.spacing = 4f;
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childControlWidth = false;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;
            RailButton(Icons.PanelLeftOpen, "Expand library", () => SetCollapsed(false, Animate));
            RailButton(Icons.Search, "Search parts (Ctrl+F)", FocusSearch);
            RailButton(Icons.Star, "Favorites", () => OpenTab(LibraryTab.Favorites));
            RailButton(Icons.History, "Recent", () => OpenTab(LibraryTab.Recent));
            Image line = _ui.Panel("Separator", _rail, _ui.Theme.PanelBorder);
            line.raycastTarget = false;
            line.rectTransform.sizeDelta = new Vector2(22f, 1f);
        }

        private void RebuildRail()
        {
            for (int i = 0; i < _railSectionButtons.Count; i++)
            {
                _railSectionButtons[i].SetActive(false);
                Destroy(_railSectionButtons[i]);
            }

            _railSectionButtons.Clear();
            for (int i = 0; i < _sections.Count; i++)
            {
                Section section = _sections[i];
                if (section.Icon == null || section.Tab != LibraryTab.All)
                {
                    continue;
                }

                _railSectionButtons.Add(RailButton(section.Icon, section.Title, () => ExpandTo(section)).gameObject);
            }
        }

        private Button RailButton(string glyph, string tooltip, UnityEngine.Events.UnityAction onClick)
        {
            Button button = _ui.IconButton(tooltip, _rail, glyph, tooltip, onClick, 34f, 16f);
            ((RectTransform)button.transform).sizeDelta = new Vector2(34f, 34f);
            return button;
        }

        private Section AddSection(string title, string key, string icon, bool expanded, LibraryTab tab = LibraryTab.All)
        {
            var section = new Section(this, _content, title, "section:" + key, icon, expanded, isCategory: false, tab);
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
                _busSection.Items.Remove(_busItems[i]);
                _displaySection.Items.Remove(_busItems[i]);
                RemoveItem(_busItems[i]);
            }

            _busItems.Clear();
            int w = _busWidth;
            string bits = w.ToString(CultureInfo.InvariantCulture) + "-bit";
            AddBusItem(ComponentRef.InputBus(w), "Bus Input", bits + " switches", Icons.Switch);
            AddBusItem(ComponentRef.OutputBus(w), "Bus Output", bits + " display", Icons.Lamp);
            AddBusItem(ComponentRef.Splitter(w), "Splitter", bits + " bus to single bits", Icons.Bus);
            AddBusItem(ComponentRef.Merger(w), "Merger", "single bits to " + bits + " bus", Icons.Bus);
            AddBusItem(ComponentRef.BusJoin(w, _joinInputs), "Bus Join", _joinInputs + " drivers onto one " + bits + " bus",
                Icons.Bus);
            AddBusItem(ComponentRef.TriState(w), "Tri-state", bits + " driver with enable", Icons.Zap);
            AddBusItem(ComponentRef.TriState(1), "Tri-state (1-bit)", "driver with enable", Icons.Zap);

            // Displays: LED and seven-segment have fixed single-bit inputs; the others follow the bus width.
            AddDisplayItem(ComponentRef.Display(DisplayStyle.Led), "LED", "lamp, lit while HIGH", Icons.Lamp);
            AddDisplayItem(ComponentRef.Display(DisplayStyle.SevenSegment), "7-Segment", "one digit, inputs a-g and dp",
                Icons.Hash);
            AddDisplayItem(ComponentRef.Display(DisplayStyle.Binary, w), "Binary Display", bits + " as lamps", Icons.Binary);
            AddDisplayItem(ComponentRef.Display(DisplayStyle.Hex, w), "Hex Display", bits + " as hex digits", Icons.Hash);
            AddDisplayItem(ComponentRef.Display(DisplayStyle.Decimal, w), "Decimal Display", bits + " unsigned number",
                Icons.Monitor);
            AddDisplayItem(ComponentRef.Display(DisplayStyle.SignedDecimal, w), "Signed Display",
                bits + " two's complement number", Icons.Monitor);
            _busSection.SetCount(7);
            _displaySection.SetCount(6);
            RebuildShortcuts();
            RefreshArmed();
            SetFilter(_filter);
        }

        private void AddBusItem(ComponentRef component, string title, string detail, string glyph)
        {
            LibraryItemView item = AddEntry(_busSection, _busRoot, new PlacementRequest(component, title), title, detail,
                glyph, LibraryTileStyle.Icon, null, "bus wiring");
            _busItems.Add(item);
        }

        private void AddDisplayItem(ComponentRef component, string title, string detail, string glyph)
        {
            LibraryItemView item = AddEntry(_displaySection, _displayRoot, new PlacementRequest(component, title), title,
                detail, glyph, LibraryTileStyle.Icon, null, "display output led seven segment hex decimal binary");
            _busItems.Add(item);
        }

        private LibraryItemView AddEntry(Section section, Transform parent, PlacementRequest request, string title,
            string detail, string tile, LibraryTileStyle style, ComponentSignature signature, string searchExtra)
        {
            if (signature == null)
            {
                _catalog.TryGetSignature(request.Component, out signature);
            }

            var spec = new LibraryEntrySpec
            {
                Request = request,
                Key = KeyOf(request),
                Title = title,
                Detail = detail,
                Description = detail,
                Category = string.Empty,
                Tile = tile,
                Style = style,
                ShowDetail = false,
                SearchExtra = searchExtra + " " + detail + (signature != null ? " " + PinNames(signature) : string.Empty),
                Inputs = signature?.Inputs.Count ?? 0,
                Outputs = signature?.Outputs.Count ?? 0,
            };
            return AddItem(section, parent, spec);
        }

        private LibraryItemView AddItem(Section section, Transform parent, LibraryEntrySpec spec)
        {
            LibraryItemView item = LibraryItemView.Create(parent, _ui, _workspace, spec, indent: section.RowIndent);
            Wire(item);
            item.HomeIndex = item.transform.GetSiblingIndex();
            item.SetPinned(_preferences.IsPinned(spec.Key));
            _items.Add(item);
            _byKey[spec.Key] = item;
            section.Items.Add(item);
            return item;
        }

        private void Wire(LibraryItemView item)
        {
            item.ContextRequested += OnItemContext;
            item.HoverChanged += OnItemHover;
            item.PinToggleRequested += TogglePin;
            item.Activated += OnItemActivated;
        }

        private void RemoveItem(LibraryItemView item)
        {
            _items.Remove(item);
            if (_byKey.TryGetValue(item.Key, out LibraryItemView current) && current == item)
            {
                _byKey.Remove(item.Key);
            }

            DestroyRow(item);
        }

        private void DestroyRow(LibraryItemView item)
        {
            if (_preview != null && _preview.Source == item)
            {
                _preview.Release(null);
            }

            // Detach before destroying: Destroy is deferred, and a dead row must not count in layouts or sibling order.
            item.gameObject.SetActive(false);
            item.transform.SetParent(null, false);
            Destroy(item.gameObject);
        }

        private void OnItemContext(LibraryItemView item, Vector2 screen)
        {
            ItemContextRequested?.Invoke(item.Request, screen);
        }

        private void OnItemHover(LibraryItemView item, bool hovered)
        {
            if (_preview == null)
            {
                return;
            }

            if (hovered && !_workspace.IsPlacing)
            {
                _keyboardPreview = false;
                _preview.Request(item, _rect);
            }
            else if (!hovered)
            {
                _preview.Release(item);
            }
        }

        private void OnItemActivated(LibraryItemView item)
        {
            _preview?.Release(null);
        }

        private void RefreshArmed()
        {
            PlacementRequest? placement = _workspace.Placement;
            bool placing = placement.HasValue;
            ComponentRef component = placing ? placement.Value.Component : default;
            SetArmed(_items, placing, component);
            SetArmed(_pinnedItems, placing, component);
            SetArmed(_recentItems, placing, component);
            if (placing)
            {
                _preview?.Release(null);
            }
        }

        private static void SetArmed(List<LibraryItemView> items, bool placing, ComponentRef component)
        {
            for (int i = 0; i < items.Count; i++)
            {
                items[i].SetArmed(placing && items[i].Request.Component == component);
            }
        }

        private void UpdateEmptyStates()
        {
            if (_customEmpty == null)
            {
                return;
            }

            // While searching, an empty "My Gates" simply disappears with the other sections that have no match.
            _customEmpty.SetActive(_customItems.Count == 0 && _filter.Length == 0);
            _favoritesEmpty.SetActive(_pinnedItems.Count == 0);
            _recentEmpty.SetActive(_recentItems.Count == 0);
            _recentBar.SetActive(_recentItems.Count > 0 && _filter.Length == 0);
        }

        private void LateUpdate()
        {
            bool focused = _search != null && _search.isFocused;
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null)
            {
                if (_searchWasFocused && keyboard.escapeKey.wasPressedThisFrame)
                {
                    // First Escape clears the search and keeps typing; a second one leaves the field.
                    if (HandleEscape())
                    {
                        focused = true;
                    }
                }
                else if (focused)
                {
                    if (keyboard.downArrowKey.wasPressedThisFrame)
                    {
                        MoveHighlight(1);
                    }
                    else if (keyboard.upArrowKey.wasPressedThisFrame)
                    {
                        MoveHighlight(-1);
                    }
                }
            }

            if (focused != _searchWasFocused)
            {
                _footer.gameObject.SetActive(focused);
                UiFactory.Stretch(_scrollRect, 2f, 2f, HeaderHeight, focused ? FooterHeight + 6f : 4f);
                if (!focused && _filter.Length == 0)
                {
                    SetHighlight(-1, false);
                }

                if (!focused)
                {
                    HideKeyboardPreview();
                }
            }

            _searchWasFocused = focused;
        }

        private void OnLibraryChanged(CircuitDefinition definition)
        {
            _preview?.ClearCache();
            RefreshCustomGates();
        }

        private void OnDestroy()
        {
            DOTween.Kill(this);
            if (_library != null)
            {
                _library.DefinitionAdded -= OnLibraryChanged;
                _library.DefinitionRemoved -= OnLibraryChanged;
                _library.DefinitionUpdated -= OnLibraryChanged;
            }

            if (_workspace != null)
            {
                _workspace.SettingsChanged -= OnWorkspaceSettingsChanged;
                _workspace.PlacementChanged -= RefreshArmed;
                _workspace.PlacementCommitted -= OnPlacementCommitted;
            }

            if (_preferences != null)
            {
                _preferences.EntriesChanged -= RebuildShortcuts;
            }
        }

        /// <summary>Rebuilds the custom gate entries when the version display setting changes.</summary>
        private void OnWorkspaceSettingsChanged()
        {
            if (_workspace.ShowGateVersions != _builtWithVersions)
            {
                _preview?.ClearCache();
                RefreshCustomGates();
            }
        }

        private static int ParseOr(string text, int fallback)
        {
            return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) ? value : fallback;
        }

        private static string PinNames(ComponentSignature signature)
        {
            var builder = new System.Text.StringBuilder();
            for (int i = 0; i < signature.Inputs.Count; i++)
            {
                builder.Append(signature.Inputs[i].Name).Append(' ');
            }

            for (int i = 0; i < signature.Outputs.Count; i++)
            {
                builder.Append(signature.Outputs[i].Name).Append(' ');
            }

            return builder.ToString();
        }

        private static string Escape(string text)
        {
            return text.IndexOf('<') < 0 ? text : "<noparse>" + text + "</noparse>";
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

        public static string CategoryOf(CircuitDefinition definition)
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

        private static int CompareForSearch(LibraryItemView a, LibraryItemView b)
        {
            int rankA = a.Rank < 0 ? int.MaxValue : a.Rank;
            int rankB = b.Rank < 0 ? int.MaxValue : b.Rank;
            return rankA != rankB ? rankA.CompareTo(rankB) : a.HomeIndex.CompareTo(b.HomeIndex);
        }

        private static int CompareHome(LibraryItemView a, LibraryItemView b)
        {
            return a.HomeIndex.CompareTo(b.HomeIndex);
        }

        /// <summary>
        /// Collapsible group: a header row (rotating chevron, title, count pill, optional action) and a body holding
        /// entries and/or nested category sections. The open/closed choice is stored in <see cref="UiPreferences"/>;
        /// searching temporarily expands sections with matches and hides the rest.
        /// </summary>
        private sealed class Section
        {
            private readonly LibraryPanel _panel;
            private readonly RectTransform _chevron;
            private readonly TextMeshProUGUI _count;
            private readonly Image _countPill;
            private readonly bool _isCategory;
            private bool _expanded;
            private bool _searching;

            public Section(LibraryPanel panel, Transform parent, string title, string key, string icon, bool expanded,
                bool isCategory, LibraryTab tab = LibraryTab.All)
            {
                _panel = panel;
                _isCategory = isCategory;
                Tab = tab;
                HeaderHidden = tab != LibraryTab.All;
                RowIndent = HeaderHidden ? 0f : isCategory ? CategoryIndent : SectionIndent;
                UiFactory ui = panel._ui;
                Title = title;
                Key = key;
                Icon = icon;
                _expanded = panel._preferences.IsSectionExpanded(key, expanded);

                Image header = ui.Panel("Section " + title, parent, Color.clear, rounded: true);
                Header = header.rectTransform;
                header.gameObject.AddComponent<LayoutElement>().preferredHeight = isCategory ? 28f : 32f;
                var button = header.gameObject.AddComponent<Button>();
                button.targetGraphic = header;
                Color hover = ui.Theme.ButtonHover;
                ColorBlock colors = button.colors;
                colors.normalColor = new Color(hover.r, hover.g, hover.b, 0f);
                colors.highlightedColor = new Color(hover.r, hover.g, hover.b, 0.55f);
                colors.pressedColor = ui.Theme.ButtonPressed;
                colors.selectedColor = colors.normalColor;
                colors.fadeDuration = 0.1f;
                header.color = Color.white;
                UiFactory.SetColorsInstant(button, colors);
                button.navigation = new Navigation { mode = Navigation.Mode.None };
                button.onClick.AddListener(Toggle);
                header.gameObject.AddComponent<PointerClickRelay>().Clicked = OnHeaderClicked;

                // Tree grid: the chevron sits left of the title, and rows start under the title.
                float indent = isCategory ? CategoryIndent - SectionIndent : 0f;
                TextMeshProUGUI chevron = ui.Icon("Chevron", Header, Icons.ChevronDown, isCategory ? 11f : 12f,
                    ui.Theme.TextMuted);
                _chevron = chevron.rectTransform;
                _chevron.anchorMin = _chevron.anchorMax = new Vector2(0f, 0.5f);
                _chevron.pivot = new Vector2(0.5f, 0.5f);
                _chevron.sizeDelta = new Vector2(16f, 16f);
                _chevron.anchoredPosition = new Vector2(indent + 13f, isCategory ? 0f : -2f);
                TitleLeft = LibraryItemView.RowPadding + (isCategory ? CategoryIndent : SectionIndent);

                TextMeshProUGUI label = isCategory
                    ? ui.Text("Title", Header, title, 12f, ui.Theme.TextSecondary, TextAlignmentOptions.MidlineLeft,
                        FontStyles.Bold)
                    : ui.Text("Title", Header, title.ToUpperInvariant(), 10.5f, ui.Theme.TextMuted,
                        TextAlignmentOptions.MidlineLeft, FontStyles.Bold);
                if (!isCategory)
                {
                    label.characterSpacing = 8f;
                }

                UiFactory.Stretch(label.rectTransform, TitleLeft, isCategory ? 40f : 74f, isCategory ? 0f : 6f, 0f);

                _countPill = ui.Panel("Count", Header, ui.Theme.ButtonNormal, rounded: true, radius: 8f);
                _countPill.raycastTarget = false;
                RectTransform pill = _countPill.rectTransform;
                pill.anchorMin = pill.anchorMax = new Vector2(1f, 0.5f);
                pill.pivot = new Vector2(1f, 0.5f);
                pill.sizeDelta = new Vector2(22f, 16f);
                pill.anchoredPosition = new Vector2(-8f, isCategory ? 0f : -2f);
                _count = ui.Text("Text", pill, string.Empty, 10f, ui.Theme.TextMuted, TextAlignmentOptions.Center);
                UiFactory.Stretch(_count.rectTransform);

                Body = UiFactory.Column("Body " + title, parent, 1f, new RectOffset(0, 0, 0, isCategory ? 2 : 6));
                Apply(instant: true);
            }

            public string Title { get; }

            public string Key { get; }

            public string Icon { get; }

            public RectTransform Header { get; private set; }

            public RectTransform Body { get; private set; }

            public Section Parent { get; set; }

            public List<LibraryItemView> Items { get; } = new List<LibraryItemView>();

            public List<Section> Children { get; } = new List<Section>();

            /// <summary>Tab the section belongs to; it is hidden on the other tabs.</summary>
            public LibraryTab Tab { get; }

            /// <summary>Show only the body (the Favorites and Recent lists).</summary>
            public bool HeaderHidden { get; }

            /// <summary>Indent of the rows in this section.</summary>
            public float RowIndent { get; }

            /// <summary>Left edge of the title text in header coordinates.</summary>
            public float TitleLeft { get; }

            public Action<Section, Vector2> HeaderRightClicked { get; set; }

            public void SetCount(int count)
            {
                _count.text = count > 0 ? count.ToString(CultureInfo.InvariantCulture) : string.Empty;
                _countPill.gameObject.SetActive(count > 0);
                _countPill.rectTransform.sizeDelta = new Vector2(Mathf.Max(20f, _count.GetPreferredValues(_count.text).x + 12f),
                    16f);
            }

            public void SetExpanded(bool expanded)
            {
                if (_expanded == expanded)
                {
                    return;
                }

                _expanded = expanded;
                _panel._preferences.SetSectionExpanded(Key, expanded);
                Apply(instant: false);
                Parent?.Apply(instant: true);
                _panel.RebuildNavigation();
            }

            /// <summary>Applies the filter state to this section and its categories (children first).</summary>
            public void ApplyFilter(bool searching)
            {
                _searching = searching;
                for (int i = 0; i < Children.Count; i++)
                {
                    Children[i].ApplyFilter(searching);
                }

                Apply(instant: true);
            }

            /// <summary>Orders entries by search rank (or back to their home order) in this section and its categories.</summary>
            public void Reorder(bool searching, List<LibraryItemView> scratch)
            {
                for (int i = 0; i < Children.Count; i++)
                {
                    Children[i].Reorder(searching, scratch);
                }

                if (Items.Count < 2)
                {
                    return;
                }

                int first = int.MaxValue;
                for (int i = 0; i < Items.Count; i++)
                {
                    first = Mathf.Min(first, Items[i].transform.GetSiblingIndex());
                }

                Items.Sort(searching ? (Comparison<LibraryItemView>)CompareForSearch : CompareHome);
                for (int i = 0; i < Items.Count; i++)
                {
                    Items[i].transform.SetSiblingIndex(first + i);
                }
            }

            /// <summary>Visible entries in visual order.</summary>
            public void CollectNavigable(List<LibraryItemView> results)
            {
                if (!Body.gameObject.activeInHierarchy)
                {
                    return;
                }

                for (int i = 0; i < Items.Count; i++)
                {
                    if (Items[i].gameObject.activeInHierarchy)
                    {
                        results.Add(Items[i]);
                    }
                }

                for (int i = 0; i < Children.Count; i++)
                {
                    Children[i].CollectNavigable(results);
                }
            }

            public void Destroy()
            {
                DestroyRect(Header);
                DestroyRect(Body);
                Header = null;
                Body = null;

                static void DestroyRect(RectTransform rect)
                {
                    if (rect != null)
                    {
                        rect.gameObject.SetActive(false);
                        rect.SetParent(null, false);
                        UnityEngine.Object.Destroy(rect.gameObject);
                    }
                }
            }

            private bool AnyVisible()
            {
                for (int i = 0; i < Items.Count; i++)
                {
                    if (Items[i].gameObject.activeSelf)
                    {
                        return true;
                    }
                }

                for (int i = 0; i < Children.Count; i++)
                {
                    if (Children[i].AnyVisible())
                    {
                        return true;
                    }
                }

                return false;
            }

            private void Toggle()
            {
                if (_searching)
                {
                    return;
                }

                SetExpanded(!_expanded);
            }

            private void OnHeaderClicked(UnityEngine.EventSystems.PointerEventData eventData)
            {
                if (eventData.button == UnityEngine.EventSystems.PointerEventData.InputButton.Right)
                {
                    HeaderRightClicked?.Invoke(this, eventData.position);
                }
            }

            private void Apply(bool instant)
            {
                if (Header == null)
                {
                    return;
                }

                LibraryTab tab = Parent != null ? Parent.Tab : Tab;
                if (tab != _panel._tab)
                {
                    Header.gameObject.SetActive(false);
                    Body.gameObject.SetActive(false);
                    return;
                }

                if (HeaderHidden)
                {
                    Header.gameObject.SetActive(false);
                    Body.gameObject.SetActive(true);
                    return;
                }

                bool open;
                if (_searching)
                {
                    bool any = AnyVisible();
                    Header.gameObject.SetActive(any);
                    Body.gameObject.SetActive(any);
                    open = true;
                }
                else
                {
                    Header.gameObject.SetActive(true);
                    Body.gameObject.SetActive(_expanded);
                    open = _expanded;
                }

                float angle = open ? 0f : 90f;
                _chevron.DOKill();
                if (instant || !_panel.Animate || !_chevron.gameObject.activeInHierarchy)
                {
                    _chevron.localEulerAngles = new Vector3(0f, 0f, angle);
                }
                else
                {
                    _chevron.DOLocalRotate(new Vector3(0f, 0f, angle), 0.16f).SetEase(Ease.OutCubic).SetUpdate(true);
                }
            }
        }
    }
}
