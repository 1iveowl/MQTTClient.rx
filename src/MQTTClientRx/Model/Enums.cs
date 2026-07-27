namespace MQTTClientRx;

/// <summary>MQTT Quality of Service level. Values match the wire protocol.</summary>
public enum QoSLevel
{
    /// <summary>Fire and forget. The message is not acknowledged.</summary>
    AtMostOnce = 0,

    /// <summary>Acknowledged delivery. The message may be delivered more than once.</summary>
    AtLeastOnce = 1,

    /// <summary>Assured delivery. The message is delivered exactly once.</summary>
    ExactlyOnce = 2
}

/// <summary>MQTT protocol version. Values match the wire protocol level byte.</summary>
public enum MqttProtocolVersion
{
    /// <summary>MQTT 3.1.0.</summary>
    V310 = 3,

    /// <summary>MQTT 3.1.1 (ISO/IEC 20922).</summary>
    V311 = 4,

    /// <summary>MQTT 5.0. The default, and the only version supporting reason codes and properties.</summary>
    V500 = 5
}

/// <summary>Transport used to reach the broker.</summary>
public enum ConnectionType
{
    /// <summary>Plain TCP (optionally wrapped in TLS via <see cref="MqttTlsOptions"/>).</summary>
    Tcp,

    /// <summary>WebSocket transport.</summary>
    WebSocket
}

/// <summary>
/// Controls whether retained messages are replayed when a subscription is established. MQTT 5.0 only.
/// </summary>
public enum RetainHandling
{
    /// <summary>Always send retained messages when the subscription is established.</summary>
    SendAtSubscribe = 0,

    /// <summary>Send retained messages only if the subscription did not already exist.</summary>
    SendAtSubscribeIfNewSubscriptionOnly = 1,

    /// <summary>Never send retained messages when the subscription is established.</summary>
    DoNotSendOnSubscribe = 2
}

/// <summary>Declares how the payload should be interpreted. MQTT 5.0 only.</summary>
public enum PayloadFormat
{
    /// <summary>Unspecified bytes.</summary>
    Unspecified = 0,

    /// <summary>UTF-8 encoded character data.</summary>
    CharacterData = 1
}

/// <summary>
/// Controls when QoS 1 and QoS 2 messages are acknowledged to the broker.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="IObserver{T}.OnNext"/> returns <c>void</c> and cannot signal that a message was
/// successfully processed. Under <see cref="Automatic"/> the acknowledgement is therefore sent as
/// soon as the message is <b>delivered to the stream</b>, not when a subscriber has <b>handled</b>
/// it - a crash mid-processing loses the message despite QoS 1/2 semantics.
/// </para>
/// <para>
/// Use <see cref="Manual"/> when at-least-once processing actually matters, and call
/// <see cref="MqttMessage.AcknowledgeAsync"/> once the message has been durably handled.
/// </para>
/// </remarks>
public enum AcknowledgementMode
{
    /// <summary>
    /// The broker is acknowledged as soon as the message is pushed into the stream. Fast, and the
    /// default, but QoS 1/2 degrades to "delivered to the stream" rather than "processed".
    /// </summary>
    Automatic,

    /// <summary>
    /// The consumer must call <see cref="MqttMessage.AcknowledgeAsync"/>. Preserves genuine
    /// at-least-once semantics; a forgotten acknowledgement will stall delivery once the broker's
    /// receive-maximum window is exhausted.
    /// </summary>
    Manual
}

/// <summary>Lifecycle state of the underlying broker connection.</summary>
public enum MqttConnectionState
{
    /// <summary>No connection, and none being attempted.</summary>
    Disconnected,

    /// <summary>First connection attempt in progress.</summary>
    Connecting,

    /// <summary>Connected and subscribed.</summary>
    Connected,

    /// <summary>The connection dropped and a reconnect attempt is scheduled or in progress.</summary>
    Reconnecting
}
