using System.Collections.Generic;
using DigitalLogicSandbox.CircuitEditor.Placement;
using DigitalLogicSandbox.CircuitEditor.Workspace;
using DigitalLogicSandbox.Commands.Circuit;
using DigitalLogicSandbox.Core.Circuit;
using DigitalLogicSandbox.Core.Connections;
using DigitalLogicSandbox.Core.Nodes;
using DigitalLogicSandbox.Core.Ports;
using DigitalLogicSandbox.Rendering;
using DigitalLogicSandbox.Rendering.Gates;
using DigitalLogicSandbox.Rendering.Wires;
using UnityEngine;
using UnityEngine.InputSystem;

namespace DigitalLogicSandbox.CircuitEditor.Interaction
{
    /// <summary>
    /// Pointer/keyboard state machine for the workspace. It reads <see cref="WorkspaceControls"/> once per frame
    /// and translates gestures into <see cref="WorkspaceController"/> API calls. It holds no circuit state of its
    /// own beyond the gesture in progress.
    ///
    /// Gestures:
    /// - Primary on a pin: wire (drag to a compatible pin, or click then click).
    /// - Primary on a node: select and drag. A click on an input boundary toggles it.
    /// - Primary on a wire: select it. Primary on empty space: rectangle select.
    /// - Middle/secondary drag, or Space+primary: pan. Scroll: zoom at the cursor.
    /// - Double-click a custom gate: open its internal circuit. Right-click without dragging: context menu.
    /// - Escape with nothing selected inside a gate: go up one level.
    /// - In read-only (inspection) levels, pins and nodes can be selected but nothing is wired, moved or toggled.
    /// </summary>
    internal sealed class WorkspaceInteraction
    {
        private const float DragThresholdPixels = 4f;
        private const float PinPickPixels = 10f;
        private const float WirePickPixels = 6f;
        private const float DoubleClickSeconds = 0.35f;

        private readonly WorkspaceController _workspace;
        private readonly WorkspaceControls _controls;
        private readonly List<Vector3> _previewPath = new List<Vector3>(8);
        private readonly List<NodeId> _rectResults = new List<NodeId>();
        private readonly List<PinRef> _compatiblePins = new List<PinRef>();

        private Mode _mode;
        private Vector2 _pressScreen;
        private Vector2 _pressWorld;
        private Vector2 _lastScreen;
        private NodeId _pressNode;
        private bool _pressAdditive;
        private bool _pressWasSelected;
        private InputAction _panButton;
        private float _panDistance;
        private float _lastClickTime = -10f;
        private NodeId _lastClickNode;

        private NodeId[] _dragNodes;
        private CircuitPoint[] _dragFrom;
        private Vector2 _dragApplied;

        private PinRef _wireStart;
        private PortDirection _wireStartDirection;
        private bool _wireSticky;

        public WorkspaceInteraction(WorkspaceController workspace, WorkspaceControls controls)
        {
            _workspace = workspace;
            _controls = controls;
        }

        private enum Mode
        {
            Idle,
            PendingNode,
            DraggingNodes,
            Wiring,
            RectSelect,
            Panning,
        }

        private CircuitDefinition Circuit => _workspace.Circuit;

        public void Tick()
        {
            if (Circuit == null)
            {
                return;
            }

            Vector2 screen = _controls.Point.ReadValue<Vector2>();
            Vector2 world = _workspace.WorkspaceCamera.ScreenToWorld(screen);
            bool overUi = _workspace.IsPointerOverUi();

            if (!_workspace.IsTextInputFocused())
            {
                HandleKeyboard();
            }

            if (_workspace.IsPlacing && _mode == Mode.Idle)
            {
                TickPlacement(world, overUi);
            }
            else
            {
                switch (_mode)
                {
                    case Mode.Idle:
                        TickIdle(screen, world, overUi);
                        break;
                    case Mode.PendingNode:
                        TickPendingNode(screen, world);
                        break;
                    case Mode.DraggingNodes:
                        TickDragging(world);
                        break;
                    case Mode.Wiring:
                        TickWiring(screen, world, overUi);
                        break;
                    case Mode.RectSelect:
                        TickRectSelect(world);
                        break;
                    case Mode.Panning:
                        TickPanning(screen);
                        break;
                }
            }

            if (!overUi && _mode != Mode.Panning)
            {
                float scroll = _controls.Scroll.ReadValue<Vector2>().y;
                if (!Mathf.Approximately(scroll, 0f))
                {
                    _workspace.WorkspaceCamera.ZoomAt(screen, Mathf.Sign(scroll));
                }
            }

            _lastScreen = screen;
        }

