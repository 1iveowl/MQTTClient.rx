using MQTTnet;

namespace MQTTClientRx;

/// <summary>
/// MQTT topic filter matching, for filtering a received stream client-side.
/// </summary>
/// <remarks>
/// Delegates to MQTTnet's comparer rather than reimplementing the wildcard rules - <c>+</c>, <c>#</c>,
/// the <c>$</c>-prefix exclusion and the empty-level cases have more edge cases than they appear to.
/// </remarks>
public static class MqttTopicMatcher
{
    private const string SharedSubscriptionPrefix = "$share/";

    /// <summary>
    /// Whether <paramref name="topic"/> matches <paramref name="topicFilter"/>.
    /// </summary>
    /// <param name="topicFilter">A filter, which may contain <c>+</c> and <c>#</c> wildcards and may be a <c>$share/</c> filter.</param>
    /// <param name="topic">The topic a message was published to.</param>
    /// <returns><see langword="false"/> when either argument is not a valid topic or filter.</returns>
    public static bool Matches(string topicFilter, string topic)
    {
        ArgumentNullException.ThrowIfNull(topicFilter);
        ArgumentNullException.ThrowIfNull(topic);

        return MqttTopicFilterComparer.Compare(topic, StripSharedSubscriptionPrefix(topicFilter))
            == MqttTopicFilterCompareResult.IsMatch;
    }

    /// <summary>
    /// Removes a <c>$share/&lt;group&gt;/</c> prefix, leaving the topic filter the broker actually
    /// matches against. Returns the input unchanged when there is no such prefix.
    /// </summary>
    public static string StripSharedSubscriptionPrefix(string topicFilter)
    {
        ArgumentNullException.ThrowIfNull(topicFilter);

        if (!topicFilter.StartsWith(SharedSubscriptionPrefix, StringComparison.Ordinal))
        {
            return topicFilter;
        }

        var separator = topicFilter.IndexOf('/', SharedSubscriptionPrefix.Length);

        return separator < 0 ? topicFilter : topicFilter[(separator + 1)..];
    }
}
