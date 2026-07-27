namespace MQTTClientRx;

/// <summary>
/// An Rx-shaped MQTT client. The broker connection is owned by the subscription to
/// <see cref="Messages"/>: subscribing connects, disposing the last subscription disconnects.
/// </summary>
/// <remarks>
/// <para>
/// There is deliberately no <c>ConnectAsync</c> and no <c>bool IsConnected</c>. Connection lifetime
/// is subscription lifetime, which is what makes the stream compose correctly with the rest of Rx -
/// and connection state is a stream (<see cref="ConnectionState"/>), not a value you poll and hope
/// is still true by the time you act on it.
/// </para>
/// <para>
/// <see cref="SubscribeAsync"/>, <see cref="UnsubscribeAsync"/> and <see cref="PublishAsync"/> act on
/// the live connection and throw <see cref="MqttNotConnectedException"/> when there is none.
/// </para>
/// </remarks>
public interface IMqttClientRx : IAsyncDisposable
{
    /// <summary>
    /// Messages received from the broker.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Cold and shared: the first subscription connects and applies
    /// <see cref="MqttClientRxOptions.TopicFilters"/>; further subscriptions share that one physical
    /// connection; disposing the last one disconnects gracefully after
    /// <see cref="MqttClientRxOptions.ShareGracePeriod"/>.
    /// </para>
    /// <para>
    /// The stream survives reconnects - a dropped connection is retried per
    /// <see cref="MqttClientRxOptions.Reconnect"/> without terminating the sequence. It calls
    /// <see cref="IObserver{T}.OnError"/> only when the policy gives up, or when the failure is one
    /// the policy considers fatal (bad credentials, rejected client identifier, and similar).
    /// </para>
    /// <para>
    /// Messages are not buffered for late subscribers. Anything published between the connection
    /// being established and a given subscriber arriving is not replayed.
    /// </para>
    /// </remarks>
    IObservable<MqttMessage> Messages { get; }

    /// <summary>
    /// Connection lifecycle transitions. Replays the current state on subscribe, so a new subscriber
    /// always learns where things stand without waiting for the next change.
    /// </summary>
    IObservable<ConnectionStateChange> ConnectionState { get; }

    /// <summary>
    /// Connections ending - whether closed by the broker, lost to the network, or refused outright.
    /// Check <see cref="MqttDisconnect.ClientWasConnected"/> to tell an established connection
    /// dropping from a connection attempt that never succeeded.
    /// </summary>
    IObservable<MqttDisconnect> Disconnects { get; }

    /// <summary>
    /// Subscribes additional topic filters on the live connection.
    /// </summary>
    /// <remarks>
    /// Filters the broker grants are remembered and re-applied after a reconnect that does not
    /// restore the session. Filters it refuses are not.
    /// </remarks>
    /// <returns>The SUBACK, with a reason code per requested filter. Inspect it - a broker may grant one filter and refuse another.</returns>
    /// <exception cref="MqttNotConnectedException">Nothing is subscribed to <see cref="Messages"/>, so there is no connection.</exception>
    Task<MqttSubscribeResult> SubscribeAsync(
        IEnumerable<MqttTopicFilter> topicFilters,
        CancellationToken cancellationToken = default);

    /// <summary>Unsubscribes topics on the live connection, and stops re-applying them after reconnects.</summary>
    /// <returns>The UNSUBACK, with a reason code per requested topic.</returns>
    /// <exception cref="MqttNotConnectedException">Nothing is subscribed to <see cref="Messages"/>, so there is no connection.</exception>
    Task<MqttUnsubscribeResult> UnsubscribeAsync(
        IEnumerable<string> topics,
        CancellationToken cancellationToken = default);

    /// <summary>Publishes a message on the live connection.</summary>
    /// <returns>
    /// The PUBACK/PUBREC for QoS 1 and 2. At QoS 0 there is nothing to acknowledge and the result
    /// always reports success.
    /// </returns>
    /// <exception cref="MqttNotConnectedException">Nothing is subscribed to <see cref="Messages"/>, so there is no connection.</exception>
    Task<MqttPublishResult> PublishAsync(
        MqttMessage message,
        CancellationToken cancellationToken = default);
}
