using System;
using DigitalLogicSandbox.Rendering;
using DigitalLogicSandbox.UI.Panels;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace DigitalLogicSandbox.UI
{
    /// <summary>
    /// Builds the application's UGUI controls in code with the shared <see cref="VisualTheme"/>, so the whole
    /// interface follows one visual language without a large hand-maintained prefab set.
    /// <para>
    /// Design language: floating rounded cards over a full-bleed workspace, soft shadows, one accent colour, icon
    /// buttons with tooltips, a calm rounded typeface (Nunito) and muted secondary text. Headings use the SemiBold
    /// weight rather than Bold so long sessions stay easy on the eyes.
    /// </para>
    /// </summary>
    public sealed class UiFactory
    {
        /// <summary>Gap between floating panels and the screen edge / each other.</summary>
        public const float Margin = 12f;

        /// <summary>Height of the floating top bars (project, tools).</summary>
        public const float ToolbarHeight = 44f;

        /// <summary>Height of the floating status pill.</summary>
        public const float StatusBarHeight = 36f;

        public const float LibraryWidth = 248f;

        /// <summary>Distance from the top of the screen to the top of the side panels.</summary>
        public const float TopInset = Margin + ToolbarHeight + Margin;

        /// <summary>Corner radius of cards and dialogs.</summary>
        public const float CardRadius = 14f;

        /// <summary>Corner radius of buttons, fields and list rows.</summary>
        public const float ControlRadius = 9f;

        public const float IconButtonSize = 34f;

        public UiFactory(VisualTheme theme)
        {
            Theme = theme ? theme : throw new ArgumentNullException(nameof(theme));
        }

        public VisualTheme Theme { get; }

        /// <summary>Shared tooltip; set by the shell. Triggers resolve it when hovered, so it may be set later.</summary>
        public TooltipView Tooltip { get; set; }

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

        /// <summary>Plain image. <paramref name="rounded"/> uses the 9-sliced rounded sprite with <paramref name="radius"/>.</summary>
        public Image Panel(string name, Transform parent, Color color, bool rounded = false, float radius = ControlRadius)
        {
            RectTransform rect = CreateRect(name, parent);
            var image = rect.gameObject.AddComponent<Image>();
            image.color = color;
            if (rounded)
            {
                ApplyRounded(image, RenderAssets.RoundedRect, radius);
            }

            return image;
        }

        /// <summary>
        /// Floating card: a transparent root (it blocks pointer input and hosts layout groups and content) with a
        /// soft drop shadow, a rounded fill and a hairline border drawn behind the content. The decoration children
        /// ignore layout, so callers can add layout groups to the returned root exactly as to a plain panel.
        /// </summary>
        public Image Card(string name, Transform parent, Color fill, float radius = CardRadius, bool shadow = true)
        {
            RectTransform root = CreateRect(name, parent);
            var hit = root.gameObject.AddComponent<Image>();
            hit.color = Color.clear;

            if (shadow)
            {
                var shadowImage = Decoration("Shadow", root, Theme.UiShadow);
                shadowImage.sprite = RenderAssets.SoftShadow;
                shadowImage.type = Image.Type.Sliced;
                // Border of 30 texels on a 64 ppu sprite; scale so the blur spans ~22 UI units.
                shadowImage.pixelsPerUnitMultiplier = RenderAssets.ShadowBorder / 64f * 100f / 22f;
                Stretch(shadowImage.rectTransform, -18f, -18f, -14f, -24f);
            }

            Image body = Decoration("Fill", root, fill);
            ApplyRounded(body, RenderAssets.RoundedRect, radius);
            Stretch(body.rectTransform);

            Image border = Decoration("Border", root, Theme.PanelBorder);
            ApplyRounded(border, RenderAssets.RoundedOutline, radius);
            Stretch(border.rectTransform);
            return hit;
        }

        /// <summary>Changes the fill colour of a <see cref="Card"/>.</summary>
        public static void SetCardFill(Image card, Color fill)
        {
            Transform body = card.transform.Find("Fill");
            if (body != null)
            {
                body.GetComponent<Image>().color = fill;
            }
        }

        private Image Decoration(string name, Transform parent, Color color)
        {
            RectTransform rect = CreateRect(name, parent);
            var image = rect.gameObject.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            rect.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            return image;
        }

        private static void ApplyRounded(Image image, Sprite sprite, float radius)
        {
            image.sprite = sprite;
            image.type = Image.Type.Sliced;
            // The rounded sprites have 16-texel borders on a 64 ppu texture (canvas reference ppu = 100), so a
            // multiplier of 25 / radius gives corners of 'radius' UI units.
            image.pixelsPerUnitMultiplier = 25f / Mathf.Max(1f, radius);
        }

        public TextMeshProUGUI Text(string name, Transform parent, string text, float size, Color color,
            TextAlignmentOptions alignment = TextAlignmentOptions.MidlineLeft, FontStyles style = FontStyles.Normal)
        {
            RectTransform rect = CreateRect(name, parent);
            var label = rect.gameObject.AddComponent<TextMeshProUGUI>();
            label.font = Theme.UiFont;
            label.text = text;
            label.fontSize = size;
            label.color = color;
            label.alignment = alignment;
            if ((style & FontStyles.Bold) != 0)
            {
                // Softer emphasis: SemiBold weight instead of the heavy bold face.
                style &= ~FontStyles.Bold;
                label.fontWeight = FontWeight.SemiBold;
            }

            label.fontStyle = style;
            label.enableWordWrapping = false;
            label.overflowMode = TextOverflowModes.Ellipsis;
            label.raycastTarget = false;
            return label;
        }

        /// <summary>One icon glyph (see <see cref="Icons"/>), centred in its rect.</summary>
        public TextMeshProUGUI Icon(string name, Transform parent, string glyph, float size, Color color)
        {
            // The text font lists the icon font as a fallback, so glyphs resolve through it. Keeping the text font as
            // the primary avoids TMP's per-label warnings about the icon font lacking '_' / '…' (TMP looks those up
            // in the primary font only); line metrics still come from the icon font that draws the glyph.
            TextMeshProUGUI icon = Text(name, parent, glyph, size, color, TextAlignmentOptions.Center);
            icon.overflowMode = TextOverflowModes.Overflow;

            // Pad each glyph quad by the SDF spread. Without it, thin flat glyphs (minus) get a quad only as tall as
            // the stroke, the anti-aliased edge is cut off and the icon vanishes at toolbar sizes.
            icon.extraPadding = true;
            return icon;
        }

        /// <summary>Rounded text button with hover/press colours from the theme.</summary>
        public Button Button(string name, Transform parent, string label, UnityAction onClick, float width = -1f,
            float fontSize = 13.5f)
        {
            Image background = Panel(name, parent, Color.white, rounded: true);
            Button button = ConfigureButton(background, Theme.ButtonNormal, Theme.ButtonHover, Theme.ButtonPressed, onClick);

            TextMeshProUGUI text = Text("Label", background.transform, label, fontSize, Theme.TextPrimary,
                TextAlignmentOptions.Center);
            Stretch(text.rectTransform, 10f, 10f);
            background.gameObject.AddComponent<ButtonVisual>().Initialize(button, Theme, text);

            var layout = background.gameObject.AddComponent<LayoutElement>();
            layout.minHeight = 32f;
            layout.preferredHeight = 32f;
            if (width > 0f)
            {
                layout.preferredWidth = width;
                layout.minWidth = width;
            }
            else
            {
                layout.preferredWidth = Mathf.Max(44f, text.GetPreferredValues(label).x + 26f);
            }

            return button;
        }

        /// <summary>Gives a button the accent (primary action) style.</summary>
        public void MakePrimary(Button button)
        {
            Color accent = Theme.Accent;
            SetColors(button, accent, Color.Lerp(accent, Color.white, 0.12f), Color.Lerp(accent, Color.black, 0.12f));
            TextMeshProUGUI label = button.GetComponentInChildren<TextMeshProUGUI>();
            if (label != null)
            {
                label.color = Theme.WorkspaceBackground;
                label.fontWeight = FontWeight.SemiBold;
                ButtonVisual visual = button.GetComponent<ButtonVisual>();
                if (visual != null)
                {
                    visual.SetLabelColor(label.color);
                }
            }
        }

        /// <summary>
        /// Square, background-less icon button (a soft tile appears on hover) with an optional tooltip. Dims when
        /// not interactable; <see cref="ButtonVisual.Active"/> highlights it as a toggled state.
        /// </summary>
        public Button IconButton(string name, Transform parent, string glyph, string tooltip, UnityAction onClick,
            float size = IconButtonSize, float iconSize = 17f)
        {
            Image background = Panel(name, parent, Color.white, rounded: true, radius: ControlRadius);
            Color hover = Theme.ButtonHover;
            Button button = ConfigureButton(background, new Color(hover.r, hover.g, hover.b, 0f), hover, Theme.ButtonPressed,
                onClick);
            TextMeshProUGUI icon = Icon("Icon", background.transform, glyph, iconSize, Theme.TextSecondary);
            Stretch(icon.rectTransform);
            background.gameObject.AddComponent<ButtonVisual>().Initialize(button, Theme, icon, hoverBrightens: true);

            var layout = background.gameObject.AddComponent<LayoutElement>();
            layout.minWidth = layout.preferredWidth = size;
            layout.minHeight = layout.preferredHeight = size;
            if (!string.IsNullOrEmpty(tooltip))
            {
                AddTooltip(background.gameObject, tooltip);
            }

            return button;
        }

        /// <summary>Shows <paramref name="text"/> in the shared tooltip while the pointer rests on <paramref name="target"/>.</summary>
        public TooltipTrigger AddTooltip(GameObject target, string text)
        {
            TooltipTrigger trigger = target.GetComponent<TooltipTrigger>();
            if (trigger == null)
            {
                trigger = target.AddComponent<TooltipTrigger>();
            }

            trigger.Initialize(this, text);
            return trigger;
        }

        private Button ConfigureButton(Image background, Color normal, Color hover, Color pressed, UnityAction onClick)
        {
            var button = background.gameObject.AddComponent<Button>();
            button.targetGraphic = background;
            SetColors(button, normal, hover, pressed);
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            if (onClick != null)
            {
                button.onClick.AddListener(onClick);
            }

            return button;
        }

        private static void SetColors(Button button, Color normal, Color hover, Color pressed)
        {
            ColorBlock colors = button.colors;
            colors.normalColor = normal;
            colors.highlightedColor = hover;
            colors.selectedColor = normal;
            colors.pressedColor = pressed;
            colors.disabledColor = new Color(normal.r, normal.g, normal.b, normal.a * 0.45f);
            colors.colorMultiplier = 1f;
            colors.fadeDuration = 0.08f;
            SetColorsInstant(button, colors);
        }

        /// <summary>
        /// Applies a colour block without the cross-fade Unity starts on assignment. A freshly built button has
        /// already taken the default (white) tint in OnEnable, so a plain assignment fades it in from white, which
        /// shows as a flash whenever a panel is rebuilt. Later hover/press/interactable changes still fade.
        /// </summary>
        public static void SetColorsInstant(Selectable selectable, ColorBlock colors)
        {
            float fade = colors.fadeDuration;
            colors.fadeDuration = 0f;
            selectable.colors = colors; // zero-length cross-fade: applied immediately
            colors.fadeDuration = fade;
            selectable.colors = colors; // target already reached: no visual change
        }

        private static readonly System.Collections.Generic.List<Selectable> SnapBuffer =
            new System.Collections.Generic.List<Selectable>();

        /// <summary>
        /// Jumps every selectable under <paramref name="root"/> straight to its current tint. Call after rebuilding
        /// a panel: controls built interactable and then disabled would otherwise fade from bright to dim.
        /// </summary>
        public static void SnapTransitions(Transform root)
        {
            root.GetComponentsInChildren(SnapBuffer);
            for (int i = 0; i < SnapBuffer.Count; i++)
            {
                SetColorsInstant(SnapBuffer[i], SnapBuffer[i].colors);
            }

            SnapBuffer.Clear();
        }

        public RectTransform Separator(Transform parent, bool vertical)
        {
            Image line = Panel("Separator", parent, Theme.PanelBorder);
            var layout = line.gameObject.AddComponent<LayoutElement>();
            if (vertical)
            {
                layout.preferredWidth = 1f;
                layout.minWidth = 1f;
                layout.preferredHeight = 20f;
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
            return InputField(name, parent, placeholder, null);
        }

        /// <summary>Rounded single-line field; an optional leading <paramref name="glyph"/> (e.g. a search icon).</summary>
        public TMP_InputField InputField(string name, Transform parent, string placeholder, string glyph)
        {
            Image background = Panel(name, parent, Theme.WorkspaceBackground, rounded: true);
            var layout = background.gameObject.AddComponent<LayoutElement>();
            layout.preferredHeight = 32f;
            layout.minHeight = 32f;
            layout.flexibleWidth = 1f;

            Image border = Panel("Border", background.transform, Theme.PanelBorder);
            ApplyRounded(border, RenderAssets.RoundedOutline, ControlRadius);
            Stretch(border.rectTransform);
            border.raycastTarget = false;

            float left = 11f;
            if (!string.IsNullOrEmpty(glyph))
            {
                TextMeshProUGUI icon = Icon("Icon", background.transform, glyph, 14f, Theme.TextMuted);
                icon.rectTransform.anchorMin = new Vector2(0f, 0f);
                icon.rectTransform.anchorMax = new Vector2(0f, 1f);
                icon.rectTransform.pivot = new Vector2(0f, 0.5f);
                icon.rectTransform.sizeDelta = new Vector2(30f, 0f);
                icon.rectTransform.anchoredPosition = new Vector2(3f, 0f);
                left = 31f;
            }

            RectTransform area = CreateRect("Text Area", background.transform);
            Stretch(area, left, 10f, 4f, 4f);
            area.gameObject.AddComponent<RectMask2D>();

            TextMeshProUGUI hint = Text("Placeholder", area, placeholder, 13f, Theme.TextMuted);
            Stretch(hint.rectTransform);
            hint.fontStyle = FontStyles.Italic;
            TextMeshProUGUI text = Text("Text", area, string.Empty, 13f, Theme.TextPrimary);
            Stretch(text.rectTransform);
            // The field scrolls its text inside the masked viewport; ellipsis would hide the caret and break
            // caret/selection placement at the end of long values.
            text.overflowMode = TextOverflowModes.Overflow;
            hint.overflowMode = TextOverflowModes.Ellipsis;

            // TMP_InputField builds its caret in OnEnable, and only when textComponent is already assigned. Adding the
            // component to an active object runs OnEnable immediately with no text component, so no caret is ever
            // created. Configure it while inactive and enable it afterwards.
            bool wasActive = background.gameObject.activeSelf;
            background.gameObject.SetActive(false);
            var field = background.gameObject.AddComponent<TMP_InputField>();
            field.textViewport = area;
            field.textComponent = text;
            field.placeholder = hint;
            field.targetGraphic = background;
            field.fontAsset = Theme.UiFont;
            ColorBlock colors = field.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.12f, 1.12f, 1.18f, 1f);
            colors.selectedColor = new Color(1.12f, 1.12f, 1.18f, 1f);
            colors.disabledColor = new Color(1f, 1f, 1f, 0.5f);
            colors.colorMultiplier = 1.4f;
            field.colors = colors;
            field.caretColor = Theme.Accent;
            field.customCaretColor = true;
            field.caretWidth = 2;
            field.caretBlinkRate = 1.1f;
            field.selectionColor = new Color(Theme.Accent.r, Theme.Accent.g, Theme.Accent.b, 0.3f);
            field.navigation = new Navigation { mode = Navigation.Mode.None };
            field.resetOnDeActivation = true;
            field.restoreOriginalTextOnEscape = true;

            // Focus ring: accent border and a slightly lifted fill while the field is being edited.
            background.gameObject.AddComponent<InputFieldVisual>().Initialize(field, border, Theme.PanelBorder,
                Theme.Accent);
            background.gameObject.SetActive(wasActive);
            return field;
        }

        /// <summary>
        /// Vertical scroll view; returns the content transform (with a vertical layout) to fill. With
        /// <paramref name="smooth"/>, a <see cref="SmoothScrollRect"/> gives eased, notch-sized wheel steps.
        /// </summary>
        public RectTransform ScrollView(string name, Transform parent, out ScrollRect scrollRect, bool smooth = false)
        {
            RectTransform root = CreateRect(name, parent);
            scrollRect = smooth ? root.gameObject.AddComponent<SmoothScrollRect>() : root.gameObject.AddComponent<ScrollRect>();
            scrollRect.horizontal = false;
            scrollRect.movementType = ScrollRect.MovementType.Clamped;
            scrollRect.scrollSensitivity = 28f;

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
            layout.padding = new RectOffset(12, 12, 6, 14);
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

        /// <summary>Labelled switch (pill track with a sliding knob); returns the toggle.</summary>
        public Toggle Toggle(string name, Transform parent, string label, bool isOn, UnityAction<bool> onChanged)
        {
            RectTransform row = CreateRect(name, parent);
            Row(row, 10f, new RectOffset(0, 0, 2, 2));
            var rowLayout = row.gameObject.AddComponent<LayoutElement>();
            rowLayout.preferredHeight = 28f;
            rowLayout.minHeight = 28f;

            TextMeshProUGUI text = Text("Label", row, label, 13f, Theme.TextPrimary);
            text.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
            text.raycastTarget = true; // clicking the label toggles too

            Image track = Panel("Track", row, Theme.ButtonPressed, rounded: true, radius: 9f);
            var trackLayout = track.gameObject.AddComponent<LayoutElement>();
            trackLayout.preferredWidth = trackLayout.minWidth = 32f;
            trackLayout.preferredHeight = trackLayout.minHeight = 18f;
            Image on = Panel("On", track.transform, Theme.Accent, rounded: true, radius: 9f);
            Stretch(on.rectTransform);
            on.raycastTarget = false;
            Image knob = Panel("Knob", track.transform, Theme.TextPrimary, rounded: true, radius: 7f);
            knob.sprite = RenderAssets.Circle;
            knob.type = Image.Type.Simple;
            knob.raycastTarget = false;
            RectTransform knobRect = knob.rectTransform;
            knobRect.anchorMin = knobRect.anchorMax = new Vector2(0f, 0.5f);
            knobRect.pivot = new Vector2(0.5f, 0.5f);
            knobRect.sizeDelta = new Vector2(14f, 14f);

            var toggle = row.gameObject.AddComponent<Toggle>();
            toggle.targetGraphic = track;
            toggle.graphic = on;
            toggle.toggleTransition = UnityEngine.UI.Toggle.ToggleTransition.Fade;
            toggle.navigation = new Navigation { mode = Navigation.Mode.None };
            toggle.isOn = isOn;
            if (onChanged != null)
            {
                toggle.onValueChanged.AddListener(onChanged);
            }

            row.gameObject.AddComponent<SwitchVisual>().Initialize(toggle, knobRect, 9f, 23f);
            return toggle;
        }

        /// <summary>Small, quiet upper-case section caption.</summary>
        public TextMeshProUGUI Caption(Transform parent, string text)
        {
            TextMeshProUGUI caption = Text("Caption " + text, parent, text.ToUpperInvariant(), 10.5f, Theme.TextMuted,
                TextAlignmentOptions.BottomLeft, FontStyles.Bold);
            caption.characterSpacing = 8f;
            caption.gameObject.AddComponent<LayoutElement>().preferredHeight = 28f;
            return caption;
        }

        /// <summary>"Label  value" row; returns the value text so callers can update it.</summary>
        public TextMeshProUGUI Property(Transform parent, string label, string value, float labelWidth = 92f)
        {
            RectTransform row = CreateRect("Property " + label, parent);
            Row(row, 6f, new RectOffset(0, 0, 0, 0));
            row.gameObject.AddComponent<LayoutElement>().preferredHeight = 24f;
            TextMeshProUGUI name = Text("Label", row, label, 12.5f, Theme.TextSecondary);
            var nameLayout = name.gameObject.AddComponent<LayoutElement>();
            nameLayout.preferredWidth = labelWidth;
            nameLayout.minWidth = labelWidth;
            TextMeshProUGUI text = Text("Value", row, value, 12.5f, Theme.TextPrimary);
            text.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
            return text;
        }

        /// <summary>Labelled single-line input field row; the field commits through <paramref name="onCommit"/> on end edit.</summary>
        public TMP_InputField FieldRow(Transform parent, string label, string value, UnityAction<string> onCommit,
            float labelWidth = 92f)
        {
            RectTransform row = CreateRect("Field " + label, parent);
            Row(row, 6f, new RectOffset(0, 0, 0, 0));
            row.gameObject.AddComponent<LayoutElement>().preferredHeight = 32f;
            TextMeshProUGUI name = Text("Label", row, label, 12.5f, Theme.TextSecondary);
            var nameLayout = name.gameObject.AddComponent<LayoutElement>();
            nameLayout.preferredWidth = labelWidth;
            nameLayout.minWidth = labelWidth;
            TMP_InputField field = InputField("Input", row, label);
            field.SetTextWithoutNotify(value ?? string.Empty);
            if (onCommit != null)
            {
                field.onEndEdit.AddListener(onCommit);
            }

            return field;
        }

        /// <summary>
        /// Segmented control: equal-width segments on a recessed track. <paramref name="tooltips"/> is optional
        /// (one entry per label, null entries allowed).
        /// </summary>
        public SegmentedControl Segmented(string name, Transform parent, string[] labels, int selected,
            System.Action<int> onChanged, string[] tooltips = null, float height = 30f, float fontSize = 12f)
        {
            Image track = Panel(name, parent, Theme.WorkspaceBackground, rounded: true, radius: 8f);
            track.raycastTarget = false;
            var trackLayout = track.gameObject.AddComponent<LayoutElement>();
            trackLayout.preferredHeight = trackLayout.minHeight = height;
            // The tiles force-expand inside the track, which would otherwise make the track itself report a
            // flexible height and grow inside vertical layouts.
            trackLayout.flexibleHeight = 0f;
            trackLayout.flexibleWidth = 0f;
            var row = track.gameObject.AddComponent<HorizontalLayoutGroup>();
            row.padding = new RectOffset(3, 3, 3, 3);
            row.spacing = 2f;
            row.childControlWidth = true;
            row.childControlHeight = true;
            row.childForceExpandWidth = true;
            row.childForceExpandHeight = true;
            var control = track.gameObject.AddComponent<SegmentedControl>();

            float width = 6f;
            for (int i = 0; i < labels.Length; i++)
            {
                Image tile = Panel("Segment " + i, track.transform, Color.clear, rounded: true, radius: 6f);
                var button = tile.gameObject.AddComponent<Button>();
                button.transition = Selectable.Transition.None;
                button.navigation = new Navigation { mode = Navigation.Mode.None };
                TextMeshProUGUI label = Text("Label", tile.transform, labels[i], fontSize, Theme.TextMuted,
                    TextAlignmentOptions.Center, FontStyles.Bold);
                label.richText = true;
                label.overflowMode = TextOverflowModes.Overflow;
                Stretch(label.rectTransform, 8f, 8f);
                float preferred = label.GetPreferredValues(labels[i]).x + 22f;
                tile.gameObject.AddComponent<LayoutElement>().preferredWidth = preferred;
                width += preferred + 2f;
                var view = tile.gameObject.AddComponent<Panels.LibraryTabView>();
                view.Initialize(Theme, tile, label);
                if (tooltips != null && i < tooltips.Length && !string.IsNullOrEmpty(tooltips[i]))
                {
                    AddTooltip(tile.gameObject, tooltips[i]);
                }

                control.Add(button, view);
            }

            trackLayout.preferredWidth = width;
            control.SetSelectedWithoutNotify(selected);
            if (onChanged != null)
            {
                control.Changed += onChanged;
            }

            return control;
        }

        /// <summary>Vertical stack (layout group) for building panels.</summary>
        public static RectTransform Column(string name, Transform parent, float spacing, RectOffset padding)
        {
            RectTransform rect = CreateRect(name, parent);
            var layout = rect.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.spacing = spacing;
            layout.padding = padding;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            return rect;
        }

        public static void DestroyChildren(Transform parent)
        {
            for (int i = parent.childCount - 1; i >= 0; i--)
            {
                GameObject child = parent.GetChild(i).gameObject;
                child.SetActive(false);
                Object.Destroy(child);
            }
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
