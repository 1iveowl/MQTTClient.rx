namespace MQTTClientRx;

/// <summary>
/// What to do when messages arrive faster than an <c>await foreach</c> consumer drains them and the
/// read buffer is full.
/// </summary>
/// <remarks>
/// This only comes into play when MQTT's own flow control cannot. At QoS 1 and 2 with
/// <see cref="AcknowledgementMode.Manual"/>, the broker will not exceed
/// <see cref="MqttClientRxOptions.ReceiveMaximum"/> unacknowledged messages, so a buffer larger than
/// that window never fills. QoS 0 has no protocol-level flow control at all, which is what these
/// strategies exist for.
/// </remarks>
public enum BufferOverflowStrategy
{
    /// <summary>
    /// End the enumeration with <see cref="MqttBufferOverflowException"/>. Loud rather than lossy -
    /// the default, because silently discarding messages is rarely what anyone wants.
    /// </summary>
    Fail,

    /// <summary>Discard the oldest buffered message to make room. Keeps the freshest data.</summary>
    DropOldest,

    /// <summary>Discard the newly arrived message. Keeps the oldest data.</summary>
    DropNewest
}

/// <summary>Tuning for <see cref="MqttClientRxAsyncEnumerable.ReadAllAsync(IMqttClientRx, MqttReadOptions, CancellationToken)"/>.</summary>
public sealed record MqttReadOptions
{
    /// <summary>The defaults: a 1024-message buffer that fails loudly rather than dropping.</summary>
    public static MqttReadOptions Default { get; } = new();

    /// <summary>
    /// How many messages may sit between the broker and the consumer. Should exceed
    /// <see cref="MqttClientRxOptions.ReceiveMaximum"/> so that MQTT flow control, not this buffer,
    /// is what limits throughput.
    /// </summary>
    public int BufferCapacity { get; init; } = 1024;

    /// <summary>What to do when <see cref="BufferCapacity"/> is reached.</summary>
    public BufferOverflowStrategy OverflowStrategy { get; init; } = BufferOverflowStrategy.Fail;

    /// <summary>
    /// Whether to acknowledge each message once the consumer comes back for the next one.
    /// </summary>
    /// <remarks>
    /// This is what makes <c>await foreach</c> genuinely at-least-once: the consumer can only ask for
    /// the next message after its loop body completed, so an acknowledgement is proof of processing
    /// rather than proof of delivery. Requires <see cref="AcknowledgementMode.Manual"/> on the
    /// client; under <see cref="AcknowledgementMode.Automatic"/> the acknowledgement was already sent
    /// and this does nothing.
    /// </remarks>
    public bool AcknowledgeWhenConsumed { get; init; } = true;

    internal void Validate()
    {
        if (BufferCapacity < 1)
        {
            throw new MqttOptionsException($"{nameof(BufferCapacity)} must be at least 1.");
        }
    }
}
