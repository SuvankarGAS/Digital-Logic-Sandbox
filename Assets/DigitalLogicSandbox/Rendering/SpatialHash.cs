using System.Collections.Generic;
using UnityEngine;

namespace DigitalLogicSandbox.Rendering
{
    /// <summary>
    /// Uniform-grid index of axis-aligned rectangles keyed by <typeparamref name="T"/>, for "what is near this
    /// area" queries over large circuits (gate bodies, wire bounds). Cell lists are pooled, so steady-state updates
    /// and queries do not allocate.
    /// </summary>
    public sealed class SpatialHash<T>
    {
        private readonly float _cellSize;
        private readonly Dictionary<long, List<T>> _cells = new Dictionary<long, List<T>>();
        private readonly Dictionary<T, Rect> _rects = new Dictionary<T, Rect>();
        private readonly Stack<List<T>> _pool = new Stack<List<T>>();
        private readonly HashSet<T> _seen = new HashSet<T>();

        public SpatialHash(float cellSize)
        {
            _cellSize = Mathf.Max(0.01f, cellSize);
        }

        public int Count => _rects.Count;

        public bool TryGetRect(T key, out Rect rect) => _rects.TryGetValue(key, out rect);

        public void Clear()
        {
            foreach (List<T> list in _cells.Values)
            {
                list.Clear();
                _pool.Push(list);
            }

            _cells.Clear();
            _rects.Clear();
        }

        /// <summary>Inserts <paramref name="key"/> or moves it to <paramref name="rect"/>.</summary>
        public void Set(T key, Rect rect)
        {
            Remove(key);
            _rects[key] = rect;
            CellRange(rect, out int x0, out int y0, out int x1, out int y1);
            for (int x = x0; x <= x1; x++)
            {
                for (int y = y0; y <= y1; y++)
                {
                    long cell = Key(x, y);
                    if (!_cells.TryGetValue(cell, out List<T> list))
                    {
                        list = _pool.Count > 0 ? _pool.Pop() : new List<T>(4);
                        _cells[cell] = list;
                    }

                    list.Add(key);
                }
            }
        }

        public bool Remove(T key)
        {
            if (!_rects.TryGetValue(key, out Rect rect))
            {
                return false;
            }

            _rects.Remove(key);
            CellRange(rect, out int x0, out int y0, out int x1, out int y1);
            for (int x = x0; x <= x1; x++)
            {
                for (int y = y0; y <= y1; y++)
                {
                    long cell = Key(x, y);
                    if (_cells.TryGetValue(cell, out List<T> list))
                    {
                        list.Remove(key);
                        if (list.Count == 0)
                        {
                            _cells.Remove(cell);
                            _pool.Push(list);
                        }
                    }
                }
            }

            return true;
        }

        /// <summary>Appends every key whose rectangle overlaps <paramref name="area"/> (each once).</summary>
        public void Query(Rect area, List<T> results)
        {
            _seen.Clear();
            CellRange(area, out int x0, out int y0, out int x1, out int y1);
            for (int x = x0; x <= x1; x++)
            {
                for (int y = y0; y <= y1; y++)
                {
                    if (!_cells.TryGetValue(Key(x, y), out List<T> list))
                    {
                        continue;
                    }

                    for (int i = 0; i < list.Count; i++)
                    {
                        T key = list[i];
                        if (_seen.Add(key) && _rects[key].Overlaps(area))
                        {
                            results.Add(key);
                        }
                    }
                }
            }
        }

        private void CellRange(Rect rect, out int x0, out int y0, out int x1, out int y1)
        {
            x0 = Mathf.FloorToInt(rect.xMin / _cellSize);
            y0 = Mathf.FloorToInt(rect.yMin / _cellSize);
            x1 = Mathf.FloorToInt(rect.xMax / _cellSize);
            y1 = Mathf.FloorToInt(rect.yMax / _cellSize);
        }

        private static long Key(int x, int y) => ((long)x << 32) ^ (uint)y;
    }
}
