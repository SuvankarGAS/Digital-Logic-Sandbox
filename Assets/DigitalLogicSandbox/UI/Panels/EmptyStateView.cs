using DigitalLogicSandbox.CircuitEditor.Workspace;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DigitalLogicSandbox.UI.Panels
{
    /// <summary>
    /// Gentle first-run guidance in the middle of an empty workspace: one sentence on how to start and the three
    /// navigation gestures. It never blocks the pointer and fades away as soon as the circuit has a part.
    /// </summary>
    public sealed class EmptyStateView : MonoBehaviour
    {
        private const float FadeSpeed = 6f;

        /// <summary>Faster fade-out, so the hint is gone before a placement ghost reaches it.</summary>
        private const float FadeOutSpeed = 12f;

        private WorkspaceController _workspace;
        private CanvasGroup _group;
        private bool _wanted;

        /// <summary>True while the hint is (or is becoming) visible.</summary>
        public bool IsShown => _wanted;

        public static EmptyStateView Create(Transform parent, UiFactory ui, WorkspaceController workspace)
        {
            RectTransform root = UiFactory.Column("Empty State", parent, 8f, new RectOffset(0, 0, 0, 0));
            root.anchorMin = root.anchorMax = new Vector2(0.5f, 0.5f);
            root.pivot = new Vector2(0.5f, 0.5f);
            root.sizeDelta = new Vector2(440f, 200f);
            root.anchoredPosition = new Vector2(0f, 20f);
            root.GetComponent<VerticalLayoutGroup>().childAlignment = TextAnchor.MiddleCenter;

            var view = root.gameObject.AddComponent<EmptyStateView>();
            view._workspace = workspace;
            view._group = root.gameObject.AddComponent<CanvasGroup>();
            view._group.blocksRaycasts = false;
            view._group.interactable = false;
            view._group.alpha = 0f;

            // Soft glowing tile with an icon.
            RectTransform tileHolder = UiFactory.CreateRect("Tile Holder", root);
            tileHolder.gameObject.AddComponent<LayoutElement>().preferredHeight = 64f;
            Image glow = ui.Panel("Glow", tileHolder, ui.Theme.AccentSoft);
            glow.sprite = DigitalLogicSandbox.Rendering.RenderAssets.Glow;
            glow.raycastTarget = false;
            glow.rectTransform.anchorMin = glow.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            glow.rectTransform.sizeDelta = new Vector2(130f, 130f);
            Image tile = ui.Panel("Tile", tileHolder, ui.Theme.PanelBackground, rounded: true, radius: 14f);
            tile.raycastTarget = false;
            tile.rectTransform.anchorMin = tile.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            tile.rectTransform.sizeDelta = new Vector2(52f, 52f);
            TextMeshProUGUI icon = ui.Icon("Icon", tile.transform, Icons.Zap, 22f, ui.Theme.SignalColor(Core.Signals.Signal.High));
            UiFactory.Stretch(icon.rectTransform);

            TextMeshProUGUI title = ui.Text("Title", root, "Start with a gate", 19f, ui.Theme.TextPrimary,
                TextAlignmentOptions.Center, FontStyles.Bold);
            title.gameObject.AddComponent<LayoutElement>().preferredHeight = 30f;

            TextMeshProUGUI body = ui.Text("Body", root,
                "Drag AND or NOT from the library, add an Input and an Output, then draw wires between the pins.",
                13.5f, ui.Theme.TextSecondary, TextAlignmentOptions.Center);
            body.enableWordWrapping = true;
            body.gameObject.AddComponent<LayoutElement>().preferredHeight = 44f;

            string dot = "  <color=#" + ColorUtility.ToHtmlStringRGB(ui.Theme.PanelBorder) + ">·</color>  ";
            TextMeshProUGUI hints = ui.Text("Hints", root,
                "Scroll to zoom" + dot + "Middle-drag to pan" + dot + "Right-click for more", 12f, ui.Theme.TextMuted,
                TextAlignmentOptions.Center);
            hints.richText = true;
            hints.gameObject.AddComponent<LayoutElement>().preferredHeight = 22f;

            workspace.StatusChanged += view.Refresh;
            workspace.NavigationChanged += view.Refresh;
            workspace.PlacementChanged += view.Refresh;
            view.Refresh();
            view._group.alpha = view._wanted ? 1f : 0f;
            return view;
        }

        public void Refresh()
        {
            _wanted = _workspace != null && _workspace.IsInitialized && _workspace.Circuit != null &&
                _workspace.Circuit.Nodes.Count == 0 && _workspace.IsEditable &&
                _workspace.Navigator.CurrentIndex == 0 &&
                // The hint is screen-space UI and always draws over the workspace: step aside for the ghost.
                !_workspace.IsPlacing;
        }

        private void LateUpdate()
        {
            float target = _wanted ? 1f : 0f;
            if (!Mathf.Approximately(_group.alpha, target))
            {
                _group.alpha = Mathf.MoveTowards(_group.alpha, target,
                    Time.unscaledDeltaTime * (_wanted ? FadeSpeed : FadeOutSpeed));
            }

            if (_group.alpha > 0f)
            {
                Center();
            }
        }

        /// <summary>Centres the hint on the visible workspace (between the library and the properties card).</summary>
        private void Center()
        {
            if (_workspace == null || _workspace.VisibleScreenArea == null || !(transform.parent is RectTransform parent))
            {
                return;
            }

            Rect area = _workspace.VisibleScreenArea();
            if (area.width <= 0f || area.height <= 0f)
            {
                return;
            }

            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, area.center, null, out Vector2 local))
            {
                Vector2 offset = parent.rect.center;
                ((RectTransform)transform).anchoredPosition = local - offset + new Vector2(0f, 20f);
            }
        }

        private void OnDestroy()
        {
            if (_workspace != null)
            {
                _workspace.StatusChanged -= Refresh;
                _workspace.NavigationChanged -= Refresh;
                _workspace.PlacementChanged -= Refresh;
            }
        }
    }
}