        /// <summary>Aborts the current gesture, restoring dragged nodes.</summary>
        public void Cancel()
        {
            switch (_mode)
            {
                case Mode.DraggingNodes:
                    ApplyDrag(Vector2.zero);
                    break;
                case Mode.Wiring:
                    EndWiring();
                    break;
                case Mode.RectSelect:
                    _workspace.SelectionRect.Hide();
                    break;
            }

            _mode = Mode.Idle;
            _panButton = null;
        }

        // ------------------------------------------------------------------------------------------------
        // Keyboard
        // ------------------------------------------------------------------------------------------------

        private void HandleKeyboard()
        {
            if (_controls.Cancel.WasPressedThisFrame())
            {
                if (_mode != Mode.Idle)
                {
                    Cancel();
                }
                else if (_workspace.IsPlacing)
                {
                    _workspace.CancelPlacement();
                }
                else if (_workspace.Selection.IsEmpty && _workspace.Navigator.CurrentIndex > 0)
                {
                    _workspace.NavigateUp();
                }
                else
                {
                    _workspace.Selection.Clear();
                }

                return;
            }

            if (_mode != Mode.Idle)
            {
                return;
            }

            if (_controls.Undo.WasPressedThisFrame())
            {
                _workspace.Undo();
            }
            else if (_controls.Redo.WasPressedThisFrame())
            {
                _workspace.Redo();
            }
            else if (_controls.Duplicate.WasPressedThisFrame())
            {
                _workspace.DuplicateSelection();
            }
            else if (_controls.SelectAll.WasPressedThisFrame())
            {
                _workspace.SelectAll();
            }
            else if (_controls.Delete.WasPressedThisFrame())
            {
                _workspace.DeleteSelection();
            }
            else if (_controls.Rotate.WasPressedThisFrame() && !_controls.MultiSelect.IsPressed())
            {
                _workspace.RotateSelection();
            }
            else if (_controls.Frame.WasPressedThisFrame())
            {
                _workspace.FrameContent();
            }
        }

        // ------------------------------------------------------------------------------------------------
        // Idle: hover, press dispatch
        // ------------------------------------------------------------------------------------------------

        private void TickIdle(Vector2 screen, Vector2 world, bool overUi)
        {
            if (overUi)
            {
                _workspace.Presenter.SetHover(NodeId.None, null);
                return;
            }

            if (TryBeginPan(screen))
            {
                return;
            }

            float pinRadius = PinRadius();
            bool overPin = _workspace.HitTester.TryHitPin(world, pinRadius, out PinRef pin, out PortDirection direction);
            NodeId hoverNode = NodeId.None;
            PinRef? hoverWire = null;
            if (overPin)
            {
                hoverNode = pin.Node;
            }
            else if (_workspace.HitTester.TryHitNode(world, out NodeId node))
            {
                hoverNode = node;
            }
            else if (_workspace.HitTester.TryHitWire(world, WirePickPixels * _workspace.WorkspaceCamera.WorldPerPixel,
                         out PinRef wire))
            {
                hoverWire = wire;
            }

            _workspace.Presenter.SetHover(hoverNode, hoverWire);

            if (!_controls.Primary.WasPressedThisFrame())
            {
                return;
            }

            _pressScreen = screen;
            _pressWorld = world;
            _pressAdditive = _controls.MultiSelect.IsPressed();

            if (overPin && !_workspace.IsEditable)
            {
                // Read-only level: pins only select their gate (or the wire feeding an input).
                if (direction == PortDirection.Input && Circuit.IsDriven(pin))
                {
                    _workspace.Selection.SelectWire(pin);
                    return;
                }

                overPin = false;
                hoverNode = pin.Node;
            }

            if (overPin)
            {
                if (direction == PortDirection.Input && Circuit.IsDriven(pin))
                {
                    // An input already has its single driver: select that wire instead of starting a new one.
                    _workspace.Selection.SelectWire(pin);
                    return;
                }

                BeginWiring(pin, direction);
                return;
            }

            if (hoverNode.IsValid)
            {
                _pressNode = hoverNode;
                _pressWasSelected = _workspace.Selection.Contains(hoverNode);
                bool isInputSwitch = IsInputBoundary(hoverNode) && _workspace.CanToggleInputs;
                if (_pressAdditive)
                {
                    if (!_pressWasSelected)
                    {
                        _workspace.Selection.Add(hoverNode);
                    }
                }
                else if (!_pressWasSelected && !isInputSwitch)
                {
                    _workspace.Selection.Select(hoverNode);
                }

                _mode = Mode.PendingNode;
                return;
            }

            if (hoverWire.HasValue)
            {
                _workspace.Selection.SelectWire(hoverWire.Value);
                return;
            }

            if (!_pressAdditive)
            {
                _workspace.Selection.Clear();
            }

            _mode = Mode.RectSelect;
        }

