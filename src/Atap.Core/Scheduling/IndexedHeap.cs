namespace Atap.Core.Scheduling;

/// <summary>Something that remembers where it sits in an <see cref="IndexedHeap{T}"/>.</summary>
internal interface IHeapItem
{
    /// <summary>The slot in the heap, or -1 while it is in none.</summary>
    int HeapIndex { get; set; }
}

/// <summary>
/// A binary min-heap where any item can be removed in O(log n), because each item knows its slot.
/// Slot 0 holds the smallest item, and the children of slot i are slots 2i + 1 and 2i + 2.
/// </summary>
internal sealed class IndexedHeap<T>(IComparer<T> comparer)
    where T : class, IHeapItem
{
    private readonly List<T> _items = [];

    public int Count => _items.Count;

    /// <summary>The item in a slot. A slot is never smaller than its parent, so a walk can stop at any slot past a limit.</summary>
    public T this[int index] => _items[index];

    public T? Peek() => _items.Count > 0 ? _items[0] : null;

    public void Add(T item)
    {
        _items.Add(item);
        item.HeapIndex = _items.Count - 1;
        SiftUp(item.HeapIndex);
    }

    public T Pop()
    {
        var top = _items[0];
        RemoveAt(0);
        return top;
    }

    /// <summary>False when the item is not in this heap.</summary>
    public bool Remove(T item)
    {
        var index = item.HeapIndex;
        if (index < 0 || index >= _items.Count || !ReferenceEquals(_items[index], item))
            return false;
        RemoveAt(index);
        return true;
    }

    /// <summary>Restores the order after the keys of any number of items changed in place.</summary>
    public void Rebuild()
    {
        for (var i = (_items.Count / 2) - 1; i >= 0; i--)
            SiftDown(i);
    }

    public void Clear()
    {
        foreach (var item in _items)
            item.HeapIndex = -1;
        _items.Clear();
    }

    private void RemoveAt(int index)
    {
        var removed = _items[index];
        var lastIndex = _items.Count - 1;
        var last = _items[lastIndex];
        _items.RemoveAt(lastIndex);
        removed.HeapIndex = -1;
        if (index == lastIndex)
            return;

        // The last item fills the gap. It may belong above or below that slot.
        Place(last, index);
        SiftUp(index);
        SiftDown(last.HeapIndex);
    }

    private void SiftUp(int index)
    {
        var item = _items[index];
        while (index > 0)
        {
            var parent = (index - 1) / 2;
            if (comparer.Compare(item, _items[parent]) >= 0)
                break;
            Place(_items[parent], index);
            index = parent;
        }
        Place(item, index);
    }

    private void SiftDown(int index)
    {
        var item = _items[index];
        var count = _items.Count;
        while (true)
        {
            var child = (2 * index) + 1;
            if (child >= count)
                break;
            if (child + 1 < count && comparer.Compare(_items[child + 1], _items[child]) < 0)
                child++;
            if (comparer.Compare(_items[child], item) >= 0)
                break;
            Place(_items[child], index);
            index = child;
        }
        Place(item, index);
    }

    private void Place(T item, int index)
    {
        _items[index] = item;
        item.HeapIndex = index;
    }
}
