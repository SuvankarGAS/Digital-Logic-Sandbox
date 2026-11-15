using System.Collections.Generic;
using DigitalLogicSandbox.Core.Nodes;
using DigitalLogicSandbox.Core.Signals;
using TMPro;
using UnityEngine;

namespace DigitalLogicSandbox.Rendering.Gates
{
    /// <summary>
    /// Pooled visual for one node: rounded body with outline, title, pins coloured by signal, and for boundaries a
    /// state indicator (input switch knob / output lamp) that glows while HIGH. A soft drop shadow lifts the body off
    /// the background. Built entirely from <see cref="GateGeometry"/>, so it knows nothing about which gate it
    /// represents. Has no Update.
    /// </summary>
    public sealed class GateView : MonoBehaviour
    {
        private readonly List<SpriteRenderer> _inputPins = new List<SpriteRenderer>();
        private readonly List<SpriteRenderer> _outputPins = new List<SpriteRenderer>();
        private readonly List<SpriteRenderer> _inputRings = new List<SpriteRenderer>();
        private readonly List<SpriteRenderer> _outputRings = new List<SpriteRenderer>();
        private readonly List<Signal> _inputSignals = new List<Signal>();
        private readonly List<Signal> _outputSignals = new List<Signal>();
        private readonly List<TextMeshPro> _inputLabels = new List<TextMeshPro>();
        private readonly List<TextMeshPro> _outputLabels = new List<TextMeshPro>();
        private readonly List<SpriteRenderer> _bitCells = new List<SpriteRenderer>();
        private readonly List<Signal> _bitSignals = new List<Signal>();
        private readonly System.Text.StringBuilder _busText = new System.Text.StringBuilder(48);

        private VisualTheme _theme;
        private SpriteRenderer _shadow;
        private SpriteRenderer _stateGlow;
        private SpriteRenderer _outline;
        private SpriteRenderer _fill;
        private SpriteRenderer _state;
        private SpriteRenderer _stateRing;
        private TextMeshPro _title;
        private TextMeshPro _subtitle;
        private DisplayVisual _display;
        private GateGeometry _geometry;
        private Signal _stateSignal;
        private bool _selected;
        private bool _hovered;
        private bool _ghost;
        private bool _ghostValid = true;

        public NodeId Node { get; private set; }

        public GateGeometry Geometry => _geometry;

        public Signal StateSignal => _stateSignal;

        public bool IsSelected => _selected;

        public static GateView Create(Transform parent, VisualTheme theme)
        {
            var go = new GameObject("Gate");
            go.transform.SetParent(parent, false);
            var view = go.AddComponent<GateView>();
            view._theme = theme;
            view._shadow = CreateSprite(go.transform, "Shadow", RenderAssets.SoftShadow, theme, SortingOrders.GateShadow);
            view._shadow.drawMode = SpriteDrawMode.Sliced;
            view._outline = CreateSprite(go.transform, "Outline", RenderAssets.RoundedRect, theme, SortingOrders.GateOutline);
            view._fill = CreateSprite(go.transform, "Fill", RenderAssets.RoundedRect, theme, SortingOrders.GateFill);
            view._outline.drawMode = SpriteDrawMode.Sliced;
            view._fill.drawMode = SpriteDrawMode.Sliced;
            view._stateGlow = CreateSprite(go.transform, "StateGlow", RenderAssets.Glow, theme, SortingOrders.GateState);
            view._stateRing = CreateSprite(go.transform, "StateRing", RenderAssets.Circle, theme, SortingOrders.GateState + 1);
            view._state = CreateSprite(go.transform, "State", RenderAssets.Circle, theme, SortingOrders.GateState + 2);
            view._title = CreateText(go.transform, "Title", 3.4f, theme, FontWeight.SemiBold);
            view._title.characterSpacing = 4f;
            view._subtitle = CreateText(go.transform, "Subtitle", 2.1f, theme, FontWeight.Regular);
            view._display = new DisplayVisual(go.transform, theme);
            return view;
        }

