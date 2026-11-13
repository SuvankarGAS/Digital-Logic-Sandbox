using System;

namespace DigitalLogicSandbox.Utilities
{
    /// <summary>
    /// FIFO queue of ints backed by a circular array. Allocation-free once capacity is reached; grows by doubling
    /// only when full. Used as the simulation worklist (deliberately not a priority queue).
    /// </summary>
    public sealed class IntRingQueue
    {
        private int[] _items;
        private int _head;
        private int _count;

        public IntRingQueue(int capacity = 16)
        {
            _items = new int[Math.Max(1, capacity)];
        }

        public int Count => _count;

        public int Capacity => _items.Length;

        public void Enqueue(int value)
        {
            if (_count == _items.Length)
            {
                Grow(_items.Length * 2);
            }

            int tail = _head + _count;
            if (tail >= _items.Length)
            {
                tail -= _items.Length;
            }

            _items[tail] = value;
            _count++;
        }

        public int Dequeue()
        {
            if (_count == 0)
            {
                throw new InvalidOperationException("Queue is empty.");
            }

            int value = _items[_head];
            _head++;
            if (_head == _items.Length)
            {
                _head = 0;
            }

            _count--;
            return value;
        }

        /// <summary>Returns the item at logical position <paramref name="index"/> (0 = next to dequeue).</summary>
        public int PeekAt(int index)
        {
            if ((uint)index >= (uint)_count)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            int position = _head + index;
            if (position >= _items.Length)
            {
                position -= _items.Length;
            }

            return _items[position];
        }

        public void Clear()
        {
            _head = 0;
            _count = 0;
        }

        public void EnsureCapacity(int capacity)
        {
            if (capacity > _items.Length)
            {
                Grow(capacity);
            }
        }

        private void Grow(int capacity)
        {
            var items = new int[capacity];
            for (int i = 0; i < _count; i++)
            {
                int position = _head + i;
                if (position >= _items.Length)
                {
                    position -= _items.Length;
                }

                items[i] = _items[position];
            }

            _items = items;
            _head = 0;
        }
    }
}
