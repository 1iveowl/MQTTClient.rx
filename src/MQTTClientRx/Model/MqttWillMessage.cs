using System.Text;

namespace MQTTClientRx;

/// <summary>
/// The message the broker publishes on this client's behalf if the connection ends without a clean
/// DISCONNECT.
/// </summary>
public sealed record MqttWillMessage
{
    /// <summary>Topic the will is published to.</summary>
    public required string Topic { get; init; }

    /// <summary>Will payload.</summary>
    public ReadOnlyMemory<byte> Payload { get; init; }

    /// <summary>Quality of service used when publishing the will.</summary>
    public QoSLevel QualityOfServiceLevel { get; init; } = QoSLevel.AtMostOnce;

    /// <summary>Whether the will is retained.</summary>
    public bool Retain { get; init; }

    /// <summary>
    /// How long the broker waits after the connection ends before publishing the will, giving the
    /// client a chance to reconnect first. MQTT 5.0 only.
    /// </summary>
    public TimeSpan? DelayInterval { get; init; }

    /// <summary>MIME type of the will payload. MQTT 5.0 only.</summary>
    public string? ContentType { get; init; }

    /// <summary>Payload format of the will. MQTT 5.0 only.</summary>
    public PayloadFormat? PayloadFormatIndicator { get; init; }

    /// <summary>Response topic carried on the will. MQTT 5.0 only.</summary>
    public string? ResponseTopic { get; init; }

    /// <summary>Correlation data carried on the will. MQTT 5.0 only.</summary>
    public ReadOnlyMemory<byte> CorrelationData { get; init; }

    /// <summary>Expiry interval applied to the will once published. MQTT 5.0 only.</summary>
    public TimeSpan? MessageExpiryInterval { get; init; }

    /// <summary>User properties carried on the will. MQTT 5.0 only.</summary>
    public IReadOnlyList<MqttUserProperty> UserProperties { get; init; } = [];

    /// <summary>Creates a will message with a text payload (UTF-8 encoded).</summary>
    public static MqttWillMessage FromString(
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
}
