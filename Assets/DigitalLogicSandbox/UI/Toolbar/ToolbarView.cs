using DigitalLogicSandbox.CircuitEditor.Workspace;
using DigitalLogicSandbox.Simulation.Engine;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DigitalLogicSandbox.UI.Toolbar
{
    /// <summary>
    /// Top of the screen, as floating cards over the workspace:
    /// <list type="bullet">
    /// <item>Project pill (top-left): file menu (New, Open, Save, Save As) and the project name; a soft dot marks
    /// unsaved changes.</item>
    /// <item>Tool bar (top-centre): icon buttons with tooltips for history, simulation, gate creation, analysis and
    /// view settings. Selection tools (Inspect, Delete) appear only while they apply.</item>
    /// <item>Window controls (top-right): minimize and exit. The app runs borderless fullscreen, so these replace
    /// the title bar buttons.</item>
    /// <item>View cluster (bottom-right): zoom, frame and grid.</item>
    /// </list>
    /// Every button calls the workspace API. Enabled states follow the undo history, the selection and the
    /// simulation state.
    /// </summary>
    public sealed class ToolbarView : MonoBehaviour
    {
        private WorkspaceController _workspace;
        private UiFactory _ui;
        private Button _undo;
        private Button _redo;
        private Button _delete;
        private Button _playPause;
        private TextMeshProUGUI _playPauseIcon;
        private Button _step;
        private Button _resetSimulation;
        private TextMeshProUGUI _title;
        private string _titleText = "Untitled";
        private Button _createGate;
        private Button _inspect;
        private Button _truthTable;
        private Button _menu;
        private Button _signals;
        private Button _properties;
        private RectTransform _selectionGroup;
        private FileMenu _fileMenu;

        /// <summary>Project buttons: New, Open, Save, Save As (items of the file menu).</summary>
        public event System.Action NewRequested;

        /// <summary>File menu: add the example gate pack to the library.</summary>
        public event System.Action ExampleGatesRequested;

        public event System.Action CpuDemoRequested;

        public event System.Action OpenRequested;

        public event System.Action SaveRequested;

        public event System.Action SaveAsRequested;

        /// <summary>Raised by the "Signals" button (signal visualization settings).</summary>
        public event System.Action SignalSettingsRequested;

        /// <summary>Raised by the "Create Gate" button.</summary>
        public event System.Action CreateGateRequested;

        /// <summary>"Truth Table" button: generate the truth table of the shown circuit.</summary>
        public event System.Action TruthTableRequested;

        /// <summary>"Properties" button: show or hide the properties panel.</summary>
        public event System.Action PropertiesRequested;

        /// <summary>Top-right exit button and File > Exit: close the application (the shell asks about unsaved work).</summary>
        public event System.Action ExitRequested;

        /// <summary>Top-right minimize button.</summary>
        public event System.Action MinimizeRequested;

        /// <summary>Top-right exit button.</summary>
        public Button ExitButton { get; private set; }

        /// <summary>Top-right minimize button.</summary>
        public Button MinimizeButton { get; private set; }

        /// <summary>"Exit" item at the bottom of the file menu.</summary>
        public Button ExitMenuButton { get; private set; }

        /// <summary>The top-right minimize / exit card.</summary>
        public RectTransform WindowControls { get; private set; }

        public Button CreateGateButton => _createGate;

        public Button NewButton { get; private set; }

        public Button OpenButton { get; private set; }

        public Button SaveButton { get; private set; }

        public Button SaveAsButton { get; private set; }

        public Button ExampleGatesButton { get; private set; }

        public Button CpuDemoButton { get; private set; }

        /// <summary>Opens the file menu.</summary>
        public Button MenuButton => _menu;

        public Button SignalsButton => _signals;

        public Button PropertiesButton => _properties;

        /// <summary>Project name with a "*" while there are unsaved changes.</summary>
        public string TitleText => _titleText;

        public Button InspectButton => _inspect;

        public Button TruthTableButton => _truthTable;

        public Button UndoButton => _undo;

        public Button RedoButton => _redo;

        public Button DeleteButton => _delete;

        public Button PlayPauseButton => _playPause;

        public Button StepButton => _step;

        public FileMenu FileMenu => _fileMenu;

        public static ToolbarView Create(Transform parent, UiFactory ui, WorkspaceController workspace)
        {
            // Transparent strip across the top: hosts the cards and defines the top edge of the free workspace.
            RectTransform rect = UiFactory.CreateRect("Toolbar", parent);
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.sizeDelta = new Vector2(0f, UiFactory.Margin + UiFactory.ToolbarHeight);
            rect.anchoredPosition = Vector2.zero;

            var view = rect.gameObject.AddComponent<ToolbarView>();
            view._workspace = workspace;
            view._ui = ui;

            view.BuildProjectPill(rect);
            view.BuildTools(rect);
            view.BuildWindowControls(rect);
            view.BuildViewCluster(parent);
            view._fileMenu = FileMenu.Create(parent, ui, view._menu.transform as RectTransform);
            view.NewButton = view._fileMenu.AddItem(Icons.FilePlus, "New project", "Ctrl+N", () => view.NewRequested?.Invoke());
            view.OpenButton = view._fileMenu.AddItem(Icons.FolderOpen, "Open…", "Ctrl+O", () => view.OpenRequested?.Invoke());
            view._fileMenu.AddSeparator();
            view.SaveButton = view._fileMenu.AddItem(Icons.Save, "Save", "Ctrl+S", () => view.SaveRequested?.Invoke());
            view.SaveAsButton = view._fileMenu.AddItem(Icons.SaveAll, "Save as…", "Ctrl+Shift+S",
                () => view.SaveAsRequested?.Invoke());
            view._fileMenu.AddSeparator();
            view.ExampleGatesButton = view._fileMenu.AddItem(Icons.PackagePlus, "Add example gates", string.Empty,
                () => view.ExampleGatesRequested?.Invoke());
            view.CpuDemoButton = view._fileMenu.AddItem(Icons.Cpu, "Open CPU demo", string.Empty,
                () => view.CpuDemoRequested?.Invoke());
            view._fileMenu.AddSeparator();
            view.ExitMenuButton = view._fileMenu.AddItem(Icons.Close, "Exit", "Alt+F4",
                () => view.ExitRequested?.Invoke());

            workspace.StatusChanged += view.Refresh;
            view.Refresh();
            return view;
        }

        private void BuildProjectPill(RectTransform strip)
        {
            Image card = _ui.Card("Project", strip, _ui.Theme.PanelBackground);
            RectTransform rect = card.rectTransform;
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(UiFactory.Margin, -UiFactory.Margin);
            rect.sizeDelta = new Vector2(0f, UiFactory.ToolbarHeight);
            UiFactory.Row(rect, 6f, new RectOffset(5, 16, 5, 5));
            rect.gameObject.AddComponent<ContentSizeFitter>().horizontalFit = ContentSizeFitter.FitMode.PreferredSize;

            _menu = _ui.IconButton("Menu", rect, Icons.Menu, "Project", () => _fileMenu.Toggle());
            _title = _ui.Text("Title", rect, "Untitled", 14f, _ui.Theme.TextPrimary, TextAlignmentOptions.MidlineLeft,
                FontStyles.Bold);
            _title.richText = true;
            var titleLayout = _title.gameObject.AddComponent<LayoutElement>();
            titleLayout.preferredWidth = 120f;
            SetProjectTitle("Untitled", false);
        }

        private void BuildTools(RectTransform strip)
        {
            Image card = _ui.Card("Tools", strip, _ui.Theme.PanelBackground);
            RectTransform rect = card.rectTransform;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = new Vector2(0f, -UiFactory.Margin);
            rect.sizeDelta = new Vector2(0f, UiFactory.ToolbarHeight);
            UiFactory.Row(rect, 2f, new RectOffset(5, 5, 5, 5), TextAnchor.MiddleCenter);
            rect.gameObject.AddComponent<ContentSizeFitter>().horizontalFit = ContentSizeFitter.FitMode.PreferredSize;

            _undo = _ui.IconButton("Undo", rect, Icons.Undo, "Undo  <color=#687088>Ctrl+Z</color>", () => _workspace.Undo());
            _redo = _ui.IconButton("Redo", rect, Icons.Redo, "Redo  <color=#687088>Ctrl+Y</color>", () => _workspace.Redo());
            Divider(rect);

            _playPause = _ui.IconButton("PlayPause", rect, Icons.Pause, "Pause simulation", _workspace.TogglePlayPause);
            _playPauseIcon = _playPause.transform.Find("Icon").GetComponent<TextMeshProUGUI>();
            _step = _ui.IconButton("Step", rect, Icons.Step, "Step once", () => _workspace.Step());
            _resetSimulation = _ui.IconButton("ResetSimulation", rect, Icons.Reset, "Reset simulation",
                _workspace.ResetSimulation);
            Divider(rect);

            _createGate = _ui.IconButton("CreateGate", rect, Icons.PackagePlus, "Create custom gate",
                () => CreateGateRequested?.Invoke());
            _truthTable = _ui.IconButton("TruthTable", rect, Icons.Table, "Truth table",
                () => TruthTableRequested?.Invoke());
            Divider(rect);

            _signals = _ui.IconButton("Signals", rect, Icons.Sparkles, "View settings",
                () => SignalSettingsRequested?.Invoke());
            _properties = _ui.IconButton("Properties", rect, Icons.Sliders, "Circuit properties",
                () => PropertiesRequested?.Invoke());

            // Selection tools: only present while something is selected.
            _selectionGroup = UiFactory.CreateRect("Selection Tools", rect);
            UiFactory.Row(_selectionGroup, 2f, new RectOffset(0, 0, 0, 0), TextAnchor.MiddleCenter);
            Divider(_selectionGroup);
            _inspect = _ui.IconButton("Inspect", _selectionGroup, Icons.Inspect, "Open internal circuit",
                () => _workspace.OpenSelected());
            _delete = _ui.IconButton("Delete", _selectionGroup, Icons.Trash, "Delete  <color=#687088>Del</color>",
                () => _workspace.DeleteSelection());
        }

        /// <summary>Top-right card: minimize and exit, where a window's title bar buttons would be.</summary>
        private void BuildWindowControls(RectTransform strip)
        {
            Image card = _ui.Card("Window Controls", strip, _ui.Theme.PanelBackground);
            RectTransform rect = card.rectTransform;
            rect.anchorMin = rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(1f, 1f);
            rect.anchoredPosition = new Vector2(-UiFactory.Margin, -UiFactory.Margin);
            rect.sizeDelta = new Vector2(0f, UiFactory.ToolbarHeight);
            UiFactory.Row(rect, 2f, new RectOffset(5, 5, 5, 5), TextAnchor.MiddleCenter);
            rect.gameObject.AddComponent<ContentSizeFitter>().horizontalFit = ContentSizeFitter.FitMode.PreferredSize;

            MinimizeButton = _ui.IconButton("Minimize", rect, Icons.Minus, "Minimize",
                () => MinimizeRequested?.Invoke());
            ExitButton = _ui.IconButton("Exit", rect, Icons.Close, "Exit  <color=#687088>Alt+F4</color>",
                () => ExitRequested?.Invoke());

            // Exit uses the error colour on hover / press so it reads as "close", like a window's close button.
            Color error = _ui.Theme.Error;
            ColorBlock colors = ExitButton.colors;
            colors.highlightedColor = new Color(error.r, error.g, error.b, 0.85f);
            colors.pressedColor = new Color(error.r * 0.8f, error.g * 0.8f, error.b * 0.8f, 1f);
            UiFactory.SetColorsInstant(ExitButton, colors);
            WindowControls = rect;
        }

        private void BuildViewCluster(Transform parent)
        {
            Image card = _ui.Card("View Controls", parent, _ui.Theme.PanelBackground);
            RectTransform rect = card.rectTransform;
            rect.anchorMin = rect.anchorMax = new Vector2(1f, 0f);
            rect.pivot = new Vector2(1f, 0f);
            rect.anchoredPosition = new Vector2(-UiFactory.Margin, UiFactory.Margin);
            rect.sizeDelta = new Vector2(0f, UiFactory.StatusBarHeight + 4f);
            UiFactory.Row(rect, 0f, new RectOffset(4, 4, 4, 4), TextAnchor.MiddleCenter);
            rect.gameObject.AddComponent<ContentSizeFitter>().horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            const float size = 32f;
            _ui.IconButton("ZoomOut", rect, Icons.Minus, "Zoom out", _workspace.ZoomOut, size, 16f);
            _ui.IconButton("ZoomIn", rect, Icons.Plus, "Zoom in", _workspace.ZoomIn, size, 16f);
            _ui.IconButton("ResetCamera", rect, Icons.Frame, "Reset view", _workspace.ResetCamera, size, 15f);
            _ui.IconButton("Grid", rect, Icons.Grid, "Toggle grid",
                () => _workspace.GridVisible = !_workspace.GridVisible, size, 15f);
            ViewCluster = rect;
        }

        /// <summary>The bottom-right zoom / frame / grid card.</summary>
        public RectTransform ViewCluster { get; private set; }

        /// <summary>Hairline between tool groups, with a little breathing room on both sides.</summary>
        private void Divider(Transform parent)
        {
            Pad(parent);
            RectTransform line = _ui.Separator(parent, vertical: true);
            line.GetComponent<LayoutElement>().preferredHeight = 18f;
            Pad(parent);
        }

        private static void Pad(Transform parent)
        {
            UiFactory.CreateRect("Pad", parent).gameObject.AddComponent<LayoutElement>().preferredWidth = 4f;
        }

        /// <summary>Shows the project name; <paramref name="dirty"/> appends "*" (drawn as a soft accent dot).</summary>
        public void SetProjectTitle(string name, bool dirty)
        {
            string shown = string.IsNullOrEmpty(name) ? "Untitled" : name;
            _titleText = shown + (dirty ? " *" : string.Empty);
            _title.text = dirty
                ? shown + "  <color=#" + ColorUtility.ToHtmlStringRGB(_ui.Theme.Accent) + ">•</color>"
                : shown;
            LayoutElement layout = _title.GetComponent<LayoutElement>();
            if (layout != null)
            {
                layout.preferredWidth = Mathf.Clamp(_title.GetPreferredValues(_title.text).x + 2f, 60f, 260f);
            }
        }

        /// <summary>Shows or hides the file menu (no project service in some test setups).</summary>
        public void SetProjectActionsAvailable(bool available)
        {
            _menu.gameObject.SetActive(available);
            NewButton.gameObject.SetActive(available);
            OpenButton.gameObject.SetActive(available);
            SaveButton.gameObject.SetActive(available);
            SaveAsButton.gameObject.SetActive(available);
            CpuDemoButton.gameObject.SetActive(available);
        }

        /// <summary>Highlights the properties button while the properties panel is pinned open.</summary>
        public void SetPropertiesActive(bool active)
        {
            _properties.GetComponent<ButtonVisual>().Active = active;
        }

        /// <summary>Highlights the signals button while the signal settings popup is open.</summary>
        public void SetSignalsActive(bool active)
        {
            _signals.GetComponent<ButtonVisual>().Active = active;
        }

        public void Refresh()
        {
            if (_workspace == null || !_workspace.IsInitialized)
            {
                return;
            }

            _undo.interactable = _workspace.History.CanUndo;
            _redo.interactable = _workspace.History.CanRedo;
            bool hasSelection = !_workspace.Selection.IsEmpty;
            _delete.interactable = hasSelection && _workspace.IsEditable;

            bool simulating = _workspace.Session.IsSimulating;
            bool running = _workspace.Runner.State == SimulationState.Running;
            _playPauseIcon.text = running ? Icons.Pause : Icons.Play;
            TooltipText(_playPause, running ? "Pause simulation" : "Run simulation");
            _playPause.interactable = simulating;
            _step.interactable = simulating && !running;
            _resetSimulation.interactable = simulating;
            _createGate.interactable = _workspace.CanPackage && _workspace.Circuit != null &&
                _workspace.Circuit.Outputs.Count > 0;
            _inspect.interactable = SelectedOpenableNode();
            _inspect.gameObject.SetActive(_inspect.interactable);
            _delete.gameObject.SetActive(hasSelection);
            _selectionGroup.gameObject.SetActive(hasSelection);
        }

        private static void TooltipText(Button button, string text)
        {
            var trigger = button.GetComponent<Panels.TooltipTrigger>();
            if (trigger != null)
            {
                trigger.Text = text;
            }
        }

        private bool SelectedOpenableNode()
        {
            if (_workspace.Selection.NodeCount != 1)
            {
                return false;
            }

            foreach (Core.Nodes.NodeId node in _workspace.Selection.Nodes)
            {
                return _workspace.CanOpenNode(node);
            }

            return false;
        }

        private void OnDestroy()
        {
            if (_workspace != null)
            {
                _workspace.StatusChanged -= Refresh;
            }
        }
    }
}
