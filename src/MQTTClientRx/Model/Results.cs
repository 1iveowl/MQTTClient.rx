namespace MQTTClientRx;

/// <summary>
/// Outcome of the CONNECT/CONNACK exchange.
/// </summary>
/// <remarks>
/// MQTTnet 5 no longer throws when the broker returns a CONNACK indicating failure, so the
/// <see cref="ResultCode"/> is the only reliable signal. <see cref="MqttClientRx"/> checks it and
/// terminates the stream with <see cref="MqttConnectFailedException"/> rather than reporting a
/// connection that never happened.
/// </remarks>
public sealed record MqttConnectResult
{
    /// <summary>CONNACK reason code. Anything other than <see cref="MqttConnectResultCode.Success"/> is a failure.</summary>
    public required MqttConnectResultCode ResultCode { get; init; }

    /// <summary>Whether the connection succeeded.</summary>
    public bool IsSuccess => ResultCode == MqttConnectResultCode.Success;

    /// <summary>
    /// Whether the broker restored a previous session. When <see langword="true"/> after a reconnect,
    /// existing subscriptions are still active and are not re-sent.
    /// </summary>
    public bool IsSessionPresent { get; init; }

    /// <summary>Client identifier assigned by the broker when none was supplied. MQTT 5.0 only.</summary>
    public string? AssignedClientIdentifier { get; init; }

    /// <summary>Human-readable explanation from the broker. MQTT 5.0 only.</summary>
    public string? ReasonString { get; init; }

    /// <summary>Keep-alive the broker requires, overriding the requested value. MQTT 5.0 only.</summary>
    public TimeSpan? ServerKeepAlive { get; init; }

    /// <summary>Another broker to connect to instead. MQTT 5.0 only, sent with <c>UseAnotherServer</c>/<c>ServerMoved</c>.</summary>
    public string? ServerReference { get; init; }

    /// <summary>Session expiry the broker applied. MQTT 5.0 only.</summary>
    public TimeSpan? SessionExpiryInterval { get; init; }

    /// <summary>Maximum number of unacknowledged QoS &gt; 0 messages the broker will accept. MQTT 5.0 only.</summary>
    public ushort? ReceiveMaximum { get; init; }

    /// <summary>Largest packet the broker will accept. MQTT 5.0 only.</summary>
    public uint? MaximumPacketSize { get; init; }

    /// <summary>Highest QoS the broker supports. MQTT 5.0 only.</summary>
    public QoSLevel MaximumQoS { get; init; } = QoSLevel.ExactlyOnce;

    /// <summary>Whether the broker supports retained messages. MQTT 5.0 only.</summary>
    public bool RetainAvailable { get; init; } = true;

    /// <summary>Whether the broker supports wildcard subscriptions. MQTT 5.0 only.</summary>
    public bool WildcardSubscriptionAvailable { get; init; } = true;

    /// <summary>Whether the broker supports shared subscriptions. MQTT 5.0 only.</summary>
    public bool SharedSubscriptionAvailable { get; init; } = true;

    /// <summary>
    /// Whether the broker supports subscription identifiers. MQTT 5.0 only, and a prerequisite for
    /// per-filter routing.
    /// </summary>
    public bool SubscriptionIdentifiersAvailable { get; init; } = true;

    /// <summary>User properties returned on the CONNACK. MQTT 5.0 only.</summary>
    public IReadOnlyList<MqttUserProperty> UserProperties { get; init; } = [];
}

/// <summary>Per-filter outcome within a <see cref="MqttSubscribeResult"/>.</summary>
/// <param name="TopicFilter">The filter this result refers to.</param>
/// <param name="ResultCode">SUBACK reason code for the filter.</param>
public sealed record MqttSubscribeResultItem(MqttTopicFilter TopicFilter, MqttSubscribeResultCode ResultCode)
{
    /// <summary>Whether the broker granted the subscription (at any QoS).</summary>
    public bool IsSuccess => ResultCode is MqttSubscribeResultCode.GrantedQoS0
        or MqttSubscribeResultCode.GrantedQoS1
        or MqttSubscribeResultCode.GrantedQoS2;

    /// <summary>QoS actually granted, which may be lower than requested. Null when the subscription failed.</summary>
    public QoSLevel? GrantedQoS => ResultCode switch
    {
        MqttSubscribeResultCode.GrantedQoS0 => QoSLevel.AtMostOnce,
        MqttSubscribeResultCode.GrantedQoS1 => QoSLevel.AtLeastOnce,
        MqttSubscribeResultCode.GrantedQoS2 => QoSLevel.ExactlyOnce,
        _ => null
    };
}

/// <summary>
/// Outcome of a SUBSCRIBE. Each requested filter gets its own reason code - a broker may reject one
/// filter and grant another, so check <see cref="Items"/> rather than assuming success.
/// </summary>
public sealed record MqttSubscribeResult
{
    /// <summary>Per-filter results, in the order the filters were requested.</summary>
    public required IReadOnlyList<MqttSubscribeResultItem> Items { get; init; }

    /// <summary>Whether every requested filter was granted.</summary>
    public bool IsSuccess => Items.All(item => item.IsSuccess);

    /// <summary>Filters the broker refused.</summary>
    public IEnumerable<MqttSubscribeResultItem> Failures => Items.Where(item => !item.IsSuccess);

    /// <summary>Human-readable explanation from the broker. MQTT 5.0 only.</summary>
    public string? ReasonString { get; init; }

    /// <summary>User properties returned on the SUBACK. MQTT 5.0 only.</summary>
    public IReadOnlyList<MqttUserProperty> UserProperties { get; init; } = [];
}

