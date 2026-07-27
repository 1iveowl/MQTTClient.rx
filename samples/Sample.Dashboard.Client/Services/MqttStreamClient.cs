using System.Reactive.Linq;
using System.Reactive.Subjects;
using DynamicData;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.SignalR.Client;
using Sample.Dashboard.Client.Models;

namespace Sample.Dashboard.Client.Services;

/// <summary>
/// Turns the server's SignalR stream back into client-side Rx: message batches feed a DynamicData
/// cache, everything else feeds a subject. No broker socket in the browser - the server does that.
/// </summary>
/// <remarks>
/// <para>
/// Threading: the subjects and the cache are only mutated from SignalR callbacks, which are
/// dispatched sequentially per connection on WebAssembly's single thread. Porting this to Blazor
/// Server would introduce multiple dispatch contexts, and every mutation here would need
/// <c>Synchronize</c> first.
/// </para>
/// <para>
/// The cache is capped. MQTT can deliver faster than a browser can render, and an unbounded list is
/// how a dashboard turns into a memory leak with a nice colour scheme.
/// </para>
/// </remarks>
public sealed class MqttStreamClient : IAsyncDisposable
{
    /// <summary>How many messages the browser keeps. The server's own ring is smaller still.</summary>
    private const int MaxMessages = 1_000;

    private readonly HubConnection _hub;
    private readonly SourceCache<MessageDto, long> _messages = new(message => message.Sequence);

    private readonly BehaviorSubject<string> _hubState = new("connecting…");
    private readonly BehaviorSubject<ConnectionDto?> _connection = new(null);
    private readonly BehaviorSubject<BrokerProfileDto?> _profile = new(null);
    private readonly BehaviorSubject<ReconnectPolicyDto?> _policy = new(null);
    private readonly BehaviorSubject<IReadOnlyList<SubscriptionDto>> _subscriptions = new([]);
    private readonly BehaviorSubject<IReadOnlyList<ActivityDto>> _activity = new([]);
    private readonly BehaviorSubject<IReadOnlyList<ConnectionDto>> _history = new([]);
    private readonly BehaviorSubject<IReadOnlyList<BrokerOptionDto>> _brokers = new([]);
    private readonly BehaviorSubject<bool> _paused = new(false);
    private readonly BehaviorSubject<long> _skipped = new(0);

    private long _total;

    public MqttStreamClient(NavigationManager navigation)
    {
        Messages = _messages.AsObservableCache();

        _hub = new HubConnectionBuilder()
            .WithUrl(navigation.ToAbsoluteUri(HubEvents.Path))
            .WithAutomaticReconnect()
            .Build();

        _hub.On<IReadOnlyList<MessageDto>>(HubEvents.Messages, OnMessages);
        _hub.On<ConnectionDto>(HubEvents.ConnectionChanged, change => _connection.OnNext(change));
        _hub.On<IReadOnlyList<SubscriptionDto>>(HubEvents.SubscriptionsChanged, subs => _subscriptions.OnNext(subs));
        _hub.On<IReadOnlyList<ActivityDto>>(HubEvents.Activity, activity => _activity.OnNext(activity));
        _hub.On<BrokerProfileDto>(HubEvents.ProfileChanged, profile => _profile.OnNext(profile));

        _hub.Reconnecting += _ => { _hubState.OnNext("reconnecting…"); return Task.CompletedTask; };
        _hub.Reconnected += _ => { _hubState.OnNext("connected"); return ReloadSnapshotAsync(); };
        _hub.Closed += _ => { _hubState.OnNext("disconnected"); return Task.CompletedTask; };
    }

    /// <summary>Live message cache, keyed by server sequence number.</summary>
    public IObservableCache<MessageDto, long> Messages { get; }

    /// <summary>State of the browser-to-server link, distinct from the broker connection.</summary>
    public IObservable<string> HubState => _hubState;

    /// <summary>Latest broker connection transition.</summary>
    public IObservable<ConnectionDto?> Connection => _connection;

    /// <summary>Which broker the server is pointed at.</summary>
    public IObservable<BrokerProfileDto?> Profile => _profile;

    /// <summary>Reconnect policy currently in force.</summary>
    public IObservable<ReconnectPolicyDto?> Policy => _policy;

    /// <summary>Active topic filters and their SUBACK reason codes.</summary>
    public IObservable<IReadOnlyList<SubscriptionDto>> Subscriptions => _subscriptions;

