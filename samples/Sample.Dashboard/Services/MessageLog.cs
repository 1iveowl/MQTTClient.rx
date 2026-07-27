using Sample.Dashboard.Client.Models;

namespace Sample.Dashboard.Services;

/// <summary>
/// A bounded ring of the most recent messages, replayed to browsers that join late.
/// </summary>
/// <remarks>
/// MQTT can deliver faster than any UI can render, so nothing here is unbounded. The cap is small on
/// purpose: this is the "what did I just miss" buffer, not a message store. Anything that needs
/// history belongs in a real subscriber, deliberately not in a dashboard sample.
/// </remarks>
public sealed class MessageLog
{
    private const int Capacity = 500;

    private readonly Lock _gate = new();
    private readonly Queue<MessageDto> _recent = new(Capacity);

    private long _sequence;
    private long _total;

    /// <summary>Total messages seen since the current connection was established.</summary>
    public long Total
    {
        get
        {
            lock (_gate)
            {
                return _total;
            }
        }
    }

    /// <summary>Allocates the next sequence number, which doubles as the client-side cache key.</summary>
    public long NextSequence() => Interlocked.Increment(ref _sequence);

    /// <summary>Records a message, evicting the oldest once the ring is full.</summary>
    public void Add(MessageDto message)
    {
        lock (_gate)
        {
            _total++;

            if (_recent.Count == Capacity)
            {
                _recent.Dequeue();
            }

            _recent.Enqueue(message);
        }
    }

    /// <summary>The retained window, oldest first.</summary>
    public IReadOnlyList<MessageDto> Snapshot()
    {
        lock (_gate)
        {
            return [.. _recent];
        }
    }

    /// <summary>Drops everything, used when switching broker so the two do not blur together.</summary>
    public void Clear()
    {
        lock (_gate)
        {
            _recent.Clear();
            _total = 0;
        }
    }
}