/// <summary>Per-topic outcome within a <see cref="MqttUnsubscribeResult"/>.</summary>
/// <param name="Topic">The topic filter this result refers to.</param>
/// <param name="ResultCode">UNSUBACK reason code for the topic.</param>
public sealed record MqttUnsubscribeResultItem(string Topic, MqttUnsubscribeResultCode ResultCode)
{
    /// <summary>Whether the unsubscribe was accepted. A topic that was never subscribed counts as success.</summary>
    public bool IsSuccess => ResultCode is MqttUnsubscribeResultCode.Success
        or MqttUnsubscribeResultCode.NoSubscriptionExisted;
}

/// <summary>Outcome of an UNSUBSCRIBE.</summary>
public sealed record MqttUnsubscribeResult
{
    /// <summary>Per-topic results, in the order the topics were requested.</summary>
    public required IReadOnlyList<MqttUnsubscribeResultItem> Items { get; init; }

    /// <summary>Whether every requested topic was unsubscribed.</summary>
    public bool IsSuccess => Items.All(item => item.IsSuccess);

    /// <summary>Human-readable explanation from the broker. MQTT 5.0 only.</summary>
    public string? ReasonString { get; init; }

    /// <summary>User properties returned on the UNSUBACK. MQTT 5.0 only.</summary>
    public IReadOnlyList<MqttUserProperty> UserProperties { get; init; } = [];
}

/// <summary>
/// Outcome of a PUBLISH. At QoS 0 there is no acknowledgement, so the result always reports success.
/// </summary>
public sealed record MqttPublishResult
{
    /// <summary>PUBACK/PUBREC reason code.</summary>
    public required MqttPublishReasonCode ReasonCode { get; init; }

    /// <summary>
    /// Whether the publish was accepted. Note that <see cref="MqttPublishReasonCode.NoMatchingSubscribers"/>
    /// counts as success - the broker accepted the message, nobody was listening.
    /// </summary>
    public bool IsSuccess => ReasonCode is MqttPublishReasonCode.Success
        or MqttPublishReasonCode.NoMatchingSubscribers;

    /// <summary>Packet identifier, present for QoS 1 and QoS 2.</summary>
    public ushort? PacketIdentifier { get; init; }

    /// <summary>Human-readable explanation from the broker. MQTT 5.0 only.</summary>
    public string? ReasonString { get; init; }

    /// <summary>User properties returned on the acknowledgement. MQTT 5.0 only.</summary>
    public IReadOnlyList<MqttUserProperty> UserProperties { get; init; } = [];
}

/// <summary>
/// A connection ending, whether initiated by the broker, the network, or this client.
/// </summary>
public sealed record MqttDisconnect
{
    /// <summary>
    /// Whether a session had actually been established. <see langword="false"/> means the connection
    /// attempt itself failed.
    /// </summary>
    public required bool ClientWasConnected { get; init; }

    /// <summary>Reason the connection ended. MQTT 5.0 populates this richly; on 3.1.1 it is coarse.</summary>
    public MqttDisconnectReasonCode? ReasonCode { get; init; }

    /// <summary>Human-readable explanation from the broker. MQTT 5.0 only.</summary>
    public string? ReasonString { get; init; }

    /// <summary>Transport-level exception, when the connection ended through a socket failure.</summary>
    public Exception? Exception { get; init; }

    /// <summary>Another broker to connect to instead. MQTT 5.0 only.</summary>
    public string? ServerReference { get; init; }

    /// <summary>User properties on the DISCONNECT packet. MQTT 5.0 only.</summary>
    public IReadOnlyList<MqttUserProperty> UserProperties { get; init; } = [];

    /// <inheritdoc />
    public override string ToString() =>
        $"{ReasonCode?.ToString() ?? "Unknown"}{(ReasonString is null ? string.Empty : $" ({ReasonString})")}" +
        $"{(ClientWasConnected ? string.Empty : " [never connected]")}";
}

/// <summary>
/// An observed change in connection lifecycle. Replaces the polled <c>bool IsConnected</c> of
/// earlier versions.
/// </summary>
public sealed record ConnectionStateChange
{
    /// <summary>The state entered.</summary>
    public required MqttConnectionState State { get; init; }

    /// <summary>
    /// Reconnect attempt number. Zero for the initial connection, incrementing for each retry.
    /// </summary>
    public int Attempt { get; init; }

    /// <summary>How long until the next reconnect attempt. Set only when <see cref="State"/> is <see cref="MqttConnectionState.Reconnecting"/>.</summary>
    public TimeSpan? NextRetryDelay { get; init; }

    /// <summary>The CONNACK, when <see cref="State"/> is <see cref="MqttConnectionState.Connected"/>.</summary>
    public MqttConnectResult? ConnectResult { get; init; }

    /// <summary>Why the previous connection ended, when moving to <see cref="MqttConnectionState.Reconnecting"/> or <see cref="MqttConnectionState.Disconnected"/>.</summary>
    public MqttDisconnect? Disconnect { get; init; }

    /// <summary>
    /// The failure that ended the connection for good, when the reconnect policy gave up. Present
    /// only on the final <see cref="MqttConnectionState.Disconnected"/>, and the difference between
    /// "not connected right now" and "not connecting again".
    /// </summary>
    public Exception? Failure { get; init; }

    /// <inheritdoc />
    public override string ToString() => State switch
    {
        MqttConnectionState.Reconnecting => $"Reconnecting (attempt {Attempt}, in {NextRetryDelay})",
        _ => State.ToString()
    };
}