        public void Configure(NodeId node, GateGeometry geometry)
        {
            Node = node;
            _geometry = geometry;
            _selected = false;
            _hovered = false;
            _stateSignal = Signal.Low;

            Vector2 size = geometry.Size;
            _fill.size = size;
            _shadow.size = size + Vector2.one * ShadowSpread;
            _shadow.transform.localPosition = new Vector3(0f, -ShadowDrop, 0f);
            bool isCustom = geometry.Kind == ComponentKind.Custom;
            bool isInput = geometry.Kind == ComponentKind.InputBoundary;
            bool isOutput = geometry.Kind == ComponentKind.OutputBoundary;
            bool isClock = geometry.Kind == ComponentKind.Clock;
            bool isBus = geometry.IsBusBoundary;
            bool isBoundary = (isInput || isOutput || isClock) && !isBus; // utility box with a state indicator

            _fill.color = isBoundary || isBus || geometry.IsDisplay ? _theme.BoundaryFill : isCustom ? _theme.CustomGateFill : _theme.GateFill;

            // Boundary state indicator sits at the end away from the pin.
            _state.gameObject.SetActive(isBoundary);
            _stateRing.gameObject.SetActive(isBoundary);
            _stateGlow.gameObject.SetActive(false);
            float indicator = Mathf.Min(size.x, size.y) * 0.62f;
            Vector2 statePosition = Vector2.zero;
            if (isBoundary)
            {
                Vector2 pinOffset = isOutput ? geometry.InputPins[0] : geometry.OutputPins[0];
                statePosition = -pinOffset.normalized * (Mathf.Max(size.x, size.y) * 0.5f - indicator * 0.5f - 0.15f);
                _state.transform.localPosition = statePosition;
                _state.transform.localScale = Vector3.one * indicator;
                _stateRing.transform.localPosition = statePosition;
                _stateRing.transform.localScale = Vector3.one * (indicator + 0.08f);
                _stateGlow.transform.localPosition = statePosition;
                _stateGlow.transform.localScale = Vector3.one * (indicator * 2.6f);
                _stateRing.color = _theme.GateOutline;
            }

            _title.text = geometry.Title;
            _title.rectTransform.sizeDelta = new Vector2(size.x, size.y);
            _title.alignment = TextAlignmentOptions.Center;
            if (isBoundary)
            {
                // Title shares the body with the indicator: shift it towards the pin side.
                float shift = (indicator * 0.5f + 0.1f);
                _title.transform.localPosition = new Vector3(-statePosition.normalized.x * shift,
                    -statePosition.normalized.y * shift, 0f);
                _title.fontSize = 2.8f;
            }
            else if (geometry.IsDisplay)
            {
                // Display: a small caption (BIN/HEX/DEC/SIGNED) along the top edge.
                _title.transform.localPosition = new Vector3(0.1f, size.y * 0.5f - 0.3f, 0f);
                _title.rectTransform.sizeDelta = new Vector2(size.x, 0.5f);
                _title.fontSize = 2.2f;
            }
            else if (isBus)
            {
                // Bus boundary: "NAME = value" above the bit cells.
                _title.transform.localPosition = new Vector3(0f, 0.36f, 0f);
                _title.rectTransform.sizeDelta = new Vector2(size.x, 0.6f);
                _title.fontSize = 2.8f;
            }
            else
            {
                _title.transform.localPosition = isCustom ? new Vector3(0f, 0.12f, 0f) : Vector3.zero;
                _title.fontSize = 3.4f;
            }

            _subtitle.gameObject.SetActive(isCustom);
            _subtitle.text = geometry.Subtitle;
            _subtitle.rectTransform.sizeDelta = new Vector2(size.x, 0.5f);
            _subtitle.transform.localPosition = new Vector3(0f, -0.32f, 0f);

            ConfigurePins(_inputPins, _inputRings, _inputSignals, geometry.InputPins, "In");
            ConfigurePins(_outputPins, _outputRings, _outputSignals, geometry.OutputPins, "Out");
            ConfigureLabels(_inputLabels, geometry.InputNames, geometry.InputPins, "InLabel");
            ConfigureLabels(_outputLabels, geometry.OutputNames, geometry.OutputPins, "OutLabel");
            ConfigureBitCells(geometry.BitCells);
            _display.Configure(geometry);

            ApplyOutline();
            ApplyState();
            ApplyGhostTint();
        }

