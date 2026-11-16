using DigitalLogicSandbox.Rendering;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace DigitalLogicSandbox.UI
{
    /// <summary>
    /// Keeps a button's label or icon in step with its state: dimmed while not interactable, brighter on hover
    /// (icon buttons), accent-coloured while <see cref="Active"/> (a toggled tool such as the properties panel).
    /// Unity's colour tint only affects the background graphic, so this handles the foreground. It touches the
    /// label only when the state actually changes.
    /// </summary>
    public sealed class ButtonVisual : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        private Button _button;
        private VisualTheme _theme;
        private TextMeshProUGUI _label;
        private Color _labelColor;
        private bool _hoverBrightens;
        private bool _hovered;
        private bool _active;
        private int _appliedState = -1;

        /// <summary>Highlights the button as switched on (accent foreground and a soft accent tile).</summary>
        public bool Active
        {
            get => _active;
            set
            {
                if (_active != value)
                {
                    _active = value;
                    Apply();
                }
            }
        }

        public void Initialize(Button button, VisualTheme theme, TextMeshProUGUI label, bool hoverBrightens = false)
        {
            _button = button;
            _theme = theme;
            _label = label;
            _labelColor = label.color;
            _hoverBrightens = hoverBrightens;
            Apply();
        }

        /// <summary>Changes the resting foreground colour (e.g. for primary buttons).</summary>
        public void SetLabelColor(Color color)
        {
            _labelColor = color;
            _appliedState = -1;
            Apply();
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            _hovered = true;
            Apply();
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            _hovered = false;
            Apply();
        }

        private void OnDisable()
        {
            _hovered = false;
        }

        private void LateUpdate()
        {
            Apply();
        }

        private void Apply()
        {
            if (_button == null || _label == null)
            {
                return;
            }

            bool interactable = _button.IsInteractable();
            int state = (interactable ? 1 : 0) | (_hovered ? 2 : 0) | (_active ? 4 : 0);
            if (state == _appliedState)
            {
                return;
            }

            bool first = _appliedState == -1;
            _appliedState = state;
            Color color = _labelColor;
            if (_active)
            {
                color = _theme.Accent;
            }
            else if (_hovered && _hoverBrightens && interactable)
            {
                color = _theme.TextPrimary;
            }

            if (!interactable)
            {
                color.a *= 0.35f;
            }

            _label.color = color;
            if (_hoverBrightens)
            {
                // Icon buttons have no resting background; while active they show a soft accent tile.
                var image = _button.targetGraphic as Image;
                if (image != null)
                {
                    ColorBlock colors = _button.colors;
                    Color normal = _active ? _theme.AccentSoft : new Color(_theme.ButtonHover.r, _theme.ButtonHover.g,
                        _theme.ButtonHover.b, 0f);
                    if (colors.normalColor != normal)
                    {
                        colors.normalColor = normal;
                        colors.selectedColor = normal;
                        colors.disabledColor = new Color(normal.r, normal.g, normal.b, normal.a * 0.45f);
                        if (first)
                        {
                            UiFactory.SetColorsInstant(_button, colors);
                        }
                        else
                        {
                            _button.colors = colors;
                        }
                    }
                }
            }
        }
    }
}
