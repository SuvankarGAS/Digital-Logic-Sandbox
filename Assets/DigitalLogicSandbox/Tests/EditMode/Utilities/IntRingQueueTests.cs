using DigitalLogicSandbox.Utilities;
using NUnit.Framework;

namespace DigitalLogicSandbox.Tests.EditMode.Utilities
{
    public sealed class IntRingQueueTests
    {
        [Test]
        public void IsFifoAcrossWrapAround()
        {
            var queue = new IntRingQueue(4);
            for (int round = 0; round < 10; round++)
            {
                queue.Enqueue(round * 2);
                queue.Enqueue(round * 2 + 1);
                Assert.That(queue.Dequeue(), Is.EqualTo(round * 2));
                Assert.That(queue.Dequeue(), Is.EqualTo(round * 2 + 1));
            }

            Assert.That(queue.Count, Is.EqualTo(0));
            Assert.That(queue.Capacity, Is.EqualTo(4), "No growth needed.");
        }

        [Test]
        public void GrowsPreservingOrder()
        {
            var queue = new IntRingQueue(2);
            queue.Enqueue(1);
            queue.Enqueue(2);
            queue.Dequeue();
            queue.Enqueue(3);
            queue.Enqueue(4);
            queue.Enqueue(5);

            Assert.That(queue.PeekAt(0), Is.EqualTo(2));
            Assert.That(queue.Dequeue(), Is.EqualTo(2));
            Assert.That(queue.Dequeue(), Is.EqualTo(3));
            Assert.That(queue.Dequeue(), Is.EqualTo(4));
            Assert.That(queue.Dequeue(), Is.EqualTo(5));
        }

        [Test]
        public void DequeueEmpty_Throws()
        {
            Assert.Throws<System.InvalidOperationException>(() => new IntRingQueue().Dequeue());
        }
    }
}
