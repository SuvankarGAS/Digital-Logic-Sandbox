using UnityEngine;
using UnityEngine.UI;

namespace DigitalLogicSandbox.UI
{
    /// <summary>
    /// Slides the knob of a switch-style toggle (built by <see cref="UiFactory.Toggle"/>) between its off and on
    /// positions with a short ease. Follows the toggle's value however it was set, including
    /// <c>SetIsOnWithoutNotify</c>, and is idle once the knob has arrived.
    /// </summary>
    public sealed class SwitchVisual : MonoBehaviour
    {
        private const float Speed = 14f;

        private Toggle _toggle;
        private RectTransform _knob;
        private float _off;
        private float _on;
        private float _position;

        public void Initialize(Toggle toggle, RectTransform knob, float off, float on)
        {
            _toggle = toggle;
            _knob = knob;
            _off = off;
            _on = on;
            _position = toggle.isOn ? on : off;
            _knob.anchoredPosition = new Vector2(_position, 0f);
        }

        private void OnEnable()
        {
            if (_toggle != null)
            {
                // Snap on show: no animation for a state change that happened while hidden.
                _position = _toggle.isOn ? _on : _off;
                _knob.anchoredPosition = new Vector2(_position, 0f);
            }
        }

        private void LateUpdate()
        {
            if (_toggle == null)
            {
                return;
            }

            float target = _toggle.isOn ? _on : _off;
            if (Mathf.Approximately(_position, target))
            {
                return;
            }

            _position = Mathf.Lerp(_position, target, 1f - Mathf.Exp(-Speed * Time.unscaledDeltaTime));
            if (Mathf.Abs(_position - target) < 0.05f)
            {
                _position = target;
            }

            _knob.anchoredPosition = new Vector2(_position, 0f);
        }
    }
}
