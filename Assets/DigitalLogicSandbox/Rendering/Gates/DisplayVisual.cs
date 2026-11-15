using System.Collections.Generic;
using DigitalLogicSandbox.Core.Nodes;
using DigitalLogicSandbox.Core.Signals;
using DigitalLogicSandbox.Rendering.Displays;
using UnityEngine;

namespace DigitalLogicSandbox.Rendering.Gates
{
    /// <summary>
    /// The lamps or seven-segment digits drawn inside a display node (Phase D). Owned and pooled by its
    /// <see cref="GateView"/>: sprites are created only when a larger display needs more, and reused when the view is
    /// reconfigured. Inputs are pushed bit by bit and applied with <see cref="Commit"/>, which recolours only; it
    /// allocates nothing. Content is drawn upright and scaled to fit the (possibly rotated) display area.
    /// </summary>
    internal sealed class DisplayVisual
    {
        private const float SegmentThickness = 0.16f;
        private const float LampDiameter = 0.4f;
        private const float DecimalPointOffset = 0.2f;

        private readonly Transform _root;
        private readonly VisualTheme _theme;
        private readonly SpriteRenderer _screen;
        private readonly List<SpriteRenderer> _segments = new List<SpriteRenderer>();
        private readonly List<SpriteRenderer> _dots = new List<SpriteRenderer>();

        private Signal[] _inputs = System.Array.Empty<Signal>();
        private byte[] _digits = System.Array.Empty<byte>();
        private DisplayStyle _style;
        private int _ledColor;
        private int _width;
        private int _digitCount;
        private int _lampCount;
        private bool _readable = true;
        private bool _ghost;
        private ulong _high;
        private ulong _unknown;

        public DisplayVisual(Transform parent, VisualTheme theme)
        {
            var go = new GameObject("Display");
            go.transform.SetParent(parent, false);
            _root = go.transform;
            _theme = theme;
            _screen = CreateSprite("Screen", RenderAssets.RoundedRect, SortingOrders.GateState);
            _screen.drawMode = SpriteDrawMode.Sliced;
            go.SetActive(false);
        }

        public bool IsActive { get; private set; }

        public int InputCount => _inputs.Length;

        public int DigitCount => _digitCount;

        public int LampCount => _lampCount;

        /// <summary>Plain-text reading, e.g. "ON", "A5", "-91", "1010" or "?".</summary>
        public string Text => IsActive ? DisplayEncoding.FormatValue(_style, _width, _high, _unknown) : string.Empty;

        /// <summary>Segment mask shown by digit <paramref name="index"/> (0 = leftmost).</summary>
        public byte GetDigit(int index)
        {
            return _digits[index];
        }

        public Signal GetInput(int index)
        {
            return _inputs[index];
        }

        public void Configure(GateGeometry geometry)
        {
            IsActive = geometry.IsDisplay;
            _root.gameObject.SetActive(IsActive);
            if (!IsActive)
            {
                return;
            }

            _style = geometry.DisplayStyle;
            _ledColor = geometry.LedColor;
            _width = geometry.BusWidth;
            _digitCount = DisplayEncoding.DigitCount(_style, _width);
            _lampCount = DisplayEncoding.LampCount(_style, _width);
            int inputs = DisplayEncoding.InputCount(_style, _width);
            if (_inputs.Length != inputs)
            {
                _inputs = new Signal[inputs];
            }

            if (_digits.Length < _digitCount)
            {
                _digits = new byte[_digitCount];
            }

            for (int i = 0; i < _inputs.Length; i++)
            {
                _inputs[i] = Signal.Low;
            }

            Layout(geometry.DisplayArea);
            Commit();
        }

        public void SetInput(int index, Signal signal)
        {
            if ((uint)index < (uint)_inputs.Length)
            {
                _inputs[index] = signal;
            }
        }

        /// <summary>Back to all-LOW inputs (no simulation).</summary>
        public void Reset()
        {
            for (int i = 0; i < _inputs.Length; i++)
            {
                _inputs[i] = Signal.Low;
            }

            Commit();
        }

