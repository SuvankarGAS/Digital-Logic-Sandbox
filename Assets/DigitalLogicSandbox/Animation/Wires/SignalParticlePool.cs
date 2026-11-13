using System.Collections.Generic;
using DigitalLogicSandbox.Rendering;
using UnityEngine;

namespace DigitalLogicSandbox.Animation.Wires
{
    /// <summary>
    /// Fixed-capacity pool of the small glowing dots drawn at travelling signal fronts. Objects are created lazily
    /// up to the capacity and then reused; nothing is destroyed while the workspace lives.
    /// </summary>
    public sealed class SignalParticlePool
    {
        private readonly Transform _root;
        private readonly int _capacity;
        private readonly Material _material;
        private readonly List<SpriteRenderer> _all = new List<SpriteRenderer>();
        private readonly Stack<int> _free = new Stack<int>();

        public SignalParticlePool(Transform parent, int capacity, Material material)
        {
            _material = material;
            _root = new GameObject("Signal Particles").transform;
            _root.SetParent(parent, false);
            _capacity = Mathf.Max(0, capacity);
        }

        public int ActiveCount => _all.Count - _free.Count;

        public int CreatedCount => _all.Count;

        /// <summary>Returns a particle handle, or -1 when the pool is exhausted (the front is then drawn without one).</summary>
        public int Acquire(Color color, float size)
        {
            int handle;
            if (_free.Count > 0)
            {
                handle = _free.Pop();
            }
            else if (_all.Count < _capacity)
            {
                handle = _all.Count;
                _all.Add(Create(handle));
            }
            else
            {
                return -1;
            }

            SpriteRenderer particle = _all[handle];
            particle.color = color;
            particle.transform.localScale = new Vector3(size, size, 1f);
            particle.gameObject.SetActive(true);
            return handle;
        }

        public void Move(int handle, Vector3 position)
        {
            if (handle >= 0)
            {
                _all[handle].transform.position = new Vector3(position.x, position.y, 0f);
            }
        }

        public void Release(int handle)
        {
            if (handle < 0 || handle >= _all.Count || !_all[handle].gameObject.activeSelf)
            {
                return;
            }

            _all[handle].gameObject.SetActive(false);
            _free.Push(handle);
        }

        public void ReleaseAll()
        {
            _free.Clear();
            for (int i = 0; i < _all.Count; i++)
            {
                _all[i].gameObject.SetActive(false);
                _free.Push(i);
            }
        }

        public void Destroy()
        {
            if (_root != null)
            {
                Object.Destroy(_root.gameObject);
            }
        }

        private SpriteRenderer Create(int index)
        {
            var go = new GameObject("Particle " + index);
            go.transform.SetParent(_root, false);
            var renderer = go.AddComponent<SpriteRenderer>();
            renderer.sprite = RenderAssets.Circle;
            if (_material != null)
            {
                renderer.sharedMaterial = _material;
            }

            renderer.sortingOrder = SortingOrders.SignalSpark;
            go.SetActive(false);
            return renderer;
        }
    }
}
