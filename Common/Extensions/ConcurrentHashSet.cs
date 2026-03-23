namespace DT_DataAcquisitionSystem.Common
{
    public class ConcurrentHashSet<T>
    {
        private readonly System.Collections.Concurrent.ConcurrentDictionary<T, byte> _dictionary =
            new System.Collections.Concurrent.ConcurrentDictionary<T, byte>();

        /// <summary>
        /// 添加元素，如果已存在则返回 false
        /// </summary>
        public bool Add(T item) => _dictionary.TryAdd(item, 0);

        /// <summary>
        /// 移除元素（必须添加此方法，否则 finally 块会报错）
        /// </summary>
        public bool Remove(T item) => _dictionary.TryRemove(item, out _);

        /// <summary>
        /// 检查是否存在
        /// </summary>
        public bool Contains(T item) => _dictionary.ContainsKey(item);

        public void Clear() => _dictionary.Clear();

        public int Count => _dictionary.Count;
    }
}