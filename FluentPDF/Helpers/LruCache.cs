using System.Collections.Generic;

namespace FluentPDF.Helpers
{
    /// <summary>
    /// 定容 LRU 缓存。非线程安全，调用方负责同步（UI 线程单线程使用时无需额外锁）。
    /// </summary>
    internal sealed class LruCache<TKey, TValue> where TKey : notnull
    {
        private readonly int _capacity;
        private readonly Dictionary<TKey, LinkedListNode<(TKey key, TValue value)>> _map;
        private readonly LinkedList<(TKey key, TValue value)> _list;

        public int Count => _map.Count;

        public LruCache(int capacity)
        {
            _capacity = capacity;
            _map  = new Dictionary<TKey, LinkedListNode<(TKey, TValue)>>(capacity);
            _list = new LinkedList<(TKey, TValue)>();
        }

        /// <summary>命中时将节点移到链表头（最近使用），返回 true。</summary>
        public bool TryGet(TKey key, out TValue? value)
        {
            if (_map.TryGetValue(key, out var node))
            {
                _list.Remove(node);
                _list.AddFirst(node);
                value = node.Value.value;
                return true;
            }
            value = default;
            return false;
        }

        /// <summary>写入缓存。已存在时更新并移到头部；超容量时淘汰最久未使用的尾节点。</summary>
        public void Put(TKey key, TValue value)
        {
            if (_map.TryGetValue(key, out var existing))
            {
                _list.Remove(existing);
                _map.Remove(key);
            }
            else if (_map.Count >= _capacity)
            {
                var last = _list.Last!;
                _map.Remove(last.Value.key);
                _list.RemoveLast();
            }
            var node = _list.AddFirst((key, value));
            _map[key] = node;
        }

        public void Clear()
        {
            _map.Clear();
            _list.Clear();
        }
    }
}
