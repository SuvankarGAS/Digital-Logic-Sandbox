using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace DigitalLogicSandbox.UI
{
    /// <summary>
    /// A UI graphic that draws a batch of solid, axis-aligned rectangles in one mesh. Callers rebuild the batch
    /// (<see cref="Clear"/>, <see cref="AddRect"/>, <see cref="Commit"/>) whenever their content changes; nothing
    /// is allocated once the lists have grown. Coordinates are local to this graphic's rect, with the origin at
    /// its top-left corner and y growing downwards (like rows on a page).
    /// </summary>
    public sealed class QuadGraphic : MaskableGraphic
    {
        /// <summary>Upper bound on rectangles per mesh (4 vertices each).</summary>
        public const int MaxRects = 16000;

        private readonly List<Rect> _rects = new List<Rect>(1024);
        private readonly List<Color32> _colors = new List<Color32>(1024);

        public int RectCount => _rects.Count;

        public void Clear()
        {
            _rects.Clear();
            _colors.Clear();
        }

        /// <summary>Adds a rectangle from (<paramref name="x0"/>, <paramref name="y0"/>) to (<paramref name="x1"/>, <paramref name="y1"/>), top-left based.</summary>
        public void AddRect(float x0, float y0, float x1, float y1, Color color)
        {
            // 4 vertices per rectangle; stay well inside a single mesh's 65 535-vertex limit.
            if (x1 <= x0 || y1 <= y0 || color.a <= 0f || _rects.Count >= MaxRects)
            {
                return;
            }

            _rects.Add(Rect.MinMaxRect(x0, y0, x1, y1));
            _colors.Add(color);
        }

        /// <summary>Uploads the batch.</summary>
        public void Commit()
        {
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            Rect bounds = rectTransform.rect;
            float left = bounds.xMin;
            float top = bounds.yMax;
            UIVertex vertex = UIVertex.simpleVert;
            for (int i = 0; i < _rects.Count; i++)
            {
                Rect r = _rects[i];
                Color32 c = _colors[i];
                if (color != Color.white)
                {
                    c = (Color)c * color;
                }

                int start = vh.currentVertCount;
                vertex.color = c;
                vertex.position = new Vector3(left + r.xMin, top - r.yMax);
                vh.AddVert(vertex);
                vertex.position = new Vector3(left + r.xMin, top - r.yMin);
                vh.AddVert(vertex);
                vertex.position = new Vector3(left + r.xMax, top - r.yMin);
                vh.AddVert(vertex);
                vertex.position = new Vector3(left + r.xMax, top - r.yMax);
                vh.AddVert(vertex);
                vh.AddTriangle(start, start + 1, start + 2);
                vh.AddTriangle(start + 2, start + 3, start);
            }
        }
    }
}
