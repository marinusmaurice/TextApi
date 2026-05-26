namespace TextAPI.Core.Buffer;

/// <summary>
/// Simple O(1) get/set LRU cache backed by a dictionary + doubly-linked list.
/// Used to cache GetLineContent results so repeated rendering of the same lines
/// avoids redundant piece-tree traversals.
/// </summary>
internal sealed class LruCache<TKey, TValue> where TKey : notnull
{
    private sealed class Entry
    {
        internal TKey    Key   = default!;
        internal TValue  Value = default!;
        internal Entry?  Prev;
        internal Entry?  Next;
    }

    private readonly int _capacity;
    private readonly Dictionary<TKey, Entry> _map;
    private readonly Entry _head = new();   // sentinel
    private readonly Entry _tail = new();   // sentinel

    internal LruCache(int capacity)
    {
        _capacity    = capacity;
        _map         = new Dictionary<TKey, Entry>(capacity);
        _head.Next   = _tail;
        _tail.Prev   = _head;
    }

    internal bool TryGet(TKey key, out TValue? value)
    {
        if (_map.TryGetValue(key, out var entry))
        {
            MoveToFront(entry);
            value = entry.Value;
            return true;
        }
        value = default;
        return false;
    }

    internal void Set(TKey key, TValue value)
    {
        if (_map.TryGetValue(key, out var existing))
        {
            existing.Value = value;
            MoveToFront(existing);
            return;
        }
        var e = new Entry { Key = key, Value = value };
        _map[key] = e;
        AddToFront(e);
        if (_map.Count > _capacity)
            Evict();
    }

    internal void Clear()
    {
        _map.Clear();
        _head.Next = _tail;
        _tail.Prev = _head;
    }

    private void MoveToFront(Entry e)
    {
        Remove(e);
        AddToFront(e);
    }

    private void AddToFront(Entry e)
    {
        e.Prev        = _head;
        e.Next        = _head.Next;
        _head.Next!.Prev = e;
        _head.Next    = e;
    }

    private static void Remove(Entry e)
    {
        e.Prev!.Next = e.Next;
        e.Next!.Prev = e.Prev;
    }

    private void Evict()
    {
        var lru = _tail.Prev!;
        if (lru == _head) return;
        Remove(lru);
        _map.Remove(lru.Key);
    }
}
