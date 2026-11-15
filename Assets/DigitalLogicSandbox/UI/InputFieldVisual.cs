using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace DigitalLogicSandbox.UI
{
    /// <summary>
    /// Focus ring for a <see cref="TMP_InputField"/>: the border turns accent-coloured (brighter on hover) while the
    /// field is being edited, so it is obvious where typing goes. Polls <see cref="TMP_InputField.isFocused"/> once per
    /// frame because TMP does not raise an event for every focus change (re-clicking a selected field, submitting with
    /// Enter); colours are written only when the state actually changes.
    /// </summary>
    public sealed class InputFieldVisual : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        private TMP_InputField _field;
        private Image _border;
        private Color _normal;
        private Color _focused;
        private bool _hovered;
        private int _appliedState = -1;

        public void Initialize(TMP_InputField field, Image border, Color normal, Color focused)
        {
            _field = field;
            _border = border;
            _normal = normal;
            _focused = focused;
            _appliedState = -1;
            Apply();
        }

        /// <summary>True while the field has keyboard focus (exposed for tests).</summary>
        public bool ShowsFocus => _appliedState == StateFocused;

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

        private void LateUpdate()
        {
            Apply();
        }

        private void OnDisable()
        {
            _hovered = false;
            Apply();
        }

        private const int StateNormal = 0;
        private const int StateHover = 1;
        private const int StateFocused = 2;

        private void Apply()
        {
            if (_field == null || _border == null)
            {
                return;
            }

            int state = _field.isFocused && isActiveAndEnabled ? StateFocused : _hovered ? StateHover : StateNormal;
            if (state == _appliedState)
            {
                return;
            }

            _appliedState = state;
            switch (state)
            {
                case StateFocused:
                    _border.color = _focused;
                    break;
                case StateHover:
                    _border.color = Color.Lerp(_normal, _focused, 0.35f);
                    break;
                default:
                    _border.color = _normal;
                    break;
            }
        }
    }
}
