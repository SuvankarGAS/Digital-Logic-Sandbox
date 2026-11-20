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
    /// <para>
    /// Digits look like a real LED display: pointed (hexagonal) segments that meet at the corners with an even gap,
    /// a slight forward lean, faint unlit segments, a soft glow around lit ones, and a dark inset screen. Every
    /// display style lights in its chosen colour (<see cref="LedColors"/>).
    /// </para>
    /// <para>
    /// Lamps (the LED and binary displays) look like indicator LEDs: a dark housing ring, a domed lens that shows a dim
    /// tint of its colour while off, a small highlight, and a glow that is clamped to stay inside the gate body so it
    /// never covers the pin or the node label.
    /// </para>
    /// </summary>
    internal sealed class DisplayVisual
    {
        /// <summary>Segment thickness, in digit units (a digit is <see cref="GateLayout.DigitWidth"/> wide).</summary>
        private const float Thickness = 0.17f;

        /// <summary>Gap between neighbouring segment tips.</summary>
        private const float SegmentGap = 0.03f;

        /// <summary>Forward lean of the digits (tan of about 7°).</summary>
        private const float Lean = 0.1228f;

        private const float LeanDegrees = 7f;
        private const float DotDiameter = 0.22f;
        private const float DotGap = 0.07f;
        private const float GlowThickness = Thickness * 3.4f;
        private const float ScreenPadding = 0.3f;

        /// <summary>Binary lamp lens and housing diameters (the lamp pitch is <see cref="GateLayout.LampPitch"/>).</summary>
        private const float LampLens = 0.36f;
        private const float LampHousing = 0.46f;

        /// <summary>LED lens and housing, as a fraction of the (square) content area.</summary>
        private const float LedLens = 0.78f;
        private const float LedHousing = 1f;

        /// <summary>Halo diameter relative to the lens, before clamping to the body.</summary>
        private const float LampHalo = 2f;

        /// <summary>Least distance between a halo's edge and the body edge.</summary>
        private const float HaloMargin = 0.06f;

        private const int ScreenOrder = SortingOrders.GateState;
        private const int GlowOrder = SortingOrders.GateStateGlow;
        private const int LitOrder = SortingOrders.GateStateLit;
        private const int TopOrder = SortingOrders.GateStateTop;

        private readonly Transform _root;
        private readonly VisualTheme _theme;
        private readonly SpriteRenderer _screen;
        private readonly SpriteRenderer _bezel;
        private readonly List<SpriteRenderer> _segments = new List<SpriteRenderer>();
        private readonly List<SpriteRenderer> _segmentGlows = new List<SpriteRenderer>();
        private readonly List<SpriteRenderer> _dots = new List<SpriteRenderer>();
        private readonly List<SpriteRenderer> _dotGlows = new List<SpriteRenderer>();
        private readonly List<SpriteRenderer> _housings = new List<SpriteRenderer>();
        private readonly List<SpriteRenderer> _highlights = new List<SpriteRenderer>();

        private Signal[] _inputs = System.Array.Empty<Signal>();
        private byte[] _digits = System.Array.Empty<byte>();
        private DisplayStyle _style;
        private int _ledColor;
        private int _width;
        private int _digitCount;
        private int _lampCount;
        private int _dotCount;
        private bool _readable = true;
        private bool _ghost;
        private int _orderOffset;
        private ulong _high;
        private ulong _unknown;
        private Vector2 _bodyHalf;

        public DisplayVisual(Transform parent, VisualTheme theme)
        {
            var go = new GameObject("Display");
            go.transform.SetParent(parent, false);
            _root = go.transform;
            _theme = theme;
            _screen = CreateSprite("Screen", RenderAssets.RoundedRect, ScreenOrder);
            _screen.drawMode = SpriteDrawMode.Sliced;
            _bezel = CreateSprite("Bezel", RenderAssets.RoundedOutline, GlowOrder);
            _bezel.drawMode = SpriteDrawMode.Sliced;
            go.SetActive(false);
        }

        public bool IsActive { get; private set; }

        public int InputCount => _inputs.Length;

        public int DigitCount => _digitCount;

        public int LampCount => _lampCount;

        /// <summary>Plain-text reading, e.g. "ON", "A5", "-91", "1010", "7 (a b c)" or "?".</summary>
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

        /// <summary>Colour a lit segment or lamp is drawn in.</summary>
        public Color LitColor => _theme.LedColor(_ledColor);

        /// <summary>
        /// World bounds of lamp <paramref name="index"/>'s glow while it is shown (lit); false when it is hidden.
        /// </summary>
        public bool TryGetLampGlow(int index, out Bounds bounds)
        {
            bounds = default;
            if (!IsActive || (uint)index >= (uint)_lampCount)
            {
                return false;
            }

            SpriteRenderer glow = _dotGlows[index];
            if (!glow.enabled || !glow.gameObject.activeInHierarchy)
            {
                return false;
            }

            bounds = glow.bounds;
            return true;
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
            _bodyHalf = geometry.Size * 0.5f;
            _digitCount = DisplayEncoding.DigitCount(_style, _width);
            _lampCount = DisplayEncoding.LampCount(_style, _width);
            _dotCount = _lampCount + (_style == DisplayStyle.SevenSegment ? 1 : 0);
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
            _orderOffset = orderOffset;
            _screen.sortingOrder = ScreenOrder + orderOffset;
            _bezel.sortingOrder = GlowOrder + orderOffset;
            SetOrder(_segmentGlows, GlowOrder + orderOffset);
            SetOrder(_dotGlows, GlowOrder + orderOffset);
            SetOrder(_segments, LitOrder + orderOffset);
            SetOrder(_dots, LitOrder + orderOffset);
            SetOrder(_housings, ScreenOrder + orderOffset);
            SetOrder(_highlights, TopOrder + orderOffset);
            if (IsActive)
            {
                ApplyColors();
            }
        }

        private static void SetOrder(List<SpriteRenderer> renderers, int order)
        {
            for (int i = 0; i < renderers.Count; i++)
            {
                renderers[i].sortingOrder = order;
            }
        }

        // ------------------------------------------------------------------------------------------------
        // Layout
        // ------------------------------------------------------------------------------------------------

        /// <summary>Unscaled size of the drawn content: digits with their lean (and the decimal point), or lamps.</summary>
        private Vector2 DesignSize()
        {
            float h = GateLayout.DigitHeight;
            switch (_style)
            {
                case DisplayStyle.SevenSegment:
                    return new Vector2(GateLayout.DigitWidth + Lean * h + DotGap + DotDiameter, h);
                case DisplayStyle.Led:
                    return Vector2.one;
                case DisplayStyle.Binary:
                    GateLayout.DisplaySize(_style, _width, out Rect area);
                    return area.size;
                default:
                    return new Vector2(_digitCount * GateLayout.DigitWidth + (_digitCount - 1) * GateLayout.DigitGap +
                        Lean * h, h);
            }
        }

        private void Layout(Rect area)
        {
            Vector2 design = DesignSize();
            float scale = Mathf.Min(area.width / design.x, area.height / design.y);
            Vector2 center = area.center;

            int segments = _digitCount * 7;
            Ensure(_segments, segments, RenderAssets.Segment, "Segment", LitOrder, sliced: true);
            Ensure(_segmentGlows, segments, RenderAssets.SegmentGlow, "Segment Glow", GlowOrder, sliced: true);
            Ensure(_dots, _dotCount, RenderAssets.Circle, "Dot", LitOrder, sliced: false);
            Ensure(_dotGlows, _dotCount, RenderAssets.Glow, "Dot Glow", GlowOrder, sliced: false);
            Ensure(_housings, _lampCount, RenderAssets.Circle, "Lamp Housing", ScreenOrder, sliced: false);
            Ensure(_highlights, _lampCount, RenderAssets.Circle, "Lamp Highlight", TopOrder, sliced: false);

            bool hasScreen = _digitCount > 0;
            _screen.gameObject.SetActive(hasScreen);
            _bezel.gameObject.SetActive(hasScreen);
            if (hasScreen)
            {
                // The screen fills the display area (a tall window for a single digit), never less than the digits.
                Vector2 size = Vector2.Max(design * scale, area.size) + Vector2.one * ScreenPadding;
                _screen.transform.localPosition = center;
                _screen.size = size;
                _bezel.transform.localPosition = center;
                _bezel.size = size;
            }

            float h = GateLayout.DigitHeight;
            for (int digit = 0; digit < _digitCount; digit++)
            {
                // The lean widens each digit by Lean·h; centre the leaning digits in the design box.
                float x = -design.x * 0.5f + Lean * h * 0.5f + GateLayout.DigitWidth * 0.5f +
                    digit * (GateLayout.DigitWidth + GateLayout.DigitGap);
                PlaceDigit(digit, new Vector2(x, 0f), center, scale);
            }

            if (_style == DisplayStyle.SevenSegment)
            {
                // Decimal point: bottom right of the digit, following the lean.
                float y = -h * 0.5f + DotDiameter * 0.5f;
                float x = -design.x * 0.5f + Lean * h * 0.5f + GateLayout.DigitWidth + DotGap + DotDiameter * 0.5f +
                    y * Lean;
                PlaceDot(0, center + new Vector2(x, y) * scale, DotDiameter * scale, DotDiameter * scale * 2.4f);
            }
            else if (_style == DisplayStyle.Led)
            {
                // The halo hugs the lamp: it stays clear of a label along the top of the body.
                PlaceLamp(0, center, LedLens * scale, LedHousing * scale, Mathf.Min(area.width, area.height) * 1.3f);
            }
            else if (_style == DisplayStyle.Binary)
            {
                // Most significant bit on the left.
                for (int bit = 0; bit < _lampCount; bit++)
                {
                    float x = -design.x * 0.5f + GateLayout.LampPitch * (0.5f + (_lampCount - 1 - bit));
                    // Neighbouring halos may overlap a little, but never pile up over the caption.
                    PlaceLamp(bit, center + new Vector2(x, 0f) * scale, LampLens * scale, LampHousing * scale,
                        GateLayout.LampPitch * 1.3f * scale);
                }
            }

            Hide(_segments, segments);
            Hide(_segmentGlows, segments);
            Hide(_dots, _dotCount);
            Hide(_dotGlows, _dotCount);
            Hide(_housings, _lampCount);
            Hide(_highlights, _lampCount);
        }

        private void PlaceDigit(int digit, Vector2 offset, Vector2 center, float scale)
        {
            const float t = Thickness;
            const float g = SegmentGap;
            float w = GateLayout.DigitWidth;
            float h = GateLayout.DigitHeight;

            // Segment centre lines: horizontals run between the vertical centre lines, verticals between the
            // horizontal ones; with 45° tips both meet along the corner diagonals, separated by the gap.
            float edgeX = w * 0.5f - t * 0.5f;
            float edgeY = h * 0.5f - t * 0.5f;
            float horizontal = 2f * edgeX - 2f * g;
            float vertical = (edgeY - 2f * g) / Mathf.Cos(LeanDegrees * Mathf.Deg2Rad);
            int first = digit * 7;
            PlaceSegment(first + 0, offset, new Vector2(0f, edgeY), horizontal, false, center, scale); // a
            PlaceSegment(first + 1, offset, new Vector2(edgeX, edgeY * 0.5f), vertical, true, center, scale); // b
            PlaceSegment(first + 2, offset, new Vector2(edgeX, -edgeY * 0.5f), vertical, true, center, scale); // c
            PlaceSegment(first + 3, offset, new Vector2(0f, -edgeY), horizontal, false, center, scale); // d
            PlaceSegment(first + 4, offset, new Vector2(-edgeX, -edgeY * 0.5f), vertical, true, center, scale); // e
            PlaceSegment(first + 5, offset, new Vector2(-edgeX, edgeY * 0.5f), vertical, true, center, scale); // f
            PlaceSegment(first + 6, offset, Vector2.zero, horizontal, false, center, scale); // g
        }

        private void PlaceSegment(int index, Vector2 digitOffset, Vector2 local, float length, bool vertical,
            Vector2 center, float scale)
        {
            // Lean: shift by the height, and tilt the vertical bars by the same angle.
            Vector2 position = digitOffset + new Vector2(local.x + local.y * Lean, local.y);
            Quaternion rotation = vertical ? Quaternion.Euler(0f, 0f, 90f - LeanDegrees) : Quaternion.identity;

            SpriteRenderer segment = _segments[index];
            segment.gameObject.SetActive(true);
            Transform transform = segment.transform;
            transform.localPosition = center + position * scale;
            transform.localRotation = rotation;
            transform.localScale = Vector3.one * (Thickness * scale);
            segment.size = new Vector2(length / Thickness, 1f);

            SpriteRenderer glow = _segmentGlows[index];
            glow.gameObject.SetActive(true);
            Transform glowTransform = glow.transform;
            glowTransform.localPosition = segment.transform.localPosition;
            glowTransform.localRotation = rotation;
            glowTransform.localScale = Vector3.one * (GlowThickness * scale);
            glow.size = new Vector2((length + GlowThickness - Thickness) / GlowThickness, 1f);
        }

        /// <summary>An indicator lamp: housing ring, lens, highlight and a halo that stays inside the body.</summary>
        private void PlaceLamp(int index, Vector2 position, float lens, float housing, float maxHalo)
        {
            PlaceDot(index, position, lens, Mathf.Min(lens * LampHalo, maxHalo));

            SpriteRenderer ring = _housings[index];
            ring.gameObject.SetActive(true);
            ring.transform.localPosition = position;
            ring.transform.localScale = new Vector3(housing, housing, 1f);

            // A small reflection towards the upper left makes the lens read as a dome.
            SpriteRenderer highlight = _highlights[index];
            highlight.gameObject.SetActive(true);
            highlight.transform.localPosition = position + new Vector2(-0.2f, 0.22f) * lens;
            highlight.transform.localScale = new Vector3(lens * 0.3f, lens * 0.24f, 1f);
        }

        private void PlaceDot(int index, Vector2 position, float diameter, float halo)
        {
            SpriteRenderer dot = _dots[index];
            dot.gameObject.SetActive(true);
            dot.transform.localPosition = position;
            dot.transform.localScale = new Vector3(diameter, diameter, 1f);

            // Keep the halo inside the gate body: it must not cover the pin, the node label or a neighbour.
            float room = 2f * (Mathf.Min(_bodyHalf.x - Mathf.Abs(position.x), _bodyHalf.y - Mathf.Abs(position.y)) -
                HaloMargin);
            halo = Mathf.Max(diameter, Mathf.Min(halo, room));
            SpriteRenderer glow = _dotGlows[index];
            glow.gameObject.SetActive(true);
            glow.transform.localPosition = position;
            glow.transform.localScale = new Vector3(halo, halo, 1f);
        }

        // ------------------------------------------------------------------------------------------------
        // Colours
        // ------------------------------------------------------------------------------------------------

        private Color ScreenColor => Color.Lerp(_theme.WorkspaceBackground, Color.black, 0.35f);

        private void ApplyColors()
        {
            Color lit = _theme.LedColor(_ledColor);
            Color screen = ScreenColor;
            // An unlit segment is a faint shadow of the lit colour, like a real display that is switched off.
            Color segmentOff = Color.Lerp(screen, lit, 0.11f);
            _screen.color = Tint(screen);
            _bezel.color = Tint(Color.Lerp(_theme.GateOutline, screen, 0.35f));

            if (_style == DisplayStyle.SevenSegment)
            {
                for (int s = 0; s < 7; s++)
                {
                    SetSegment(s, _inputs[s], lit, segmentOff);
                }

                SetDot(0, _inputs[7], lit, segmentOff);
                return;
            }

            if (_digitCount > 0)
            {
                for (int digit = 0; digit < _digitCount; digit++)
                {
                    byte mask = _digits[digit];
                    for (int s = 0; s < 7; s++)
                    {
                        bool on = ((mask >> s) & 1) != 0;
                        // An unreadable bus shows warning-coloured dashes.
                        Signal state = !on ? Signal.Low : _readable ? Signal.High : Signal.Unknown;
                        SetSegment(digit * 7 + s, state, lit, segmentOff);
                    }
                }

                return;
            }

            // Lamps: a dark housing, and a lens that keeps a dim tint of its colour while off, like a real LED.
            Color housing = Color.Lerp(_theme.GateOutline, screen, 0.5f);
            Color lensOff = Color.Lerp(screen, lit, 0.24f);
            for (int i = 0; i < _lampCount; i++)
            {
                Signal state = _inputs[i];
                SetDot(i, state, lit, lensOff);
                _housings[i].color = Tint(housing);
                _highlights[i].color = Tint(new Color(1f, 1f, 1f, state == Signal.High ? 0.55f : 0.16f));
            }
        }

        private void SetSegment(int index, Signal state, Color lit, Color off)
        {
            _segments[index].color = Tint(StateColor(state, lit, off));
            SetGlow(_segmentGlows[index], state, lit, 1f);
        }

        private void SetDot(int index, Signal state, Color lit, Color off)
        {
            _dots[index].color = Tint(StateColor(state, lit, off));
            SetGlow(_dotGlows[index], state, lit, 0.9f);
        }

        private void SetGlow(SpriteRenderer glow, Signal state, Color lit, float strength)
        {
            bool on = state == Signal.High && !_ghost && _theme.GlowStrength > 0f;
            glow.enabled = on;
            if (on)
            {
                glow.color = new Color(lit.r, lit.g, lit.b, Mathf.Clamp01(_theme.GlowStrength * 2.2f) * 0.55f * strength);
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

        // ------------------------------------------------------------------------------------------------
        // Pooling
        // ------------------------------------------------------------------------------------------------

        private void Ensure(List<SpriteRenderer> pool, int count, Sprite sprite, string name, int order, bool sliced)
        {
            while (pool.Count < count)
            {
                SpriteRenderer renderer = CreateSprite(name, sprite, order + _orderOffset);
                if (sliced)
                {
                    renderer.drawMode = SpriteDrawMode.Sliced;
                }

                pool.Add(renderer);
            }
        }

        private static void Hide(List<SpriteRenderer> pool, int used)
        {
            for (int i = used; i < pool.Count; i++)
            {
                pool[i].gameObject.SetActive(false);
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
