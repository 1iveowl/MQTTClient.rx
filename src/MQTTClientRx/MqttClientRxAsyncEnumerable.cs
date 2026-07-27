using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace MQTTClientRx;

/// <summary>
/// Pull-based consumption of the message stream.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="IObserver{T}.OnNext"/> returns <c>void</c>, so a push-based stream can never learn that
/// a message was successfully processed - which is why
/// <see cref="AcknowledgementMode.Automatic"/> acknowledges on delivery rather than on completion.
/// An <c>await foreach</c> loop does not have that limitation: asking for the next message is itself
/// proof that the previous loop body finished.
/// </para>
/// <para>
/// That makes <see cref="ReadAllAsync(IMqttClientRx, MqttReadOptions, CancellationToken)"/> the
/// honest way to get at-least-once processing out of this library.
/// </para>
/// </remarks>
public static class MqttClientRxAsyncEnumerable
{
    /// <summary>
    /// Reads messages as an <see cref="IAsyncEnumerable{T}"/>, using <see cref="MqttReadOptions.Default"/>.
    /// </summary>
    public static IAsyncEnumerable<MqttMessage> ReadAllAsync(
        this IMqttClientRx client,
        CancellationToken cancellationToken = default) =>
        client.ReadAllAsync(MqttReadOptions.Default, cancellationToken);

    /// <summary>
    /// Reads messages as an <see cref="IAsyncEnumerable{T}"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Enumerating connects, exactly as subscribing to <see cref="IMqttClientRx.Messages"/> does, and
    /// abandoning the enumeration disconnects. Reconnects are transparent - the sequence does not end
    /// because a connection dropped.
    /// </para>
    /// <para>
    /// With <see cref="AcknowledgementMode.Manual"/> and
    /// <see cref="MqttReadOptions.AcknowledgeWhenConsumed"/>, each message is acknowledged when the
    /// consumer requests the next one. A loop body that throws, or a <c>break</c>, therefore leaves
    /// the message unacknowledged and the broker redelivers it. The final message of a completed
    /// sequence is likewise left unacknowledged: by then the connection that would carry the
    /// acknowledgement is already gone.
    /// </para>
    /// <para>
    /// Backpressure is real but comes from the protocol, not from this method. At QoS 1 and 2 the
    /// broker will not exceed <see cref="MqttClientRxOptions.ReceiveMaximum"/> unacknowledged
    /// messages, so a slow consumer slows the broker. QoS 0 has no such window, which is what
    /// <see cref="MqttReadOptions.OverflowStrategy"/> governs.
    /// </para>
    /// </remarks>
    /// <exception cref="MqttBufferOverflowException">
    /// The consumer fell behind and <see cref="BufferOverflowStrategy.Fail"/> is in effect.
    /// </exception>
    public static async IAsyncEnumerable<MqttMessage> ReadAllAsync(
        this IMqttClientRx client,
        MqttReadOptions options,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(options);

        options.Validate();

        var channel = CreateChannel(options);

        // The observable serialises notifications, so there is exactly one writer; the enumerator is
        // the only reader.
        using var subscription = client.Messages.Subscribe(
            message => Write(channel.Writer, message, options),
            error => channel.Writer.TryComplete(error),
            () => channel.Writer.TryComplete());

        MqttMessage? unacknowledged = null;

        await foreach (var message in channel.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
        {
            // Reaching here means the consumer's previous loop body ran to completion, which is the
            // only evidence of successful processing this API can offer.
            //
            // Unless we are being cancelled: the acknowledgement would fail on a connection that is
            // already going away, and leaving it unsent is the correct at-least-once outcome -
            // the broker redelivers.
            if (unacknowledged is not null && !cancellationToken.IsCancellationRequested)
            {
                await unacknowledged.AcknowledgeAsync(cancellationToken).ConfigureAwait(false);
            }

            unacknowledged = options.AcknowledgeWhenConsumed ? message : null;

            yield return message;
        }
    }

    private static Channel<MqttMessage> CreateChannel(MqttReadOptions options) =>
        Channel.CreateBounded<MqttMessage>(new BoundedChannelOptions(options.BufferCapacity)
        {
            SingleReader = true,
            SingleWriter = true,
            FullMode = options.OverflowStrategy switch
            {
                BufferOverflowStrategy.DropOldest => BoundedChannelFullMode.DropOldest,
                BufferOverflowStrategy.DropNewest => BoundedChannelFullMode.DropWrite,

                // Wait makes TryWrite report the overflow instead of silently absorbing it. Nothing
                // ever actually waits: the writer is a synchronous OnNext.
                _ => BoundedChannelFullMode.Wait
            }
        });

    private static void Write(ChannelWriter<MqttMessage> writer, MqttMessage message, MqttReadOptions options)
    {
        if (writer.TryWrite(message))
        {
            return;
        }

        // Only reachable under BufferOverflowStrategy.Fail; the dropping modes always accept a write.
        writer.TryComplete(new MqttBufferOverflowException(options.BufferCapacity));
    }
}
