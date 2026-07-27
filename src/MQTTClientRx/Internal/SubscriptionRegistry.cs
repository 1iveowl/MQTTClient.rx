namespace MQTTClientRx.Internal;

/// <summary>
/// The set of topic filters that should be active on any live connection.
/// </summary>
/// <remarks>
/// MQTTnet 5 removed the managed client, so nothing restores subscriptions after a reconnect. This
/// registry is the record of what the caller asked for - the initial filters plus anything added at
/// runtime, minus anything unsubscribed - and a reconnecting session replays it whenever the broker
/// did not restore the session itself.
/// </remarks>
internal sealed class SubscriptionRegistry
{
    private readonly Lock _gate = new();
    private readonly Dictionary<string, MqttTopicFilter> _filters = new(StringComparer.Ordinal);

    internal SubscriptionRegistry(IEnumerable<MqttTopicFilter> initial) => Add(initial);

    internal void Add(IEnumerable<MqttTopicFilter> filters)
    {
        lock (_gate)
        {
            foreach (var filter in filters)
            {
                // Re-subscribing a topic replaces its options, matching broker behaviour.
                _filters[filter.Topic] = filter;
            }
        }
    }

    internal void Remove(IEnumerable<string> topics)
    {
        lock (_gate)
        {
            foreach (var topic in topics)
            {
                _filters.Remove(topic);
            }
        }
    }

    internal IReadOnlyList<MqttTopicFilter> Snapshot()
    {
        lock (_gate)
        {
            return _filters.Count == 0 ? [] : [.. _filters.Values];
        }
    }
}
