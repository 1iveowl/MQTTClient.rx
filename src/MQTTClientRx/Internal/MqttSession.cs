using Microsoft.Extensions.Logging;
using MQTTnet;

namespace MQTTClientRx.Internal;

/// <summary>
/// One physical broker connection, from CONNECT to DISCONNECT.
/// </summary>
/// <remarks>
/// <para>
/// A session is created when someone subscribes and destroyed when the connection ends. Everything
/// about its lifetime is expressed by <see cref="RunAsync"/>: the method connects, subscribes, pumps
/// messages into the observer, and returns when the subscription is disposed or throws when the
/// connection drops. That "throws on drop" is what drives the reconnect operator upstream.
/// </para>
/// <para>
/// Exactly one code path writes to the observer, and it holds <see cref="_gate"/> while doing so.
/// Combined with <see cref="Terminate"/> taking the same lock before the method returns, this
/// guarantees the Rx grammar: no <c>OnNext</c> can overlap with, or follow, the terminal
/// notification that <c>Observable.Create</c> raises when this task completes.
/// </para>
/// </remarks>
internal sealed class MqttSession : IAsyncDisposable
{
    private readonly MqttClientRxOptions _options;
    private readonly SubscriptionRegistry _subscriptions;
    private readonly ILogger _logger;
    private readonly TimeProvider _timeProvider;
    private readonly Action<MqttDisconnect> _onDisconnect;
    private readonly IMqttClient _client;
    private readonly Lock _gate = new();

    private readonly TaskCompletionSource<MqttDisconnect?> _stopped =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private IObserver<MqttMessage>? _observer;
    private bool _terminated;
    private int _disposed;

    internal MqttSession(
        MqttClientRxOptions options,
        SubscriptionRegistry subscriptions,
        ILogger logger,
        TimeProvider timeProvider,
        Action<MqttDisconnect> onDisconnect)
    {
        _options = options;
        _subscriptions = subscriptions;
        _logger = logger;
        _timeProvider = timeProvider;
        _onDisconnect = onDisconnect;
        _client = new MqttClientFactory().CreateMqttClient();
    }

    /// <summary>The live MQTTnet client. Only valid while <see cref="RunAsync"/> has not returned.</summary>
    internal IMqttClient Client => _client;

    /// <summary>The CONNACK for this session, once connected.</summary>
    internal MqttConnectResult? ConnectResult { get; private set; }

    /// <summary>
    /// Connects, subscribes, and pumps messages until the subscription is disposed or the connection
    /// drops.
    /// </summary>
    /// <param name="observer">Receives messages for the lifetime of this connection.</param>
    /// <param name="isReconnect">
    /// Whether this session follows an earlier one. Controls whether a broker-restored session is
    /// trusted to still hold our subscriptions.
    /// </param>
    /// <param name="onConnected">Invoked once the CONNACK is accepted and subscriptions are in place.</param>
    /// <param name="cancellationToken">Cancelled when the last subscriber disposes.</param>
    /// <exception cref="MqttConnectFailedException">The broker refused the connection, or the transport failed.</exception>
    /// <exception cref="MqttSubscribeFailedException">The broker refused one or more topic filters.</exception>
    /// <exception cref="MqttDisconnectedException">An established connection ended.</exception>
    internal async Task RunAsync(
        IObserver<MqttMessage> observer,
        bool isReconnect,
        Action<MqttConnectResult> onConnected,
        CancellationToken cancellationToken)
    {
        _observer = observer;

        _client.ApplicationMessageReceivedAsync += OnApplicationMessageReceivedAsync;
        _client.DisconnectedAsync += OnDisconnectedAsync;

        try
        {
            var connectResult = await ConnectAsync(cancellationToken).ConfigureAwait(false);
            ConnectResult = connectResult;

            await ApplySubscriptionsAsync(connectResult, isReconnect, cancellationToken).ConfigureAwait(false);

            onConnected(connectResult);

            await using var registration = cancellationToken.Register(
                static state => ((TaskCompletionSource<MqttDisconnect?>)state!).TrySetResult(null),
                _stopped).ConfigureAwait(false);

            var disconnect = await _stopped.Task.ConfigureAwait(false);

            if (disconnect is not null)
            {
                // An established connection ended. Surfacing this as an error is what lets the
                // reconnect operator decide whether to try again; if it declines, the exception is
                // what the subscriber sees.
                throw new MqttDisconnectedException(disconnect);
            }
        }
        finally
        {
            Terminate();

            _client.ApplicationMessageReceivedAsync -= OnApplicationMessageReceivedAsync;
            _client.DisconnectedAsync -= OnDisconnectedAsync;
        }
    }

