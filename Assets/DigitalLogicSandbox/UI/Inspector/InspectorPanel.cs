using System;
using System.Collections.Generic;
using System.Globalization;
using DigitalLogicSandbox.CircuitEditor.Navigation;
using DigitalLogicSandbox.CircuitEditor.Workspace;
using DigitalLogicSandbox.Components.Abstractions;
using DigitalLogicSandbox.Core.Circuit;
using DigitalLogicSandbox.Core.Connections;
using DigitalLogicSandbox.Core.Nodes;
using DigitalLogicSandbox.Core.Ports;
using DigitalLogicSandbox.Core.Signals;
using DigitalLogicSandbox.Rendering.Gates;
using DigitalLogicSandbox.Simulation.Engine;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DigitalLogicSandbox.UI.Inspector
{
    /// <summary>
    /// Floating properties card on the right. It stays out of the way until it is useful: it appears when
    /// something is selected, or when the user pins it open from the toolbar (circuit properties). With nothing
    /// selected it shows the circuit (name, interface, packaging); with one node it shows the gate (type, pins,
    /// position, custom gate details, boundary port name); with a wire it shows source, destination and live
    /// signal. The card grows with its content up to the available height. Content is rebuilt only when the
    /// selection, history or library changes. Live signal values are refreshed in place.
    /// </summary>
    public sealed class InspectorPanel : MonoBehaviour
    {
        public const float Width = 288f;

        private const float HeaderHeight = 50f;

        /// <summary>Space kept free at the bottom (status pill and view controls).</summary>
        private const float BottomReserve = UiFactory.Margin + UiFactory.StatusBarHeight + UiFactory.Margin + 6f;

        private readonly List<LiveValue> _liveValues = new List<LiveValue>();
        private TextMeshProUGUI _clockStatus;

        private UiFactory _ui;
        private WorkspaceController _workspace;
        private RectTransform _content;
        private TextMeshProUGUI _header;
        private bool _rebuildPending = true;
        private CircuitDefinition _shownCircuit;
        private RectTransform _rect;
        private RectTransform _canvas;
        private TextMeshProUGUI _headerIcon;
        private bool _pinned;
        private float _appliedHeight = -1f;

        /// <summary>Raised when the panel is shown or hidden (argument: pinned state, for the toolbar button).</summary>
        public event Action<bool> PinnedChanged;

        /// <summary>
        /// Keeps the panel open with nothing selected (circuit properties). Selecting something always shows it.
        /// </summary>
        public bool Pinned
        {
            get => _pinned;
            set
            {
                if (_pinned == value)
                {
                    return;
                }

                _pinned = value;
                UpdateVisibility();
                PinnedChanged?.Invoke(_pinned);
            }
        }

        /// <summary>True while the card is on screen.</summary>
        public bool IsShown => gameObject.activeSelf;

        /// <summary>Requested by the "Create Custom Gate" buttons; argument = package selection only.</summary>
        public event Action<bool> PackageRequested;

        /// <summary>Raised by "Save as new version..." while a gate is being edited.</summary>
        public event Action SaveDraftRequested;

        /// <summary>"Generate Truth Table" for the shown circuit.</summary>
        public event Action CircuitTruthTableRequested;

        /// <summary>"Sequence Analysis..." for the shown circuit.</summary>
        public event Action SequenceAnalysisRequested;

        /// <summary>"Truth Table" for the selected gate.</summary>
        public event Action<NodeId> NodeTruthTableRequested;

        public string HeaderText => _header.text;

        public static InspectorPanel Create(Transform parent, UiFactory ui, WorkspaceController workspace)
        {
            Image background = ui.Card("Inspector", parent, ui.Theme.PanelBackground);
            RectTransform rect = background.rectTransform;
            rect.anchorMin = rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(1f, 1f);
            rect.anchoredPosition = new Vector2(-UiFactory.Margin, -UiFactory.TopInset);
            rect.sizeDelta = new Vector2(Width, 300f);

            var panel = background.gameObject.AddComponent<InspectorPanel>();
            panel._ui = ui;
            panel._workspace = workspace;
            panel._rect = rect;
            panel._canvas = (RectTransform)parent;

            panel._headerIcon = ui.Icon("HeaderIcon", rect, Icons.Sliders, 15f, ui.Theme.Accent);
            RectTransform iconRect = panel._headerIcon.rectTransform;
            iconRect.anchorMin = iconRect.anchorMax = new Vector2(0f, 1f);
            iconRect.pivot = new Vector2(0f, 1f);
            iconRect.sizeDelta = new Vector2(20f, HeaderHeight);
            iconRect.anchoredPosition = new Vector2(16f, 0f);

            panel._header = ui.Text("Header", rect, "Properties", 14.5f, ui.Theme.TextPrimary,
                TextAlignmentOptions.MidlineLeft, FontStyles.Bold);
            RectTransform headerRect = panel._header.rectTransform;
            headerRect.anchorMin = new Vector2(0f, 1f);
            headerRect.anchorMax = new Vector2(1f, 1f);
            headerRect.pivot = new Vector2(0.5f, 1f);
            headerRect.offsetMin = new Vector2(44f, -HeaderHeight);
            headerRect.offsetMax = new Vector2(-46f, 0f);

            Button close = ui.IconButton("Close", rect, Icons.Close, "Close", panel.Close, 28f, 14f);
            RectTransform closeRect = (RectTransform)close.transform;
            closeRect.anchorMin = closeRect.anchorMax = new Vector2(1f, 1f);
            closeRect.pivot = new Vector2(1f, 1f);
            closeRect.sizeDelta = new Vector2(28f, 28f);
            closeRect.anchoredPosition = new Vector2(-11f, -11f);

            RectTransform scroll = UiFactory.CreateRect("Scroll", rect);
            UiFactory.Stretch(scroll, 2f, 2f, HeaderHeight - 6f, 4f);
            panel._content = ui.ScrollView("Body", scroll, out _);
            UiFactory.Stretch((RectTransform)panel._content.parent.parent);
            ((VerticalLayoutGroup)panel._content.GetComponent<LayoutGroup>()).padding = new RectOffset(16, 16, 0, 14);

            workspace.StatusChanged += panel.OnStatusChanged;
            workspace.Selection.Changed += panel.MarkRebuild;
            workspace.Selection.Changed += panel.UpdateVisibility;
            workspace.Navigator.HistoryChanged += panel.MarkRebuild;
            workspace.NavigationChanged += panel.MarkRebuild;
            workspace.Library.DefinitionAdded += panel.OnLibraryChanged;
            workspace.Library.DefinitionRemoved += panel.OnLibraryChanged;
            workspace.Library.DefinitionUpdated += panel.OnLibraryChanged;
            panel.Rebuild();
            panel.UpdateVisibility();
            return panel;
        }

        /// <summary>Hides the card: unpins it and clears the selection that kept it open.</summary>
        public void Close()
        {
            if (_workspace != null && _workspace.IsInitialized && !_workspace.Selection.IsEmpty)
            {
                _workspace.Selection.Clear();
            }

            Pinned = false;
            UpdateVisibility();
        }

        /// <summary>Shown while pinned or while something is selected.</summary>
        private void UpdateVisibility()
        {
            bool show = _pinned || (_workspace != null && _workspace.IsInitialized && !_workspace.Selection.IsEmpty);
            if (gameObject.activeSelf != show)
            {
                gameObject.SetActive(show);
                _appliedHeight = -1f;
            }
        }

        private void LateUpdate()
        {
            // Grow with the content, up to the free height between the toolbar and the bottom controls.
            float available = _canvas.rect.height - UiFactory.TopInset - BottomReserve;
            float wanted = HeaderHeight + LayoutUtility.GetPreferredHeight(_content) + 6f;
            float height = Mathf.Clamp(wanted, 120f, Mathf.Max(120f, available));
            if (!Mathf.Approximately(height, _appliedHeight))
            {
                _appliedHeight = height;
                _rect.sizeDelta = new Vector2(Width, height);
            }
        }

        /// <summary>Forces an immediate rebuild (used after loading a circuit and by tests).</summary>
        public void Rebuild()
        {
            _rebuildPending = false;
            _shownCircuit = _workspace != null && _workspace.IsInitialized ? _workspace.Circuit : null;
            _liveValues.Clear();
            _clockStatus = null;
            UiFactory.DestroyChildren(_content);
            if (_workspace == null || !_workspace.IsInitialized || _workspace.Circuit == null)
            {
                _header.text = "Properties";
                return;
            }

            CircuitDefinition circuit = _workspace.Circuit;
            if (_workspace.Selection.Wire.HasValue)
            {
                BuildWire(circuit, _workspace.Selection.Wire.Value);
            }
            else if (_workspace.Selection.NodeCount == 1)
            {
                foreach (NodeId node in _workspace.Selection.Nodes)
                {
                    BuildNode(circuit, node);
                }
            }
            else if (_workspace.Selection.NodeCount > 1)
            {
                BuildMultiple();
            }
            else
            {
                BuildCircuit(circuit);
            }

            RefreshLiveValues();
        }

        private void OnStatusChanged()
        {
            if (_rebuildPending || !ReferenceEquals(_shownCircuit, _workspace.Circuit))
            {
                Rebuild();
            }
            else
            {
                RefreshLiveValues();
            }

            UpdateVisibility();
        }

        private void MarkRebuild()
        {
            _rebuildPending = true;
        }

        private void OnLibraryChanged(CircuitDefinition definition)
        {
            _rebuildPending = true;
        }

        // ------------------------------------------------------------------------------------------------
        // Sections
        // ------------------------------------------------------------------------------------------------

        private void BuildCircuit(CircuitDefinition circuit)
        {
            NavigationLevel level = _workspace.CurrentLevel;
            _header.text = level == null || level.Kind == NavigationLevelKind.Root
                ? "Circuit"
                : level.IsDraft ? "Editing gate" : "Custom gate (inspecting)";
            _headerIcon.text = Icons.Sliders;
            BuildLevelSection(level);
            bool editable = _workspace.IsEditable;
            _ui.FieldRow(_content, "Name", circuit.Name,
                value => _workspace.SetCircuitProperties(value, circuit.Description, circuit.Category)).interactable = editable;
            _ui.FieldRow(_content, "Description", circuit.Description,
                value => _workspace.SetCircuitProperties(circuit.Name, value, circuit.Category)).interactable = editable;
            _ui.FieldRow(_content, "Category", circuit.Category,
                value => _workspace.SetCircuitProperties(circuit.Name, circuit.Description, value)).interactable = editable;

            int gates = 0;
            for (int i = 0; i < circuit.Nodes.Count; i++)
            {
                gates += circuit.Nodes[i].Component.IsUtility ? 0 : 1;
            }

            _ui.Property(_content, "Gates", gates.ToString(CultureInfo.InvariantCulture));
            _ui.Property(_content, "Wires", circuit.Connections.Count.ToString(CultureInfo.InvariantCulture));

            BuildInterfaceList(circuit, circuit.Inputs, "Input interface", input: true);
            BuildInterfaceList(circuit, circuit.Outputs, "Output interface", input: false);

            // Clock settings only matter once the circuit has a clock.
            bool hasClock = false;
            for (int i = 0; i < circuit.Nodes.Count && !hasClock; i++)
            {
                hasClock = circuit.Nodes[i].Component.Kind == ComponentKind.Clock;
            }

            if (hasClock)
            {
                BuildClockSection();
            }

            _ui.Caption(_content, "Analysis");
            _ui.Button("TruthTable", _content, "Generate truth table", () => CircuitTruthTableRequested?.Invoke())
                .interactable = circuit.Outputs.Count > 0;
            if (hasClock)
            {
                _ui.Button("SequenceAnalysis", _content, "Sequence analysis", () => SequenceAnalysisRequested?.Invoke())
                    .interactable = circuit.Outputs.Count > 0;
            }

            if (!_workspace.CanPackage)
            {
                return;
            }

            _ui.Caption(_content, "Reuse");
            TextMeshProUGUI note = _ui.Text("Note", _content,
                "Turn this circuit into a reusable gate. Its inputs and outputs become the gate's pins.", 12f,
                _ui.Theme.TextSecondary, TextAlignmentOptions.TopLeft);
            note.enableWordWrapping = true;
            note.gameObject.AddComponent<LayoutElement>().preferredHeight = 34f;
            Button package = _ui.Button("Package", _content, "Create custom gate", () => PackageRequested?.Invoke(false));
            package.interactable = circuit.Outputs.Count > 0;
            _ui.MakePrimary(package);
        }

        /// <summary>Mode explanation and actions for inspected gates and drafts.</summary>
        private void BuildLevelSection(NavigationLevel level)
        {
            if (level == null || level.Kind == NavigationLevelKind.Root)
            {
                return;
            }

            string text = level.IsDraft
                ? "Draft of version " + level.Circuit.Version + ". Existing instances keep using " + level.DraftSource +
                  " until you save."
                : level.CanToggleInputs
                    ? "Read-only view of this gate's implementation."
                    : "Read-only view. Signals are live values of this instance; its inputs are driven by the enclosing circuit.";
            TextMeshProUGUI note = _ui.Text("Mode", _content, text, 12f,
                level.IsDraft ? _ui.Theme.Warning : _ui.Theme.TextSecondary, TextAlignmentOptions.TopLeft);
            note.enableWordWrapping = true;
            note.gameObject.AddComponent<LayoutElement>().preferredHeight = 48f;
            if (level.IsDraft)
            {
                _ui.Button("SaveDraft", _content, "Save as new version...", () => SaveDraftRequested?.Invoke())
                    .interactable = level.HasUnsavedChanges;
                _ui.Button("DiscardDraft", _content, "Discard changes", () => _workspace.DiscardDraft());
            }
            else
            {
                _ui.Button("Edit", _content, "Edit implementation", () => _workspace.BeginEdit())
                    .interactable = _workspace.Navigator.CanBeginEdit(out _);
                _ui.Button("Up", _content, "Back to parent circuit", () => _workspace.NavigateUp());
            }
        }

        private void BuildInterfaceList(CircuitDefinition circuit, IReadOnlyList<InterfacePort> ports, string title,
            bool input)
        {
            _ui.Caption(_content, title);
            if (ports.Count == 0)
            {
                TextMeshProUGUI empty = _ui.Text("Empty", _content,
                    input ? "No inputs. Add Input switches from the library." : "No outputs. Add Output lamps from the library.",
                    12f, _ui.Theme.TextSecondary);
                empty.enableWordWrapping = true;
                empty.gameObject.AddComponent<LayoutElement>().preferredHeight = 30f;
                return;
            }

            for (int i = 0; i < ports.Count; i++)
            {
                InterfacePort port = ports[i];
                TMP_InputField field = _ui.FieldRow(_content, (i + 1) + ".", port.Name,
                    value => _workspace.RenameInterfacePort(port.Id, value.Trim()), 28f);
                field.characterLimit = 16;
                field.interactable = _workspace.IsEditable;
                if (_workspace.IsEditable && ports.Count > 1)
                {
                    int index = i;
                    Button up = _ui.IconButton("Up", field.transform.parent, Icons.ChevronUp, "Move up",
                        () => _workspace.MoveInterfacePort(port.Id, index - 1), 24f, 13f);
                    up.interactable = i > 0;
                    Button down = _ui.IconButton("Down", field.transform.parent, Icons.ChevronDown, "Move down",
                        () => _workspace.MoveInterfacePort(port.Id, index + 1), 24f, 13f);
                    down.interactable = i < ports.Count - 1;
                }
                if (circuit.TryGetBoundaryNode(port.Id, out NodeId node))
                {
                    TextMeshProUGUI value = _ui.Text("Value", field.transform.parent, "0", 12.5f, _ui.Theme.TextPrimary,
                        TextAlignmentOptions.MidlineRight, FontStyles.Bold);
                    value.gameObject.AddComponent<LayoutElement>().preferredWidth = 26f;
                    _liveValues.Add(LiveValue.ForBoundary(value, node, input, compact: true));
                }
            }
        }

        private void BuildNode(CircuitDefinition circuit, NodeId id)
        {
            if (!circuit.TryGetNode(id, out NodeDefinition node))
            {
                BuildCircuit(circuit);
                return;
            }

            ComponentCatalog catalog = _workspace.Catalog;
            catalog.TryGetSignature(node.Component, out ComponentSignature signature);
            string title = _workspace.Geometry.TitleOf(node);
            _header.text = title;
            _headerIcon.text = HeaderIconOf(node.Component.Kind);

            _ui.Property(_content, "Type", DescribeKind(node.Component));
            if (node.IsBoundary)
            {
                circuit.TryGetInterfacePort(node.InterfacePort, out InterfacePort port);
                bool input = node.Component.Kind == ComponentKind.InputBoundary;
                TMP_InputField field = _ui.FieldRow(_content, "Port name", port?.Name ?? string.Empty,
                    value => _workspace.RenameInterfacePort(node.InterfacePort, value.Trim()));
                field.characterLimit = 16;
                field.interactable = _workspace.IsEditable;
                int index = input ? circuit.IndexOfInput(node.InterfacePort) : circuit.IndexOfOutput(node.InterfacePort);
                _ui.Property(_content, "Order", (index + 1) + " of " + (input ? circuit.Inputs.Count : circuit.Outputs.Count));
                if (node.Component.Width > 1)
                {
                    _ui.Property(_content, "Width", node.Component.Width + " bits (bit 0 = LSB)");
                }

                _liveValues.Add(LiveValue.ForBoundary(_ui.Property(_content, "Value", "0"), node.Id, input));
                if (input && node.Component.Width > 1 && _workspace.CanToggleInputs)
                {
                    TMP_InputField value = _ui.FieldRow(_content, "Set value", string.Empty, text =>
                    {
                        if (ulong.TryParse(text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out ulong parsed))
                        {
                            _workspace.SetInputValue(node.Id, parsed);
                        }
                    });
                    value.contentType = TMP_InputField.ContentType.IntegerNumber;
                }
            }
            else
            {
                _ui.Property(_content, "Inputs", (signature?.Inputs.Count ?? 0).ToString(CultureInfo.InvariantCulture));
                _ui.Property(_content, "Outputs", (signature?.Outputs.Count ?? 0).ToString(CultureInfo.InvariantCulture));
                if (node.Component.Width > 1)
                {
                    _ui.Property(_content, "Width", node.Component.Width + " bits");
                }

                if (node.Component.Kind == ComponentKind.Display)
                {
                    _liveValues.Add(LiveValue.ForDisplay(_ui.Property(_content, "Shows", "-"), node.Id));
                }
            }

            _ui.Property(_content, "Position", node.Position.X.ToString("0.##", CultureInfo.InvariantCulture) + ", " +
                node.Position.Y.ToString("0.##", CultureInfo.InvariantCulture));
            _ui.Property(_content, "Rotation", ((int)node.Rotation * 90) + "°");

            if (node.Component.Kind == ComponentKind.Custom)
            {
                BuildCustomGateInfo(node.Component.Definition);
            }

            if (node.Component.Kind == ComponentKind.Clock)
            {
                BuildClockSection();
            }

            _ui.Caption(_content, "Actions");
            if (node.Component.Kind == ComponentKind.Custom)
            {
                Button open = _ui.Button("Open", _content, "Look inside", () => _workspace.OpenNode(id));
                open.interactable = _workspace.CanOpenNode(id);
                _ui.MakePrimary(open);
            }

            if (!node.Component.IsUtility)
            {
                _ui.Button("NodeTruthTable", _content, "Truth table", () => NodeTruthTableRequested?.Invoke(id));
            }

            if (_workspace.CanPackage)
            {
                _ui.Button("Package", _content, "Create custom gate from selection", () => PackageRequested?.Invoke(true));
            }
        }

        private static string HeaderIconOf(ComponentKind kind)
        {
            switch (kind)
            {
                case ComponentKind.Custom:
                    return Icons.Component;
                case ComponentKind.InputBoundary:
                    return Icons.Switch;
                case ComponentKind.OutputBoundary:
                    return Icons.Lamp;
                case ComponentKind.Clock:
                    return Icons.Clock;
                case ComponentKind.Display:
                    return Icons.Monitor;
                case ComponentKind.Splitter:
                case ComponentKind.Merger:
                case ComponentKind.BusJoin:
                case ComponentKind.TriState:
                    return Icons.Bus;
                default:
                    return Icons.Cpu;
            }
        }

        /// <summary>Clock and power-on settings (project-wide) plus the live clock state.</summary>
        private void BuildClockSection()
        {
            _ui.Caption(_content, "Clock & power-on");
            TMP_InputField frequency = _ui.FieldRow(_content, "Frequency (Hz)",
                _workspace.ClockFrequency.ToString("0.##", CultureInfo.InvariantCulture), value =>
                {
                    if (float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float hz))
                    {
                        _workspace.ClockFrequency = hz;
                    }

                    MarkRebuild(); // show the clamped value
                });
            frequency.contentType = TMP_InputField.ContentType.DecimalNumber;
            _ui.Toggle("ClockEnabled", _content, "Clock runs while playing", _workspace.ClockEnabled,
                value => _workspace.ClockEnabled = value);
            _ui.Toggle("PowerOnUnknown", _content, "Power on loops as unknown (X)",
                _workspace.PowerOnState == PowerOnState.Unknown,
                value => _workspace.PowerOnState = value ? PowerOnState.Unknown : PowerOnState.Resolved);
            _clockStatus = _ui.Property(_content, "Clock", "-");
            TextMeshProUGUI note = _ui.Text("ClockNote", _content,
                "Click a clock node (or Step while paused) for a single edge. Power-on mode applies at the next Reset.",
                11.5f, _ui.Theme.TextSecondary, TextAlignmentOptions.TopLeft);
            note.enableWordWrapping = true;
        }

        private void RefreshClockStatus()
        {
            if (_clockStatus == null)
            {
                return;
            }

            SimulationRunner runner = _workspace.Runner;
            if (runner == null || runner.Context == null || !runner.HasClock)
            {
                _clockStatus.text = "no clock in this circuit";
                return;
            }

            _clockStatus.text = runner.Context.Clock.ToDisplayChar() + "  ·  " +
                runner.ClockCycles.ToString(CultureInfo.InvariantCulture) + " cycles";
        }

        private void BuildCustomGateInfo(DefinitionRef reference)
        {
            _ui.Caption(_content, "Custom gate");
            if (!_workspace.Catalog.TryGetDefinition(reference, out CircuitDefinition definition))
            {
                TextMeshProUGUI missing = _ui.Text("Missing", _content, "Definition " + reference + " is missing.", 12.5f,
                    _ui.Theme.Error);
                missing.gameObject.AddComponent<LayoutElement>().preferredHeight = 22f;
                return;
            }

            DefinitionId gateId = definition.Id;
            TMP_InputField gateName = _ui.FieldRow(_content, "Gate", definition.Name, value =>
            {
                if (!string.IsNullOrWhiteSpace(value))
                {
                    _workspace.RenameCustomGate(gateId, value.Trim());
                }
            });
            gateName.characterLimit = 32;
            _ui.Property(_content, "Version", definition.Version.ToString(CultureInfo.InvariantCulture));
            if (!string.IsNullOrEmpty(definition.Category))
            {
                _ui.Property(_content, "Category", definition.Category);
            }

            int internalGates = 0;
            for (int i = 0; i < definition.Nodes.Count; i++)
            {
                internalGates += definition.Nodes[i].IsBoundary ? 0 : 1;
            }

            _ui.Property(_content, "Internal", internalGates + " gates, " + definition.Connections.Count + " wires");
            var versions = new List<CircuitDefinition>();
            _workspace.Library.GetVersions(definition.Id, versions);
            _ui.Property(_content, "Versions", versions.Count.ToString(CultureInfo.InvariantCulture));
            if (!string.IsNullOrEmpty(definition.Description))
            {
                TextMeshProUGUI description = _ui.Text("Description", _content, definition.Description, 12f,
                    _ui.Theme.TextSecondary, TextAlignmentOptions.TopLeft);
                description.enableWordWrapping = true;
                description.gameObject.AddComponent<ContentSizeFitter>().verticalFit =
                    ContentSizeFitter.FitMode.PreferredSize;
            }
        }

        private void BuildMultiple()
        {
            _header.text = _workspace.Selection.NodeCount + " parts selected";
            _headerIcon.text = Icons.Boxes;
            int gates = 0;
            int inputs = 0;
            int outputs = 0;
            foreach (NodeId id in _workspace.Selection.Nodes)
            {
                if (!_workspace.Circuit.TryGetNode(id, out NodeDefinition node))
                {
                    continue;
                }

                switch (node.Component.Kind)
                {
                    case ComponentKind.InputBoundary:
                        inputs++;
                        break;
                    case ComponentKind.OutputBoundary:
                        outputs++;
                        break;
                    case ComponentKind.Clock:
                        break;
                    default:
                        gates++;
                        break;
                }
            }

            _ui.Property(_content, "Gates", gates.ToString(CultureInfo.InvariantCulture));
            _ui.Property(_content, "Inputs", inputs.ToString(CultureInfo.InvariantCulture));
            _ui.Property(_content, "Outputs", outputs.ToString(CultureInfo.InvariantCulture));
            if (_workspace.CanPackage)
            {
                _ui.Caption(_content, "Actions");
                Button package = _ui.Button("Package", _content, "Create custom gate", () => PackageRequested?.Invoke(true));
                _ui.MakePrimary(package);
            }
        }

        private void BuildWire(CircuitDefinition circuit, PinRef destination)
        {
            _header.text = "Wire";
            _headerIcon.text = Icons.Activity;
            if (!circuit.TryGetConnectionTo(destination, out ConnectionDefinition connection))
            {
                return;
            }

            _ui.Property(_content, "Source", DescribePin(circuit, connection.Source), 80f);
            _ui.Property(_content, "Destination", DescribePin(circuit, destination), 80f);
            _liveValues.Add(LiveValue.ForWire(_ui.Property(_content, "Signal", "-", 80f), destination));
            _ui.Button("Delete", _content, "Delete wire", () => _workspace.DeleteWire(destination)).interactable =
                _workspace.IsEditable;
        }

        // ------------------------------------------------------------------------------------------------
        // Live values
        // ------------------------------------------------------------------------------------------------

        private void RefreshLiveValues()
        {
            if (_workspace == null || !_workspace.IsInitialized)
            {
                return;
            }

            RefreshClockStatus();
            CircuitSession session = _workspace.Session;
            for (int i = 0; i < _liveValues.Count; i++)
            {
                LiveValue live = _liveValues[i];
                if (live.Text == null)
                {
                    continue;
                }

                if (live.IsDisplay)
                {
                    live.Text.text = _workspace.Presenter.TryGetGateView(live.Node, out GateView display)
                        ? display.DisplayText
                        : "-";
                    live.Text.color = _ui.Theme.TextPrimary;
                    continue;
                }

                Signal value;
                if (live.IsWire)
                {
                    int net = _workspace.Presenter.GetWireNet(live.Wire);
                    if (net < 0 || !session.IsSimulating)
                    {
                        live.Text.text = "-";
                        continue;
                    }

                    value = _workspace.Presenter.GetBusWireSignal(live.Wire) ?? session.Runner.Context.GetNet(net);
                }
                else
                {
                    if (session.GetBoundaryWidth(live.Node) > 1)
                    {
                        live.Text.text = session.FormatBoundaryValue(live.Node);
                        live.Text.color = _ui.Theme.TextPrimary;
                        continue;
                    }

                    value = live.Input ? session.GetInputValue(live.Node) : session.GetOutputValue(live.Node);
                }

                live.Text.text = live.Compact ? value.ToDisplayChar().ToString() : Describe(value);
                live.Text.color = value == Signal.High ? _ui.Theme.SignalColor(Signal.High) : _ui.Theme.TextPrimary;
            }
        }

        private static string Describe(Signal signal)
        {
            switch (signal)
            {
                case Signal.High:
                    return "1 (HIGH)";
                case Signal.Low:
                    return "0 (LOW)";
                case Signal.Unknown:
                    return "X (unknown)";
                default:
                    return "Z (high-Z)";
            }
        }

        private string DescribePin(CircuitDefinition circuit, PinRef pin)
        {
            if (!circuit.TryGetNode(pin.Node, out NodeDefinition node))
            {
                return "?";
            }

            string title = _workspace.Geometry.TitleOf(node);
            if (node.IsBoundary)
            {
                return title + " (" + (node.Component.Kind == ComponentKind.InputBoundary ? "input" : "output") + ")";
            }

            string portName = pin.Port.ToString();
            if (_workspace.Catalog.TryGetSignature(node.Component, out ComponentSignature signature))
            {
                for (int i = 0; i < signature.Inputs.Count; i++)
                {
                    if (signature.Inputs[i].Id == pin.Port)
                    {
                        portName = signature.Inputs[i].Name;
                    }
                }

                for (int i = 0; i < signature.Outputs.Count; i++)
                {
                    if (signature.Outputs[i].Id == pin.Port)
                    {
                        portName = signature.Outputs[i].Name;
                    }
                }
            }

            return title + " #" + node.Id.Value + " . " + portName;
        }

        private static string DescribeKind(ComponentRef component)
        {
            switch (component.Kind)
            {
                case ComponentKind.Primitive:
                    return "Built-in gate";
                case ComponentKind.Custom:
                    return "Custom gate";
                case ComponentKind.InputBoundary:
                    return "Circuit input";
                case ComponentKind.Clock:
                    return "Clock (global)";
                case ComponentKind.Splitter:
                    return "Bus splitter";
                case ComponentKind.Merger:
                    return "Bus merger";
                case ComponentKind.BusJoin:
                    return "Bus join (" + component.Arity + " drivers)";
                case ComponentKind.TriState:
                    return "Tri-state driver";
                case ComponentKind.Display:
                    return DisplayStyles.DisplayName(component.DisplayStyle);
                default:
                    return "Circuit output";
            }
        }

        private void OnDestroy()
        {
            if (_workspace == null)
            {
                return;
            }

            _workspace.StatusChanged -= OnStatusChanged;
            _workspace.Selection.Changed -= MarkRebuild;
            _workspace.Selection.Changed -= UpdateVisibility;
            _workspace.Navigator.HistoryChanged -= MarkRebuild;
            _workspace.NavigationChanged -= MarkRebuild;
            if (_workspace.Library != null)
            {
                _workspace.Library.DefinitionAdded -= OnLibraryChanged;
                _workspace.Library.DefinitionRemoved -= OnLibraryChanged;
                _workspace.Library.DefinitionUpdated -= OnLibraryChanged;
            }
        }

        private readonly struct LiveValue
        {
            private LiveValue(TextMeshProUGUI text, NodeId node, bool input, PinRef wire, bool isWire, bool compact,
                bool isDisplay = false)
            {
                IsDisplay = isDisplay;
                Compact = compact;
                Text = text;
                Node = node;
                Input = input;
                Wire = wire;
                IsWire = isWire;
            }

            public TextMeshProUGUI Text { get; }

            public NodeId Node { get; }

            public bool Input { get; }

            public PinRef Wire { get; }

            public bool IsWire { get; }

            /// <summary>Reading of a display node (taken from its view).</summary>
            public bool IsDisplay { get; }

            public static LiveValue ForDisplay(TextMeshProUGUI text, NodeId node)
            {
                return new LiveValue(text, node, false, default, false, false, true);
            }

            /// <summary>Show "0"/"1" instead of "0 (LOW)" (narrow columns).</summary>
            public bool Compact { get; }

            public static LiveValue ForBoundary(TextMeshProUGUI text, NodeId node, bool input, bool compact = false)
            {
                return new LiveValue(text, node, input, default, false, compact);
            }

            public static LiveValue ForWire(TextMeshProUGUI text, PinRef wire)
            {
                return new LiveValue(text, default, false, wire, true, false);
            }
        }
    }
}
