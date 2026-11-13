using UnityEngine;

namespace DigitalLogicSandbox.Rendering
{
    /// <summary>Rubber-band selection rectangle: a translucent fill plus an outline loop. Created once and reused.</summary>
    public sealed class SelectionRectView : MonoBehaviour
    {
        private SpriteRenderer _fill;
        private LineRenderer _outline;

        public static SelectionRectView Create(Transform parent, VisualTheme theme)
        {
            var go = new GameObject("Selection Rectangle");
            go.transform.SetParent(parent, false);
            var view = go.AddComponent<SelectionRectView>();

            var fillGo = new GameObject("Fill");
            fillGo.transform.SetParent(go.transform, false);
            view._fill = fillGo.AddComponent<SpriteRenderer>();
            view._fill.sprite = RenderAssets.Square;
            view._fill.sharedMaterial = theme.UnlitMaterial;
            Color fill = theme.Selection;
            fill.a = 0.12f;
            view._fill.color = fill;
            view._fill.sortingOrder = SortingOrders.SelectionRectangle;

            view._outline = go.AddComponent<LineRenderer>();
            view._outline.useWorldSpace = true;
            view._outline.loop = true;
            view._outline.positionCount = 4;
            view._outline.sharedMaterial = theme.UnlitMaterial;
            view._outline.startColor = theme.Selection;
            view._outline.endColor = theme.Selection;
            view._outline.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            view._outline.receiveShadows = false;
            view._outline.sortingOrder = SortingOrders.SelectionRectangle + 1;
            go.SetActive(false);
            return view;
        }

        /// <summary>Shows the rectangle between two world corners; <paramref name="lineWidth"/> keeps it crisp at any zoom.</summary>
        public void Show(Vector2 a, Vector2 b, float lineWidth)
        {
            gameObject.SetActive(true);
            Vector2 min = Vector2.Min(a, b);
            Vector2 max = Vector2.Max(a, b);
            _fill.transform.position = new Vector3((min.x + max.x) * 0.5f, (min.y + max.y) * 0.5f, 0f);
            _fill.transform.localScale = new Vector3(Mathf.Max(0.0001f, max.x - min.x), Mathf.Max(0.0001f, max.y - min.y), 1f);
            _outline.widthMultiplier = lineWidth;
            _outline.SetPosition(0, new Vector3(min.x, min.y));
            _outline.SetPosition(1, new Vector3(max.x, min.y));
            _outline.SetPosition(2, new Vector3(max.x, max.y));
            _outline.SetPosition(3, new Vector3(min.x, max.y));
        }

        public void Hide()
        {
            gameObject.SetActive(false);
        }
    }
}
