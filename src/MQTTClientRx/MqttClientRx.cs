using System.Reactive.Concurrency;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MQTTClientRx.Internal;

namespace MQTTClientRx;

/// <summary>
/// An Rx-shaped MQTT 5.0 / 3.1.1 client built on MQTTnet.
/// </summary>
/// <example>
/// <code>
/// await using var client = new MqttClientRx(new MqttClientRxOptions
/// {
///     Uri = new Uri("mqtt://test.mosquitto.org:1883"),
///     TopicFilters = [MqttTopicFilter.Create("sensors/#", QoSLevel.AtLeastOnce)]
/// });
///
/// // Subscribing is what connects.
/// using var subscription = client.Messages.Subscribe(
///     message => Console.WriteLine($"{message.Topic}: {message.GetPayloadAsString()}"),
///     error => Console.WriteLine($"Failed: {error.Message}"));
/// </code>
/// </example>
public sealed class MqttClientRx : IMqttClientRx
{
    private readonly MqttClientRxOptions _options;
    private readonly SubscriptionRegistry _subscriptions;
    private readonly ILogger _logger;
    private readonly IScheduler _scheduler;
    private readonly TimeProvider _timeProvider;
    private readonly AttemptCounter _attempts = new();
    private readonly CancellationTokenSource _lifetime = new();
    private readonly BehaviorSubject<ConnectionStateChange> _connectionState;
    private readonly Subject<MqttDisconnect> _disconnects = new();
    private readonly IObservable<MqttMessage> _messages;

    private MqttSession? _session;
    private Exception? _terminalFailure;
    private Task _sessionTeardown = Task.CompletedTask;
    private int _sessionCount;
    private int _disposed;

    /// <summary>Creates a client. No network activity happens until something subscribes to <see cref="Messages"/>.</summary>
    /// <exception cref="MqttOptionsException">The options are inconsistent, or use MQTT 5.0 features under an earlier protocol version.</exception>
    public MqttClientRx(MqttClientRxOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        // Fail here rather than on first subscribe, so a misconfiguration surfaces at the call site
        // that caused it.
        options.Validate();

        _options = options;
        _subscriptions = new SubscriptionRegistry(options.TopicFilters);
        _logger = options.Logger ?? NullLogger.Instance;
        _scheduler = options.Scheduler ?? Scheduler.Default;
        _timeProvider = options.TimeProvider ?? TimeProvider.System;
        _connectionState = new BehaviorSubject<ConnectionStateChange>(
            new ConnectionStateChange { State = MqttConnectionState.Disconnected });

        _messages = BuildMessageStream();
    }

    /// <inheritdoc />
    public IObservable<MqttMessage> Messages => _messages;

    /// <inheritdoc />
    public IObservable<ConnectionStateChange> ConnectionState => _connectionState.AsObservable();

    /// <inheritdoc />
    public IObservable<MqttDisconnect> Disconnects => _disconnects.AsObservable();

    private IObservable<MqttMessage> BuildMessageStream()
    {
        var connected = Observable
            .Defer(CreateSession)
            .RetryWithPolicy(
                _options.Reconnect,
                _scheduler,
                _attempts,
                nextJitter: Random.Shared.NextDouble,
                onRetryScheduled: OnRetryScheduled)
            // Remember why the pipeline stopped, so the final Disconnected can say whether this is
            // a teardown or a surrender. Only errors that outlived the retry policy reach here.
            .Do(_ => { }, exception => Volatile.Write(ref _terminalFailure, exception))
            .Finally(() => PublishState(new ConnectionStateChange
            {
                State = MqttConnectionState.Disconnected,
                Failure = Volatile.Read(ref _terminalFailure)
            }))
            .Publish();

        // RefCount is what makes connection lifetime equal subscription lifetime: the first
        // subscriber connects, the last one to leave disconnects. The grace period stops a
        // subscriber handover from tearing down and rebuilding the connection needlessly.
        return _options.ShareGracePeriod > TimeSpan.Zero
            ? connected.RefCount(_options.ShareGracePeriod, _scheduler)
            : connected.RefCount();
    }

    /// <summary>
    /// One connection attempt, expressed so that its whole lifetime is the subscription's lifetime.
    /// </summary>
    /// <remarks>
    /// The async <c>Observable.Create</c> overload is what makes graceful teardown possible: the
    /// token is cancelled when the subscription is disposed, the method returns, and the
    /// <c>await using</c> sends a clean DISCONNECT on the way out. The older
    /// <c>Materialize/Dematerialize</c> approach never saw unsubscription at all and leaked the
    /// connection every time.
    /// </remarks>
    private IObservable<MqttMessage> CreateSession() =>
        Observable.Create<MqttMessage>(async (observer, cancellationToken) =>
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

            Volatile.Write(ref _terminalFailure, null);

            var isReconnect = Interlocked.Increment(ref _sessionCount) > 1;

            PublishState(new ConnectionStateChange
            {
                State = MqttConnectionState.Connecting,
                Attempt = _attempts.Current
            });

            var session = new MqttSession(_options, _subscriptions, _logger, _timeProvider, PublishDisconnect);
            var teardown = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            Interlocked.Exchange(ref _session, session);
            _ = Interlocked.Exchange(ref _sessionTeardown, teardown.Task);

            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);

            try
            {
                await session.RunAsync(observer, isReconnect, OnSessionConnected, linked.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (linked.IsCancellationRequested)
            {
                // Teardown, not failure: completing normally stops the reconnect operator from
                // treating a deliberate unsubscribe as something to recover from.
            }
            finally
            {
                Interlocked.CompareExchange(ref _session, null, session);

                await session.DisposeAsync().ConfigureAwait(false);
                teardown.TrySetResult();
            }
        });

