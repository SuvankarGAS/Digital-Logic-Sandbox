using DigitalLogicSandbox.CircuitEditor.Workspace;
using DigitalLogicSandbox.Components.Abstractions;
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

        public SignalSettingsPanel SignalSettings { get; private set; }

        public TruthTableDialog TruthTable { get; private set; }

        /// <summary>Input/clock sequences for sequential circuits.</summary>
        public SequenceAnalysisDialog SequenceAnalysis { get; private set; }

        /// <summary>Generic question dialog (unsaved changes, recovery).</summary>
        public ConfirmDialog Confirm { get; private set; }

        /// <summary>Open / Save As picker. Null when no project service is attached.</summary>
        public ProjectFileDialog ProjectFiles { get; private set; }

        /// <summary>Project file operations, or null (UI-only setups such as some tests).</summary>
        public IProjectService Projects { get; private set; }

        private WorkspaceController _workspace;

        public Canvas Canvas => _canvas;

        public static AppShell Create(VisualTheme theme, WorkspaceController workspace, ComponentCatalog catalog,
            DefinitionLibrary library, TruthTableLimits truthTableLimits = null, IProjectService projects = null)
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
            shell.Library = LibraryPanel.Create(go.transform, shell.Factory, workspace, catalog, library);
            shell.Toolbar = ToolbarView.Create(go.transform, shell.Factory, workspace);
            shell.StatusBar = StatusBarView.Create(go.transform, shell.Factory, workspace);
            shell.Inspector = InspectorPanel.Create(go.transform, shell.Factory, workspace);
            shell.NavigationBar = NavigationBar.Create(go.transform, shell.Factory, workspace);
            shell.SignalSettings = SignalSettingsPanel.Create(go.transform, shell.Factory, workspace);
            shell.ContextMenu = ContextMenuView.Create(go.transform, shell.Factory, workspace);
            shell.PackagingDialog = PackagingDialog.Create(go.transform, shell.Factory, workspace);
            shell.VersionSaveDialog = VersionSaveDialog.Create(go.transform, shell.Factory, workspace);
            shell.TruthTable = TruthTableDialog.Create(go.transform, shell.Factory, workspace, truthTableLimits);
            shell.SequenceAnalysis = SequenceAnalysisDialog.Create(go.transform, shell.Factory, workspace);
            shell.Confirm = ConfirmDialog.Create(go.transform, shell.Factory, workspace);
            shell._workspace = workspace;
            workspace.VisibleScreenArea = shell.GetWorkspaceScreenRect;
            shell.AttachProjects(projects);
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
            shell.Inspector.CircuitTruthTableRequested += shell.TruthTable.OpenForCurrentCircuit;
            shell.ContextMenu.CircuitTruthTableRequested += shell.TruthTable.OpenForCurrentCircuit;
            shell.Inspector.NodeTruthTableRequested += node => shell.TruthTable.OpenForNode(node);
            shell.ContextMenu.NodeTruthTableRequested += node => shell.TruthTable.OpenForNode(node);
            shell.ContextMenu.ComponentTruthTableRequested += (component, _) => shell.TruthTable.OpenForComponent(component);
            shell.TruthTable.SequenceAnalysisRequested += shell.SequenceAnalysis.Open;
            shell.Inspector.SequenceAnalysisRequested += shell.SequenceAnalysis.OpenForCurrentCircuit;
            shell.Library.ItemContextRequested += (request, screen) => shell.ContextMenu.ShowForLibraryEntry(screen, request);
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

        /// <summary>Open dialog, asking to save unsaved changes first.</summary>
        public void RequestOpen()
        {
            if (Projects != null)
            {
                ConfirmDiscard("opening another project", () => ProjectFiles.Show(ProjectFileDialogMode.Open));
            }
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
                ProjectFiles.Show(ProjectFileDialogMode.SaveAs);
                return;
            }

            Projects.Save(out string message);
            _workspace.PostNotification(message);
        }

        public void RequestSaveAs()
        {
            ProjectFiles?.Show(ProjectFileDialogMode.SaveAs);
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
                ProjectFiles.Show(ProjectFileDialogMode.SaveAs, proceed);
                return;
            }

            bool saved = Projects.Save(out string message);
            _workspace.PostNotification(message);
            if (saved)
            {
                proceed();
            }
        }

        private void Update()
        {
            Keyboard keyboard = Keyboard.current;
            if (Projects == null || keyboard == null || _workspace == null || _workspace.InputSuspended ||
                IsTextInputFocused())
            {
                return;
            }

            bool control = keyboard.leftCtrlKey.isPressed || keyboard.rightCtrlKey.isPressed;
            if (!control)
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