    /// <summary>Connection activity log.</summary>
    public IObservable<IReadOnlyList<ActivityDto>> Activity => _activity;

    /// <summary>Recent connection transitions, for the timeline.</summary>
    public IObservable<IReadOnlyList<ConnectionDto>> History => _history;

    /// <summary>Brokers offered in the picker.</summary>
    public IObservable<IReadOnlyList<BrokerOptionDto>> Brokers => _brokers;

    /// <summary>Whether the message feed is paused.</summary>
    public IObservable<bool> Paused => _paused;

    /// <summary>How many messages arrived while paused.</summary>
    public IObservable<long> Skipped => _skipped;

    /// <summary>Total messages received since the page loaded; the rate meter differentiates this.</summary>
    public long Total => Interlocked.Read(ref _total);

    /// <summary>Connects to the hub and loads the initial snapshot.</summary>
    public async Task StartAsync()
    {
        if (_hub.State != HubConnectionState.Disconnected)
        {
            return;
        }

        try
        {
            await _hub.StartAsync();
            _hubState.OnNext("connected");
            await ReloadSnapshotAsync();
        }
        catch (Exception exception)
        {
            _hubState.OnNext($"failed: {exception.Message}");
        }
    }

    /// <summary>Stops accepting messages into the cache; arrivals are counted instead.</summary>
    public void SetPaused(bool paused)
    {
        if (paused)
        {
            _skipped.OnNext(0);
        }

        _paused.OnNext(paused);
    }

    /// <summary>Empties the message cache.</summary>
    public void ClearMessages()
    {
        _messages.Clear();
        _skipped.OnNext(0);
    }

    // -- Commands ----------------------------------------------------------------------------

    public Task ConnectAsync(string uri, string topicFilter, string qos, string protocolVersion, ReconnectPolicyDto policy) =>
        _hub.InvokeAsync("Connect", uri, topicFilter, qos, protocolVersion, policy);

    public Task DisconnectAsync() => _hub.InvokeAsync("Disconnect");

    public Task BreakAsync(bool keepBrokerUnreachable) => _hub.InvokeAsync("Break", keepBrokerUnreachable);

    public Task RestoreAsync() => _hub.InvokeAsync("Restore");

    public Task<string?> SubscribeAsync(string topic, string qos) =>
        _hub.InvokeAsync<string?>("Subscribe", topic, qos);

    public Task<string?> UnsubscribeAsync(string topic) => _hub.InvokeAsync<string?>("Unsubscribe", topic);

    // -- Internals ---------------------------------------------------------------------------

    private void OnMessages(IReadOnlyList<MessageDto> batch)
    {
        Interlocked.Add(ref _total, batch.Count);

        if (_paused.Value)
        {
            _skipped.OnNext(_skipped.Value + batch.Count);
            return;
        }

        _messages.Edit(updater =>
        {
            updater.AddOrUpdate(batch);

            // Sequence numbers are monotonic, so the smallest keys are the oldest messages.
            var excess = updater.Count - MaxMessages;

            if (excess > 0)
            {
                updater.RemoveKeys([.. updater.Keys.Order().Take(excess)]);
            }
        });
    }

    private async Task ReloadSnapshotAsync()
    {
        try
        {
            var snapshot = await _hub.InvokeAsync<SnapshotDto>("GetSnapshot");

            _profile.OnNext(snapshot.Profile);
            _policy.OnNext(snapshot.Policy);
            _brokers.OnNext(snapshot.Brokers);
            _subscriptions.OnNext(snapshot.Subscriptions);
            _activity.OnNext(snapshot.Activity);
            _history.OnNext(snapshot.ConnectionHistory);

            if (snapshot.Connection is { } connection)
            {
                _connection.OnNext(connection);
            }

            // Seed the cache so a page opened mid-stream is not blank until the next message.
            _messages.Edit(updater =>
            {
                updater.Clear();
                updater.AddOrUpdate(snapshot.RecentMessages);
            });
        }
        catch (Exception exception)
        {
            _hubState.OnNext($"snapshot failed: {exception.Message}");
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _hub.DisposeAsync();

        _messages.Dispose();
        _hubState.Dispose();
        _connection.Dispose();
        _profile.Dispose();
        _policy.Dispose();
        _subscriptions.Dispose();
        _activity.Dispose();
        _history.Dispose();
        _brokers.Dispose();
        _paused.Dispose();
        _skipped.Dispose();
    }
}