        public void SetPosition(Vector2 position)
        {
            transform.localPosition = new Vector3(position.x, position.y, 0f);
        }

        public void SetSelected(bool selected)
        {
            if (_selected == selected)
            {
                return;
            }

            _selected = selected;
            ApplyOutline();
        }

        public void SetHovered(bool hovered)
        {
            if (_hovered == hovered)
            {
                return;
            }

            _hovered = hovered;
            ApplyOutline();
        }

        /// <summary>Semi-transparent placement preview; <paramref name="valid"/> false tints it as invalid.</summary>
        public void SetGhost(bool ghost, bool valid = true)
        {
            _ghost = ghost;
            _ghostValid = valid;
            int offset = ghost ? SortingOrders.GhostOffset : 0;
            _shadow.sortingOrder = SortingOrders.GateShadow + offset;
            _shadow.gameObject.SetActive(!ghost);
            _outline.sortingOrder = SortingOrders.GateOutline + offset;
            _fill.sortingOrder = SortingOrders.GateFill + offset;
            _stateGlow.sortingOrder = SortingOrders.GateState + offset;
            _stateRing.sortingOrder = SortingOrders.GateState + 1 + offset;
            _state.sortingOrder = SortingOrders.GateState + 2 + offset;
            _title.sortingOrder = SortingOrders.Text + offset;
            _subtitle.sortingOrder = SortingOrders.Text + offset;
            SetPinOrder(_inputPins, _inputRings, offset);
            SetPinOrder(_outputPins, _outputRings, offset);
            for (int i = 0; i < _bitCells.Count; i++)
            {
                _bitCells[i].sortingOrder = SortingOrders.GateState + offset;
            }

            _display.SetGhost(ghost, offset);
            ApplyOutline();
            ApplyGhostTint();
        }

        public void SetInputSignal(int index, Signal signal)
        {
            if ((uint)index >= (uint)_inputSignals.Count || _inputSignals[index] == signal)
            {
                return;
            }

            _inputSignals[index] = signal;
            _inputPins[index].color = Tint(PinColor(signal));
        }

        public void SetOutputSignal(int index, Signal signal)
        {
            if ((uint)index >= (uint)_outputSignals.Count || _outputSignals[index] == signal)
            {
                return;
            }

            _outputSignals[index] = signal;
            _outputPins[index].color = Tint(PinColor(signal));
        }

        public Signal GetInputSignal(int index)
        {
            return _inputSignals[index];
        }

        public Signal GetOutputSignal(int index)
        {
            return _outputSignals[index];
        }

        /// <summary>Number of bit cells (bus boundaries only).</summary>
        public int BitCount => _geometry != null ? _geometry.BitCells.Length : 0;

        /// <summary>State of one bit cell of a bus boundary; updates the value readout.</summary>
        public void SetBitSignal(int bit, Signal signal)
        {
            if ((uint)bit >= (uint)BitCount || _bitSignals[bit] == signal)
            {
                return;
            }

            _bitSignals[bit] = signal;
            _bitCells[bit].color = Tint(BitColor(signal));
            UpdateBusTitle();
        }

        public Signal GetBitSignal(int bit)
        {
            return _bitSignals[bit];
        }

        /// <summary>True when this view draws a display (LED, seven-segment, binary, hex or decimal).</summary>
        public bool IsDisplay => _display.IsActive;

        /// <summary>Input bits a display reads (8 for seven-segment: a–g, dp).</summary>
        public int DisplayInputCount => _display.InputCount;

        /// <summary>Seven-segment digits a display draws.</summary>
        public int DisplayDigitCount => _display.DigitCount;

