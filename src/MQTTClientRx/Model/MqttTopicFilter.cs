namespace MQTTClientRx;

/// <summary>
/// A topic subscription, optionally with MQTT 5.0 subscription options.
/// </summary>
/// <remarks>
/// Shared subscriptions need no special support - use a <c>$share/&lt;group&gt;/&lt;topic&gt;</c>
/// topic string. They require MQTT 5.0 and a broker that advertises shared subscription support.
/// </remarks>
public sealed record MqttTopicFilter
{
    /// <summary>The topic filter, which may contain the <c>+</c> and <c>#</c> wildcards.</summary>
    public required string Topic { get; init; }

    /// <summary>Maximum quality of service the broker should use when delivering matching messages.</summary>
    public QoSLevel QualityOfServiceLevel { get; init; } = QoSLevel.AtMostOnce;

    /// <summary>
    /// When <see langword="true"/>, the broker will not echo back messages this client published
    /// itself. MQTT 5.0 only.
    /// </summary>
    public bool NoLocal { get; init; }

    /// <summary>
    /// When <see langword="true"/>, the retain flag is forwarded as published rather than cleared.
    /// MQTT 5.0 only.
    /// </summary>
    public bool RetainAsPublished { get; init; }

    /// <summary>Controls replay of retained messages when this subscription is established. MQTT 5.0 only.</summary>
    public RetainHandling RetainHandling { get; init; } = RetainHandling.SendAtSubscribe;

    /// <summary>Creates a topic filter for <paramref name="topic"/> at the given QoS.</summary>
    public static MqttTopicFilter Create(string topic, QoSLevel qualityOfServiceLevel = QoSLevel.AtMostOnce) =>
        new() { Topic = topic, QualityOfServiceLevel = qualityOfServiceLevel };

    /// <inheritdoc />
    public override string ToString() => $"{Topic} [{QualityOfServiceLevel}]";
}
