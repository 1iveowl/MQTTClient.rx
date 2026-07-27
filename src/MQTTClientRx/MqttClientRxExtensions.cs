using System.Reactive.Linq;
using System.Reactive.Threading.Tasks;
using System.Text;

namespace MQTTClientRx;

/// <summary>Convenience overloads over <see cref="IMqttClientRx"/>.</summary>
public static class MqttClientRxExtensions
{
    /// <summary>Subscribes one or more topic filters.</summary>
    public static Task<MqttSubscribeResult> SubscribeAsync(
        this IMqttClientRx client,
        params MqttTopicFilter[] topicFilters)
    {
        ArgumentNullException.ThrowIfNull(client);
        return client.SubscribeAsync(topicFilters);
    }

    /// <summary>Subscribes a single topic at the given quality of service.</summary>
    public static Task<MqttSubscribeResult> SubscribeAsync(
        this IMqttClientRx client,
        string topic,
        QoSLevel qualityOfServiceLevel = QoSLevel.AtMostOnce,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        return client.SubscribeAsync([MqttTopicFilter.Create(topic, qualityOfServiceLevel)], cancellationToken);
    }

    /// <summary>Unsubscribes one or more topics.</summary>
    public static Task<MqttUnsubscribeResult> UnsubscribeAsync(
        this IMqttClientRx client,
        params string[] topics)
    {
        ArgumentNullException.ThrowIfNull(client);
        return client.UnsubscribeAsync(topics.AsEnumerable());
    }

    /// <summary>Publishes a text payload, UTF-8 encoded unless another encoding is supplied.</summary>
    public static Task<MqttPublishResult> PublishAsync(
        this IMqttClientRx client,
        string topic,
        string payload,
        QoSLevel qualityOfServiceLevel = QoSLevel.AtMostOnce,
        bool retain = false,
        Encoding? encoding = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        return client.PublishAsync(
            MqttMessage.FromString(topic, payload, qualityOfServiceLevel, retain, encoding),
            cancellationToken);
    }

    /// <summary>
    /// Messages whose topic matches <paramref name="topicFilter"/>, evaluated client-side against
    /// MQTT wildcard rules.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This filters the shared <see cref="IMqttClientRx.Messages"/> stream; it does not send a
    /// SUBSCRIBE. The broker must already be delivering the topic, either through
    /// <see cref="MqttClientRxOptions.TopicFilters"/> or a prior
    /// <see cref="IMqttClientRx.SubscribeAsync"/>.
    /// </para>
    /// <para>
    /// Shared subscription filters (<c>$share/&lt;group&gt;/&lt;topic&gt;</c>) are matched on the
    /// topic portion, since the broker strips the prefix before delivery.
    /// </para>
    /// </remarks>
    public static IObservable<MqttMessage> WhereTopicMatches(this IMqttClientRx client, string topicFilter)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(topicFilter);

        return client.Messages.Where(message => MqttTopicMatcher.Matches(topicFilter, message.Topic));
    }

    /// <summary>
    /// Completes once the client reaches <see cref="MqttConnectionState.Connected"/>, or fails if the
    /// connection cannot be established.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Useful in tests and in imperative startup code. It does <b>not</b> establish the connection -
    /// something must be subscribed to <see cref="IMqttClientRx.Messages"/> for there to be one.
    /// </para>
    /// <para>
    /// Faults with the underlying failure once the <see cref="ReconnectPolicy"/> gives up. Note that
    /// the default policy never does, so against an unreachable broker this waits until the caller's
    /// <paramref name="cancellationToken"/> says otherwise.
    /// </para>
    /// </remarks>
    public static Task<MqttConnectResult> WaitForConnectionAsync(
        this IMqttClientRx client,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);

        return client.ConnectionState
            .Where(change => change.Failure is not null
                || (change.State == MqttConnectionState.Connected && change.ConnectResult is not null))
            .Take(1)
            .SelectMany(change => change.Failure is { } failure
                ? Observable.Throw<MqttConnectResult>(failure)
                : Observable.Return(change.ConnectResult!))
            .ToTask(cancellationToken);
    }
}