        /// <summary>Plain-text reading of a display, e.g. "ON", "A5", "-91", "1010" or "?".</summary>
        public string DisplayText => _display.Text;

        /// <summary>Sets one display input; call <see cref="CommitDisplay"/> after the last change.</summary>
        public void SetDisplayInput(int index, Signal signal)
        {
            _display.SetInput(index, signal);
        }

        /// <summary>Decodes the display inputs and redraws lamps or digits.</summary>
        public void CommitDisplay()
        {
            _display.Commit();
        }

        public void ResetDisplay()
        {
            if (_display.IsActive)
            {
                _display.Reset();
            }
        }

        public Signal GetDisplayInput(int index)
        {
            return _display.GetInput(index);
        }

        /// <summary>Segment mask of one display digit (0 = leftmost; bit 0 = a … bit 6 = g).</summary>
        public byte GetDisplayDigit(int index)
        {
            return _display.GetDigit(index);
        }

        /// <summary>The readout shown on a bus boundary, e.g. "A = 10" or "A = ?" when a bit is not binary.</summary>
        public string BusText => _title.text;

        /// <summary>Boundary indicator state (switch value or lamp).</summary>
        public void SetStateSignal(Signal signal)
        {
            if (_stateSignal == signal)
            {
                return;
            }

            _stateSignal = signal;
            ApplyState();
        }

        public void SetPinHighlight(bool input, int index, bool highlighted)
        {
            List<SpriteRenderer> rings = input ? _inputRings : _outputRings;
            if ((uint)index < (uint)rings.Count)
            {
                rings[index].gameObject.SetActive(highlighted);
            }
        }

        public void ClearPinHighlights()
        {
            for (int i = 0; i < _inputRings.Count; i++)
            {
                _inputRings[i].gameObject.SetActive(false);
            }

            for (int i = 0; i < _outputRings.Count; i++)
            {
                _outputRings[i].gameObject.SetActive(false);
            }
        }

        private void ConfigurePins(List<SpriteRenderer> pins, List<SpriteRenderer> rings, List<Signal> signals,
            Vector2[] offsets, string prefix)
        {
            while (pins.Count < offsets.Length)
            {
                pins.Add(CreateSprite(transform, prefix + "Pin", RenderAssets.Circle, _theme, SortingOrders.Pin));
                SpriteRenderer ring = CreateSprite(transform, prefix + "Ring", RenderAssets.Circle, _theme,
                    SortingOrders.PinHighlight);
                rings.Add(ring);
                signals.Add(Signal.Low);
            }

            float pinSize = _theme.PinRadius * 2f;
            for (int i = 0; i < pins.Count; i++)
            {
                bool active = i < offsets.Length;
                pins[i].gameObject.SetActive(active);
                rings[i].gameObject.SetActive(false);
                if (!active)
                {
                    continue;
                }

                pins[i].transform.localPosition = offsets[i];
                pins[i].transform.localScale = Vector3.one * pinSize;
                pins[i].color = Tint(PinColor(Signal.Low));
                rings[i].transform.localPosition = offsets[i];
                rings[i].transform.localScale = Vector3.one * (pinSize * 2.4f);
                rings[i].color = new Color(_theme.CompatiblePin.r, _theme.CompatiblePin.g, _theme.CompatiblePin.b, 0.4f);
                signals[i] = Signal.Low;
            }
        }

        private void ConfigureBitCells(Vector2[] cells)
        {
            while (_bitCells.Count < cells.Length)
            {
                SpriteRenderer cell = CreateSprite(transform, "Bit", RenderAssets.RoundedRect, _theme, SortingOrders.GateState);
                cell.drawMode = SpriteDrawMode.Sliced;
                _bitCells.Add(cell);
                _bitSignals.Add(Signal.Low);
            }

            for (int i = 0; i < _bitCells.Count; i++)
            {
                bool active = i < cells.Length;
                _bitCells[i].gameObject.SetActive(active);
                if (!active)
                {
                    continue;
                }

                _bitCells[i].transform.localPosition = cells[i];
                _bitCells[i].size = Vector2.one * GateLayout.BitCellSize;
                _bitSignals[i] = Signal.Low;
                _bitCells[i].color = Tint(BitColor(Signal.Low));
            }

            if (cells.Length > 0)
            {
                UpdateBusTitle();
            }
        }