        private bool TryBeginPan(Vector2 screen)
        {
            InputAction button = null;
            if (_controls.Middle.WasPressedThisFrame())
            {
                button = _controls.Middle;
            }
            else if (_controls.Secondary.WasPressedThisFrame())
            {
                button = _controls.Secondary;
            }
            else if (_controls.Primary.WasPressedThisFrame() && _controls.PanModifier.IsPressed())
            {
                button = _controls.Primary;
            }

            if (button == null)
            {
                return false;
            }

            _panButton = button;
            _lastScreen = screen;
            _pressScreen = screen;
            _panDistance = 0f;
            _mode = Mode.Panning;
            _workspace.Presenter.SetHover(NodeId.None, null);
            return true;
        }

        private void TickPanning(Vector2 screen)
        {
            if (_panButton == null || !_panButton.IsPressed())
            {
                bool contextClick = _panButton == _controls.Secondary && _panDistance < DragThresholdPixels;
                _mode = Mode.Idle;
                _panButton = null;
                if (contextClick)
                {
                    _workspace.RequestContextMenu(_pressScreen, _workspace.WorkspaceCamera.ScreenToWorld(_pressScreen));
                }

                return;
            }

            _panDistance = Mathf.Max(_panDistance, (screen - _pressScreen).magnitude);
            _workspace.WorkspaceCamera.Pan(screen - _lastScreen);
        }

        // ------------------------------------------------------------------------------------------------
        // Node press / drag
        // ------------------------------------------------------------------------------------------------

        private void TickPendingNode(Vector2 screen, Vector2 world)
        {
            if (_controls.Primary.WasReleasedThisFrame() || !_controls.Primary.IsPressed())
            {
                // A click without dragging.
                float now = Time.unscaledTime;
                bool doubleClick = !_pressAdditive && _pressNode == _lastClickNode &&
                    now - _lastClickTime <= DoubleClickSeconds;
                _lastClickTime = doubleClick ? -10f : now;
                _lastClickNode = _pressNode;
                if (doubleClick && _workspace.CanOpenNode(_pressNode))
                {
                    _mode = Mode.Idle;
                    _workspace.OpenNode(_pressNode);
                    return;
                }

                if (_pressAdditive)
                {
                    if (_pressWasSelected)
                    {
                        _workspace.Selection.Toggle(_pressNode);
                    }
                }
                else if (IsInputBoundary(_pressNode) && _workspace.CanToggleInputs)
                {
                    _workspace.ToggleInput(_pressNode);
                }
                else
                {
                    _workspace.Selection.Select(_pressNode);
                }

                _mode = Mode.Idle;
                return;
            }

            if ((screen - _pressScreen).sqrMagnitude < DragThresholdPixels * DragThresholdPixels ||
                !_workspace.IsEditable)
            {
                return; // read-only levels never move nodes
            }

            if (!_workspace.Selection.Contains(_pressNode))
            {
                if (_pressAdditive)
                {
                    _workspace.Selection.Add(_pressNode);
                }
                else
                {
                    _workspace.Selection.Select(_pressNode);
                }
            }

            _dragNodes = _workspace.Selection.ToSortedArray();
            TransformNodesCommand.CaptureTransforms(Circuit, _dragNodes, out _dragFrom, out _);
            _dragApplied = Vector2.zero;
            _mode = Mode.DraggingNodes;
            TickDragging(world);
        }

