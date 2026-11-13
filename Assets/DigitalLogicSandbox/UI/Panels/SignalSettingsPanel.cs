using System.Globalization;
using DigitalLogicSandbox.Animation.Signals;
using DigitalLogicSandbox.CircuitEditor.Workspace;
using DigitalLogicSandbox.Rendering.Wires;
using DigitalLogicSandbox.UI.Inspector;
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
            Image window = ui.Panel("Signal Settings", parent, ui.Theme.PanelHeader, rounded: true);
            RectTransform rect = window.rectTransform;
            rect.anchorMin = rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(1f, 1f);
            rect.sizeDelta = new Vector2(270f, 214f);
            rect.anchoredPosition = new Vector2(-InspectorPanel.Width - 10f, -UiFactory.ToolbarHeight - NavigationBar.Height - 6f);

            var panel = window.gameObject.AddComponent<SignalSettingsPanel>();
            panel._workspace = workspace;
            panel._settings = workspace.AnimationSettings;

            RectTransform column = UiFactory.Column("Content", rect, 6f, new RectOffset(14, 14, 10, 12));
            UiFactory.Stretch(column);
            ui.Caption(column, "Signal visualization");
            panel._enabled = ui.Toggle("Animate", column, "Animate signal propagation", panel._settings.Enabled,
                value => panel._settings.Enabled = value);

            RectTransform speedRow = UiFactory.CreateRect("Speed", column);
            UiFactory.Row(speedRow, 6f, new RectOffset(0, 0, 0, 0));
            speedRow.gameObject.AddComponent<LayoutElement>().preferredHeight = 30f;
            panel._slower = ui.Button("Slower", speedRow, "-", () => panel.StepSpeed(-1), 34f);
            panel._speedLabel = ui.Text("Value", speedRow, string.Empty, 13f, ui.Theme.TextPrimary, TextAlignmentOptions.Center);
            panel._speedLabel.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
            panel._faster = ui.Button("Faster", speedRow, "+", () => panel.StepSpeed(1), 34f);

            panel._particles = ui.Toggle("Particles", column, "Travelling particles", panel._settings.ShowParticles,
                value => panel._settings.ShowParticles = value);
            panel._highContrast = ui.Toggle("Contrast", column, "High-contrast signals",
                workspace.VisualizationMode == SignalVisualizationMode.HighContrast,
                value => workspace.VisualizationMode = value ? SignalVisualizationMode.HighContrast : SignalVisualizationMode.Standard);
            ui.Button("Reset", column, "Reset visual state", workspace.ResetVisualState);

            panel._settings.Changed += panel.Refresh;
            panel.Refresh();
            window.gameObject.SetActive(false);
            return panel;
        }

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
            }
        }

        public void Close()
        {
            gameObject.SetActive(false);
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