    private async Task<MqttConnectResult> ConnectAsync(CancellationToken cancellationToken)
    {
        var mqttNetOptions = MqttNetMapper.ToMqttNet(_options);

        MqttClientConnectResult raw;

        try
        {
            raw = await _client.ConnectAsync(mqttNetOptions, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            // Transport-level failure: no CONNACK was ever received.
            throw new MqttConnectFailedException(_options.Uri, exception);
        }

        var result = MqttNetMapper.ToConnectResult(raw);

        // MQTTnet 5 no longer throws when the broker returns a CONNACK indicating failure, so this
        // check is the only thing standing between a rejected connection and a caller who believes
        // they are connected.
        if (!result.IsSuccess)
        {
            throw new MqttConnectFailedException(result, _options.Uri);
        }

        _logger.LogDebug(
            "Connected to {Uri} as {ClientId} (session present: {SessionPresent}).",
            _options.Uri,
            result.AssignedClientIdentifier ?? _options.ClientId,
            result.IsSessionPresent);

        return result;
    }

    private async Task ApplySubscriptionsAsync(
        MqttConnectResult connectResult,
        bool isReconnect,
        CancellationToken cancellationToken)
    {
        var filters = _subscriptions.Snapshot();

        if (filters.Count == 0)
        {
            return;
        }

        // A reconnect that restored the session still holds our subscriptions; re-sending them would
        // replay retained messages for no reason.
        if (isReconnect && connectResult.IsSessionPresent)
        {
            _logger.LogDebug("Session restored by the broker; keeping {Count} existing subscription(s).", filters.Count);
            return;
        }

        var result = await SubscribeAsync(_client, filters, cancellationToken).ConfigureAwait(false);

        if (!result.IsSuccess)
        {
            throw new MqttSubscribeFailedException(result);
        }

        _logger.LogDebug("Subscribed {Count} topic filter(s).", filters.Count);
    }

    internal static async Task<MqttSubscribeResult> SubscribeAsync(
        IMqttClient client,
        IReadOnlyList<MqttTopicFilter> filters,
        CancellationToken cancellationToken)
    {
        var raw = await client
            .SubscribeAsync(MqttNetMapper.ToSubscribeOptions(filters), cancellationToken)
            .ConfigureAwait(false);

        return MqttNetMapper.ToSubscribeResult(raw, filters);
    }

    private Task OnApplicationMessageReceivedAsync(MqttApplicationMessageReceivedEventArgs args)
    {
        if (_options.Acknowledgement == AcknowledgementMode.Manual)
        {
            // Hold the acknowledgement until the consumer calls MqttMessage.AcknowledgeAsync, so
            // QoS 1/2 means "processed" rather than "handed to the stream".
            args.AutoAcknowledge = false;
        }

        var message = MqttNetMapper.ToMqttMessage(args);

        lock (_gate)
        {
            if (_terminated)
            {
                return Task.CompletedTask;
            }

            _observer?.OnNext(message);
        }

        return Task.CompletedTask;
    }

    private Task OnDisconnectedAsync(MqttClientDisconnectedEventArgs args)
    {
        var disconnect = MqttNetMapper.ToDisconnect(args);

        _logger.LogDebug("Connection to {Uri} ended: {Disconnect}.", _options.Uri, disconnect);

        // MQTTnet raises this for refused connection attempts as well as for established connections
        // that dropped; ClientWasConnected is what distinguishes them. Both are worth reporting -
        // filtering one out is precisely the bug that made earlier versions hang on a dead socket.
        _onDisconnect(disconnect);

        _stopped.TrySetResult(disconnect);

        return Task.CompletedTask;
    }

    /// <summary>
    /// Closes the window in which messages may still be pushed. Taking the same lock the message
    /// handler holds means any in-flight <c>OnNext</c> completes before this returns.
    /// </summary>
    private void Terminate()
    {
        lock (_gate)
        {
            _terminated = true;
            _observer = null;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        Terminate();

        try
        {
            if (_client.IsConnected)
            {
                using var timeout = new CancellationTokenSource(_options.DisconnectTimeout, _timeProvider);

                var disconnectOptions = new MqttClientDisconnectOptionsBuilder()
                    .WithReason(MqttClientDisconnectOptionsReason.NormalDisconnection)
                    .Build();

                // A clean DISCONNECT tells the broker not to publish the will message. Dropping the
                // socket instead - which is what Dispose() alone does - makes every teardown look
                // like a crash.
                await _client.DisconnectAsync(disconnectOptions, timeout.Token).ConfigureAwait(false);
            }
        }
        catch (Exception exception)
        {
            _logger.LogDebug(exception, "Graceful disconnect from {Uri} failed; dropping the connection.", _options.Uri);
        }
        finally
        {
            _client.Dispose();
        }
    }
}
