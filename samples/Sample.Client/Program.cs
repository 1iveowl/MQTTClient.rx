using System.Reactive.Linq;
using MQTTClientRx;

// A tour of MQTTClientRx 4.0 against the public Mosquitto test broker.
//
// Run a specific section with:  dotnet run -- <basic|tls|websocket|publish|mqtt5|resilient|manual-ack|pull>
// Everything here connects to test.mosquitto.org, which is a shared public broker - expect other
// people's traffic on the wildcard topics.

var sample = args.FirstOrDefault() ?? "basic";
using var stopping = new CancellationTokenSource();

Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    stopping.Cancel();
};

try
{
    await (sample switch
    {
        "basic" => BasicAsync(stopping.Token),
        "tls" => TlsAsync(stopping.Token),
        "websocket" => WebSocketAsync(stopping.Token),
        "publish" => PublishAsync(stopping.Token),
        "mqtt5" => Mqtt5PropertiesAsync(stopping.Token),
        "resilient" => ResilientAsync(stopping.Token),
        "manual-ack" => ManualAcknowledgementAsync(stopping.Token),
        "pull" => PullAsync(stopping.Token),
        _ => Unknown(sample)
    });
}
catch (OperationCanceledException) when (stopping.IsCancellationRequested)
{
    // Ctrl+C is how most of these samples are meant to end; it is not a failure.
    Console.WriteLine("Stopped.");
}

static Task Unknown(string sample)
{
    Console.Error.WriteLine($"Unknown sample '{sample}'.");
    Console.Error.WriteLine("Try: basic, tls, websocket, publish, mqtt5, resilient, manual-ack, pull");
    return Task.CompletedTask;
}

// ---------------------------------------------------------------------------------------------
// The shortest thing that works. Note there is no ConnectAsync: subscribing is what connects, and
// disposing the subscription is what disconnects.
// ---------------------------------------------------------------------------------------------
static async Task BasicAsync(CancellationToken cancellationToken)
{
    await using var client = new MqttClientRx(new MqttClientRxOptions
    {
        Uri = new Uri("mqtt://test.mosquitto.org:1883"),
        TopicFilters = [MqttTopicFilter.Create("$SYS/broker/uptime", QoSLevel.AtMostOnce)]
    });

    using var subscription = client.Messages.Subscribe(
        message => Console.WriteLine($"{message.Topic}: {message.GetPayloadAsString()}"),
        error => Console.Error.WriteLine($"Stream failed: {error.Message}"),
        () => Console.WriteLine("Stream completed."));

    Console.WriteLine("Listening. Press Ctrl+C to stop.");
    await WaitAsync(cancellationToken);
}

// ---------------------------------------------------------------------------------------------
// TLS. MqttTlsOptions.Insecure exists for test brokers; it accepts any certificate, which is never
// appropriate in production.
// ---------------------------------------------------------------------------------------------
static async Task TlsAsync(CancellationToken cancellationToken)
{
    await using var client = new MqttClientRx(new MqttClientRxOptions
    {
        Uri = new Uri("mqtts://test.mosquitto.org:8886"),
        Tls = new MqttTlsOptions(),
        TopicFilters = [MqttTopicFilter.Create("$SYS/broker/uptime")]
    });

    using var subscription = client.Messages.Subscribe(
        message => Console.WriteLine($"[tls] {message.Topic}: {message.GetPayloadAsString()}"));

    await WaitAsync(cancellationToken);
}

static async Task WebSocketAsync(CancellationToken cancellationToken)
{
    await using var client = new MqttClientRx(new MqttClientRxOptions
    {
        Uri = new Uri("ws://test.mosquitto.org:8080/mqtt"),
        ConnectionType = ConnectionType.WebSocket,
        TopicFilters = [MqttTopicFilter.Create("$SYS/broker/uptime")]
    });

    using var subscription = client.Messages.Subscribe(
        message => Console.WriteLine($"[ws] {message.Topic}: {message.GetPayloadAsString()}"));

    await WaitAsync(cancellationToken);
}

