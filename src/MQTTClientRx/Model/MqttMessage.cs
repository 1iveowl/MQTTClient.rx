using System.Text;
using MQTTClientRx.Internal;

namespace MQTTClientRx;

/// <summary>
/// An MQTT application message, used both for messages received from the broker and for messages
/// published to it.
/// </summary>
/// <remarks>
/// <para>
/// Properties marked "MQTT 5.0 only" are <see langword="null"/> or empty when the session negotiated
/// <see cref="MqttProtocolVersion.V311"/> or <see cref="MqttProtocolVersion.V310"/>. Setting them on
/// an outbound message under those versions is rejected at connect time by
/// <see cref="MqttClientRxOptions.Validate"/>.
/// </para>
/// <para>
/// For received messages the payload is copied out of the transport buffer, so it stays valid for as
/// long as you hold the message. That costs one allocation per message and is what makes it safe to
/// buffer, replay or hand the message to another thread.
/// </para>
/// </remarks>
public sealed record MqttMessage
{
    /// <summary>The topic the message was published to.</summary>
    public required string Topic { get; init; }

    /// <summary>The message payload. Empty by default.</summary>
    public ReadOnlyMemory<byte> Payload { get; init; }

    /// <summary>Quality of service level.</summary>
    public QoSLevel QualityOfServiceLevel { get; init; } = QoSLevel.AtMostOnce;

    /// <summary>Whether the broker should retain this message as the last known good value for the topic.</summary>
    public bool Retain { get; init; }

    /// <summary>MIME type of the payload, e.g. <c>application/json</c>. MQTT 5.0 only.</summary>
    public string? ContentType { get; init; }

    /// <summary>Declares whether the payload is UTF-8 character data or unspecified bytes. MQTT 5.0 only.</summary>
    public PayloadFormat? PayloadFormatIndicator { get; init; }

    /// <summary>Topic a response to this message should be published to. MQTT 5.0 only.</summary>
    public string? ResponseTopic { get; init; }

    /// <summary>Opaque data used to correlate a response with its request. MQTT 5.0 only.</summary>
    public ReadOnlyMemory<byte> CorrelationData { get; init; }

    /// <summary>How long the broker may retain this message for undelivered subscribers. MQTT 5.0 only.</summary>
    public TimeSpan? MessageExpiryInterval { get; init; }

    /// <summary>Arbitrary key/value metadata carried with the message. MQTT 5.0 only.</summary>
    public IReadOnlyList<MqttUserProperty> UserProperties { get; init; } = [];

    /// <summary>
    /// Identifiers of the subscriptions that caused this message to be delivered. MQTT 5.0 only, and
    /// only populated when the broker advertises subscription identifier support.
    /// </summary>
    public IReadOnlyList<uint> SubscriptionIdentifiers { get; init; } = [];

    /// <summary>Alias the broker used in place of the full topic name. MQTT 5.0 only.</summary>
    public ushort? TopicAlias { get; init; }

    /// <summary>
    /// Set on received messages only; carries the handle used by <see cref="AcknowledgeAsync"/>.
    /// </summary>
    internal MqttAcknowledgement? Acknowledgement { get; init; }

    /// <summary>
    /// Acknowledges this message to the broker. Only meaningful when the client was created with
    /// <see cref="AcknowledgementMode.Manual"/> and the message arrived at QoS 1 or QoS 2.
    /// </summary>
    /// <remarks>
    /// Deliberately forgiving, and idempotent. Under <see cref="AcknowledgementMode.Automatic"/> the
    /// acknowledgement was already sent when the message entered the stream; on a message you
    /// constructed yourself there is nothing to acknowledge; and acknowledging twice does nothing the
    /// second time. None of those are errors, so none of them throw.
    /// </remarks>
    public Task AcknowledgeAsync(CancellationToken cancellationToken = default) =>
        Acknowledgement?.AcknowledgeAsync(cancellationToken) ?? Task.CompletedTask;

    /// <summary>Decodes the payload as text. Defaults to UTF-8.</summary>
    public string GetPayloadAsString(Encoding? encoding = null) =>
        (encoding ?? Encoding.UTF8).GetString(Payload.Span);

    /// <summary>Copies the payload into a new array.</summary>
    public byte[] GetPayloadAsArray() => Payload.ToArray();

    /// <summary>Creates a message whose payload is the UTF-8 (or supplied) encoding of <paramref name="payload"/>.</summary>
    public static MqttMessage FromString(
        string topic,
        string payload,
        QoSLevel qualityOfServiceLevel = QoSLevel.AtMostOnce,
        bool retain = false,
        Encoding? encoding = null) => new()
        {
            Topic = topic,
            Payload = (encoding ?? Encoding.UTF8).GetBytes(payload),
            QualityOfServiceLevel = qualityOfServiceLevel,
            Retain = retain
        };

    /// <summary>Creates a message with a binary payload.</summary>
    public static MqttMessage FromBytes(
        string topic,
        ReadOnlyMemory<byte> payload,
        QoSLevel qualityOfServiceLevel = QoSLevel.AtMostOnce,
        bool retain = false) => new()
        {
            Topic = topic,
            Payload = payload,
            QualityOfServiceLevel = qualityOfServiceLevel,
            Retain = retain
        };

    /// <summary>
    /// A short, log-friendly description. Replaces the generated record <c>ToString()</c>, which
    /// would dump the payload bytes and the internal MQTTnet handle.
    /// </summary>
    public override string ToString() =>
        $"{Topic} [{QualityOfServiceLevel}{(Retain ? ", retain" : string.Empty)}] {Payload.Length} bytes";
}
