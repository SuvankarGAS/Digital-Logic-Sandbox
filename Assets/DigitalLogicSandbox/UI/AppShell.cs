using System.Collections.Generic;
using DigitalLogicSandbox.CircuitEditor.Placement;
using DigitalLogicSandbox.CircuitEditor.Workspace;
using DigitalLogicSandbox.Components.Abstractions;
using DigitalLogicSandbox.Core.Circuit;
using DigitalLogicSandbox.Core.Nodes;
using DigitalLogicSandbox.GateLibrary.Custom;
using DigitalLogicSandbox.Rendering;
using DigitalLogicSandbox.TruthTable.Generation;
using DigitalLogicSandbox.UI.ContextMenus;
using DigitalLogicSandbox.UI.Dialogs;
using DigitalLogicSandbox.UI.Inspector;
using DigitalLogicSandbox.UI.Panels;
using DigitalLogicSandbox.UI.Toolbar;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace DigitalLogicSandbox.UI
{
    /// <summary>
    /// Screen-space UI floating over a full-bleed workspace: project pill and tool bar (top), breadcrumb pill (only
    /// inside custom gates), gate library (left), properties card (right, only while something is selected or when
    /// pinned), status pill and view controls (bottom), first-run hint, tooltip, context menu and modal dialogs.
    /// The workspace camera renders full screen behind the panels. Pointer input over the panels is filtered out
    /// by the workspace through <see cref="IsPointerOverUi"/>.
    /// </summary>
    public sealed class AppShell : MonoBehaviour
    {
        private const string ProjectExtension = DigitalLogicSandbox.Persistence.ProjectFormat.FileExtension;

        private bool _useNativeFileDialogs;
        private bool _quitConfirmed;

        /// <summary>
        /// Performs the actual quit once the user has confirmed. Defaults to <see cref="Application.Quit()"/>;
        /// tests replace it.
        /// </summary>
        public System.Action QuitAction { get; set; } = Application.Quit;

        private Canvas _canvas;

        public UiFactory Factory { get; private set; }

        public ToolbarView Toolbar { get; private set; }

        public LibraryPanel Library { get; private set; }

        public StatusBarView StatusBar { get; private set; }

        public InspectorPanel Inspector { get; private set; }

        public PackagingDialog PackagingDialog { get; private set; }

        public NavigationBar NavigationBar { get; private set; }

        /// <summary>Shared hover tooltip.</summary>
        public TooltipView Tooltip { get; private set; }

        /// <summary>Guidance shown on an empty canvas.</summary>
        public EmptyStateView EmptyState { get; private set; }

        public ContextMenuView ContextMenu { get; private set; }

        public VersionSaveDialog VersionSaveDialog { get; private set; }

        /// <summary>Assembly editor for gates fed by program words (the CPU demo's ROM).</summary>
        public ProgramEditorDialog ProgramEditor { get; private set; }

        public InstructionSetDialog InstructionSetEditor { get; private set; }

        public SignalSettingsPanel SignalSettings { get; private set; }

        public TruthTableDialog TruthTable { get; private set; }

        /// <summary>Hover card beside the gate library (gate drawing, pins, small truth table).</summary>
        public GatePreviewCard GatePreview { get; private set; }

        /// <summary>Edits a custom gate's name, icon, category and description from the library.</summary>
        public GateDetailsDialog GateDetails { get; private set; }

        /// <summary>One-line text prompt (e.g. renaming a gate category).</summary>
        public TextPromptDialog Prompt { get; private set; }

        /// <summary>Per-user interface state (library width, sections, pinned and recent entries).</summary>
        public UiPreferences Preferences { get; private set; }

        /// <summary>Input/clock sequences for sequential circuits.</summary>
        public SequenceAnalysisDialog SequenceAnalysis { get; private set; }

        /// <summary>Generic question dialog (unsaved changes, recovery).</summary>
        public ConfirmDialog Confirm { get; private set; }

        /// <summary>Open / Save As picker. Null when no project service is attached.</summary>
        public ProjectFileDialog ProjectFiles { get; private set; }

        /// <summary>
        /// Use the operating system's Open / Save As dialogs (Windows) instead of the in-app picker, also for CSV
        /// export. On by default in Windows player builds; off in the editor so tests never block on a modal window.
        /// </summary>
        public bool UseNativeFileDialogs
        {
            get => _useNativeFileDialogs;
            set
            {
                _useNativeFileDialogs = value && NativeFileDialog.IsAvailable;
                if (TruthTable != null)
                {
                    TruthTable.PickExportPath = _useNativeFileDialogs ? PickCsvPath : null;
                    SequenceAnalysis.PickExportPath = _useNativeFileDialogs ? PickCsvPath : null;
                }
            }
        }

        /// <summary>Project file operations, or null (UI-only setups such as some tests).</summary>
        public IProjectService Projects { get; private set; }

        private WorkspaceController _workspace;

        public Canvas Canvas => _canvas;

        public static AppShell Create(VisualTheme theme, WorkspaceController workspace, ComponentCatalog catalog,
            DefinitionLibrary library, TruthTableLimits truthTableLimits = null, IProjectService projects = null,
            UiPreferences preferences = null)
        {
            var go = new GameObject("UI", typeof(RectTransform));
            go.layer = 5;
            var shell = go.AddComponent<AppShell>();
            shell._canvas = go.AddComponent<Canvas>();
            shell._canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            shell._canvas.sortingOrder = 10;
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1600f, 900f);
            scaler.matchWidthOrHeight = 0.5f;
            go.AddComponent<GraphicRaycaster>();

            shell.Factory = new UiFactory(theme);
            shell.EmptyState = EmptyStateView.Create(go.transform, shell.Factory, workspace);
            shell.Preferences = preferences ?? new UiPreferences(new MemoryPreferenceStore());
            shell.Library = LibraryPanel.Create(go.transform, shell.Factory, workspace, catalog, library, shell.Preferences);
            shell.Toolbar = ToolbarView.Create(go.transform, shell.Factory, workspace);
            shell.StatusBar = StatusBarView.Create(go.transform, shell.Factory, workspace);
            shell.Inspector = InspectorPanel.Create(go.transform, shell.Factory, workspace);
            shell.NavigationBar = NavigationBar.Create(go.transform, shell.Factory, workspace);
            shell.SignalSettings = SignalSettingsPanel.Create(go.transform, shell.Factory, workspace,
                shell.NavigationBar);
            shell.ContextMenu = ContextMenuView.Create(go.transform, shell.Factory, workspace);
            shell.PackagingDialog = PackagingDialog.Create(go.transform, shell.Factory, workspace);
            shell.VersionSaveDialog = VersionSaveDialog.Create(go.transform, shell.Factory, workspace);
            shell.ProgramEditor = ProgramEditorDialog.Create(go.transform, shell.Factory, workspace);
            shell.TruthTable = TruthTableDialog.Create(go.transform, shell.Factory, workspace, truthTableLimits);
            shell.SequenceAnalysis = SequenceAnalysisDialog.Create(go.transform, shell.Factory, workspace);
            shell.InstructionSetEditor = InstructionSetDialog.Create(go.transform, shell.Factory, workspace,
                null);
            shell.Confirm = ConfirmDialog.Create(go.transform, shell.Factory, workspace);
            shell.GateDetails = GateDetailsDialog.Create(go.transform, shell.Factory, workspace);
            shell.Prompt = TextPromptDialog.Create(go.transform, shell.Factory, workspace);
            shell.InstructionSetEditor.AttachConfirm(shell.Confirm);
            shell.ProgramEditor.Attach(shell.ContextMenu, shell.InstructionSetEditor);
            shell._workspace = workspace;
            workspace.VisibleScreenArea = shell.GetWorkspaceScreenRect;
            shell.AttachProjects(projects);
            shell.UseNativeFileDialogs = NativeFileDialog.DefaultEnabled;
            shell.GatePreview = GatePreviewCard.Create(go.transform, shell.Factory, workspace,
                () => shell.TruthTable != null ? shell.TruthTable.Generator : null);
            shell.Library.AttachPreview(shell.GatePreview);
            shell.Tooltip = TooltipView.Create(go.transform, shell.Factory);
            shell.Factory.Tooltip = shell.Tooltip;

            shell.Toolbar.SignalSettingsRequested += shell.SignalSettings.Toggle;
            shell.SignalSettings.OpenChanged += shell.Toolbar.SetSignalsActive;
            shell.Toolbar.PropertiesRequested += () => shell.Inspector.Pinned = !shell.Inspector.Pinned;
            shell.Inspector.PinnedChanged += shell.Toolbar.SetPropertiesActive;
            shell.Toolbar.CreateGateRequested += () => shell.PackagingDialog.Open(selectionOnly: false);
            shell.Inspector.PackageRequested += selectionOnly => shell.PackagingDialog.Open(selectionOnly);
            shell.ContextMenu.PackageRequested += selectionOnly => shell.PackagingDialog.Open(selectionOnly);
            shell.Inspector.SaveDraftRequested += shell.VersionSaveDialog.Open;
            shell.NavigationBar.SaveRequested += shell.VersionSaveDialog.Open;
            shell.Toolbar.TruthTableRequested += shell.TruthTable.OpenForCurrentCircuit;
            shell.Toolbar.ExampleGatesRequested += () => workspace.LoadExampleGates();
            shell.Inspector.CircuitTruthTableRequested += shell.TruthTable.OpenForCurrentCircuit;
            shell.ContextMenu.CircuitTruthTableRequested += shell.TruthTable.OpenForCurrentCircuit;
            shell.Inspector.NodeTruthTableRequested += node => shell.TruthTable.OpenForNode(node);
            shell.ContextMenu.NodeTruthTableRequested += node => shell.TruthTable.OpenForNode(node);
            shell.ContextMenu.ComponentTruthTableRequested += (component, _) => shell.TruthTable.OpenForComponent(component);
            shell.ContextMenu.ProgramEditRequested += node => shell.ProgramEditor.Open(node);
            shell.Inspector.ProgramEditRequested += node => shell.ProgramEditor.Open(node);
            shell.TruthTable.SequenceAnalysisRequested += shell.SequenceAnalysis.Open;
            shell.Inspector.SequenceAnalysisRequested += shell.SequenceAnalysis.OpenForCurrentCircuit;
            shell.Library.ItemContextRequested += shell.ShowLibraryEntryMenu;
            shell.Library.CategoryContextRequested += shell.ShowCategoryMenu;
            shell.Toolbar.ExitRequested += shell.RequestExit;
            shell.Toolbar.MinimizeRequested += AppWindow.Minimize;
            Application.wantsToQuit += shell.OnWantsToQuit;
            return shell;
        }

        // ------------------------------------------------------------------------------------------------
        // Project commands (toolbar, shortcuts)
        // ------------------------------------------------------------------------------------------------

        /// <summary>New project, asking to save unsaved changes first.</summary>
        public void RequestNew()
        {
            if (Projects != null)
            {
                ConfirmDiscard("starting a new project", Projects.NewProject);
            }
        }

        /// <summary>Opens the CPU demo as a new project, asking to save unsaved changes first.</summary>
        public void RequestCpuDemo()
        {
            if (Projects != null)
            {
                ConfirmDiscard("opening the CPU demo", Projects.OpenCpuDemo);
            }
        }

        /// <summary>Open dialog, asking to save unsaved changes first.</summary>
        public void RequestOpen()
        {
            if (Projects != null)
            {
                ConfirmDiscard("opening another project", () => ShowProjectFiles(ProjectFileDialogMode.Open));
            }
        }

        /// <summary>Closes the application, asking to save unsaved changes first.</summary>
        public void RequestExit()
        {
            if (Projects == null)
            {
                QuitNow();
                return;
            }

            ConfirmDiscard("exiting", QuitNow);
        }

        private void QuitNow()
        {
            _quitConfirmed = true;
            QuitAction?.Invoke();
        }

        /// <summary>
        /// Alt+F4 and other close requests from the OS: with unsaved changes, cancel the quit and show the same
        /// Save / Don't save / Cancel prompt as the exit button.
        /// </summary>
        private bool OnWantsToQuit()
        {
            if (_quitConfirmed || Projects == null || !Projects.IsDirty)
            {
                return true;
            }

            if (Confirm == null || !Confirm.IsOpen)
            {
                RequestExit();
            }

            return false;
        }

        /// <summary>Saves to the current file, or asks for a name when the project was never saved.</summary>
        public void RequestSave()
        {
            if (Projects == null)
            {
                return;
            }

            if (Projects.CurrentPath == null)
            {
                ShowProjectFiles(ProjectFileDialogMode.SaveAs);
                return;
            }

            Projects.Save(out string message);
            _workspace.PostNotification(message);
        }

        public void RequestSaveAs()
        {
            if (Projects != null)
            {
                ShowProjectFiles(ProjectFileDialogMode.SaveAs);
            }
        }

        /// <summary>
        /// Opens or saves through the native dialog when enabled, otherwise shows the in-app picker.
        /// <paramref name="afterSuccess"/> runs after a successful open or save; cancelling does nothing.
        /// </summary>
        private void ShowProjectFiles(ProjectFileDialogMode mode, System.Action afterSuccess = null)
        {
            bool save = mode == ProjectFileDialogMode.SaveAs;
            if (!TryPickNative(save, save ? "Save project as" : "Open project", NativeFileDialog.ProjectFilter,
                    ProjectFolderOf(Projects.CurrentPath), save ? Projects.DisplayName + ProjectExtension : null,
                    ProjectExtension.Substring(1), out bool cancelled, out string path))
            {
                if (!cancelled)
                {
                    ProjectFiles.Show(mode, afterSuccess);
                }

                return;
            }

            bool succeeded = save ? Projects.SaveAs(path, out string message) : Projects.Open(path, out message);
            if (!string.IsNullOrEmpty(message))
            {
                _workspace.PostNotification(message);
            }

            if (succeeded)
            {
                afterSuccess?.Invoke();
            }
        }

        private string ProjectFolderOf(string path)
        {
            string folder = string.IsNullOrEmpty(path) ? null : System.IO.Path.GetDirectoryName(path);
            return string.IsNullOrEmpty(folder) ? Projects.ProjectsDirectory : folder;
        }

        private string PickCsvPath(string directory, string suggestedName)
        {
            return TryPickNative(true, "Export CSV", NativeFileDialog.CsvFilter, directory, suggestedName, "csv",
                out _, out string path)
                ? path
                : null;
        }

        /// <summary>
        /// Runs the native dialog. Returns true with a path when the user picked one. Returns false with
        /// <paramref name="cancelled"/> set when they cancelled, or with it clear when native dialogs are off or
        /// failed (the caller then falls back to the in-app picker).
        /// </summary>
        private bool TryPickNative(bool save, string title, string filter, string directory, string defaultName,
            string extension, out bool cancelled, out string path)
        {
            cancelled = false;
            path = null;
            if (!_useNativeFileDialogs)
            {
                return false;
            }

            try
            {
                bool picked = save
                    ? NativeFileDialog.TrySave(title, filter, directory, defaultName, extension, out path)
                    : NativeFileDialog.TryOpen(title, filter, directory, out path);
                cancelled = !picked;
                return picked;
            }
            catch (System.Exception exception) when (exception is System.DllNotFoundException ||
                                                     exception is System.EntryPointNotFoundException ||
                                                     exception is System.IO.IOException ||
                                                     exception is System.UnauthorizedAccessException)
            {
                Debug.LogWarning("Native file dialogs are unavailable; using the in-app picker. " + exception.Message);
                UseNativeFileDialogs = false;
                return false;
            }
        }

        private void AttachProjects(IProjectService projects)
        {
            Projects = projects;
            bool available = projects != null;
            Toolbar.SetProjectActionsAvailable(available);
            if (!available)
            {
                Toolbar.SetProjectTitle("Digital Logic Sandbox", false);
                return;
            }

            ProjectFiles = ProjectFileDialog.Create(transform, Factory, _workspace, projects);
            Toolbar.NewRequested += RequestNew;
            Toolbar.OpenRequested += RequestOpen;
            Toolbar.CpuDemoRequested += RequestCpuDemo;
            Toolbar.SaveRequested += RequestSave;
            Toolbar.SaveAsRequested += RequestSaveAs;
            projects.Changed += RefreshProjectTitle;
            RefreshProjectTitle();
        }

        private void RefreshProjectTitle()
        {
            Toolbar.SetProjectTitle(Projects.DisplayName, Projects.IsDirty);
        }

        private void ConfirmDiscard(string action, System.Action proceed)
        {
            if (!Projects.IsDirty)
            {
                proceed();
                return;
            }

            Confirm.Show("Unsaved changes",
                "Save the changes to '" + Projects.DisplayName + "' before " + action + "?",
                "Save", () => SaveThen(proceed),
                "Don't save", proceed,
                "Cancel", null);
        }

        private void SaveThen(System.Action proceed)
        {
            if (Projects.CurrentPath == null)
            {
                ShowProjectFiles(ProjectFileDialogMode.SaveAs, proceed);
                return;
            }

            bool saved = Projects.Save(out string message);
            _workspace.PostNotification(message);
            if (saved)
            {
                proceed();
            }
        }

        // ------------------------------------------------------------------------------------------------
        // Gate library menus
        // ------------------------------------------------------------------------------------------------

        private readonly List<MenuItem> _menuItems = new List<MenuItem>();
        private readonly List<string> _categories = new List<string>();

        /// <summary>
        /// Right-click menu of a library entry: place / truth table / open (built by the menu itself), then favorites and,
        /// for custom gates, rename, details, duplicate and delete.
        /// </summary>
        public void ShowLibraryEntryMenu(PlacementRequest request, Vector2 screen)
        {
            _menuItems.Clear();
            bool pinned = Library.IsPinned(request);
            _menuItems.Add(new MenuItem(pinned ? "Remove from favorites" : "Add to favorites",
                () => Library.SetPinned(request, !pinned)));
            if (request.Component.Kind == ComponentKind.Custom)
            {
                DefinitionId id = request.Component.Definition.Id;
                _menuItems.Add(MenuItem.Separator);
                _menuItems.Add(new MenuItem("Rename…", () => OpenGateDetails(id, focusName: true)));
                _menuItems.Add(new MenuItem("Edit details…", () => OpenGateDetails(id, focusName: false)));
                _menuItems.Add(new MenuItem("Duplicate", () => _workspace.DuplicateCustomGate(id)));
                bool canDelete = _workspace.CanDeleteCustomGate(id, out _);
                _menuItems.Add(new MenuItem(canDelete ? "Delete…" : "Delete… (in use)", () => RequestDeleteGate(id)));
            }

            ContextMenu.ShowForLibraryEntry(screen, request, _menuItems);
        }

        /// <summary>Right-click menu of a custom gate category header.</summary>
        public void ShowCategoryMenu(string category, IReadOnlyList<DefinitionId> gates, Vector2 screen)
        {
            _menuItems.Clear();
            var ids = new List<DefinitionId>(gates);
            _menuItems.Add(new MenuItem("Rename category…", () => RequestRenameCategory(category, ids), ids.Count > 0));
            ContextMenu.ShowItems(screen, _menuItems);
        }

        /// <summary>Opens the gate details dialog for a custom gate.</summary>
        public void OpenGateDetails(DefinitionId id, bool focusName)
        {
            Library.GetCategories(_categories);
            GateDetails.Open(id, _categories, focusName);
        }

        /// <summary>Asks before deleting a custom gate; explains why when it is still in use.</summary>
        public void RequestDeleteGate(DefinitionId id)
        {
            if (!_workspace.Library.TryGetLatest(id, out CircuitDefinition definition))
            {
                return;
            }

            if (!_workspace.CanDeleteCustomGate(id, out string reason))
            {
                Confirm.Show("Can\u2019t delete \u2018" + definition.Name + "\u2019", reason, "OK", null, null, null, null);
                return;
            }

            Confirm.Show("Delete \u2018" + definition.Name + "\u2019?",
                "The gate and all its versions are removed from the library. You can undo this.", "Delete",
                () => _workspace.DeleteCustomGate(id));
        }

        /// <summary>Asks for a new name for a gate category and moves its gates there (one undoable step).</summary>
        public void RequestRenameCategory(string category, IReadOnlyList<DefinitionId> gates)
        {
            string initial = category == LibraryPanel.UncategorizedLabel ? string.Empty : category;
            Prompt.Show("Rename Category", "Every gate in \u2018" + category + "\u2019 moves to the new category.", initial,
                "Rename", name =>
                {
                    string target = string.IsNullOrWhiteSpace(name) ? LibraryPanel.UncategorizedLabel : name;
                    Library.RenameCategoryState(category, target);
                    _workspace.SetCustomGateCategory(gates, name);
                });
        }

        private void Update()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null || _workspace == null || _workspace.InputSuspended || IsTextInputFocused())
            {
                return;
            }

            bool control = keyboard.leftCtrlKey.isPressed || keyboard.rightCtrlKey.isPressed;
            if (!control)
            {
                return;
            }

            if (keyboard.fKey.wasPressedThisFrame)
            {
                Library.FocusSearch();
                return;
            }

            if (Projects == null)
            {
                return;
            }

            bool shift = keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed;
            if (keyboard.sKey.wasPressedThisFrame)
            {
                if (shift)
                {
                    RequestSaveAs();
                }
                else
                {
                    RequestSave();
                }
            }
            else if (keyboard.oKey.wasPressedThisFrame)
            {
                RequestOpen();
            }
            else if (keyboard.nKey.wasPressedThisFrame)
            {
                RequestNew();
            }
        }

        private void OnDestroy()
        {
            Application.wantsToQuit -= OnWantsToQuit;
            if (Projects != null)
            {
                Projects.Changed -= RefreshProjectTitle;
            }
        }

        /// <summary>True while the pointer is over any UI element that blocks raycasts.</summary>
        /// <summary>
        /// Screen-pixel rect of the workspace between the docked panels (library, inspector, toolbar, breadcrumbs,
        /// status bar). Hidden panels do not reduce it.
        /// </summary>
        public Rect GetWorkspaceScreenRect()
        {
            float left = 0f;
            float right = Screen.width;
            float bottom = 0f;
            float top = Screen.height;
            if (Library != null && IsShown(Library))
            {
                left = Mathf.Max(left, ScreenRectOf(Library.transform).xMax);
            }

            if (Inspector != null && IsShown(Inspector))
            {
                right = Mathf.Min(right, ScreenRectOf(Inspector.transform).xMin);
            }

            if (Toolbar != null && IsShown(Toolbar))
            {
                top = Mathf.Min(top, ScreenRectOf(Toolbar.transform).yMin);
            }

            if (NavigationBar != null && IsShown(NavigationBar))
            {
                top = Mathf.Min(top, ScreenRectOf(NavigationBar.transform).yMin);
            }

            if (StatusBar != null && IsShown(StatusBar))
            {
                bottom = Mathf.Max(bottom, ScreenRectOf(StatusBar.transform).yMax);
            }

            if (Toolbar != null && Toolbar.ViewCluster != null && Toolbar.ViewCluster.gameObject.activeInHierarchy)
            {
                bottom = Mathf.Max(bottom, ScreenRectOf(Toolbar.ViewCluster).yMax);
            }

            return Rect.MinMaxRect(left, bottom, Mathf.Max(left, right), Mathf.Max(bottom, top));
        }

        private static bool IsShown(Component panel)
        {
            return panel.gameObject.activeInHierarchy;
        }

        private static readonly Vector3[] Corners = new Vector3[4];

        /// <summary>Screen-pixel rect of a UI element on this screen-space-overlay canvas.</summary>
        private static Rect ScreenRectOf(Transform element)
        {
            var rect = (RectTransform)element;
            rect.GetWorldCorners(Corners);
            return Rect.MinMaxRect(Corners[0].x, Corners[0].y, Corners[2].x, Corners[2].y);
        }

        public static bool IsPointerOverUi()
        {
            EventSystem eventSystem = EventSystem.current;
            return eventSystem != null && eventSystem.IsPointerOverGameObject();
        }

        /// <summary>True while a text field has keyboard focus (workspace shortcuts must not fire).</summary>
        public static bool IsTextInputFocused()
        {
            EventSystem eventSystem = EventSystem.current;
            if (eventSystem == null || eventSystem.currentSelectedGameObject == null)
            {
                return false;
            }

            TMP_InputField field = eventSystem.currentSelectedGameObject.GetComponent<TMP_InputField>();
            return field != null && field.isFocused;
        }
    }
}
