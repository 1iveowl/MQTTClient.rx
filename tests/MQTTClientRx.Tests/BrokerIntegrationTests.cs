using System.Reactive.Linq;
using System.Reactive.Threading.Tasks;
using MQTTClientRx.Tests.Infrastructure;

namespace MQTTClientRx.Tests;

/// <summary>
/// End-to-end behaviour against a real broker. These are the tests that prove the lifecycle claims -
/// that subscribing connects, that disposing disconnects, and that a dropped connection is
/// transparently recovered - none of which can be established against a mock.
/// </summary>
[Collection(MosquittoCollection.Name)]
public class BrokerIntegrationTests(MosquittoFixture broker)
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    [BrokerFact]
    public async Task Subscribing_connects_and_disposing_the_last_subscription_disconnects()
    {
        await using var client = new MqttClientRx(broker.Options());

        var states = new List<MqttConnectionState>();
        using var stateWatch = client.ConnectionState.Subscribe(change => states.Add(change.State));

        Assert.Equal([MqttConnectionState.Disconnected], states);

        var subscription = client.Messages.Subscribe(_ => { });
        try
        {
            var connectResult = await client.WaitForConnectionAsync().WaitAsync(Timeout);

            Assert.True(connectResult.IsSuccess);
            Assert.Contains(MqttConnectionState.Connected, states);
        }
        finally
        {
            subscription.Dispose();
        }

        await Poll.UntilAsync(() => states[^1] == MqttConnectionState.Disconnected, Timeout, "the client to disconnect");
    }

    [BrokerFact]
    public async Task Receives_a_message_it_published()
    {
        var topic = NewTopic();

        await using var client = new MqttClientRx(broker.Options() with
        {
            TopicFilters = [MqttTopicFilter.Create(topic, QoSLevel.AtLeastOnce)]
        });

        var received = new TaskCompletionSource<MqttMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var subscription = client.Messages.Subscribe(message => received.TrySetResult(message));

        await client.WaitForConnectionAsync().WaitAsync(Timeout);

        var publishResult = await client.PublishAsync(topic, "hello", QoSLevel.AtLeastOnce);
        Assert.True(publishResult.IsSuccess);

        var message = await received.Task.WaitAsync(Timeout);

        Assert.Equal(topic, message.Topic);
        Assert.Equal("hello", message.GetPayloadAsString());
        Assert.Equal(QoSLevel.AtLeastOnce, message.QualityOfServiceLevel);
    }

    [BrokerTheory]
    [InlineData(QoSLevel.AtMostOnce)]
    [InlineData(QoSLevel.AtLeastOnce)]
    [InlineData(QoSLevel.ExactlyOnce)]
    public async Task Round_trips_a_message_at_every_quality_of_service(QoSLevel qos)
    {
        var topic = NewTopic();

        await using var client = new MqttClientRx(broker.Options() with
        {
            TopicFilters = [MqttTopicFilter.Create(topic, qos)]
        });

        var received = new TaskCompletionSource<MqttMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var subscription = client.Messages.Subscribe(message => received.TrySetResult(message));

        await client.WaitForConnectionAsync().WaitAsync(Timeout);
        await client.PublishAsync(topic, "payload", qos);

        var message = await received.Task.WaitAsync(Timeout);

        Assert.Equal("payload", message.GetPayloadAsString());
    }

    [BrokerFact]
    public async Task Round_trips_MQTT_5_user_properties_and_content_type()
    {
        var topic = NewTopic();

        await using var client = new MqttClientRx(broker.Options() with
        {
            TopicFilters = [MqttTopicFilter.Create(topic, QoSLevel.AtLeastOnce)]
        });

        var received = new TaskCompletionSource<MqttMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var subscription = client.Messages.Subscribe(message => received.TrySetResult(message));

        await client.WaitForConnectionAsync().WaitAsync(Timeout);

        await client.PublishAsync(MqttMessage.FromString(topic, "{}", QoSLevel.AtLeastOnce) with
        {
            ContentType = "application/json",
            PayloadFormatIndicator = PayloadFormat.CharacterData,
            ResponseTopic = $"{topic}/reply",
            CorrelationData = new byte[] { 7, 7 },
            UserProperties = [new MqttUserProperty("tenant", "acme")]
        });

        var message = await received.Task.WaitAsync(Timeout);

        Assert.Equal("application/json", message.ContentType);
        Assert.Equal(PayloadFormat.CharacterData, message.PayloadFormatIndicator);
        Assert.Equal($"{topic}/reply", message.ResponseTopic);
        Assert.Equal(new byte[] { 7, 7 }, message.CorrelationData.ToArray());
        Assert.Equal(new MqttUserProperty("tenant", "acme"), Assert.Single(message.UserProperties));
    }

    [BrokerFact]
    public async Task Under_MQTT_3_1_1_the_version_5_properties_come_back_empty()
    {
        var topic = NewTopic();

        await using var client = new MqttClientRx(broker.Options() with
        {
            ProtocolVersion = MqttProtocolVersion.V311,
            TopicFilters = [MqttTopicFilter.Create(topic, QoSLevel.AtLeastOnce)]
        });

        var received = new TaskCompletionSource<MqttMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var subscription = client.Messages.Subscribe(message => received.TrySetResult(message));

        await client.WaitForConnectionAsync().WaitAsync(Timeout);
        await client.PublishAsync(topic, "plain", QoSLevel.AtLeastOnce);

        var message = await received.Task.WaitAsync(Timeout);

        Assert.Equal("plain", message.GetPayloadAsString());
        Assert.Null(message.ContentType);
        Assert.Empty(message.UserProperties);
    }

    [BrokerFact]
    public async Task Subscribe_reports_the_granted_quality_of_service_per_filter()
    {
        await using var client = new MqttClientRx(broker.Options());

        using var subscription = client.Messages.Subscribe(_ => { });
        await client.WaitForConnectionAsync().WaitAsync(Timeout);

        var result = await client.SubscribeAsync(
        [
            MqttTopicFilter.Create(NewTopic(), QoSLevel.AtMostOnce),
            MqttTopicFilter.Create(NewTopic(), QoSLevel.ExactlyOnce)
        ]);

        Assert.True(result.IsSuccess);
        Assert.Equal(QoSLevel.AtMostOnce, result.Items[0].GrantedQoS);
        Assert.Equal(QoSLevel.ExactlyOnce, result.Items[1].GrantedQoS);
    }

    [BrokerFact]
    public async Task Unsubscribing_stops_delivery()
    {
        var topic = NewTopic();

        await using var client = new MqttClientRx(broker.Options() with
        {
            TopicFilters = [MqttTopicFilter.Create(topic, QoSLevel.AtLeastOnce)]
        });

        var count = 0;
        using var subscription = client.Messages.Subscribe(_ => Interlocked.Increment(ref count));

        await client.WaitForConnectionAsync().WaitAsync(Timeout);

        var unsubscribed = await client.UnsubscribeAsync(topic);
        Assert.True(unsubscribed.IsSuccess);

        await client.PublishAsync(topic, "ignored", QoSLevel.AtLeastOnce);
        await Task.Delay(TimeSpan.FromSeconds(1));

        Assert.Equal(0, Volatile.Read(ref count));
    }

    [BrokerFact]
    public async Task Two_subscribers_share_one_connection()
    {
        await using var client = new MqttClientRx(broker.Options() with
        {
            ShareGracePeriod = TimeSpan.FromSeconds(2)
        });

        var connects = 0;
        using var stateWatch = client.ConnectionState
            .Where(change => change.State == MqttConnectionState.Connected)
            .Subscribe(_ => Interlocked.Increment(ref connects));

        using var first = client.Messages.Subscribe(_ => { });
        await client.WaitForConnectionAsync().WaitAsync(Timeout);

        using var second = client.Messages.Subscribe(_ => { });
        await Task.Delay(TimeSpan.FromMilliseconds(500));

        Assert.Equal(1, Volatile.Read(ref connects));
    }

    [ControllableBrokerFact]
    public async Task Reconnects_and_resubscribes_after_the_broker_restarts()
    {
        var topic = NewTopic();

        await using var client = new MqttClientRx(broker.Options() with
        {
            TopicFilters = [MqttTopicFilter.Create(topic, QoSLevel.AtLeastOnce)],
            KeepAlivePeriod = TimeSpan.FromSeconds(2),
            Reconnect = ReconnectPolicy.Default with
            {
                InitialDelay = TimeSpan.FromMilliseconds(200),
                MaxDelay = TimeSpan.FromSeconds(2)
            }
        });

        var connects = 0;
        using var stateWatch = client.ConnectionState
            .Where(change => change.State == MqttConnectionState.Connected)
            .Subscribe(_ => Interlocked.Increment(ref connects));

        var received = new TaskCompletionSource<MqttMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var subscription = client.Messages.Subscribe(message => received.TrySetResult(message));

        await client.WaitForConnectionAsync().WaitAsync(Timeout);

        await broker.RestartAsync();

        // The stream must survive the outage rather than terminating, and the topic filter must be
        // re-applied on the new connection without the caller doing anything.
        await Poll.UntilAsync(() => Volatile.Read(ref connects) >= 2, Timeout, "the client to reconnect");

        await client.PublishAsync(topic, "after restart", QoSLevel.AtLeastOnce);

        var message = await received.Task.WaitAsync(Timeout);
        Assert.Equal("after restart", message.GetPayloadAsString());
    }

    [ControllableBrokerFact]
    public async Task A_disconnect_is_reported_with_ClientWasConnected_set()
    {
        await using var client = new MqttClientRx(broker.Options() with
        {
            Reconnect = ReconnectPolicy.None
        });

        var disconnect = client.Disconnects.FirstAsync().ToTask();

        using var subscription = client.Messages.Subscribe(_ => { }, _ => { });
        await client.WaitForConnectionAsync().WaitAsync(Timeout);

        await broker.RestartAsync();

        var observed = await disconnect.WaitAsync(Timeout);

        // The predicate that earlier versions filtered on was inverted; a real disconnect has
        // ClientWasConnected set, and is exactly the case that must be observed.
        Assert.True(observed.ClientWasConnected);
    }

    [BrokerFact]
    public async Task Manual_acknowledgement_lets_the_consumer_acknowledge_explicitly()
    {
        var topic = NewTopic();

        await using var client = new MqttClientRx(broker.Options() with
        {
            Acknowledgement = AcknowledgementMode.Manual,
            TopicFilters = [MqttTopicFilter.Create(topic, QoSLevel.AtLeastOnce)]
        });

        var received = new TaskCompletionSource<MqttMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var subscription = client.Messages.Subscribe(message => received.TrySetResult(message));

        await client.WaitForConnectionAsync().WaitAsync(Timeout);
        await client.PublishAsync(topic, "needs ack", QoSLevel.AtLeastOnce);

        var message = await received.Task.WaitAsync(Timeout);

        await message.AcknowledgeAsync();

        // A second acknowledgement must not throw; consumers retrying a handler should be safe.
        await message.AcknowledgeAsync();
    }

    private static string NewTopic() => $"mqttclientrx/test/{Guid.NewGuid():N}";
}