    private void OnSessionConnected(MqttConnectResult result)
    {
        // A connection that came up resets the backoff curve, so a long-lived client that drops
        // after hours starts again from the initial delay rather than the ceiling.
        _attempts.Reset();

        PublishState(new ConnectionStateChange
        {
            State = MqttConnectionState.Connected,
            ConnectResult = result
        });
    }

    private void OnRetryScheduled(MqttReconnectContext context, TimeSpan delay)
    {
        _logger.LogInformation(
            "Reconnecting to {Uri} in {Delay} (attempt {Attempt}): {Reason}",
            _options.Uri,
            delay,
            context.Attempt,
            context.Exception.Message);

        PublishState(new ConnectionStateChange
        {
            State = MqttConnectionState.Reconnecting,
            Attempt = context.Attempt,
            NextRetryDelay = delay,
            Disconnect = (context.Exception as MqttDisconnectedException)?.Disconnect
        });
    }

    private void PublishState(ConnectionStateChange change) => Publish(_connectionState, change);

    private void PublishDisconnect(MqttDisconnect disconnect) => Publish(_disconnects, disconnect);

    /// <summary>
    /// Pushes to a subject unless the client is shutting down.
    /// </summary>
    /// <remarks>
    /// Both subjects are fed from MQTTnet's event threads and from the Rx pipeline's <c>Finally</c>,
    /// either of which can still be in flight when <see cref="DisposeAsync"/> disposes them. The
    /// flag closes the common case; the catch closes the race that remains. A late notification
    /// during teardown is not worth faulting a background thread over.
    /// </remarks>
    private void Publish<T>(IObserver<T> subject, T value)
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            return;
        }

        try
        {
            subject.OnNext(value);
        }
        catch (ObjectDisposedException)
        {
        }
    }

    /// <inheritdoc />
    public async Task<MqttSubscribeResult> SubscribeAsync(
        IEnumerable<MqttTopicFilter> topicFilters,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(topicFilters);

        var filters = AsList(topicFilters);

        if (filters.Count == 0)
        {
            return new MqttSubscribeResult { Items = [] };
        }

        var result = await ExecuteAsync(
            "subscribe",
            (session, token) => MqttSession.SubscribeAsync(session.Client, filters, token),
            cancellationToken).ConfigureAwait(false);

        // Remember only what the broker actually granted; replaying a refused filter on every
        // reconnect would just repeat the refusal.
        _subscriptions.Add(result.Items.Where(item => item.IsSuccess).Select(item => item.TopicFilter));

        return result;
    }

    /// <inheritdoc />
    public async Task<MqttUnsubscribeResult> UnsubscribeAsync(
        IEnumerable<string> topics,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(topics);

        var list = AsList(topics);

        if (list.Count == 0)
        {
            return new MqttUnsubscribeResult { Items = [] };
        }

        var result = await ExecuteAsync(
            "unsubscribe",
            async (session, token) => MqttNetMapper.ToUnsubscribeResult(
                await session.Client
                    .UnsubscribeAsync(MqttNetMapper.ToUnsubscribeOptions(list), token)
                    .ConfigureAwait(false)),
            cancellationToken).ConfigureAwait(false);

        _subscriptions.Remove(result.Items.Where(item => item.IsSuccess).Select(item => item.Topic));

        return result;
    }

    /// <inheritdoc />
    public Task<MqttPublishResult> PublishAsync(
        MqttMessage message,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        return ExecuteAsync(
            "publish",
            async (session, token) => MqttNetMapper.ToPublishResult(
                await session.Client
                    .PublishAsync(MqttNetMapper.ToMqttNet(message), token)
                    .ConfigureAwait(false)),
            cancellationToken);
    }

    /// <summary>
    /// Runs an operation against the live connection.
    /// </summary>
    /// <remarks>
    /// Every public operation needs the same three things - a connected session, a bounded
    /// cancellation scope, and a non-capturing await. Funnelling them through here means none of
    /// them can be forgotten in a fourth.
    /// </remarks>
    private async Task<TResult> ExecuteAsync<TResult>(
        string operation,
        Func<MqttSession, CancellationToken, Task<TResult>> action,
        CancellationToken cancellationToken)
    {
        var session = RequireSession(operation);

        using var scope = new OperationScope(
            cancellationToken,
            _lifetime.Token,
            _options.OperationTimeout,
            _timeProvider);

        return await action(session, scope.Token).ConfigureAwait(false);
    }

    private MqttSession RequireSession(string operation)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

        var session = Volatile.Read(ref _session);

        if (session is null || !session.Client.IsConnected)
        {
            throw new MqttNotConnectedException(operation);
        }

        return session;
    }

    private static IReadOnlyList<T> AsList<T>(IEnumerable<T> source) =>
        source as IReadOnlyList<T> ?? [.. source];

    /// <summary>
    /// Disconnects any live connection and releases the client.
    /// </summary>
    /// <remarks>
    /// Cancelling the client's lifetime ends the active session the same way disposing the last
    /// subscription would - through a clean DISCONNECT, not a dropped socket - and stops the
    /// reconnect operator from treating it as a failure worth retrying.
    /// </remarks>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        await _lifetime.CancelAsync().ConfigureAwait(false);

        try
        {
            await _sessionTeardown.WaitAsync(_options.DisconnectTimeout, _timeProvider).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            _logger.LogDebug("Timed out waiting for the connection to {Uri} to close.", _options.Uri);
        }

        _connectionState.OnNext(new ConnectionStateChange { State = MqttConnectionState.Disconnected });
        _connectionState.OnCompleted();
        _connectionState.Dispose();

        _disconnects.OnCompleted();
        _disconnects.Dispose();

        _lifetime.Dispose();
    }
}
