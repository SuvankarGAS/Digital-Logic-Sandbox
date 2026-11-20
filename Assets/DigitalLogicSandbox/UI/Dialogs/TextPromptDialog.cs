using System;
using DigitalLogicSandbox.CircuitEditor.Workspace;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace DigitalLogicSandbox.UI.Dialogs
{
    /// <summary>Small modal asking for one line of text (e.g. renaming a gate category). Enter confirms, Escape cancels.</summary>
    public sealed class TextPromptDialog : MonoBehaviour
    {
        private WorkspaceController _workspace;
        private TextMeshProUGUI _title;
        private TextMeshProUGUI _message;
        private TMP_InputField _field;
        private Button _confirm;
        private Action<string> _onConfirm;
        private Func<string, string> _validate;
        private Color _errorColor;
        private Color _hintColor;

        public bool IsOpen => gameObject.activeSelf;

        public TMP_InputField Field => _field;

        public static TextPromptDialog Create(Transform parent, UiFactory ui, WorkspaceController workspace)
        {
            Image blocker = ui.Panel("Text Prompt Dialog", parent, ui.Theme.Scrim);
            UiFactory.Stretch(blocker.rectTransform);
            var dialog = blocker.gameObject.AddComponent<TextPromptDialog>();
            dialog._workspace = workspace;
            dialog._errorColor = ui.Theme.Error;
            dialog._hintColor = ui.Theme.TextMuted;

            Image window = ui.Card("Window", blocker.rectTransform, ui.Theme.DialogBackground);
            RectTransform rect = window.rectTransform;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(420f, 196f);

            RectTransform column = UiFactory.Column("Content", rect, 10f, new RectOffset(20, 20, 16, 16));
            UiFactory.Stretch(column);
            dialog._title = ui.Text("Title", column, string.Empty, 17f, ui.Theme.TextPrimary,
                TextAlignmentOptions.MidlineLeft, FontStyles.Bold);
            dialog._title.gameObject.AddComponent<LayoutElement>().preferredHeight = 26f;
            dialog._field = ui.InputField("Field", column, string.Empty);
            dialog._field.characterLimit = 40;
            dialog._field.onValueChanged.AddListener(_ => dialog.Validate());
            dialog._message = ui.Text("Message", column, string.Empty, 12f, ui.Theme.TextMuted, TextAlignmentOptions.TopLeft);
            dialog._message.enableWordWrapping = true;
            var messageLayout = dialog._message.gameObject.AddComponent<LayoutElement>();
            messageLayout.preferredHeight = 32f;
            messageLayout.flexibleHeight = 1f;

            RectTransform buttons = UiFactory.CreateRect("Buttons", column);
            UiFactory.Row(buttons, 8f, new RectOffset(0, 0, 0, 0), TextAnchor.MiddleRight);
            buttons.gameObject.AddComponent<LayoutElement>().preferredHeight = 34f;
            UiFactory.Flexible(buttons);
            ui.Button("Cancel", buttons, "Cancel", dialog.Close, 100f);
            dialog._confirm = ui.Button("Confirm", buttons, "OK", () => dialog.Confirm(), 110f);
            ui.MakePrimary(dialog._confirm);
            blocker.gameObject.SetActive(false);
            return dialog;
        }

        /// <summary>
        /// Shows the prompt. <paramref name="validate"/> returns an error message for unusable text (or null);
        /// <paramref name="onConfirm"/> receives the trimmed text.
        /// </summary>
        public void Show(string title, string hint, string initial, string confirmLabel, Action<string> onConfirm,
            Func<string, string> validate = null)
        {
            _title.text = title ?? string.Empty;
            _message.text = hint ?? string.Empty;
            _hint = hint ?? string.Empty;
            _onConfirm = onConfirm;
            _validate = validate;
            _confirm.GetComponentInChildren<TextMeshProUGUI>().text = string.IsNullOrEmpty(confirmLabel) ? "OK" : confirmLabel;
            _field.SetTextWithoutNotify(initial ?? string.Empty);
            Validate();
            gameObject.SetActive(true);
            transform.SetAsLastSibling();
            _workspace.InputSuspended = true;
            _field.Select();
            _field.ActivateInputField();
        }

        private string _hint = string.Empty;

        public void Close()
        {
            if (!IsOpen)
            {
                return;
            }

            gameObject.SetActive(false);
            _workspace.InputSuspended = false;
        }

        public bool Confirm()
        {
            if (!Validate())
            {
                return false;
            }

            Action<string> action = _onConfirm;
            string text = _field.text.Trim();
            Close();
            action?.Invoke(text);
            return true;
        }

        private bool Validate()
        {
            string error = _validate?.Invoke(_field.text.Trim());
            _message.text = error ?? _hint;
            _message.color = error != null ? _errorColor : _hintColor;
            _confirm.interactable = error == null;
            return error == null;
        }

        private void Update()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null)
            {
                return;
            }

            if (keyboard.escapeKey.wasPressedThisFrame)
            {
                Close();
            }
            else if ((keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame) &&
                     _confirm.interactable)
            {
                Confirm();
            }
        }
    }
}