/// <summary>Failure paths that need no broker at all.</summary>
public class ConnectionFailureTests
{
    [Fact]
    public async Task Operations_without_a_subscription_report_that_there_is_no_connection()
    {
        await using var client = new MqttClientRx(new MqttClientRxOptions
        {
            Uri = new Uri("mqtt://127.0.0.1:1883")
        });

        // No subscriber means no connection, so nothing here reaches the network.
        await Assert.ThrowsAsync<MqttNotConnectedException>(() => client.PublishAsync("t", "nothing is subscribed"));
        await Assert.ThrowsAsync<MqttNotConnectedException>(() => client.SubscribeAsync("t"));
    }

    [Fact]
    public async Task A_refused_connection_terminates_the_stream_with_an_error()
    {
        await using var client = new MqttClientRx(new MqttClientRxOptions
        {
            // Port 1 is reserved and nothing listens on it, so the connection is refused promptly.
            Uri = new Uri("mqtt://127.0.0.1:1"),
            OperationTimeout = TimeSpan.FromSeconds(5),
            Reconnect = ReconnectPolicy.None
        });

        var failure = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);

        using var subscription = client.Messages.Subscribe(
            _ => { },
            error => failure.TrySetResult(error));

        var exception = await failure.Task.WaitAsync(TimeSpan.FromSeconds(30));

