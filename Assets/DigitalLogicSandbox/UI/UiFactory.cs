using System;
using DigitalLogicSandbox.Rendering;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace DigitalLogicSandbox.UI
{
    /// <summary>
    /// Builds the application's UGUI controls in code with the shared <see cref="VisualTheme"/>, so the whole
    /// interface follows one visual language without a large hand-maintained prefab set.
    /// </summary>
    public sealed class UiFactory
    {
        public const float ToolbarHeight = 44f;
        public const float StatusBarHeight = 28f;
        public const float LibraryWidth = 248f;

        public UiFactory(VisualTheme theme)
        {
            Theme = theme ? theme : throw new ArgumentNullException(nameof(theme));
        }

        public VisualTheme Theme { get; }

        public static RectTransform CreateRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            go.layer = parent != null ? parent.gameObject.layer : 5;
            return (RectTransform)go.transform;
        }

        public static void Stretch(RectTransform rect, float left = 0f, float right = 0f, float top = 0f, float bottom = 0f)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(left, bottom);
            rect.offsetMax = new Vector2(-right, -top);
        }

        public Image Panel(string name, Transform parent, Color color, bool rounded = false)
        {
            RectTransform rect = CreateRect(name, parent);
            var image = rect.gameObject.AddComponent<Image>();
            image.color = color;
            if (rounded)
            {
                image.sprite = RenderAssets.RoundedRect;
                image.type = Image.Type.Sliced;
                image.pixelsPerUnitMultiplier = 4f;
            }

            return image;
        }

        public TextMeshProUGUI Text(string name, Transform parent, string text, float size, Color color,
            TextAlignmentOptions alignment = TextAlignmentOptions.MidlineLeft, FontStyles style = FontStyles.Normal)
        {
            RectTransform rect = CreateRect(name, parent);
            var label = rect.gameObject.AddComponent<TextMeshProUGUI>();
            label.text = text;
            label.fontSize = size;
            label.color = color;
            label.alignment = alignment;
            label.fontStyle = style;
            label.enableWordWrapping = false;
            label.overflowMode = TextOverflowModes.Ellipsis;
            label.raycastTarget = false;
            return label;
        }

        /// <summary>Flat text button with hover/press colours from the theme.</summary>
        public Button Button(string name, Transform parent, string label, UnityAction onClick, float width = -1f,
            float fontSize = 14f)
        {
            Image background = Panel(name, parent, Color.white, rounded: true);
            var button = background.gameObject.AddComponent<Button>();
            button.targetGraphic = background;
            ColorBlock colors = button.colors;
            colors.normalColor = Theme.ButtonNormal;
            colors.highlightedColor = Theme.ButtonHover;
            colors.selectedColor = Theme.ButtonNormal;
            colors.pressedColor = Theme.ButtonPressed;
            colors.disabledColor = new Color(Theme.ButtonNormal.r, Theme.ButtonNormal.g, Theme.ButtonNormal.b, 0.4f);
            colors.colorMultiplier = 1f;
            colors.fadeDuration = 0.06f;
            button.colors = colors;
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            if (onClick != null)
            {
                button.onClick.AddListener(onClick);
            }

            TextMeshProUGUI text = Text("Label", background.transform, label, fontSize, Theme.TextPrimary,
                TextAlignmentOptions.Center);
            Stretch(text.rectTransform, 8f, 8f);

            var layout = background.gameObject.AddComponent<LayoutElement>();
            layout.minHeight = 30f;
            layout.preferredHeight = 30f;
            if (width > 0f)
            {
                layout.preferredWidth = width;
                layout.minWidth = width;
            }
            else
            {
                layout.preferredWidth = Mathf.Max(40f, text.GetPreferredValues(label).x + 20f);
            }

            return button;
        }

        public RectTransform Separator(Transform parent, bool vertical)
        {
            Image line = Panel("Separator", parent, Theme.PanelBorder);
            var layout = line.gameObject.AddComponent<LayoutElement>();
            if (vertical)
            {
                layout.preferredWidth = 1f;
                layout.minWidth = 1f;
                layout.preferredHeight = 24f;
            }
            else
            {
                layout.preferredHeight = 1f;
                layout.minHeight = 1f;
                layout.flexibleWidth = 1f;
            }

            line.raycastTarget = false;
            return line.rectTransform;
        }

        public TMP_InputField InputField(string name, Transform parent, string placeholder)
        {
            Image background = Panel(name, parent, Theme.ButtonNormal, rounded: true);
            var layout = background.gameObject.AddComponent<LayoutElement>();
            layout.preferredHeight = 30f;
            layout.minHeight = 30f;
            layout.flexibleWidth = 1f;

            RectTransform area = CreateRect("Text Area", background.transform);
            Stretch(area, 10f, 10f, 4f, 4f);
            area.gameObject.AddComponent<RectMask2D>();

            TextMeshProUGUI hint = Text("Placeholder", area, placeholder, 13f, Theme.TextSecondary);
            hint.fontStyle = FontStyles.Italic;
            Stretch(hint.rectTransform);
            TextMeshProUGUI text = Text("Text", area, string.Empty, 13f, Theme.TextPrimary);
            Stretch(text.rectTransform);

            var field = background.gameObject.AddComponent<TMP_InputField>();
            field.textViewport = area;
            field.textComponent = text;
            field.placeholder = hint;
            field.targetGraphic = background;
            field.caretColor = Theme.TextPrimary;
            field.customCaretColor = true;
            field.selectionColor = new Color(Theme.Accent.r, Theme.Accent.g, Theme.Accent.b, 0.35f);
            field.navigation = new Navigation { mode = Navigation.Mode.None };
            return field;
        }

        /// <summary>Vertical scroll view; returns the content transform (with a vertical layout) to fill.</summary>
        public RectTransform ScrollView(string name, Transform parent, out ScrollRect scrollRect)
        {
            RectTransform root = CreateRect(name, parent);
            scrollRect = root.gameObject.AddComponent<ScrollRect>();
            scrollRect.horizontal = false;
            scrollRect.movementType = ScrollRect.MovementType.Clamped;
            scrollRect.scrollSensitivity = 24f;

            RectTransform viewport = CreateRect("Viewport", root);
            Stretch(viewport);
            viewport.gameObject.AddComponent<RectMask2D>();
            var viewportImage = viewport.gameObject.AddComponent<Image>();
            viewportImage.color = new Color(0f, 0f, 0f, 0f);

            RectTransform content = CreateRect("Content", viewport);
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            content.offsetMin = Vector2.zero;
            content.offsetMax = Vector2.zero;
            var layout = content.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            layout.spacing = 4f;
            layout.padding = new RectOffset(10, 10, 8, 12);
            var fitter = content.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            scrollRect.viewport = viewport;
            scrollRect.content = content;
            return content;
        }

        public static HorizontalLayoutGroup Row(RectTransform rect, float spacing, RectOffset padding,
            TextAnchor alignment = TextAnchor.MiddleLeft)
        {
            var layout = rect.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = spacing;
            layout.padding = padding;
            layout.childAlignment = alignment;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;
            return layout;
        }

        public static LayoutElement Flexible(Transform parent)
        {
            RectTransform spacer = CreateRect("Spacer", parent);
            var layout = spacer.gameObject.AddComponent<LayoutElement>();
            layout.flexibleWidth = 1f;
            return layout;
        }
    }
}
