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
    /// Small popup (toolbar "View settings") with the visualization controls: signal animation on/off, speed,
    /// travelling particles, high-contrast style, how wire crossings are drawn, whether gate names show their version,
    /// and "reset visual state". Changes apply immediately and never affect simulation results.
    /// </summary>
    public sealed class SignalSettingsPanel : MonoBehaviour
    {
        private static readonly float[] SpeedSteps = { 0.25f, 0.5f, 1f, 2f, 4f };

        /// <summary>Gap between the breadcrumb bar and the popup when both are shown.</summary>
        private const float NavigationGap = 8f;

        private WorkspaceController _workspace;
        private NavigationBar _navigationBar;
        private SignalAnimationSettings _settings;
        private Toggle _enabled;
        private Toggle _particles;
        private Toggle _highContrast;
        private Toggle _versions;
        private TextMeshProUGUI _speedLabel;
        private Button _slower;
        private Button _faster;
        private readonly Button[] _crossingButtons = new Button[3];

        public bool IsOpen => gameObject.activeSelf;

        public Toggle EnabledToggle => _enabled;

        public Toggle ParticlesToggle => _particles;

        public Toggle HighContrastToggle => _highContrast;

        /// <summary>"Show versions in gate names".</summary>
        public Toggle VersionsToggle => _versions;

        public Button SlowerButton => _slower;

        public Button FasterButton => _faster;

        /// <summary>The crossing style button for <paramref name="style"/> (Hop, Gap, Plain).</summary>
        public Button CrossingButton(WireCrossingStyle style) => _crossingButtons[(int)style];

        public string SpeedText => _speedLabel.text;

        /// <summary>
        /// Creates the popup. When <paramref name="navigationBar"/> is given, the popup opens below it while it is
        /// shown (both sit at the top centre), so the breadcrumbs stay readable.
        /// </summary>
        public static SignalSettingsPanel Create(Transform parent, UiFactory ui, WorkspaceController workspace,
            NavigationBar navigationBar = null)
        {
            Image window = ui.Card("Signal Settings", parent, ui.Theme.PanelHeader, radius: 12f);
            RectTransform rect = window.rectTransform;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.sizeDelta = new Vector2(280f, 384f);
            rect.anchoredPosition = new Vector2(0f, -UiFactory.TopInset + 4f);

            var panel = window.gameObject.AddComponent<SignalSettingsPanel>();
            panel._workspace = workspace;
            panel._settings = workspace.AnimationSettings;
            panel._navigationBar = navigationBar;

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
            ui.Caption(column, "Wire crossings");
            RectTransform crossingRow = UiFactory.CreateRect("Crossings", column);
            UiFactory.Row(crossingRow, 6f, new RectOffset(0, 0, 0, 0));
            crossingRow.gameObject.AddComponent<LayoutElement>().preferredHeight = 32f;
            for (int i = 0; i < panel._crossingButtons.Length; i++)
            {
                var style = (WireCrossingStyle)i;
                panel._crossingButtons[i] = ui.Button("Crossing " + style, crossingRow, style.ToString(),
                    () =>
                    {
                        workspace.WireCrossingStyle = style;
                        panel.Refresh();
                    }, 78f, 12.5f);
            }

            ui.AddTooltip(panel._crossingButtons[(int)WireCrossingStyle.Hop].gameObject, "Hop over other wires");
            ui.AddTooltip(panel._crossingButtons[(int)WireCrossingStyle.Gap].gameObject, "Break the wire at crossings");
            ui.AddTooltip(panel._crossingButtons[(int)WireCrossingStyle.Plain].gameObject, "Wires simply cross");
            ui.Caption(column, "Gates");
            panel._versions = ui.Toggle("Versions", column, "Show versions in gate names (v2)",
                workspace.ShowGateVersions, value => workspace.ShowGateVersions = value);
            ui.Separator(column, vertical: false);
            ui.Button("Reset", column, "Reset visual state", workspace.ResetVisualState);

            panel._settings.Changed += panel.Refresh;
            workspace.NavigationChanged += panel.UpdatePosition;
            workspace.StatusChanged += panel.UpdatePosition;
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
                UpdatePosition();
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
            _versions.SetIsOnWithoutNotify(_workspace.ShowGateVersions);
            _speedLabel.text = "Speed " + _settings.Speed.ToString("0.##", CultureInfo.InvariantCulture) + "x";
            _slower.interactable = _settings.Speed > SignalAnimationSettings.MinSpeed;
            _faster.interactable = _settings.Speed < SignalAnimationSettings.MaxSpeed;
            _particles.interactable = _settings.Enabled;
            for (int i = 0; i < _crossingButtons.Length; i++)
            {
                // The current style has an accent label, and (not colour alone) a bold one.
                bool current = (int)_workspace.WireCrossingStyle == i;
                ButtonVisual visual = _crossingButtons[i].GetComponent<ButtonVisual>();
                if (visual != null)
                {
                    visual.Active = current;
                }

                TextMeshProUGUI label = _crossingButtons[i].GetComponentInChildren<TextMeshProUGUI>();
                if (label != null)
                {
                    label.fontStyle = current ? FontStyles.Bold : FontStyles.Normal;
                }
            }
        }

        /// <summary>Top offset of the popup: under the toolbar, and under the breadcrumb bar while that is shown.</summary>
        public float TopOffset
        {
            get
            {
                float y = -UiFactory.TopInset + 4f;
                if (_navigationBar != null && _navigationBar.gameObject.activeSelf)
                {
                    y -= NavigationBar.Height + NavigationGap;
                }

                return y;
            }
        }

        private void UpdatePosition()
        {
            var rect = (RectTransform)transform;
            rect.anchoredPosition = new Vector2(0f, TopOffset);
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

            if (_workspace != null)
            {
                _workspace.NavigationChanged -= UpdatePosition;
                _workspace.StatusChanged -= UpdatePosition;
            }
        }
    }
}
