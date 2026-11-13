using DigitalLogicSandbox.Core.Signals;
using UnityEngine;

namespace DigitalLogicSandbox.Rendering
{
    /// <summary>
    /// The application's single visual language: workspace colours, signal styling, sizes and the UI palette.
    /// Signals are distinguished by colour <em>and</em> line weight so the state is readable without colour vision.
    /// </summary>
    [CreateAssetMenu(fileName = "VisualTheme", menuName = "Digital Logic Sandbox/Visual Theme")]
    public sealed class VisualTheme : ScriptableObject
    {
        [Header("Materials")]
        [SerializeField] private Material _unlitMaterial;
        [SerializeField] private Material _gridMaterial;

        [Header("Workspace")]
        [SerializeField] private Color _workspaceBackground = Hex(0x1A1C21);
        [SerializeField] private Color _gridMinor = Hex(0x22252C);
        [SerializeField] private Color _gridMajor = Hex(0x2C3039);

        [Header("Gates")]
        [SerializeField] private Color _gateFill = Hex(0x2A2F3C);
        [SerializeField] private Color _gateOutline = Hex(0x4A5268);
        [SerializeField] private Color _customGateFill = Hex(0x2B2A40);
        [SerializeField] private Color _customGateOutline = Hex(0x6B5FB5);
        [SerializeField] private Color _boundaryFill = Hex(0x252A33);
        [SerializeField] private Color _gateText = Hex(0xE6E9EF);
        [SerializeField] private Color _gateSubtext = Hex(0x8A93A6);
        [SerializeField] private Color _selection = Hex(0x4DA3FF);
        [SerializeField] private Color _hover = Hex(0x7A8499);
        [SerializeField] private Color _compatiblePin = Hex(0xFFD166);
        [SerializeField] private Color _invalid = Hex(0xFF5D73);

        [Header("Signals")]
        [SerializeField] private Color _signalLow = Hex(0x596172);
        [SerializeField] private Color _signalHigh = Hex(0x3DDC97);
        [SerializeField] private Color _signalUnknown = Hex(0xF5A524);
        [SerializeField] private Color _signalHighImpedance = Hex(0x9B8AFB);

        [Header("Sizes (world units)")]
        [SerializeField] private float _wireWidth = 0.07f;
        [SerializeField] private float _wireWidthHigh = 0.12f;
        [SerializeField] private float _pinRadius = 0.16f;
        [SerializeField] private float _outlineThickness = 0.06f;
        [SerializeField] private float _selectedOutlineThickness = 0.12f;

        [Header("UI")]
        [SerializeField] private Color _panelBackground = Hex(0x15171B);
        [SerializeField] private Color _panelBorder = Hex(0x2A2E36);
        [SerializeField] private Color _panelHeader = Hex(0x1C1F25);
        [SerializeField] private Color _buttonNormal = Hex(0x252932);
        [SerializeField] private Color _buttonHover = Hex(0x30353F);
        [SerializeField] private Color _buttonPressed = Hex(0x3A404C);
        [SerializeField] private Color _textPrimary = Hex(0xE6E9EF);
        [SerializeField] private Color _textSecondary = Hex(0x8A93A6);
        [SerializeField] private Color _accent = Hex(0x4DA3FF);
        [SerializeField] private Color _error = Hex(0xFF5D73);
        [SerializeField] private Color _warning = Hex(0xF5A524);

        public Material UnlitMaterial => _unlitMaterial;

        public Material GridMaterial => _gridMaterial;

        public Color WorkspaceBackground => _workspaceBackground;

        public Color GridMinor => _gridMinor;

        public Color GridMajor => _gridMajor;

        public Color GateFill => _gateFill;

        public Color GateOutline => _gateOutline;

        public Color CustomGateFill => _customGateFill;

        public Color CustomGateOutline => _customGateOutline;

        public Color BoundaryFill => _boundaryFill;

        public Color GateText => _gateText;

        public Color GateSubtext => _gateSubtext;

        public Color Selection => _selection;

        public Color Hover => _hover;

        public Color CompatiblePin => _compatiblePin;

        public Color Invalid => _invalid;

        public float PinRadius => _pinRadius;

        public float OutlineThickness => _outlineThickness;

        public float SelectedOutlineThickness => _selectedOutlineThickness;

        public Color PanelBackground => _panelBackground;

        public Color PanelBorder => _panelBorder;

        public Color PanelHeader => _panelHeader;

        public Color ButtonNormal => _buttonNormal;

        public Color ButtonHover => _buttonHover;

        public Color ButtonPressed => _buttonPressed;

        public Color TextPrimary => _textPrimary;

        public Color TextSecondary => _textSecondary;

        public Color Accent => _accent;

        public Color Error => _error;

        public Color Warning => _warning;

        public Color SignalColor(Signal signal)
        {
            switch (signal)
            {
                case Signal.High:
                    return _signalHigh;
                case Signal.Unknown:
                    return _signalUnknown;
                case Signal.HighImpedance:
                    return _signalHighImpedance;
                default:
                    return _signalLow;
            }
        }

        /// <summary>HIGH wires are thicker than LOW wires: state stays readable without relying on colour.</summary>
        public float WireWidth(Signal signal)
        {
            return signal == Signal.High || signal == Signal.Unknown ? _wireWidthHigh : _wireWidth;
        }

        /// <summary>Assigns materials (used when creating the theme asset and for runtime fallbacks).</summary>
        public void SetMaterials(Material unlit, Material grid)
        {
            _unlitMaterial = unlit;
            _gridMaterial = grid;
        }

        /// <summary>
        /// Ensures materials exist even if the asset was created without them (e.g. a runtime default in tests).
        /// </summary>
        public void EnsureMaterials()
        {
            if (_unlitMaterial == null)
            {
                _unlitMaterial = new Material(Shader.Find("DigitalLogicSandbox/Unlit")) { name = "DLS Unlit (runtime)" };
            }

            if (_gridMaterial == null)
            {
                _gridMaterial = new Material(Shader.Find("DigitalLogicSandbox/Grid")) { name = "DLS Grid (runtime)" };
            }
        }

        public static VisualTheme CreateRuntimeDefault()
        {
            var theme = CreateInstance<VisualTheme>();
            theme.name = "VisualTheme (runtime default)";
            theme.EnsureMaterials();
            return theme;
        }

        private static Color Hex(int rgb)
        {
            return new Color(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f, 1f);
        }
    }
}
