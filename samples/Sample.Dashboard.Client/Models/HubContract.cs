namespace Sample.Dashboard.Client.Models;

/// <summary>
/// The SignalR contract, shared by both sides so a rename cannot desynchronise them.
/// </summary>
public static class HubEvents
{
    /// <summary>Hub endpoint path.</summary>
    public const string Path = "/mqtt";

    // Server → client
    /// <summary>A batch of received messages. Batched rather than per-message; see <c>BrokerConnection</c>.</summary>
    public const string Messages = nameof(Messages);

    /// <summary>A connection lifecycle transition.</summary>
    public const string ConnectionChanged = nameof(ConnectionChanged);

    /// <summary>The active subscription set changed.</summary>
    public const string SubscriptionsChanged = nameof(SubscriptionsChanged);

    /// <summary>The broker profile in use changed.</summary>
    public const string ProfileChanged = nameof(ProfileChanged);

    /// <summary>Free-text activity for the connection log.</summary>
    public const string Activity = nameof(Activity);
}

/// <summary>One received MQTT message, projected for the wire.</summary>
/// <param name="Sequence">Monotonic per-server-run, and the cache key.</param>
/// <param name="ReceivedAt">Server receive time (UTC).</param>
/// <param name="Topic">Topic the message arrived on.</param>
/// <param name="Payload">Payload rendered as text, truncated for display.</param>
/// <param name="PayloadBytes">True payload length before truncation.</param>
/// <param name="IsText">Whether the payload decoded cleanly as UTF-8 text.</param>
/// <param name="Qos">Quality of service, as a display string.</param>
/// <param name="Retain">Whether the retain flag was set.</param>
/// <param name="ContentType">MQTT 5 content type.</param>
/// <param name="PayloadFormat">MQTT 5 payload format indicator.</param>
/// <param name="ResponseTopic">MQTT 5 response topic.</param>
/// <param name="CorrelationData">MQTT 5 correlation data, hex-encoded.</param>
/// <param name="MessageExpirySeconds">MQTT 5 message expiry interval.</param>
/// <param name="UserProperties">MQTT 5 user properties.</param>
/// <param name="SubscriptionIdentifiers">MQTT 5 subscription identifiers.</param>
public sealed record MessageDto(
    long Sequence,
    DateTimeOffset ReceivedAt,
    string Topic,
    string Payload,
    int PayloadBytes,
    bool IsText,
    string Qos,
    bool Retain,
    string? ContentType,
    string? PayloadFormat,
    string? ResponseTopic,
    string? CorrelationData,
    int? MessageExpirySeconds,
    IReadOnlyList<UserPropertyDto> UserProperties,
    IReadOnlyList<uint> SubscriptionIdentifiers)
{
    /// <summary>Whether this message carries any MQTT 5 property at all.</summary>
    public bool HasVersion5Properties =>
        ContentType is not null
        || PayloadFormat is not null
        || ResponseTopic is not null
        || CorrelationData is not null
        || MessageExpirySeconds is not null
        || UserProperties.Count > 0
        || SubscriptionIdentifiers.Count > 0;
}

/// <summary>An MQTT 5 user property.</summary>
public sealed record UserPropertyDto(string Name, string Value);

/// <summary>What the broker told us it supports, from the CONNACK.</summary>
public sealed record BrokerCapabilitiesDto(
    string ResultCode,
    bool SessionPresent,
    string? AssignedClientId,
    string? ReasonString,
    int? ServerKeepAliveSeconds,
    int? SessionExpirySeconds,
    int? ReceiveMaximum,
    long? MaximumPacketSize,
    string MaximumQos,
    bool RetainAvailable,
    bool WildcardSubscriptionAvailable,
    bool SharedSubscriptionAvailable,
    bool SubscriptionIdentifiersAvailable);

/// <summary>A connection lifecycle transition, mirroring <c>ConnectionStateChange</c>.</summary>
public sealed record ConnectionDto(
    DateTimeOffset At,
    string State,
    int Attempt,
    double? NextRetryDelaySeconds,
    BrokerCapabilitiesDto? Capabilities,
    string? Failure);

/// <summary>A connection ending, mirroring <c>MqttDisconnect</c>.</summary>
public sealed record DisconnectDto(
    DateTimeOffset At,
    bool ClientWasConnected,
    string? ReasonCode,
    string? ReasonString,
    string? Error);

/// <summary>One active topic filter and the reason code the broker returned for it.</summary>
public sealed record SubscriptionDto(
    string Topic,
    string Qos,
    string ResultCode,
    bool IsSuccess,
    string? GrantedQos);

/// <summary>The broker the server is pointed at, and how.</summary>
public sealed record BrokerProfileDto(
    string Name,
    string Uri,
    string ProtocolVersion,
    bool UseTls,
    string ConnectionType,
    bool OutageSimulationAvailable);

/// <summary>Reconnect policy knobs, editable from the Connection page.</summary>
public sealed record ReconnectPolicyDto(
    double InitialDelaySeconds,
    double MaxDelaySeconds,
    double BackoffFactor,
    double JitterFactor,
    int? MaxAttempts);

/// <summary>A line in the connection activity log.</summary>
public sealed record ActivityDto(DateTimeOffset At, string Severity, string Text);

/// <summary>Everything a newly connected browser needs to render without waiting for live traffic.</summary>
public sealed record SnapshotDto(
    BrokerProfileDto Profile,
    ReconnectPolicyDto Policy,
    ConnectionDto? Connection,
    IReadOnlyList<MessageDto> RecentMessages,
    IReadOnlyList<SubscriptionDto> Subscriptions,
    IReadOnlyList<ConnectionDto> ConnectionHistory,
    IReadOnlyList<ActivityDto> Activity,
    IReadOnlyList<BrokerOptionDto> Brokers);

/// <summary>A selectable broker in the picker.</summary>
public sealed record BrokerOptionDto(
    string Name,
    string Uri,
    string Description,
    bool RequiresTls,
    string ConnectionType,
    string DefaultTopicFilter);