        // Earlier versions swallowed this in a catch-all and reported nothing at all.
        Assert.IsType<MqttConnectFailedException>(exception);
    }

    [Fact]
    public async Task WaitForConnectionAsync_faults_once_the_policy_gives_up()
    {
        await using var client = new MqttClientRx(new MqttClientRxOptions
        {
            Uri = new Uri("mqtt://127.0.0.1:1"),
            OperationTimeout = TimeSpan.FromSeconds(5),
            Reconnect = ReconnectPolicy.Default with
            {
                MaxAttempts = 2,
                InitialDelay = TimeSpan.FromMilliseconds(50),
                MaxDelay = TimeSpan.FromMilliseconds(100)
            }
        });

        var waiting = client.WaitForConnectionAsync();

        using var subscription = client.Messages.Subscribe(_ => { }, _ => { });

        // Without the terminal failure on the state stream this would hang until the caller's own
        // timeout, turning "the broker is unreachable" into "something took too long".
        await Assert.ThrowsAsync<MqttConnectFailedException>(() => waiting.WaitAsync(TimeSpan.FromSeconds(30)));
    }

    [Fact]
    public async Task A_refused_connection_is_retried_when_the_policy_allows_it()
    {
        var attempts = 0;

        await using var client = new MqttClientRx(new MqttClientRxOptions
        {
            Uri = new Uri("mqtt://127.0.0.1:1"),
            OperationTimeout = TimeSpan.FromSeconds(5),
            Reconnect = ReconnectPolicy.Default with
            {
                InitialDelay = TimeSpan.FromMilliseconds(50),
                MaxDelay = TimeSpan.FromMilliseconds(100),
                MaxAttempts = 3
            }
        });

        using var stateWatch = client.ConnectionState
            .Where(change => change.State == MqttConnectionState.Reconnecting)
            .Subscribe(_ => Interlocked.Increment(ref attempts));

        var failure = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);

        using var subscription = client.Messages.Subscribe(_ => { }, error => failure.TrySetResult(error));

        await failure.Task.WaitAsync(TimeSpan.FromSeconds(30));

        Assert.Equal(3, Volatile.Read(ref attempts));
    }
}