        /// <summary>Decodes the current inputs and recolours lamps and segments.</summary>
        public void Commit()
        {
            if (!IsActive)
            {
                return;
            }

            _high = 0;
            _unknown = 0;
            for (int i = 0; i < _inputs.Length; i++)
            {
                if (_inputs[i] == Signal.High)
                {
                    _high |= 1UL << i;
                }
                else if (_inputs[i] != Signal.Low)
                {
                    _unknown |= 1UL << i;
                }
            }

            _readable = _digitCount == 0 || DisplayEncoding.EncodeDigits(_style, _width, _high, _unknown, _digits);
            ApplyColors();
        }

        public void SetGhost(bool ghost, int orderOffset)
        {
            _ghost = ghost;
            _screen.sortingOrder = SortingOrders.GateState + orderOffset;
            for (int i = 0; i < _segments.Count; i++)
            {
                _segments[i].sortingOrder = SortingOrders.GateState + 1 + orderOffset;
            }

            for (int i = 0; i < _dots.Count; i++)
            {
                _dots[i].sortingOrder = SortingOrders.GateState + 1 + orderOffset;
            }

            if (IsActive)
            {
                ApplyColors();
            }
        }

        private void Layout(Rect area)
        {
            GateLayout.DisplaySize(_style, _width, out Rect design);
            Vector2 designSize = _style == DisplayStyle.SevenSegment
                ? new Vector2(GateLayout.DigitWidth + DecimalPointOffset * 2f + SegmentThickness, GateLayout.DigitHeight)
                : _style == DisplayStyle.Led ? Vector2.one : design.size;
            float scale = Mathf.Min(area.width / designSize.x, area.height / designSize.y);
            Vector2 center = area.center;

            int segments = _digitCount * 7;
            int dots = _lampCount + (_style == DisplayStyle.SevenSegment ? 1 : 0);
            Ensure(_segments, segments, RenderAssets.Square, "Segment");
            Ensure(_dots, dots, RenderAssets.Circle, "Dot");

            _screen.gameObject.SetActive(_digitCount > 0);
            if (_digitCount > 0)
            {
                _screen.transform.localPosition = center;
                _screen.size = designSize * scale + Vector2.one * 0.24f;
            }

            for (int digit = 0; digit < _digitCount; digit++)
            {
                float x = _style == DisplayStyle.SevenSegment
                    ? -designSize.x * 0.5f + GateLayout.DigitWidth * 0.5f
                    : -designSize.x * 0.5f + GateLayout.DigitWidth * 0.5f + digit * (GateLayout.DigitWidth + GateLayout.DigitGap);
                PlaceDigit(digit, new Vector2(x, 0f), center, scale);
            }

            if (_style == DisplayStyle.SevenSegment)
            {
                var dp = new Vector2(-designSize.x * 0.5f + GateLayout.DigitWidth + DecimalPointOffset,
                    -GateLayout.DigitHeight * 0.5f + SegmentThickness * 0.5f);
                Place(_dots[0], center + dp * scale, Vector2.one * (SegmentThickness * 1.4f * scale));
            }
            else if (_style == DisplayStyle.Led)
            {
                Place(_dots[0], center, Vector2.one * scale);
            }
            else
            {
                // Binary: most significant bit on the left.
                for (int bit = 0; bit < _lampCount; bit++)
                {
                    float x = -designSize.x * 0.5f + GateLayout.LampPitch * (0.5f + (_lampCount - 1 - bit));
                    Place(_dots[bit], center + new Vector2(x, 0f) * scale, Vector2.one * (LampDiameter * scale));
                }
            }

            for (int i = segments; i < _segments.Count; i++)
            {
                _segments[i].gameObject.SetActive(false);
            }

            for (int i = dots; i < _dots.Count; i++)
            {
                _dots[i].gameObject.SetActive(false);
            }
        }

