using System.Reactive.Linq;
using System.Reactive.Subjects;
using MQTTClientRx.Internal;
using MQTTnet;
using MQTTnet.Packets;

namespace MQTTClientRx.Tests.Infrastructure;

/// <summary>
/// An <see cref="IMqttClientRx"/> whose message stream is driven by the test, so the pull-based
/// bridge can be exercised without a broker.
/// </summary>
internal sealed class FakeMqttClientRx : IMqttClientRx
{
    private readonly Subject<MqttMessage> _messages = new();

    public IObservable<MqttMessage> Messages => _messages.AsObservable();

    public IObservable<ConnectionStateChange> ConnectionState => Observable.Empty<ConnectionStateChange>();

    public IObservable<MqttDisconnect> Disconnects => Observable.Empty<MqttDisconnect>();

    public bool HasSubscribers => _messages.HasObservers;

    public void Emit(MqttMessage message) => _messages.OnNext(message);

    public void Fail(Exception exception) => _messages.OnError(exception);

    public void Complete() => _messages.OnCompleted();

    public Task<MqttSubscribeResult> SubscribeAsync(
        IEnumerable<MqttTopicFilter> topicFilters,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<MqttUnsubscribeResult> UnsubscribeAsync(
        IEnumerable<string> topics,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<MqttPublishResult> PublishAsync(
        MqttMessage message,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public ValueTask DisposeAsync()
    {
        _messages.Dispose();
        return ValueTask.CompletedTask;
    }
}

/// <summary>
/// Builds messages that carry a real MQTTnet acknowledgement handle, so
/// <see cref="MqttMessage.AcknowledgeAsync"/> can be observed rather than assumed.
/// </summary>
internal static class AcknowledgeableMessage
{
    internal static MqttMessage Create(string topic, string payload, List<string> acknowledged)
    {
        var applicationMessage = new MqttApplicationMessageBuilder()
            .WithTopic(topic)
            .WithPayload(payload)
            .WithQualityOfServiceLevel(MQTTnet.Protocol.MqttQualityOfServiceLevel.AtLeastOnce)
            .Build();

        var args = new MqttApplicationMessageReceivedEventArgs(
            clientId: "test",
            applicationMessage,
            new MqttPublishPacket(),
            acknowledgeHandler: (_, _) =>
            {
                lock (acknowledged)
                {
                    acknowledged.Add(payload);
                }

                return Task.CompletedTask;
            })
        {
            // Manual mode: the library holds the acknowledgement until the consumer is done.
            AutoAcknowledge = false
        };

        return MqttMessage.FromString(topic, payload, QoSLevel.AtLeastOnce) with
        {
            Acknowledgement = new MqttAcknowledgement(args)
        };
    }
}
