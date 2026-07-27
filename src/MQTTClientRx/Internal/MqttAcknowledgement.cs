using MQTTnet;

namespace MQTTClientRx.Internal;

/// <summary>
/// The acknowledgement handle for one received message, made safe to invoke more than once.
/// </summary>
/// <remarks>
/// <para>
/// MQTTnet throws <see cref="InvalidOperationException"/> if the same message is acknowledged twice,
/// and a message can legitimately reach that point more than once - a consumer may call
/// <see cref="MqttMessage.AcknowledgeAsync"/> inside their own handler while
/// <see cref="MqttClientRxAsyncEnumerable.ReadAllAsync(IMqttClientRx, MqttReadOptions, CancellationToken)"/>
/// also acknowledges it on the next pull. Faulting the stream over a duplicate acknowledgement would
/// punish the more careful consumer.
/// </para>
/// <para>
/// Shared by every <c>with</c>-derived copy of a message, so the once-only guarantee survives
/// record copying.
/// </para>
/// </remarks>
internal sealed class MqttAcknowledgement(MqttApplicationMessageReceivedEventArgs args)
{
    private int _acknowledged;

    internal Task AcknowledgeAsync(CancellationToken cancellationToken)
    {
        // Automatic mode already acknowledged on delivery; there is nothing left to send.
        if (args.AutoAcknowledge)
        {
            return Task.CompletedTask;
        }

        return Interlocked.Exchange(ref _acknowledged, 1) == 0
            ? args.AcknowledgeAsync(cancellationToken)
            : Task.CompletedTask;
    }
}