        private Color BitColor(Signal signal)
        {
            // LOW cells use the outline colour so every bit stays visible against the boundary fill.
            return signal == Signal.Low ? _theme.GateOutline : _theme.SignalColor(signal);
        }

        private void UpdateBusTitle()
        {
            ulong value = 0;
            bool binary = true;
            for (int bit = 0; bit < BitCount; bit++)
            {
                Signal signal = _bitSignals[bit];
                if (signal == Signal.High)
                {
                    value |= 1UL << bit;
                }
                else if (signal != Signal.Low)
                {
                    binary = false;
                }
            }

            _busText.Clear();
            _busText.Append(_geometry.Title).Append(" = ");
            if (binary)
            {
                _busText.Append(value);
            }
            else
            {
                _busText.Append('?');
            }

            _title.SetText(_busText);
        }

        private void ConfigureLabels(List<TextMeshPro> labels, string[] names, Vector2[] pins, string prefix)
        {
            while (labels.Count < names.Length)
            {
                TextMeshPro label = CreateText(transform, prefix, 2.2f, _theme, FontWeight.Regular);
                label.rectTransform.sizeDelta = new Vector2(3f, 0.6f);
                labels.Add(label);
            }

            for (int i = 0; i < labels.Count; i++)
            {
                bool active = i < names.Length;
                labels[i].gameObject.SetActive(active);
                if (!active)
                {
                    continue;
                }

                // Place the label just inside the body, next to its pin, aligned towards the pin.
                Vector2 pin = pins[i];
                Vector2 inward = Mathf.Abs(pin.x) >= Mathf.Abs(pin.y)
                    ? new Vector2(-Mathf.Sign(pin.x), 0f)
                    : new Vector2(0f, -Mathf.Sign(pin.y));
                TextMeshPro label = labels[i];
                label.text = names[i] ?? string.Empty;
                label.color = Tint(_theme.GateSubtext);
                label.sortingOrder = _title.sortingOrder;
                bool horizontal = inward.y == 0f;
                label.alignment = !horizontal
                    ? TextAlignmentOptions.Center
                    : inward.x > 0f ? TextAlignmentOptions.MidlineLeft : TextAlignmentOptions.MidlineRight;
                label.rectTransform.pivot = !horizontal
                    ? new Vector2(0.5f, 0.5f)
                    : inward.x > 0f ? new Vector2(0f, 0.5f) : new Vector2(1f, 0.5f);
                label.transform.localPosition = pin + inward * (horizontal ? 0.18f : 0.3f);
            }
        }

        private void ApplyOutline()
        {
            if (_geometry == null)
            {
                return;
            }

            bool isCustom = _geometry.Kind == ComponentKind.Custom;
            Color color = isCustom ? _theme.CustomGateOutline : _theme.GateOutline;
            float thickness = _theme.OutlineThickness;
            if (_ghost && !_ghostValid)
            {
                color = _theme.Invalid;
            }
            else if (_selected)
            {
                color = _theme.Selection;
                thickness = _theme.SelectedOutlineThickness;
            }
            else if (_hovered)
            {
                color = Color.Lerp(color, _theme.Hover, 0.6f);
            }

            _outline.size = _geometry.Size + Vector2.one * (thickness * 2f);
            _outline.color = Tint(color);
        }