        private void TickDragging(Vector2 world)
        {
            Vector2 delta = world - _pressWorld;
            if (_workspace.SnapToGrid)
            {
                delta = GridSnap.Snap(delta);
            }

            if (delta != _dragApplied)
            {
                ApplyDrag(delta);
            }

            if (_controls.Primary.WasReleasedThisFrame() || !_controls.Primary.IsPressed())
            {
                if (_dragApplied != Vector2.zero)
                {
                    _workspace.RecordMove(_dragNodes, _dragFrom);
                }

                _mode = Mode.Idle;
            }
        }

        private void ApplyDrag(Vector2 delta)
        {
            if (_dragNodes == null)
            {
                return;
            }

            for (int i = 0; i < _dragNodes.Length; i++)
            {
                if (Circuit.ContainsNode(_dragNodes[i]))
                {
                    Circuit.MoveNode(_dragNodes[i], _dragFrom[i].Offset(delta.x, delta.y));
                }
            }

            _dragApplied = delta;
        }

        // ------------------------------------------------------------------------------------------------
        // Wiring
        // ------------------------------------------------------------------------------------------------

        private void BeginWiring(PinRef start, PortDirection direction)
        {
            _wireStart = start;
            _wireStartDirection = direction;
            _wireSticky = false;
            _mode = Mode.Wiring;
            _workspace.Selection.Clear();
            _workspace.Presenter.SetHover(NodeId.None, null);

            // Highlight every pin the wire could legally end on.
            _compatiblePins.Clear();
            IReadOnlyList<NodeDefinition> nodes = Circuit.Nodes;
            for (int n = 0; n < nodes.Count; n++)
            {
                GateGeometry geometry = _workspace.Geometry.GetGate(nodes[n].Id);
                PortId[] ports = direction == PortDirection.Output ? geometry.InputPorts : geometry.OutputPorts;
                for (int p = 0; p < ports.Length; p++)
                {
                    var candidate = new PinRef(nodes[n].Id, ports[p]);
                    if (_workspace.CheckConnection(start, candidate, out _, out _) == ConnectionError.None)
                    {
                        _compatiblePins.Add(candidate);
                        _workspace.Presenter.HighlightPin(candidate);
                    }
                }
            }
        }

        private void TickWiring(Vector2 screen, Vector2 world, bool overUi)
        {
            if (_controls.Secondary.WasPressedThisFrame())
            {
                Cancel();
                return;
            }

            bool hasTarget = TryFindWireTarget(world, out PinRef target, out Vector2 targetWorld);
            UpdatePreview(hasTarget ? targetWorld : world, hasTarget, target);

            bool released = _controls.Primary.WasReleasedThisFrame();
            bool pressed = _controls.Primary.WasPressedThisFrame();
            if (!_wireSticky && released)
            {
                if (hasTarget && !overUi)
                {
                    _workspace.Connect(_wireStart, target);
                    Cancel();
                }
                else if ((screen - _pressScreen).sqrMagnitude < DragThresholdPixels * DragThresholdPixels)
                {
                    // Click on a pin without dragging: keep wiring until the next click.
                    _wireSticky = true;
                }
                else
                {
                    Cancel();
                }
            }
            else if (_wireSticky && pressed)
            {
                if (hasTarget && !overUi)
                {
                    _workspace.Connect(_wireStart, target);
                }

                Cancel();
            }
        }

        private bool TryFindWireTarget(Vector2 world, out PinRef target, out Vector2 targetWorld)
        {
            targetWorld = default;
            if (_workspace.HitTester.TryHitPin(world, PinRadius() * 1.5f, out target, out _) &&
                target != _wireStart && _compatiblePins.Contains(target))
            {
                return _workspace.Geometry.TryGetPin(target, out targetWorld, out _);
            }

            target = default;
            return false;
        }

