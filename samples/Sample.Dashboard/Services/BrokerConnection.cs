using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Text;
using Microsoft.AspNetCore.SignalR;
using MQTTClientRx;
using Sample.Dashboard.Client.Models;
using Sample.Dashboard.Hubs;

namespace Sample.Dashboard.Services;

/// <summary>
/// Owns the one <see cref="MqttClientRx"/> the whole dashboard shares, and projects its observables
/// onto SignalR.
/// </summary>
/// <remarks>
/// <para>
/// The server holds the broker connection rather than each browser, because a browser cannot open a
/// TCP socket - a WebAssembly client could only ever reach <c>ws://</c> brokers, and the TCP and TLS
/// paths would be undemonstrable. It is also what a real dashboard does: one connection, N viewers,
/// credentials staying server-side.
/// </para>
/// <para>
/// Subscribing to <see cref="IMqttClientRx.Messages"/> is what opens the connection; disposing that
/// subscription closes it. There is no Connect call to make.
/// </para>
/// </remarks>
public sealed class BrokerConnection(
    IHubContext<MqttHub> hub,
    MessageLog log,
    IHostApplicationLifetime lifetime,
    ILogger<BrokerConnection> logger) : IHostedService, IAsyncDisposable
{
    private const int HistoryLimit = 60;

    /// <summary>
    /// How long messages are gathered before a SignalR send. A busy broker can deliver thousands a
    /// second; batching turns that into ten sends a second regardless of rate, which is the
    /// difference between a responsive page and a wedged browser.
    /// </summary>
    private static readonly TimeSpan BatchWindow = TimeSpan.FromMilliseconds(100);

    private readonly SemaphoreSlim _switching = new(1, 1);
    private readonly List<ConnectionDto> _history = [];
    private readonly List<ActivityDto> _activity = [];
    private readonly List<SubscriptionDto> _subscriptions = [];
    private readonly Lock _stateGate = new();

    private Session? _session;

    /// <summary>The broker currently configured.</summary>
    public BrokerProfileDto Profile { get; private set; } =
        new(BrokerCatalog.NameFor(BrokerCatalog.DefaultUri), BrokerCatalog.DefaultUri,
            nameof(MqttProtocolVersion.V500), false, nameof(ConnectionType.Tcp),
            OutageSimulationAvailable: true);

    /// <summary>The reconnect policy currently in force.</summary>
    public ReconnectPolicyDto Policy { get; private set; } = ToDto(ReconnectPolicy.Default with { MaxAttempts = 10 });

    /// <summary>The most recent connection state transition.</summary>
    public ConnectionDto? Current { get; private set; }

    /// <summary>Whether an outage can be simulated, which needs the loopback proxy (TCP only).</summary>
    public bool CanSimulateOutage => _session?.Proxy is not null;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        // Deliberately deferred to ApplicationStarted rather than dialling out here. IHostedService
        // starts before Kestrel binds, so connecting in this method means a bind failure - a port
        // already in use, say - leaves a broker connection open for a process that is about to die,
        // and buries the real error under MQTT reconnect noise. ApplicationStarted only fires once
        // the web host is actually listening.
        lifetime.ApplicationStarted.Register(() => _ = StartBrokerAsync());

        return Task.CompletedTask;
    }

    private async Task StartBrokerAsync()
    {
        try
        {
            await ConnectAsync(new BrokerRequest(
                BrokerCatalog.DefaultUri,
                BrokerCatalog.DefaultTopicFilter,
                QoSLevel.AtMostOnce,
                MqttProtocolVersion.V500,
                Policy));
        }
        catch (Exception exception)
        {
            // A broker that will not answer must not take the web host down with it: the dashboard
            // is still usable, and the Connection page is where you would go to fix this.
            logger.LogError(exception, "Initial broker connection failed. Use the Connection page to retry.");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => TearDownAsync();

    /// <summary>Everything a newly connected browser needs to render immediately.</summary>
    public SnapshotDto Snapshot()
    {
        lock (_stateGate)
        {
            return new SnapshotDto(
                Profile,
                Policy,
                Current,
                log.Snapshot(),
                [.. _subscriptions],
                [.. _history],
                [.. _activity],
                BrokerCatalog.Options);
        }
    }

    /// <summary>
    /// Points the dashboard at a broker, replacing any existing connection.
    /// </summary>
    /// <remarks>
    /// Options are immutable, so a policy change is also a reconnection - which is honest: the
    /// backoff curve belongs to the connection that is using it.
    /// </remarks>
    public async Task ConnectAsync(BrokerRequest request)
    {
        await _switching.WaitAsync();

        try
        {
            await TearDownAsync();

            if (!Uri.TryCreate(request.Uri, UriKind.Absolute, out var uri))
            {
                Record("error", $"'{request.Uri}' is not an absolute URI.");
                await BroadcastActivityAsync();
                return;
            }

            log.Clear();

            lock (_stateGate)
            {
                _subscriptions.Clear();
                _history.Clear();
            }

            Policy = request.Policy ?? Policy;

            var session = BuildSession(uri, request);
            _session = session;

            Profile = new BrokerProfileDto(
                BrokerCatalog.NameFor(request.Uri),
                request.Uri,
                request.ProtocolVersion.ToString(),
                session.UseTls,
                session.ConnectionType.ToString(),
                session.Proxy is not null);

            Record("info", $"Connecting to {request.Uri} as {request.ProtocolVersion}.");

            Wire(session, request);

            await hub.Clients.All.SendAsync(HubEvents.ProfileChanged, Profile);
            await BroadcastActivityAsync();
        }
        finally
        {
            _switching.Release();
        }
    }

    /// <summary>Closes the connection and leaves it closed.</summary>
    public async Task DisconnectAsync()
    {
        await _switching.WaitAsync();

        try
        {
            await TearDownAsync();
            Record("info", "Disconnected by request.");

            var dto = new ConnectionDto(DateTimeOffset.UtcNow, nameof(MqttConnectionState.Disconnected), 0, null, null, null);
            await PublishConnectionAsync(dto);
            await BroadcastActivityAsync();
        }
        finally
        {
            _switching.Release();
        }
    }

    /// <summary>
    /// Severs the live socket without a DISCONNECT packet, so the reconnect policy has something
    /// real to recover from.
    /// </summary>
    public async Task BreakAsync(bool keepBrokerUnreachable)
    {
        var proxy = _session?.Proxy;

        if (proxy is null)
        {
            Record("warn", "Outage simulation needs the loopback proxy, which only fronts TCP brokers.");
            await BroadcastActivityAsync();
            return;
        }

        proxy.SetRefusing(keepBrokerUnreachable);

        var dropped = proxy.Break();

        Record("warn", keepBrokerUnreachable
            ? $"Severed {dropped} socket(s) and blocked reconnects — watch the backoff grow."
            : $"Severed {dropped} socket(s). The next attempt should succeed.");

        await BroadcastActivityAsync();
    }

    /// <summary>Stops refusing connections, letting a blocked reconnect succeed.</summary>
    public async Task RestoreAsync()
    {
        if (_session?.Proxy is { } proxy)
        {
            proxy.SetRefusing(false);
            Record("info", "Broker reachable again.");
            await BroadcastActivityAsync();
        }
    }

    /// <summary>Adds a topic filter to the live connection and reports the broker's reason code.</summary>
    public async Task<string?> SubscribeAsync(string topic, QoSLevel qos)
    {
        if (_session is not { } session)
        {
            return "Not connected.";
        }

        try
        {
            var result = await session.Client.SubscribeAsync([MqttTopicFilter.Create(topic, qos)]);

            lock (_stateGate)
            {
                foreach (var item in result.Items)
                {
                    _subscriptions.RemoveAll(s => s.Topic == item.TopicFilter.Topic);
                    _subscriptions.Add(new SubscriptionDto(
                        item.TopicFilter.Topic,
                        item.TopicFilter.QualityOfServiceLevel.ToString(),
                        item.ResultCode.ToString(),
                        item.IsSuccess,
                        item.GrantedQoS?.ToString()));
                }
            }

            Record(result.IsSuccess ? "info" : "warn",
                $"SUBSCRIBE {topic} → {string.Join(", ", result.Items.Select(i => i.ResultCode))}");

            await BroadcastSubscriptionsAsync();
            await BroadcastActivityAsync();

            return result.IsSuccess ? null : $"Broker refused: {result.Items[0].ResultCode}";
        }
        catch (MqttClientRxException exception)
        {
            Record("error", $"SUBSCRIBE {topic} failed: {exception.Message}");
            await BroadcastActivityAsync();
            return exception.Message;
        }
    }

    /// <summary>Removes a topic filter from the live connection.</summary>
    public async Task<string?> UnsubscribeAsync(string topic)
    {
        if (_session is not { } session)
        {
            return "Not connected.";
        }

        try
        {
            var result = await session.Client.UnsubscribeAsync([topic]);

            lock (_stateGate)
            {
                _subscriptions.RemoveAll(s => s.Topic == topic);
            }

            Record("info", $"UNSUBSCRIBE {topic} → {string.Join(", ", result.Items.Select(i => i.ResultCode))}");

            await BroadcastSubscriptionsAsync();
            await BroadcastActivityAsync();

            return null;
        }
        catch (MqttClientRxException exception)
        {
            Record("error", $"UNSUBSCRIBE {topic} failed: {exception.Message}");
            await BroadcastActivityAsync();
            return exception.Message;
        }
    }

    private Session BuildSession(Uri uri, BrokerRequest request)
    {
        var scheme = uri.Scheme.ToLowerInvariant();
        var isWebSocket = scheme is "ws" or "wss";
        var useTls = scheme is "mqtts" or "wss";
        var connectionType = isWebSocket ? ConnectionType.WebSocket : ConnectionType.Tcp;

        BrokerProxy? proxy = null;
        var clientUri = uri;

        if (!isWebSocket)
        {
            // Front TCP brokers with the loopback proxy so the outage button has a socket to kill.
            // WebSocket cannot be proxied this way - the Host header would name the proxy - so those
            // connections go direct and simply cannot simulate an outage.
            var port = uri.IsDefaultPort ? (useTls ? 8883 : 1883) : uri.Port;
            proxy = BrokerProxy.Start(uri.Host, port, logger);
            clientUri = new Uri($"mqtt://127.0.0.1:{proxy.Port}");
        }

        var options = new MqttClientRxOptions
        {
            Uri = clientUri,
            ConnectionType = connectionType,
            ProtocolVersion = request.ProtocolVersion,
            ClientId = NewClientId(),
            KeepAlivePeriod = TimeSpan.FromSeconds(15),
            TopicFilters = string.IsNullOrWhiteSpace(request.TopicFilter)
                ? []
                : [MqttTopicFilter.Create(request.TopicFilter, request.Qos)],
            Reconnect = FromDto(Policy),

            // The certificate is validated against the real broker's name, not the proxy's loopback
            // address: the proxy forwards bytes and never terminates TLS.
            Tls = useTls ? new MqttTlsOptions { TargetHost = uri.Host } : null,
            Logger = logger
        };

        return new Session(new MqttClientRx(options), proxy, useTls, connectionType);
    }

    private void Wire(Session session, BrokerRequest request)
    {
        var composite = new CompositeDisposable();

        composite.Add(session.Client.ConnectionState
            .Select(change => Observable.FromAsync(() => OnConnectionChangedAsync(change)))
            .Concat()
            .Subscribe(_ => { }, exception => logger.LogError(exception, "Connection state pump failed.")));

        composite.Add(session.Client.Disconnects
            .Select(disconnect => Observable.FromAsync(() => OnDisconnectedAsync(disconnect)))
            .Concat()
            .Subscribe(_ => { }, exception => logger.LogError(exception, "Disconnect pump failed.")));

        // Subscribing here is what connects. Everything above is metadata; this is the connection.
        composite.Add(session.Client.Messages
            .Select(ToDto)
            .Buffer(BatchWindow)
            .Where(batch => batch.Count > 0)
            .Select(batch => Observable.FromAsync(() => hub.Clients.All.SendAsync(HubEvents.Messages, batch)))
            .Concat()
            .Subscribe(
                _ => { },
                exception => _ = OnStreamFailedAsync(exception)));

        // The heartbeat needs a live connection, so it starts with the session and dies with it.
        // Order matters: CancellationDisposable cancels, and the source itself is disposed after.
        var heartbeat = new CancellationTokenSource();
        composite.Add(new CancellationDisposable(heartbeat));
        composite.Add(heartbeat);
        _ = HeartbeatAsync(session.Client, heartbeat.Token);

        session.Subscriptions = composite;

        if (!string.IsNullOrWhiteSpace(request.TopicFilter))
        {
            lock (_stateGate)
            {
                _subscriptions.Add(new SubscriptionDto(
                    request.TopicFilter, request.Qos.ToString(), "Pending", true, null));
            }
        }
    }

    /// <summary>
    /// Publishes a small message on a loop so the Live page has something to show even against a
    /// broker with no ambient traffic of its own. It also exercises the publish path, and carries
    /// MQTT 5 properties so the detail panel has something to render.
    /// </summary>
    private async Task HeartbeatAsync(IMqttClientRx client, CancellationToken cancellationToken)
    {
        var beat = 0L;

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2));

        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                beat++;

                try
                {
                    await client.PublishAsync(
                        MqttMessage.FromString(
                            "mqttclientrx/dashboard/heartbeat",
                            $$"""{"beat":{{beat}},"utc":"{{DateTimeOffset.UtcNow:O}}"}""") with
                        {
                            ContentType = "application/json",
                            PayloadFormatIndicator = PayloadFormat.CharacterData,
                            UserProperties = [new MqttUserProperty("source", "dashboard")]
                        },
                        cancellationToken);
                }
                catch (MqttClientRxException)
                {
                    // Between connections. The next tick will find the connection back, or not.
                }
            }
        }
        catch (OperationCanceledException)
        {
            // The session ended. This runs detached, so an escaping cancellation would become an
            // unobserved task fault rather than anything anyone sees.
        }
    }

    private async Task OnConnectionChangedAsync(ConnectionStateChange change)
    {
        var dto = new ConnectionDto(
            DateTimeOffset.UtcNow,
            change.State.ToString(),
            change.Attempt,
            change.NextRetryDelay?.TotalSeconds,
            change.ConnectResult is { } result ? ToDto(result) : null,
            change.Failure?.Message);

        if (change.State == MqttConnectionState.Connected)
        {
            Record("info", "Connected.");
            await BroadcastSubscriptionsAsync();
        }
        else if (change.State == MqttConnectionState.Reconnecting)
        {
            Record("warn", $"Reconnecting — attempt {change.Attempt}, next try in {change.NextRetryDelay?.TotalSeconds:F1}s.");
        }
        else if (change.Failure is { } failure)
        {
            Record("error", $"Gave up: {failure.Message}");
        }

        await PublishConnectionAsync(dto);
        await BroadcastActivityAsync();
    }

    private Task OnDisconnectedAsync(MqttDisconnect disconnect)
    {
        // Reported through the activity log only. A separate Disconnected hub event carried the same
        // information in a structured DTO that no page ever subscribed to - one signal, two wires.
        Record(disconnect.ClientWasConnected ? "warn" : "error",
            disconnect.ClientWasConnected
                ? $"Connection lost: {disconnect.ReasonCode}{Describe(disconnect.ReasonString)}."
                : $"Connection attempt failed: {disconnect.ReasonCode}{Describe(disconnect.ReasonString)}.");

        return BroadcastActivityAsync();
    }

    private static string Describe(string? reasonString) =>
        string.IsNullOrWhiteSpace(reasonString) ? string.Empty : $" ({reasonString})";

    private async Task OnStreamFailedAsync(Exception exception)
    {
        Record("error", $"Message stream ended: {exception.Message}");
        logger.LogWarning(exception, "Message stream terminated.");
        await BroadcastActivityAsync();
    }

    private async Task PublishConnectionAsync(ConnectionDto dto)
    {
        lock (_stateGate)
        {
            Current = dto;
            _history.Add(dto);

            if (_history.Count > HistoryLimit)
            {
                _history.RemoveRange(0, _history.Count - HistoryLimit);
            }
        }

        await hub.Clients.All.SendAsync(HubEvents.ConnectionChanged, dto);
    }

    private Task BroadcastSubscriptionsAsync()
    {
        SubscriptionDto[] snapshot;

        lock (_stateGate)
        {
            snapshot = [.. _subscriptions];
        }

        return hub.Clients.All.SendAsync(HubEvents.SubscriptionsChanged, snapshot);
    }

    private Task BroadcastActivityAsync()
    {
        ActivityDto[] snapshot;

        lock (_stateGate)
        {
            snapshot = [.. _activity];
        }

        return hub.Clients.All.SendAsync(HubEvents.Activity, snapshot);
    }

    private void Record(string severity, string text)
    {
        lock (_stateGate)
        {
            _activity.Add(new ActivityDto(DateTimeOffset.UtcNow, severity, text));

            if (_activity.Count > HistoryLimit)
            {
                _activity.RemoveRange(0, _activity.Count - HistoryLimit);
            }
        }
    }

    private async Task TearDownAsync()
    {
        if (Interlocked.Exchange(ref _session, null) is { } session)
        {
            await session.DisposeAsync();
        }
    }

    private MessageDto ToDto(MqttMessage message)
    {
        var (text, isText) = RenderPayload(message.Payload);

        var dto = new MessageDto(
            log.NextSequence(),
            DateTimeOffset.UtcNow,
            message.Topic,
            text,
            message.Payload.Length,
            isText,
            message.QualityOfServiceLevel.ToString(),
            message.Retain,
            message.ContentType,
            message.PayloadFormatIndicator?.ToString(),
            message.ResponseTopic,
            message.CorrelationData.IsEmpty ? null : Convert.ToHexString(message.CorrelationData.Span),
            message.MessageExpiryInterval is { } expiry ? (int)expiry.TotalSeconds : null,
            [.. message.UserProperties.Select(p => new UserPropertyDto(p.Name, p.Value))],
            message.SubscriptionIdentifiers);

        log.Add(dto);

        return dto;
    }

    private const int PayloadPreviewLimit = 512;

    private static (string Text, bool IsText) RenderPayload(ReadOnlyMemory<byte> payload)
    {
        if (payload.IsEmpty)
        {
            return (string.Empty, true);
        }

        var slice = payload.Length > PayloadPreviewLimit ? payload[..PayloadPreviewLimit] : payload;

        try
        {
            var text = new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(slice.Span);

            // Control characters mean this is binary that happens to decode; show it as bytes rather
            // than as mojibake.
            if (!text.Any(c => char.IsControl(c) && c is not ('\n' or '\r' or '\t')))
            {
                return (payload.Length > PayloadPreviewLimit ? text + "…" : text, true);
            }
        }
        catch (DecoderFallbackException)
        {
        }

        return (Convert.ToHexString(slice.Span), false);
    }

    /// <summary>
    /// A client identifier that is actually unique per connection.
    /// </summary>
    /// <remarks>
    /// MQTT 3.1.1 caps identifiers at 23 characters, and every broker enforces one session per
    /// identifier - a second connect with the same one takes the session over and closes the first.
    /// The prefix is short on purpose so the random part survives the cap: the previous prefix,
    /// "mqttclientrx-dashboard-", was itself exactly 23 characters, so truncation removed the whole
    /// GUID and handed every connection the same identifier. The symptom is a reconnect loop
    /// reporting "Error while authenticating. Connection closed."
    /// </remarks>
    private static string NewClientId()
    {
        const int maxLength = 23;
        const string prefix = "rx-dash-";

        return string.Concat(prefix, Guid.NewGuid().ToString("N"))[..maxLength];
    }

    private static BrokerCapabilitiesDto ToDto(MqttConnectResult result) => new(
        result.ResultCode.ToString(),
        result.IsSessionPresent,
        result.AssignedClientIdentifier,
        result.ReasonString,
        result.ServerKeepAlive is { } keepAlive ? (int)keepAlive.TotalSeconds : null,
        result.SessionExpiryInterval is { } expiry ? (int)expiry.TotalSeconds : null,
        result.ReceiveMaximum,
        result.MaximumPacketSize,
        result.MaximumQoS.ToString(),
        result.RetainAvailable,
        result.WildcardSubscriptionAvailable,
        result.SharedSubscriptionAvailable,
        result.SubscriptionIdentifiersAvailable);

    private static ReconnectPolicyDto ToDto(ReconnectPolicy policy) => new(
        policy.InitialDelay.TotalSeconds,
        policy.MaxDelay.TotalSeconds,
        policy.BackoffFactor,
        policy.JitterFactor,
        policy.MaxAttempts);

    private static ReconnectPolicy FromDto(ReconnectPolicyDto dto) => ReconnectPolicy.Default with
    {
        InitialDelay = TimeSpan.FromSeconds(Math.Clamp(dto.InitialDelaySeconds, 0.1, 60)),
        MaxDelay = TimeSpan.FromSeconds(Math.Clamp(dto.MaxDelaySeconds, 0.1, 300)),
        BackoffFactor = Math.Clamp(dto.BackoffFactor, 1.0, 10.0),
        JitterFactor = Math.Clamp(dto.JitterFactor, 0, 1),

        // Bounded on purpose. Retrying forever against a public broker is how you get throttled,
        // and it was: pointing an unbounded policy at test.mosquitto.org during development earned
        // a temporary block. Ten attempts is more than enough to watch the curve.
        MaxAttempts = dto.MaxAttempts ?? 10,

        // A refused subscription is not a transient failure - the broker will refuse it identically
        // on the next attempt. This is the ShouldRetry hook earning its place.
        ShouldRetry = context =>
            context.Exception is not MqttSubscribeFailedException
            && ReconnectPolicy.IsRetryableByDefault(context)
    };

    public async ValueTask DisposeAsync()
    {
        await TearDownAsync();
        _switching.Dispose();
    }

    /// <summary>One broker connection and everything that has to die with it.</summary>
    private sealed class Session(IMqttClientRx client, BrokerProxy? proxy, bool useTls, ConnectionType connectionType)
        : IAsyncDisposable
    {
        public IMqttClientRx Client { get; } = client;

        public BrokerProxy? Proxy { get; } = proxy;

        public bool UseTls { get; } = useTls;

        public ConnectionType ConnectionType { get; } = connectionType;

        public IDisposable? Subscriptions { get; set; }

        public async ValueTask DisposeAsync()
        {
            // Order matters: dropping the subscription is what closes the MQTT connection, and it
            // has to go before the proxy underneath it.
            Subscriptions?.Dispose();

            await Client.DisposeAsync();

            if (Proxy is not null)
            {
                await Proxy.DisposeAsync();
            }
        }
    }
}
