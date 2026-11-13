using DigitalLogicSandbox.CircuitEditor.Workspace;
using DigitalLogicSandbox.Simulation.Engine;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DigitalLogicSandbox.UI.Toolbar
{
    /// <summary>
    /// Top toolbar. Every button calls the workspace API. Enabled states follow the undo history, the selection and
    /// the simulation state. Project and analysis actions are added by later phases as their systems are built.
    /// </summary>
    public sealed class ToolbarView : MonoBehaviour
    {
        private WorkspaceController _workspace;
        private Button _undo;
        private Button _redo;
        private Button _delete;
        private Button _playPause;
        private TextMeshProUGUI _playPauseLabel;
        private Button _step;
        private Button _resetSimulation;
        private TextMeshProUGUI _title;
        private Button _createGate;
        private Button _inspect;

        /// <summary>Raised by the "Signals" button (signal visualization settings).</summary>
        public event System.Action SignalSettingsRequested;

        /// <summary>Raised by the "Create Gate" button.</summary>
        public event System.Action CreateGateRequested;

        public Button CreateGateButton => _createGate;

        public Button InspectButton => _inspect;

        public Button UndoButton => _undo;

        public Button RedoButton => _redo;

        public Button DeleteButton => _delete;

        public Button PlayPauseButton => _playPause;

        public Button StepButton => _step;

        public static ToolbarView Create(Transform parent, UiFactory ui, WorkspaceController workspace)
        {
            Image background = ui.Panel("Toolbar", parent, ui.Theme.PanelHeader);
            RectTransform rect = background.rectTransform;
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.sizeDelta = new Vector2(0f, UiFactory.ToolbarHeight);
            rect.anchoredPosition = Vector2.zero;
            UiFactory.Row(rect, 6f, new RectOffset(12, 12, 7, 7));

            var view = background.gameObject.AddComponent<ToolbarView>();
            view._workspace = workspace;

            view._title = ui.Text("Title", rect, "Digital Logic Sandbox", 15f, ui.Theme.TextPrimary,
                TextAlignmentOptions.MidlineLeft, FontStyles.Bold);
            view._title.gameObject.AddComponent<LayoutElement>().preferredWidth = 190f;
            ui.Separator(rect, vertical: true);

            view._undo = ui.Button("Undo", rect, "Undo", () => workspace.Undo());
            view._redo = ui.Button("Redo", rect, "Redo", () => workspace.Redo());
            view._delete = ui.Button("Delete", rect, "Delete", () => workspace.DeleteSelection());
            ui.Separator(rect, vertical: true);

            ui.Button("ZoomIn", rect, "Zoom +", workspace.ZoomIn);
            ui.Button("ZoomOut", rect, "Zoom -", workspace.ZoomOut);
            ui.Button("ResetCamera", rect, "Reset View", workspace.ResetCamera);
            ui.Button("Grid", rect, "Grid", () => workspace.GridVisible = !workspace.GridVisible);
            ui.Separator(rect, vertical: true);

            view._playPause = ui.Button("PlayPause", rect, "Pause", workspace.TogglePlayPause, 76f);
            view._playPauseLabel = view._playPause.GetComponentInChildren<TextMeshProUGUI>();
            view._step = ui.Button("Step", rect, "Step", () => workspace.Step());
            view._resetSimulation = ui.Button("ResetSimulation", rect, "Reset Sim", workspace.ResetSimulation);
            ui.Button("Signals", rect, "Signals", () => view.SignalSettingsRequested?.Invoke());

            ui.Separator(rect, vertical: true);
            view._createGate = ui.Button("CreateGate", rect, "Create Gate", () => view.CreateGateRequested?.Invoke());
            view._inspect = ui.Button("Inspect", rect, "Inspect", () => workspace.OpenSelected());

            UiFactory.Flexible(rect);

            workspace.StatusChanged += view.Refresh;
            view.Refresh();
            return view;
        }

        public void Refresh()
        {
            if (_workspace == null || !_workspace.IsInitialized)
            {
                return;
            }

            _undo.interactable = _workspace.History.CanUndo;
            _redo.interactable = _workspace.History.CanRedo;
            _delete.interactable = !_workspace.Selection.IsEmpty && _workspace.IsEditable;

            bool simulating = _workspace.Session.IsSimulating;
            bool running = _workspace.Runner.State == SimulationState.Running;
            _playPauseLabel.text = running ? "Pause" : "Play";
            _playPause.interactable = simulating;
            _step.interactable = simulating && !running;
            _resetSimulation.interactable = simulating;
            _createGate.interactable = _workspace.CanPackage && _workspace.Circuit != null &&
                _workspace.Circuit.Outputs.Count > 0;
            _inspect.interactable = SelectedOpenableNode();
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
