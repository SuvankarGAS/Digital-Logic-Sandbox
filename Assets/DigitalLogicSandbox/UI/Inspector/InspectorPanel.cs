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
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DigitalLogicSandbox.UI.Inspector
{
    /// <summary>
    /// Right-hand properties panel. With nothing selected it shows the circuit (name, interface, packaging); with
    /// one node it shows the gate (type, pins, position, custom gate details, boundary port name); with a wire it
    /// shows source, destination and live signal. Content is rebuilt only when the selection, history or library
    /// changes. Live signal values are refreshed in place.
    /// </summary>
    public sealed class InspectorPanel : MonoBehaviour
    {
        public const float Width = 280f;

        private readonly List<LiveValue> _liveValues = new List<LiveValue>();

        private UiFactory _ui;
        private WorkspaceController _workspace;
        private RectTransform _content;
        private TextMeshProUGUI _header;
        private bool _rebuildPending = true;
        private CircuitDefinition _shownCircuit;

        /// <summary>Requested by the "Create Custom Gate" buttons; argument = package selection only.</summary>
        public event Action<bool> PackageRequested;

        /// <summary>Raised by "Save as new version..." while a gate is being edited.</summary>
        public event Action SaveDraftRequested;

        public string HeaderText => _header.text;

        public static InspectorPanel Create(Transform parent, UiFactory ui, WorkspaceController workspace)
        {
            Image background = ui.Panel("Inspector", parent, ui.Theme.PanelBackground);
            RectTransform rect = background.rectTransform;
            rect.anchorMin = new Vector2(1f, 0f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(1f, 0.5f);
            rect.offsetMin = new Vector2(-Width, UiFactory.StatusBarHeight);
            rect.offsetMax = new Vector2(0f, -UiFactory.ToolbarHeight);

            Image border = ui.Panel("Border", rect, ui.Theme.PanelBorder);
            border.raycastTarget = false;
            border.rectTransform.anchorMin = new Vector2(0f, 0f);
            border.rectTransform.anchorMax = new Vector2(0f, 1f);
            border.rectTransform.sizeDelta = new Vector2(1f, 0f);
            border.rectTransform.anchoredPosition = Vector2.zero;

            var panel = background.gameObject.AddComponent<InspectorPanel>();
            panel._ui = ui;
            panel._workspace = workspace;

            panel._header = ui.Text("Header", rect, "Properties", 15f, ui.Theme.TextPrimary,
                TextAlignmentOptions.MidlineLeft, FontStyles.Bold);
            RectTransform headerRect = panel._header.rectTransform;
            headerRect.anchorMin = new Vector2(0f, 1f);
            headerRect.anchorMax = new Vector2(1f, 1f);
            headerRect.pivot = new Vector2(0.5f, 1f);
            headerRect.offsetMin = new Vector2(14f, -40f);
            headerRect.offsetMax = new Vector2(-14f, -8f);

            RectTransform scroll = UiFactory.CreateRect("Scroll", rect);
            UiFactory.Stretch(scroll, 1f, 0f, 40f, 0f);
            panel._content = ui.ScrollView("Body", scroll, out _);
            UiFactory.Stretch((RectTransform)panel._content.parent.parent);

            workspace.StatusChanged += panel.OnStatusChanged;
            workspace.Selection.Changed += panel.MarkRebuild;
            workspace.Navigator.HistoryChanged += panel.MarkRebuild;
            workspace.NavigationChanged += panel.MarkRebuild;
            workspace.Library.DefinitionAdded += panel.OnLibraryChanged;
            workspace.Library.DefinitionRemoved += panel.OnLibraryChanged;
            panel.Rebuild();
            return panel;
        }

        /// <summary>Forces an immediate rebuild (used after loading a circuit and by tests).</summary>
        public void Rebuild()
        {
            _rebuildPending = false;
            _shownCircuit = _workspace != null && _workspace.IsInitialized ? _workspace.Circuit : null;
            _liveValues.Clear();
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
                gates += circuit.Nodes[i].IsBoundary ? 0 : 1;
            }

            _ui.Property(_content, "Gates", gates.ToString(CultureInfo.InvariantCulture));
            _ui.Property(_content, "Wires", circuit.Connections.Count.ToString(CultureInfo.InvariantCulture));

            BuildInterfaceList(circuit, circuit.Inputs, "Input interface", input: true);
            BuildInterfaceList(circuit, circuit.Outputs, "Output interface", input: false);

            if (!_workspace.CanPackage)
            {
                return;
            }

            _ui.Caption(_content, "Packaging");
            TextMeshProUGUI note = _ui.Text("Note", _content,
                "Turn this circuit into a reusable gate. Its inputs and outputs become the gate's pins.", 12f,
                _ui.Theme.TextSecondary, TextAlignmentOptions.TopLeft);
            note.enableWordWrapping = true;
            note.gameObject.AddComponent<LayoutElement>().preferredHeight = 34f;
            _ui.Button("Package", _content, "Create Custom Gate...", () => PackageRequested?.Invoke(false));
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
                _liveValues.Add(LiveValue.ForBoundary(_ui.Property(_content, "Value", "0"), node.Id, input));
            }
            else
            {
                _ui.Property(_content, "Inputs", (signature?.Inputs.Count ?? 0).ToString(CultureInfo.InvariantCulture));
                _ui.Property(_content, "Outputs", (signature?.Outputs.Count ?? 0).ToString(CultureInfo.InvariantCulture));
            }

            _ui.Property(_content, "Position", node.Position.X.ToString("0.##", CultureInfo.InvariantCulture) + ", " +
                node.Position.Y.ToString("0.##", CultureInfo.InvariantCulture));
            _ui.Property(_content, "Rotation", ((int)node.Rotation * 90) + " deg");
            _ui.Property(_content, "Node id", node.Id.ToString());

            if (signature != null && !node.IsBoundary)
            {
                _ui.Caption(_content, "Pins");
                for (int i = 0; i < signature.Inputs.Count; i++)
                {
                    _ui.Property(_content, "In  " + signature.Inputs[i].Name, string.Empty);
                }

                for (int i = 0; i < signature.Outputs.Count; i++)
                {
                    _ui.Property(_content, "Out " + signature.Outputs[i].Name, string.Empty);
                }
            }

            if (node.Component.Kind == ComponentKind.Custom)
            {
                BuildCustomGateInfo(node.Component.Definition);
            }

            _ui.Caption(_content, "Actions");
            if (node.Component.Kind == ComponentKind.Custom)
            {
                _ui.Button("Open", _content, "Open Internal Circuit", () => _workspace.OpenNode(id)).interactable =
                    _workspace.CanOpenNode(id);
            }

            if (_workspace.CanPackage)
            {
                _ui.Button("Package", _content, "Create Custom Gate from selection...", () => PackageRequested?.Invoke(true));
            }
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

            _ui.Property(_content, "Gate", definition.Name);
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
                _ui.Button("Package", _content, "Create Custom Gate from selection...", () => PackageRequested?.Invoke(true));
            }
        }

        private void BuildWire(CircuitDefinition circuit, PinRef destination)
        {
            _header.text = "Wire";
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

            CircuitSession session = _workspace.Session;
            for (int i = 0; i < _liveValues.Count; i++)
            {
                LiveValue live = _liveValues[i];
                if (live.Text == null)
                {
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

                    value = session.Runner.Context.GetNet(net);
                }
                else
                {
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
            _workspace.Navigator.HistoryChanged -= MarkRebuild;
            _workspace.NavigationChanged -= MarkRebuild;
            if (_workspace.Library != null)
            {
                _workspace.Library.DefinitionAdded -= OnLibraryChanged;
                _workspace.Library.DefinitionRemoved -= OnLibraryChanged;
            }
        }

        private readonly struct LiveValue
        {
            private LiveValue(TextMeshProUGUI text, NodeId node, bool input, PinRef wire, bool isWire, bool compact)
            {
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
