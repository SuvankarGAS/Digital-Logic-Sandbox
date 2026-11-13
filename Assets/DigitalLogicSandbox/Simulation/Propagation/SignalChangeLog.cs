using System;

namespace DigitalLogicSandbox.Simulation.Propagation
{
    /// <summary>
    /// Append-only buffer of <see cref="SignalChange"/>s filled by the engine and drained by consumers
    /// (animation, inspector) which call <see cref="Clear"/> after reading. Array-backed: no allocation once
    /// warmed up. If consumers stop draining, recording stops at the configured cap and
    /// <see cref="Overflowed"/> tells them to do a full refresh instead.
    /// </summary>
    public sealed class SignalChangeLog
    {
        private SignalChange[] _items;
        private readonly int _maxCount;

        public SignalChangeLog(int initialCapacity = 256, int maxCount = 1 << 20)
        {
            _items = new SignalChange[Math.Max(1, initialCapacity)];
            _maxCount = Math.Max(1, maxCount);
        }

        public int Count { get; private set; }

        /// <summary>True if changes were dropped since the last <see cref="Clear"/>.</summary>
        public bool Overflowed { get; private set; }

        public SignalChange this[int index]
        {
            get
            {
                if ((uint)index >= (uint)Count)
                {
                    throw new ArgumentOutOfRangeException(nameof(index));
                }

                return _items[index];
            }
        }

        public void Add(in SignalChange change)
        {
            if (Count == _items.Length)
            {
                if (Count >= _maxCount)
                {
                    Overflowed = true;
                    return;
                }

                Array.Resize(ref _items, Math.Min(_items.Length * 2, _maxCount));
            }

            _items[Count++] = change;
        }

        public void Clear()
        {
            Count = 0;
            Overflowed = false;
        }

        /// <summary>Allocation-free view of the recorded changes.</summary>
        public ReadOnlySpan<SignalChange> AsSpan()
        {
            return new ReadOnlySpan<SignalChange>(_items, 0, Count);
        }
    }
}