// ---------------------------------------------------------------------------------------------
// Publishing needs a live connection, which means something must be subscribed. Results carry the
// broker's reason code rather than being discarded.
// ---------------------------------------------------------------------------------------------
static async Task PublishAsync(CancellationToken cancellationToken)
{
    var topic = $"MQTTClientRx/sample/{Guid.NewGuid():N}";

    await using var client = new MqttClientRx(new MqttClientRxOptions
    {
        Uri = new Uri("mqtt://test.mosquitto.org:1883"),
        TopicFilters = [MqttTopicFilter.Create(topic, QoSLevel.AtLeastOnce)]
    });

    using var subscription = client.Messages.Subscribe(
        message => Console.WriteLine($"echo: {message.GetPayloadAsString()}"));

    await client.WaitForConnectionAsync(cancellationToken);

    for (var i = 1; i <= 5 && !cancellationToken.IsCancellationRequested; i++)
    {
        var result = await client.PublishAsync(topic, $"message {i}", QoSLevel.AtLeastOnce, cancellationToken: cancellationToken);

        Console.WriteLine($"published {i}: {result.ReasonCode} (success: {result.IsSuccess})");

        await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
    }
}

// ---------------------------------------------------------------------------------------------
// MQTT 5.0 message properties. Under V311 these come back null/empty rather than throwing, and
// setting them in the options is rejected at construction.
// ---------------------------------------------------------------------------------------------
static async Task Mqtt5PropertiesAsync(CancellationToken cancellationToken)
{
    var topic = $"MQTTClientRx/sample/{Guid.NewGuid():N}";

    await using var client = new MqttClientRx(new MqttClientRxOptions
    {
        Uri = new Uri("mqtt://test.mosquitto.org:1883"),
        ProtocolVersion = MqttProtocolVersion.V500,
        SessionExpiryInterval = TimeSpan.FromMinutes(5),
        ReceiveMaximum = 32,
        UserProperties = [new MqttUserProperty("client", "MQTTClientRx-sample")],
        TopicFilters = [MqttTopicFilter.Create(topic, QoSLevel.AtLeastOnce)]
    });

    using var subscription = client.Messages.Subscribe(message =>
    {
        Console.WriteLine($"topic:        {message.Topic}");
        Console.WriteLine($"payload:      {message.GetPayloadAsString()}");
        Console.WriteLine($"content type: {message.ContentType}");
        Console.WriteLine($"response to:  {message.ResponseTopic}");
        Console.WriteLine($"properties:   {string.Join(", ", message.UserProperties)}");
    });

    var connectResult = await client.WaitForConnectionAsync(cancellationToken);

    Console.WriteLine($"Broker supports subscription identifiers: {connectResult.SubscriptionIdentifiersAvailable}");
    Console.WriteLine($"Broker supports shared subscriptions:     {connectResult.SharedSubscriptionAvailable}");
    Console.WriteLine($"Broker receive maximum:                   {connectResult.ReceiveMaximum}");

    await client.PublishAsync(
        MqttMessage.FromString(topic, """{"temperature":21.5}""", QoSLevel.AtLeastOnce) with
        {
            ContentType = "application/json",
            PayloadFormatIndicator = PayloadFormat.CharacterData,
            ResponseTopic = $"{topic}/reply",
            CorrelationData = Guid.NewGuid().ToByteArray(),
            UserProperties = [new MqttUserProperty("source", "sample")]
        },
        cancellationToken);

    await Task.Delay(TimeSpan.FromSeconds(3), cancellationToken);
}

// ---------------------------------------------------------------------------------------------
// The reconnect story. MQTTnet 5 removed ManagedClient, so this is what replaces it - and it is
// observable, so you can show connection state in a UI without polling.
// ---------------------------------------------------------------------------------------------
static async Task ResilientAsync(CancellationToken cancellationToken)
{
    await using var client = new MqttClientRx(new MqttClientRxOptions
    {
        Uri = new Uri("mqtt://test.mosquitto.org:1883"),
        TopicFilters = [MqttTopicFilter.Create("$SYS/broker/uptime")],
        Reconnect = ReconnectPolicy.Default with
        {
            InitialDelay = TimeSpan.FromSeconds(1),
            MaxDelay = TimeSpan.FromSeconds(20),
            BackoffFactor = 2.0,
            JitterFactor = 0.2,

            // Stop hammering a broker that has told us who we are is the problem.
            ShouldRetry = context => context.ConnectResultCode is not MqttConnectResultCode.NotAuthorized
        }
    });

    using var states = client.ConnectionState.Subscribe(change => Console.WriteLine($"state: {change}"));
    using var drops = client.Disconnects.Subscribe(drop => Console.WriteLine($"dropped: {drop}"));

    using var subscription = client.Messages
        .Select(message => message.GetPayloadAsString())
        .Subscribe(
            uptime => Console.WriteLine($"uptime: {uptime}"),
            error => Console.Error.WriteLine($"gave up: {error.Message}"));

    // Not "Connected" - subscribing only starts the attempt, and the state stream above reports
    // when it actually lands.
    Console.WriteLine("Listening. Kill your network to watch it recover. Ctrl+C to stop.");
    await WaitAsync(cancellationToken);
}

