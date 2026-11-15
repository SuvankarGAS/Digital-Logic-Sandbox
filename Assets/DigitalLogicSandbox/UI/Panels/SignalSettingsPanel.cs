using System.Globalization;
using DigitalLogicSandbox.Animation.Signals;
using DigitalLogicSandbox.CircuitEditor.Workspace;
using DigitalLogicSandbox.Rendering.Wires;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace DigitalLogicSandbox.UI.Panels
{
    /// <summary>
    /// Small popup (toolbar "Signals") with the signal visualization controls: animation on/off, speed,
    /// travelling particles, high-contrast style and "reset visual state". Changes apply immediately and never
    /// affect simulation results.
    /// </summary>
    public sealed class SignalSettingsPanel : MonoBehaviour
    {
        private static readonly float[] SpeedSteps = { 0.25f, 0.5f, 1f, 2f, 4f };

        private WorkspaceController _workspace;
        private SignalAnimationSettings _settings;
        private Toggle _enabled;
        private Toggle _particles;
        private Toggle _highContrast;
        private TextMeshProUGUI _speedLabel;
        private Button _slower;
        private Button _faster;

        public bool IsOpen => gameObject.activeSelf;

        public Toggle EnabledToggle => _enabled;

        public Toggle ParticlesToggle => _particles;

        public Toggle HighContrastToggle => _highContrast;

        public Button SlowerButton => _slower;

        public Button FasterButton => _faster;

        public string SpeedText => _speedLabel.text;

        public static SignalSettingsPanel Create(Transform parent, UiFactory ui, WorkspaceController workspace)
        {
            Image window = ui.Card("Signal Settings", parent, ui.Theme.PanelHeader, radius: 12f);
            RectTransform rect = window.rectTransform;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.sizeDelta = new Vector2(280f, 250f);
            rect.anchoredPosition = new Vector2(0f, -UiFactory.TopInset + 4f);

            var panel = window.gameObject.AddComponent<SignalSettingsPanel>();
            panel._workspace = workspace;
            panel._settings = workspace.AnimationSettings;

            RectTransform column = UiFactory.Column("Content", rect, 6f, new RectOffset(16, 16, 6, 14));
            UiFactory.Stretch(column);
            ui.Caption(column, "Signal animation");
            panel._enabled = ui.Toggle("Animate", column, "Animate signal propagation", panel._settings.Enabled,
                value => panel._settings.Enabled = value);

            RectTransform speedRow = UiFactory.CreateRect("Speed", column);
            UiFactory.Row(speedRow, 6f, new RectOffset(0, 0, 0, 0));
            speedRow.gameObject.AddComponent<LayoutElement>().preferredHeight = 30f;
            panel._speedLabel = ui.Text("Value", speedRow, string.Empty, 13f, ui.Theme.TextPrimary);
            panel._speedLabel.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
            panel._slower = ui.IconButton("Slower", speedRow, Icons.Minus, "Slower", () => panel.StepSpeed(-1), 28f, 14f);
            panel._faster = ui.IconButton("Faster", speedRow, Icons.Plus, "Faster", () => panel.StepSpeed(1), 28f, 14f);

            panel._particles = ui.Toggle("Particles", column, "Travelling sparks", panel._settings.ShowParticles,
                value => panel._settings.ShowParticles = value);
            panel._highContrast = ui.Toggle("Contrast", column, "High-contrast signals",
                workspace.VisualizationMode == SignalVisualizationMode.HighContrast,
                value => workspace.VisualizationMode = value ? SignalVisualizationMode.HighContrast : SignalVisualizationMode.Standard);
            ui.Separator(column, vertical: false);
            ui.Button("Reset", column, "Reset visual state", workspace.ResetVisualState);

            panel._settings.Changed += panel.Refresh;
            panel.Refresh();
            window.gameObject.SetActive(false);
            return panel;
        }

        /// <summary>Raised when the popup opens (true) or closes (false).</summary>
        public event System.Action<bool> OpenChanged;

        public void Toggle()
        {
            if (IsOpen)
            {
                Close();
            }
            else
            {
                Refresh();
                gameObject.SetActive(true);
                transform.SetAsLastSibling();
                OpenChanged?.Invoke(true);
            }
        }

        public void Close()
        {
            if (!gameObject.activeSelf)
            {
                return;
            }

            gameObject.SetActive(false);
            OpenChanged?.Invoke(false);
        }

        /// <summary>Moves the speed one step along 0.25x … 4x.</summary>
        public void StepSpeed(int direction)
        {
            int index = 0;
            for (int i = 0; i < SpeedSteps.Length; i++)
            {
                if (Mathf.Abs(SpeedSteps[i] - _settings.Speed) < Mathf.Abs(SpeedSteps[index] - _settings.Speed))
                {
                    index = i;
                }
            }

            _settings.Speed = SpeedSteps[Mathf.Clamp(index + direction, 0, SpeedSteps.Length - 1)];
        }

        private void Refresh()
        {
            _enabled.SetIsOnWithoutNotify(_settings.Enabled);
            _particles.SetIsOnWithoutNotify(_settings.ShowParticles);
            _highContrast.SetIsOnWithoutNotify(_workspace.VisualizationMode == SignalVisualizationMode.HighContrast);
            _speedLabel.text = "Speed " + _settings.Speed.ToString("0.##", CultureInfo.InvariantCulture) + "x";
            _slower.interactable = _settings.Speed > SignalAnimationSettings.MinSpeed;
            _faster.interactable = _settings.Speed < SignalAnimationSettings.MaxSpeed;
            _particles.interactable = _settings.Enabled;
        }

        private void Update()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
            {
                Close();
            }
        }

        private void OnDestroy()
        {
            if (_settings != null)
            {
                _settings.Changed -= Refresh;
            }
        }
    }
}
