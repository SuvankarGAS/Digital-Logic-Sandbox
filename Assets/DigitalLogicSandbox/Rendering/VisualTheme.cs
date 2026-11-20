using DigitalLogicSandbox.Core.Signals;
using TMPro;
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
        [Tooltip("Material with the DigitalLogicSandbox/Wire shader (anti-aliased signed-distance wires with glow).")]
        [SerializeField] private Material _wireMaterial;

        [Header("Fonts")]
        [Tooltip("Text font for the whole application (UI and workspace). Falls back to Resources/DLSFonts.")]
        [SerializeField] private TMP_FontAsset _uiFont;
        [Tooltip("Icon font (Lucide). Glyphs are listed in DigitalLogicSandbox.UI.Icons.")]
        [SerializeField] private TMP_FontAsset _iconFont;

        [Header("Workspace")]
        [SerializeField] private Color _workspaceBackground = Hex(0x0D0F15);
        [SerializeField] private Color _gridMinor = Hex(0x252A3A);
        [SerializeField] private Color _gridMajor = Hex(0x363E55);
        [SerializeField] private Color _vignette = new Color(0.02f, 0.025f, 0.04f, 0.85f);

        [Header("Gates")]
        [SerializeField] private Color _gateFill = Hex(0x181B25);
        [SerializeField] private Color _gateOutline = Hex(0x272C3B);
        [SerializeField] private Color _customGateFill = Hex(0x1A1A29);
        [SerializeField] private Color _customGateOutline = Hex(0x3B3762);
        [SerializeField] private Color _boundaryFill = Hex(0x141720);
        [SerializeField] private Color _gateText = Hex(0xE6E8F0);
        [SerializeField] private Color _gateSubtext = Hex(0x737A90);
        [SerializeField] private Color _gateShadow = new Color(0f, 0f, 0f, 0.55f);
        [SerializeField] private Color _selection = Hex(0x8FA3FF);
        [SerializeField] private Color _hover = Hex(0x4C5468);
        [SerializeField] private Color _compatiblePin = Hex(0xF6CF8C);
        [SerializeField] private Color _invalid = Hex(0xFF7D8C);

        [Header("Signals")]
        [SerializeField] private Color _signalLow = Hex(0x323849);
        [SerializeField] private Color _signalHigh = Hex(0x5CE8D0);
        [SerializeField] private Color _signalUnknown = Hex(0xF4B860);
        [SerializeField] private Color _signalHighImpedance = Hex(0xA98CF8);
        [Tooltip("LED display colours 1-5 (Red, Orange, Green, Blue, Pink). Colour 0 is the HIGH signal colour.")]
        [SerializeField] private Color[] _ledColors =
        {
            Hex(0xFF6B7A), Hex(0xFF9F43), Hex(0x8BE36B), Hex(0x63A9FF), Hex(0xFF7AD9),
        };
        [Tooltip("Opacity of the soft glow drawn around energized wires and pins (0 disables it).")]
        [SerializeField, Range(0f, 1f)] private float _glowStrength = 0.2f;

        [Header("Sizes (world units)")]
        [SerializeField] private float _wireWidth = 0.055f;
        [SerializeField] private float _wireWidthHigh = 0.085f;
        [SerializeField] private float _pinRadius = 0.12f;
        [Tooltip("How far the glow of an energized wire reaches beyond its edge (world units).")]
        [SerializeField] private float _wireGlowRadius = 0.26f;
        [SerializeField] private float _outlineThickness = 0.035f;
        [SerializeField] private float _selectedOutlineThickness = 0.055f;

        [Header("UI")]
        [SerializeField] private Color _panelBackground = new Color(0.075f, 0.084f, 0.112f, 1f); // opaque: glowing gate text showed through at 0.96
        [SerializeField] private Color _panelBorder = Hex(0x252A38);
        [SerializeField] private Color _panelHeader = Hex(0x171A23);
        [SerializeField] private Color _buttonNormal = Hex(0x1D212C);
        [SerializeField] private Color _buttonHover = Hex(0x272C3A);
        [SerializeField] private Color _buttonPressed = Hex(0x323849);
        [SerializeField] private Color _textPrimary = Hex(0xE6E8F0);
        [SerializeField] private Color _textSecondary = Hex(0x8C93A8);
        [SerializeField] private Color _textMuted = Hex(0x687088);
        [SerializeField] private Color _accent = Hex(0x8FA3FF);
        [SerializeField] private Color _error = Hex(0xFF7D8C);
        [SerializeField] private Color _warning = Hex(0xF4B860);
        [SerializeField] private Color _scrim = new Color(0.02f, 0.025f, 0.04f, 0.62f);
        [SerializeField] private Color _uiShadow = new Color(0f, 0f, 0f, 0.5f);

        public Material UnlitMaterial => _unlitMaterial;

        public Material GridMaterial => _gridMaterial;

        public Material WireMaterial => _wireMaterial;

        public float WireGlowRadius => _wireGlowRadius;

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

        /// <summary>
        /// Panel colour made fully opaque, for modal dialogs: even if a theme makes panels slightly translucent, gate
        /// titles behind a dialog must not show through its text.
        /// </summary>
        public Color DialogBackground => new Color(_panelBackground.r, _panelBackground.g, _panelBackground.b, 1f);

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

        public Color TextMuted => _textMuted;

        /// <summary>Dimmed backdrop behind modal dialogs.</summary>
        public Color Scrim => _scrim;

        /// <summary>Soft drop shadow under floating panels.</summary>
        public Color UiShadow => _uiShadow;

        /// <summary>Accent at low opacity: selected rows, active toggles, focus rings.</summary>
        public Color AccentSoft => new Color(_accent.r, _accent.g, _accent.b, 0.16f);

        public Color Vignette => _vignette;

        public Color GateShadow => _gateShadow;

        public float GlowStrength => _glowStrength;

        /// <summary>Application text font (never null once resolved; falls back to the TMP default).</summary>
        public TMP_FontAsset UiFont
        {
            get
            {
                if (_uiFont == null)
                {
                    _uiFont = Resources.Load<TMP_FontAsset>("DLSFonts/Nunito-Regular SDF");
                }

                return _uiFont != null ? _uiFont : TMP_Settings.defaultFontAsset;
            }
        }

        /// <summary>Icon font; null only if the asset is missing (icons then fall back through the text font).</summary>
        public TMP_FontAsset IconFont
        {
            get
            {
                if (_iconFont == null)
                {
                    _iconFont = Resources.Load<TMP_FontAsset>("DLSFonts/lucide SDF");
                }

                return _iconFont != null ? _iconFont : UiFont;
            }
        }

        /// <summary>Lit colour of an LED display (index 0 = the HIGH signal colour; see <c>LedColors</c>).</summary>
        public Color LedColor(int index)
        {
            return index > 0 && _ledColors != null && index - 1 < _ledColors.Length
                ? _ledColors[index - 1]
                : _signalHigh;
        }

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
                _unlitMaterial = new Material(FindShader("DigitalLogicSandbox/Unlit")) { name = "DLS Unlit (runtime)" };
            }

            if (_gridMaterial == null)
            {
                _gridMaterial = new Material(FindShader("DigitalLogicSandbox/Grid")) { name = "DLS Grid (runtime)" };
            }

            if (_wireMaterial == null)
            {
                _wireMaterial = new Material(FindShader("DigitalLogicSandbox/Wire")) { name = "DLS Wire (runtime)" };
            }
        }

        /// <summary>
        /// Finds a shader by name. If it was left out of a build, logs which one and falls back to a built-in shader
        /// so the app still draws (flat, without the custom effects) instead of throwing.
        /// </summary>
        private static Shader FindShader(string name)
        {
            Shader shader = Shader.Find(name);
            if (shader != null)
            {
                return shader;
            }

            Debug.LogError("Shader '" + name + "' is missing from this build; using Sprites/Default instead.");
            return Shader.Find("Sprites/Default");
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