// ---------------------------------------------------------------------------------------------
// Manual acknowledgement. Under the default (Automatic) the broker is acknowledged as soon as the
// message enters the stream, so a crash mid-processing loses it despite QoS 1. Manual mode holds
// the acknowledgement until you say so.
// ---------------------------------------------------------------------------------------------
static async Task ManualAcknowledgementAsync(CancellationToken cancellationToken)
{
    var topic = $"MQTTClientRx/sample/{Guid.NewGuid():N}";

    await using var client = new MqttClientRx(new MqttClientRxOptions
    {
        Uri = new Uri("mqtt://test.mosquitto.org:1883"),
        Acknowledgement = AcknowledgementMode.Manual,
        ReceiveMaximum = 10,
        TopicFilters = [MqttTopicFilter.Create(topic, QoSLevel.AtLeastOnce)]
    });

    using var subscription = client.Messages
        .Select(message => Observable.FromAsync(async () =>
        {
            await ProcessAsync(message);

            // Only now is the broker allowed to forget the message.
            await message.AcknowledgeAsync(cancellationToken);
        }))
        .Concat()
        .Subscribe(
            _ => { },
            error => Console.Error.WriteLine($"processing failed: {error.Message}"));

    await client.WaitForConnectionAsync(cancellationToken);

    for (var i = 1; i <= 3; i++)
    {
        await client.PublishAsync(topic, $"job {i}", QoSLevel.AtLeastOnce, cancellationToken: cancellationToken);
    }

    await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
}

// ---------------------------------------------------------------------------------------------
// Pull-based consumption. This is the one shape that gives genuine at-least-once processing:
// asking for the next message is itself proof that the previous loop body finished, so that is
// when the acknowledgement is sent. A body that throws leaves its message unacknowledged and the
// broker redelivers it.
// ---------------------------------------------------------------------------------------------
static async Task PullAsync(CancellationToken cancellationToken)
{
    var topic = $"MQTTClientRx/sample/{Guid.NewGuid():N}";

    await using var client = new MqttClientRx(new MqttClientRxOptions
    {
        Uri = new Uri("mqtt://test.mosquitto.org:1883"),
        Acknowledgement = AcknowledgementMode.Manual,
        ReceiveMaximum = 8,          // the broker will not run further ahead than this
        TopicFilters = [MqttTopicFilter.Create(topic, QoSLevel.AtLeastOnce)]
    });

    var publishing = Task.Run(async () =>
    {
        await client.WaitForConnectionAsync(cancellationToken);

        for (var i = 1; i <= 5; i++)
        {
            await client.PublishAsync(topic, $"job {i}", QoSLevel.AtLeastOnce, cancellationToken: cancellationToken);
        }
    }, cancellationToken);

    var options = MqttReadOptions.Default with { BufferCapacity = 32 };

    await foreach (var message in client.ReadAllAsync(options, cancellationToken))
    {
        // A slow consumer slows the broker: no more than ReceiveMaximum messages are ever in
        // flight, because none of them are acknowledged until this loop comes back around.
        await ProcessAsync(message);
    }

    await publishing;
}

static async Task ProcessAsync(MqttMessage message)
{
    Console.WriteLine($"processing {message.GetPayloadAsString()}...");
    await Task.Delay(TimeSpan.FromMilliseconds(500));
    Console.WriteLine($"processed  {message.GetPayloadAsString()}");
}

static async Task WaitAsync(CancellationToken cancellationToken)
{
    try
    {
        await Task.Delay(Timeout.Infinite, cancellationToken);
    }
    catch (OperationCanceledException)
    {
        Console.WriteLine("Stopping.");
    }
}