        private void PlaceDigit(int digit, Vector2 offset, Vector2 center, float scale)
        {
            const float t = SegmentThickness;
            float w = GateLayout.DigitWidth;
            float h = GateLayout.DigitHeight;
            var horizontal = new Vector2(w - t * 1.8f, t);
            var vertical = new Vector2(t, h * 0.5f - t * 1.1f);
            int first = digit * 7;
            Place(_segments[first + 0], center + (offset + new Vector2(0f, h * 0.5f - t * 0.5f)) * scale, horizontal * scale); // a
            Place(_segments[first + 1], center + (offset + new Vector2(w * 0.5f - t * 0.5f, h * 0.25f)) * scale, vertical * scale); // b
            Place(_segments[first + 2], center + (offset + new Vector2(w * 0.5f - t * 0.5f, -h * 0.25f)) * scale, vertical * scale); // c
            Place(_segments[first + 3], center + (offset + new Vector2(0f, -h * 0.5f + t * 0.5f)) * scale, horizontal * scale); // d
            Place(_segments[first + 4], center + (offset + new Vector2(-w * 0.5f + t * 0.5f, -h * 0.25f)) * scale, vertical * scale); // e
            Place(_segments[first + 5], center + (offset + new Vector2(-w * 0.5f + t * 0.5f, h * 0.25f)) * scale, vertical * scale); // f
            Place(_segments[first + 6], center + offset * scale, horizontal * scale); // g
        }

        private void ApplyColors()
        {
            Color lit = _style == DisplayStyle.Led ? _theme.LedColor(_ledColor) : _theme.SignalColor(Signal.High);
            Color screen = _theme.WorkspaceBackground;
            Color segmentOff = Color.Lerp(screen, _theme.GateOutline, 0.45f);
            _screen.color = Tint(screen);

            if (_style == DisplayStyle.SevenSegment)
            {
                for (int s = 0; s < 8; s++)
                {
                    Color color = StateColor(_inputs[s], lit, segmentOff);
                    if (s < 7)
                    {
                        _segments[s].color = Tint(color);
                    }
                    else
                    {
                        _dots[0].color = Tint(color);
                    }
                }

                return;
            }

            if (_digitCount > 0)
            {
                Color unreadable = _theme.SignalColor(Signal.Unknown);
                for (int digit = 0; digit < _digitCount; digit++)
                {
                    byte mask = _digits[digit];
                    for (int s = 0; s < 7; s++)
                    {
                        bool on = ((mask >> s) & 1) != 0;
                        _segments[digit * 7 + s].color = Tint(on ? (_readable ? lit : unreadable) : segmentOff);
                    }
                }

                return;
            }

            // Lamps sit on the body fill: LOW uses the outline colour so an unlit lamp stays visible.
            for (int i = 0; i < _lampCount; i++)
            {
                _dots[i].color = Tint(StateColor(_inputs[i], lit, _theme.GateOutline));
            }
        }

        private Color StateColor(Signal signal, Color lit, Color off)
        {
            return signal == Signal.High ? lit : signal == Signal.Low ? off : _theme.SignalColor(signal);
        }

        private Color Tint(Color color)
        {
            if (_ghost)
            {
                color.a *= 0.55f;
            }

            return color;
        }

        private static void Place(SpriteRenderer sprite, Vector2 position, Vector2 size)
        {
            sprite.gameObject.SetActive(true);
            sprite.transform.localPosition = position;
            sprite.transform.localScale = new Vector3(size.x, size.y, 1f);
        }

        private void Ensure(List<SpriteRenderer> pool, int count, Sprite sprite, string name)
        {
            while (pool.Count < count)
            {
                pool.Add(CreateSprite(name, sprite, SortingOrders.GateState + 1));
            }
        }

        private SpriteRenderer CreateSprite(string name, Sprite sprite, int order)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_root, false);
            var renderer = go.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.sharedMaterial = _theme.UnlitMaterial;
            renderer.sortingOrder = order;
            return renderer;
        }
    }
}