        private void UpdatePreview(Vector2 end, bool valid, PinRef target)
        {
            CircuitGeometry geometry = _workspace.Geometry;
            if (!geometry.TryGetPin(_wireStart, out Vector2 start, out _))
            {
                Cancel();
                return;
            }

            // Route from driver to destination so the preview matches the final wire. The free end behaves like an
            // unrotated pin with no body; once snapped, the target pin's own side and body are used.
            bool startIsOutput = _wireStartDirection == PortDirection.Output;
            PortDirection endDirection = startIsOutput ? PortDirection.Input : PortDirection.Output;
            Vector2 startNormal = geometry.GetPinNormal(_wireStart.Node, _wireStartDirection);
            Rect startBody = geometry.GetBounds(_wireStart.Node);
            Vector2 endNormal = startIsOutput ? Vector2.left : Vector2.right;
            Rect endBody = default;
            if (valid)
            {
                endNormal = geometry.GetPinNormal(target.Node, endDirection);
                endBody = geometry.GetBounds(target.Node);
            }

            if (startIsOutput)
            {
                WireRouter.Route(start, startNormal, startBody, end, endNormal, endBody, _previewPath);
            }
            else
            {
                WireRouter.Route(end, endNormal, endBody, start, startNormal, startBody, _previewPath);
            }

            _workspace.Presenter.ShowWirePreview(_previewPath, valid);
        }

        private void EndWiring()
        {
            _workspace.Presenter.HideWirePreview();
            _workspace.Presenter.ClearPinHighlights();
            _compatiblePins.Clear();
            _wireSticky = false;
        }

        // ------------------------------------------------------------------------------------------------
        // Rectangle selection
        // ------------------------------------------------------------------------------------------------

        private void TickRectSelect(Vector2 world)
        {
            float lineWidth = 1.5f * _workspace.WorkspaceCamera.WorldPerPixel;
            _workspace.SelectionRect.Show(_pressWorld, world, lineWidth);

            if (!_controls.Primary.WasReleasedThisFrame() && _controls.Primary.IsPressed())
            {
                return;
            }

            _workspace.SelectionRect.Hide();
            _mode = Mode.Idle;
            Vector2 min = Vector2.Min(_pressWorld, world);
            Vector2 max = Vector2.Max(_pressWorld, world);
            if ((max - min).sqrMagnitude < 0.0001f)
            {
                return;
            }

            _rectResults.Clear();
            _workspace.HitTester.QueryNodes(Rect.MinMaxRect(min.x, min.y, max.x, max.y), _rectResults);
            if (_rectResults.Count > 0 || !_pressAdditive)
            {
                _workspace.Selection.SetNodes(_rectResults, _pressAdditive);
            }
        }

        // ------------------------------------------------------------------------------------------------
        // Placement
        // ------------------------------------------------------------------------------------------------

        private void TickPlacement(Vector2 world, bool overUi)
        {
            _workspace.UpdatePlacementGhost(world, visible: !overUi);

            if (_controls.Secondary.WasPressedThisFrame())
            {
                _workspace.CancelPlacement();
                return;
            }

            if (_workspace.PlacementFromDrag)
            {
                if (_controls.Primary.WasReleasedThisFrame() || !_controls.Primary.IsPressed())
                {
                    if (overUi)
                    {
                        _workspace.CancelPlacement();
                    }
                    else
                    {
                        _workspace.CommitPlacement(world, keepPlacing: false);
                    }
                }

                return;
            }

            if (_controls.Primary.WasPressedThisFrame() && !overUi)
            {
                _workspace.CommitPlacement(world, keepPlacing: _controls.MultiSelect.IsPressed());
            }
        }

        // ------------------------------------------------------------------------------------------------
        // Helpers
        // ------------------------------------------------------------------------------------------------

        private float PinRadius()
        {
            return Mathf.Max(_workspace.Theme.PinRadius * 1.6f, PinPickPixels * _workspace.WorkspaceCamera.WorldPerPixel);
        }

        private bool IsInputBoundary(NodeId node)
        {
            return Circuit.TryGetNode(node, out NodeDefinition definition) &&
                definition.Component.Kind == ComponentKind.InputBoundary;
        }
    }
}
