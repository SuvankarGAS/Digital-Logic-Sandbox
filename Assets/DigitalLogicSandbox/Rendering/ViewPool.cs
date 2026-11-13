using System;
using System.Collections.Generic;
using UnityEngine;

namespace DigitalLogicSandbox.Rendering
{
    /// <summary>Reuses view components instead of creating/destroying GameObjects as the circuit changes.</summary>
    public sealed class ViewPool<T> where T : Component
    {
        private readonly Func<T> _factory;
        private readonly Stack<T> _free = new Stack<T>();

        public ViewPool(Func<T> factory)
        {
            _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        }

        public int FreeCount => _free.Count;

        public int CreatedCount { get; private set; }

        public T Get()
        {
            T view;
            if (_free.Count > 0)
            {
                view = _free.Pop();
            }
            else
            {
                view = _factory();
                CreatedCount++;
            }

            view.gameObject.SetActive(true);
            return view;
        }

        public void Release(T view)
        {
            if (view == null)
            {
                return;
            }

            view.gameObject.SetActive(false);
            _free.Push(view);
        }
    }
}
