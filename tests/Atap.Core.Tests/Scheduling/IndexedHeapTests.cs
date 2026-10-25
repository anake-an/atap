using Atap.Core.Scheduling;

namespace Atap.Core.Tests.Scheduling;

public sealed class IndexedHeapTests
{
    [Fact]
    public void RandomAddsAndRemoves_AlwaysPopInOrder()
    {
        var random = new Random(7);
        var heap = new IndexedHeap<Node>(Comparer<Node>.Create((a, b) => a.Key.CompareTo(b.Key)));
        var inHeap = new List<Node>();

        for (var round = 0; round < 2000; round++)
        {
            var action = random.Next(3);
            if (action < 2 || inHeap.Count == 0)
            {
                var node = new Node(random.Next(1000));
                heap.Add(node);
                inHeap.Add(node);
            }
            else
            {
                var node = inHeap[random.Next(inHeap.Count)];
                Assert.True(heap.Remove(node));
                Assert.False(heap.Remove(node));
                inHeap.Remove(node);
            }
            Assert.Equal(inHeap.Count, heap.Count);
            Assert.Equal(inHeap.Min(n => n.Key), heap.Peek()!.Key);
        }

        var popped = new List<int>();
        while (heap.Count > 0)
            popped.Add(heap.Pop().Key);
        Assert.Equal(inHeap.Select(n => n.Key).Order(), popped);
        Assert.All(inHeap, n => Assert.Equal(-1, n.HeapIndex));
    }

    [Fact]
    public void Rebuild_RestoresOrderAfterKeysChangeInPlace()
    {
        var heap = new IndexedHeap<Node>(Comparer<Node>.Create((a, b) => a.Key.CompareTo(b.Key)));
        var nodes = Enumerable.Range(0, 50).Select(i => new Node(i)).ToList();
        foreach (var node in nodes)
            heap.Add(node);

        foreach (var node in nodes)
            node.Key = 100 - node.Key;
        heap.Rebuild();

        var popped = new List<int>();
        while (heap.Count > 0)
            popped.Add(heap.Pop().Key);
        Assert.Equal(Enumerable.Range(51, 50), popped);
    }

    [Fact]
    public void Clear_ForgetsEverySlot()
    {
        var heap = new IndexedHeap<Node>(Comparer<Node>.Create((a, b) => a.Key.CompareTo(b.Key)));
        var node = new Node(1);
        heap.Add(node);
        heap.Add(new Node(2));

        heap.Clear();

        Assert.Equal(0, heap.Count);
        Assert.Null(heap.Peek());
        Assert.False(heap.Remove(node));
    }

    private sealed class Node(int key) : IHeapItem
    {
        public int Key { get; set; } = key;

        public int HeapIndex { get; set; } = -1;
    }
}
