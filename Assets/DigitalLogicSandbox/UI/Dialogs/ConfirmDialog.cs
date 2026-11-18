using System;
using DigitalLogicSandbox.CircuitEditor.Workspace;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace DigitalLogicSandbox.UI.Dialogs
{
    /// <summary>
    /// Small modal question with up to three answers (e.g. "Save" / "Don't save" / "Cancel", or
    /// "Recover" / "Discard"). Escape picks the cancel answer when there is one.
    /// </summary>
    public sealed class ConfirmDialog : MonoBehaviour
    {
        private WorkspaceController _workspace;
        private TextMeshProUGUI _title;
        private TextMeshProUGUI _message;
        private Button _primary;
        private Button _secondary;
        private Button _cancel;
        private Action _onPrimary;
        private Action _onSecondary;
        private Action _onCancel;

        public bool IsOpen => gameObject.activeSelf;

        public string TitleText => _title.text;

        public string MessageText => _message.text;

        public Button PrimaryButton => _primary;

        public Button SecondaryButton => _secondary;

        public Button CancelButton => _cancel;

        public static ConfirmDialog Create(Transform parent, UiFactory ui, WorkspaceController workspace)
        {
            Image blocker = ui.Panel("Confirm Dialog", parent, ui.Theme.Scrim);
            UiFactory.Stretch(blocker.rectTransform);
            var dialog = blocker.gameObject.AddComponent<ConfirmDialog>();
            dialog._workspace = workspace;

            Image window = ui.Card("Window", blocker.rectTransform, ui.Theme.DialogBackground);
            RectTransform rect = window.rectTransform;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(480f, 220f);

            RectTransform column = UiFactory.Column("Content", rect, 10f, new RectOffset(20, 20, 16, 16));
            UiFactory.Stretch(column);
            dialog._title = ui.Text("Title", column, string.Empty, 17f, ui.Theme.TextPrimary,
                TextAlignmentOptions.MidlineLeft, FontStyles.Bold);
            dialog._title.gameObject.AddComponent<LayoutElement>().preferredHeight = 26f;
            dialog._message = ui.Text("Message", column, string.Empty, 13f, ui.Theme.TextSecondary, TextAlignmentOptions.TopLeft);
            dialog._message.enableWordWrapping = true;
            dialog._message.richText = true;
            var messageLayout = dialog._message.gameObject.AddComponent<LayoutElement>();
            messageLayout.flexibleHeight = 1f;
            messageLayout.preferredHeight = 90f;

            RectTransform buttons = UiFactory.CreateRect("Buttons", column);
            UiFactory.Row(buttons, 8f, new RectOffset(0, 0, 0, 0), TextAnchor.MiddleRight);
            buttons.gameObject.AddComponent<LayoutElement>().preferredHeight = 34f;
            UiFactory.Flexible(buttons);
            dialog._cancel = ui.Button("Cancel", buttons, "Cancel", () => dialog.Answer(dialog._onCancel), 100f);
            dialog._secondary = ui.Button("Secondary", buttons, "No", () => dialog.Answer(dialog._onSecondary), 120f);
            dialog._primary = ui.Button("Primary", buttons, "Yes", () => dialog.Answer(dialog._onPrimary), 120f);
            ui.MakePrimary(dialog._primary);

            blocker.gameObject.SetActive(false);
            return dialog;
        }

        /// <summary>
        /// Shows the question. Pass null for <paramref name="secondaryLabel"/> or <paramref name="cancelLabel"/>
        /// to hide those buttons.
        /// </summary>
        public void Show(string title, string message, string primaryLabel, Action onPrimary,
            string secondaryLabel = null, Action onSecondary = null, string cancelLabel = "Cancel", Action onCancel = null)
        {
            _title.text = title ?? string.Empty;
            _message.text = message ?? string.Empty;
            Configure(_primary, primaryLabel);
            Configure(_secondary, secondaryLabel);
            Configure(_cancel, cancelLabel);
            _onPrimary = onPrimary;
            _onSecondary = onSecondary;
            _onCancel = onCancel;
            gameObject.SetActive(true);
            transform.SetAsLastSibling();
            _workspace.InputSuspended = true;
        }

        public void Close()
        {
            if (!IsOpen)
            {
                return;
            }

            gameObject.SetActive(false);
            _workspace.InputSuspended = false;
        }

        private void Answer(Action action)
        {
            Close();
            action?.Invoke();
        }

        private static void Configure(Button button, string label)
        {
            button.gameObject.SetActive(!string.IsNullOrEmpty(label));
            if (!string.IsNullOrEmpty(label))
            {
                TextMeshProUGUI text = button.GetComponentInChildren<TextMeshProUGUI>();
                text.text = label;
                var layout = button.GetComponent<LayoutElement>();
                float width = Mathf.Max(100f, text.GetPreferredValues(label).x + 28f);
                layout.preferredWidth = width;
                layout.minWidth = width;
            }
        }

        private void Update()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame && _cancel.gameObject.activeSelf)
            {
                Answer(_onCancel);
            }
        }
    }
}