        private void ApplyState()
        {
            if (_geometry == null || !_state.gameObject.activeSelf)
            {
                return;
            }

            Color color = _stateSignal == Signal.High
                ? _theme.SignalColor(Signal.High)
                : _stateSignal == Signal.Low ? _theme.ButtonNormal : _theme.SignalColor(_stateSignal);
            _state.color = Tint(color);
            _stateRing.color = Tint(_stateSignal == Signal.High ? _theme.SignalColor(Signal.High) : _theme.GateOutline);

            // Lit switches and lamps glow softly; the glow is hidden entirely otherwise.
            bool glow = _stateSignal == Signal.High && _theme.GlowStrength > 0f && !_ghost;
            _stateGlow.gameObject.SetActive(glow);
            if (glow)
            {
                Color halo = _theme.SignalColor(Signal.High);
                halo.a = Mathf.Clamp01(_theme.GlowStrength * 2.2f);
                _stateGlow.color = halo;
            }
        }

        /// <summary>
        /// Pin colour: LOW pins use a quiet neutral that stays visible on the gate body; other states use the
        /// signal colour.
        /// </summary>
        private Color PinColor(Signal signal)
        {
            return signal == Signal.Low ? _theme.Hover : _theme.SignalColor(signal);
        }

        private void ApplyGhostTint()
        {
            if (_geometry == null)
            {
                return;
            }

            for (int i = 0; i < BitCount; i++)
            {
                _bitCells[i].color = Tint(BitColor(_bitSignals[i]));
            }

            Color fill = _geometry.Kind == ComponentKind.InputBoundary || _geometry.Kind == ComponentKind.OutputBoundary ||
                _geometry.Kind == ComponentKind.Clock || _geometry.Kind == ComponentKind.Display
                ? _theme.BoundaryFill
                : _geometry.Kind == ComponentKind.Custom ? _theme.CustomGateFill : _theme.GateFill;
            _fill.color = Tint(fill);
            _title.color = Tint(_theme.GateText);
            _subtitle.color = Tint(_theme.GateSubtext);
            for (int i = 0; i < _inputLabels.Count; i++)
            {
                _inputLabels[i].color = Tint(_theme.GateSubtext);
                _inputLabels[i].sortingOrder = _title.sortingOrder;
            }

            for (int i = 0; i < _outputLabels.Count; i++)
            {
                _outputLabels[i].color = Tint(_theme.GateSubtext);
                _outputLabels[i].sortingOrder = _title.sortingOrder;
            }
            ApplyState();
            for (int i = 0; i < _inputPins.Count; i++)
            {
                _inputPins[i].color = Tint(PinColor(_inputSignals[i]));
            }

            for (int i = 0; i < _outputPins.Count; i++)
            {
                _outputPins[i].color = Tint(PinColor(_outputSignals[i]));
            }

            _shadow.color = _theme.GateShadow;
        }

        private Color Tint(Color color)
        {
            if (_ghost)
            {
                color.a *= 0.55f;
            }

            return color;
        }

        private static void SetPinOrder(List<SpriteRenderer> pins, List<SpriteRenderer> rings, int offset)
        {
            for (int i = 0; i < pins.Count; i++)
            {
                pins[i].sortingOrder = SortingOrders.Pin + offset;
                rings[i].sortingOrder = SortingOrders.PinHighlight + offset;
            }
        }

        private static SpriteRenderer CreateSprite(Transform parent, string name, Sprite sprite, VisualTheme theme, int order)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var renderer = go.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.sharedMaterial = theme.UnlitMaterial;
            renderer.sortingOrder = order;
            return renderer;
        }

        /// <summary>How much larger than the body the shadow is (world units), and how far it drops.</summary>
        private const float ShadowSpread = 1.0f;
        private const float ShadowDrop = 0.14f;

        private static TextMeshPro CreateText(Transform parent, string name, float size, VisualTheme theme, FontWeight weight)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var text = go.AddComponent<TextMeshPro>();
            text.font = theme.UiFont;
            text.fontSize = size;
            text.fontStyle = FontStyles.Normal;
            text.fontWeight = weight;
            text.alignment = TextAlignmentOptions.Center;
            text.enableWordWrapping = false;
            text.overflowMode = TextOverflowModes.Overflow;
            text.sortingOrder = SortingOrders.Text;
            text.raycastTarget = false;
            return text;
        }
    }
}
