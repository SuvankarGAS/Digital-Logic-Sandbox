using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace DigitalLogicSandbox.UI.Toolbar
{
    /// <summary>
    /// Dropdown under the project pill's menu button: rows with an icon, a label and a muted shortcut hint. A
    /// full-screen transparent blocker closes it on any outside click; Escape closes it too. Items stay functional
    /// while the menu is closed (their buttons can be invoked directly).
    /// </summary>
    public sealed class FileMenu : MonoBehaviour, IPointerDownHandler
    {
        private const float Width = 236f;

        private UiFactory _ui;
        private RectTransform _anchor;
        private RectTransform _menu;

        public bool IsOpen => gameObject.activeSelf;

        public static FileMenu Create(Transform parent, UiFactory ui, RectTransform anchor)
        {
            Image blocker = ui.Panel("File Menu", parent, Color.clear);
            UiFactory.Stretch(blocker.rectTransform);
            var view = blocker.gameObject.AddComponent<FileMenu>();
            view._ui = ui;
            view._anchor = anchor;

            Image menu = ui.Card("Menu", blocker.rectTransform, ui.Theme.PanelHeader, radius: 12f);
            view._menu = menu.rectTransform;
            view._menu.anchorMin = view._menu.anchorMax = new Vector2(0f, 1f);
            view._menu.pivot = new Vector2(0f, 1f);
            view._menu.sizeDelta = new Vector2(Width, 0f);
            var layout = menu.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(6, 6, 6, 6);
            layout.spacing = 1f;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            menu.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            blocker.gameObject.SetActive(false);
            return view;
        }

        /// <summary>Adds a row; clicking it closes the menu, then runs <paramref name="action"/>.</summary>
        public Button AddItem(string glyph, string label, string shortcut, UnityAction action)
        {
            Image row = _ui.Panel(label, _menu, Color.white, rounded: true, radius: 8f);
            var button = row.gameObject.AddComponent<Button>();
            button.targetGraphic = row;
            ColorBlock colors = button.colors;
            Color hover = _ui.Theme.ButtonHover;
            colors.normalColor = new Color(hover.r, hover.g, hover.b, 0f);
            colors.highlightedColor = hover;
            colors.selectedColor = colors.normalColor;
            colors.pressedColor = _ui.Theme.ButtonPressed;
            colors.disabledColor = colors.normalColor;
            colors.fadeDuration = 0.06f;
            UiFactory.SetColorsInstant(button, colors);
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            button.onClick.AddListener(() =>
            {
                Close();
                action?.Invoke();
            });
            row.gameObject.AddComponent<LayoutElement>().preferredHeight = 34f;

            TextMeshProUGUI icon = _ui.Icon("Icon", row.transform, glyph, 15f, _ui.Theme.TextSecondary);
            icon.rectTransform.anchorMin = new Vector2(0f, 0f);
            icon.rectTransform.anchorMax = new Vector2(0f, 1f);
            icon.rectTransform.pivot = new Vector2(0f, 0.5f);
            icon.rectTransform.sizeDelta = new Vector2(34f, 0f);
            icon.rectTransform.anchoredPosition = Vector2.zero;
            TextMeshProUGUI text = _ui.Text("Label", row.transform, label, 13.5f, _ui.Theme.TextPrimary);
            UiFactory.Stretch(text.rectTransform, 36f, 90f);
            TextMeshProUGUI hint = _ui.Text("Shortcut", row.transform, shortcut, 11.5f, _ui.Theme.TextMuted,
                TextAlignmentOptions.MidlineRight);
            UiFactory.Stretch(hint.rectTransform, 120f, 12f);
            return button;
        }

        public void AddSeparator()
        {
            RectTransform holder = UiFactory.CreateRect("Separator", _menu);
            holder.gameObject.AddComponent<LayoutElement>().preferredHeight = 9f;
            Image line = _ui.Panel("Line", holder, _ui.Theme.PanelBorder);
            line.raycastTarget = false;
            line.rectTransform.anchorMin = new Vector2(0f, 0.5f);
            line.rectTransform.anchorMax = new Vector2(1f, 0.5f);
            line.rectTransform.sizeDelta = new Vector2(-16f, 1f);
        }

        public void Toggle()
        {
            if (IsOpen)
            {
                Close();
            }
            else
            {
                Open();
            }
        }

        public void Open()
        {
            gameObject.SetActive(true);
            transform.SetAsLastSibling();
            PlaceUnderAnchor();
        }

        public void Close()
        {
            gameObject.SetActive(false);
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (eventData.pointerCurrentRaycast.gameObject == gameObject)
            {
                Close();
            }
        }

        private void Update()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
            {
                Close();
            }
        }

        private static readonly Vector3[] Corners = new Vector3[4];

        private void PlaceUnderAnchor()
        {
            var root = (RectTransform)transform;
            _anchor.GetWorldCorners(Corners);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(root, Corners[0], null, out Vector2 local);
            Vector2 fromTopLeft = local + root.rect.size * root.pivot - new Vector2(0f, root.rect.height);
            _menu.anchoredPosition = new Vector2(fromTopLeft.x - 5f, fromTopLeft.y - 12f);
        }
    }
}
