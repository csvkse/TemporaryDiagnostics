namespace TemporaryDiagnostics.Infrastructure.Logging;

/// <summary>
/// 线程安全的固定容量环形缓冲区（Ring Buffer）。
/// 保证内存占用恒定，淘汰旧数据，零持续 GC 分配压力。
/// </summary>
public sealed class ConcurrentRingBuffer<T>
{
    private readonly T[] _buffer;
    private readonly int _capacity;
    private int _head;
    private int _count;
    private readonly object _lock = new();

    public int Capacity => _capacity;

    public ConcurrentRingBuffer(int capacity)
    {
        _capacity = Math.Max(10, capacity);
        _buffer = new T[_capacity];
    }

    public void Add(T item)
    {
        lock (_lock)
        {
            _buffer[_head] = item;
            _head = (_head + 1) % _capacity;
            if (_count < _capacity)
            {
                _count++;
            }
        }
    }

    public IReadOnlyList<T> ToList(int limit = 100)
    {
        lock (_lock)
        {
            var take = Math.Min(_count, limit);
            var result = new List<T>(take);

            // 倒序读取最近的数据
            for (var i = 0; i < take; i++)
            {
                var index = (_head - 1 - i + _capacity) % _capacity;
                result.Add(_buffer[index]);
            }

            return result;
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            Array.Clear(_buffer, 0, _capacity);
            _head = 0;
            _count = 0;
        }
    }
}
