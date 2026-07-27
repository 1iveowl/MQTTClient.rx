using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using ReactiveUI;
using ReactiveUI.SourceGenerators;
using Sample.Dashboard.Client.Models;
using Sample.Dashboard.Client.Services;

namespace Sample.Dashboard.Client.ViewModels;

/// <summary>
/// The connection lifecycle, made visible.
/// </summary>
/// <remarks>
/// This is the page the sample exists for. The library's distinguishing feature is that a dropped
/// connection recovers on a policy-driven backoff curve and re-applies its subscriptions - which is
/// a paragraph in a README and an animation here. The outage buttons cut the real socket, so what
/// you watch is the reconnect layer working, not a simulation of it.
/// </remarks>
public sealed partial class ConnectionViewModel : ReactiveObject, IDisposable
{
    private readonly CompositeDisposable _cleanup = [];
    private readonly MqttStreamClient _client;

    private readonly ObservableAsPropertyHelper<ConnectionDto?> _connection;
    private readonly ObservableAsPropertyHelper<BrokerProfileDto?> _profile;
    private readonly ObservableAsPropertyHelper<IReadOnlyList<BrokerOptionDto>> _brokers;
    private readonly ObservableAsPropertyHelper<IReadOnlyList<SubscriptionDto>> _subscriptions;
    private readonly ObservableAsPropertyHelper<IReadOnlyList<ActivityDto>> _activity;
    private readonly ObservableAsPropertyHelper<IReadOnlyList<ConnectionDto>> _history;
    private readonly ObservableAsPropertyHelper<string> _hubState;

    /// <summary>Broker URI to connect to; seeded from the picker but free-text.</summary>
    [Reactive]
    private string _uri = "mqtt://broker.hivemq.com:1883";

    /// <summary>Topic filter applied on connect, and re-applied after a reconnect.</summary>
    [Reactive]
    private string _topicFilter = "mqttclientrx/dashboard/#";

    /// <summary>Quality of service for the initial filter.</summary>
    [Reactive]
    private string _qos = "AtMostOnce";

    /// <summary>Protocol version to negotiate.</summary>
    [Reactive]
    private string _protocolVersion = "V500";

    /// <summary>Topic to add from the subscription box.</summary>
    [Reactive]
    private string _newTopic = string.Empty;

    /// <summary>Quality of service for the subscription box.</summary>
    [Reactive]
    private string _newTopicQos = "AtMostOnce";

    /// <summary>Last error shown next to the subscription box.</summary>
    [Reactive]
    private string? _error;

    // -- Reconnect policy knobs -------------------------------------------------------------

    /// <summary>Delay before the first retry, in seconds.</summary>
    [Reactive]
    private double _initialDelay = 1;

    /// <summary>Ceiling on the retry delay, in seconds.</summary>
    [Reactive]
    private double _maxDelay = 30;

    /// <summary>Multiplier applied per attempt.</summary>
    [Reactive]
    private double _backoffFactor = 2;

    /// <summary>Fraction of the delay to randomise by.</summary>
    [Reactive]
    private double _jitterFactor = 0.2;

    public ConnectionViewModel(MqttStreamClient client)
    {
        _client = client;

        _connection = client.Connection.ToProperty(this, vm => vm.Connection);
        _cleanup.Add(_connection);

        _profile = client.Profile.ToProperty(this, vm => vm.Profile);
        _cleanup.Add(_profile);

        _brokers = client.Brokers.ToProperty(this, vm => vm.Brokers, initialValue: []);
        _cleanup.Add(_brokers);

        _subscriptions = client.Subscriptions.ToProperty(this, vm => vm.Subscriptions, initialValue: []);
        _cleanup.Add(_subscriptions);

        _activity = client.Activity.ToProperty(this, vm => vm.Activity, initialValue: []);
        _cleanup.Add(_activity);

        _history = client.History.ToProperty(this, vm => vm.History, initialValue: []);
        _cleanup.Add(_history);

        _hubState = client.HubState.ToProperty(this, vm => vm.HubState, initialValue: "connecting…");
        _cleanup.Add(_hubState);

        // Seed the editable fields from whatever the server is actually using, so the form reflects
        // reality rather than defaults.
        _cleanup.Add(client.Profile.Where(p => p is not null).Subscribe(profile =>
        {
            Uri = profile!.Uri;
            ProtocolVersion = profile.ProtocolVersion;
        }));

        _cleanup.Add(client.Policy.Where(p => p is not null).Subscribe(policy =>
        {
            InitialDelay = policy!.InitialDelaySeconds;
            MaxDelay = policy.MaxDelaySeconds;
            BackoffFactor = policy.BackoffFactor;
            JitterFactor = policy.JitterFactor;
        }));

        Connect = ReactiveCommand.CreateFromTask(() =>
            client.ConnectAsync(Uri, TopicFilter, Qos, ProtocolVersion, CurrentPolicy()));
        _cleanup.Add(Connect);

        Disconnect = ReactiveCommand.CreateFromTask(client.DisconnectAsync);
        _cleanup.Add(Disconnect);

        // Sever the socket but let the next attempt through: one clean reconnect.
        Drop = ReactiveCommand.CreateFromTask(() => client.BreakAsync(false));
        _cleanup.Add(Drop);

        // Sever it and keep refusing, so the backoff curve grows attempt by attempt.
        Outage = ReactiveCommand.CreateFromTask(() => client.BreakAsync(true));
        _cleanup.Add(Outage);

        Restore = ReactiveCommand.CreateFromTask(client.RestoreAsync);
        _cleanup.Add(Restore);

        Subscribe = ReactiveCommand.CreateFromTask(async () =>
        {
            Error = await client.SubscribeAsync(NewTopic, NewTopicQos);

            if (Error is null)
            {
                NewTopic = string.Empty;
            }
        });
        _cleanup.Add(Subscribe);

        ApplyPolicy = ReactiveCommand.CreateFromTask(() =>
            client.ConnectAsync(Uri, TopicFilter, Qos, ProtocolVersion, CurrentPolicy()));
        _cleanup.Add(ApplyPolicy);
    }

    /// <summary>Latest connection transition.</summary>
    public ConnectionDto? Connection => _connection.Value;

    /// <summary>Which broker the server is pointed at.</summary>
    public BrokerProfileDto? Profile => _profile.Value;

    /// <summary>Brokers offered in the picker.</summary>
    public IReadOnlyList<BrokerOptionDto> Brokers => _brokers.Value;

    /// <summary>Active topic filters with their SUBACK reason codes.</summary>
    public IReadOnlyList<SubscriptionDto> Subscriptions => _subscriptions.Value;

    /// <summary>Connection activity log, newest last.</summary>
    public IReadOnlyList<ActivityDto> Activity => _activity.Value;

    /// <summary>Recent connection transitions, for the timeline.</summary>
    public IReadOnlyList<ConnectionDto> History => _history.Value;

    /// <summary>State of the browser-to-server link.</summary>
    public string HubState => _hubState.Value;

    /// <summary>What the broker reported it supports, once connected.</summary>
    public BrokerCapabilitiesDto? Capabilities => Connection?.Capabilities;

    /// <summary>Whether the broker connection is up.</summary>
    public bool IsConnected => Connection?.State == "Connected";

    /// <summary>Whether the outage buttons can do anything; needs the loopback proxy, so TCP only.</summary>
    public bool CanSimulateOutage => Profile?.OutageSimulationAvailable ?? false;

    public ReactiveCommand<Unit, Unit> Connect { get; }

    public ReactiveCommand<Unit, Unit> Disconnect { get; }

    public ReactiveCommand<Unit, Unit> Drop { get; }

    public ReactiveCommand<Unit, Unit> Outage { get; }

    public ReactiveCommand<Unit, Unit> Restore { get; }

    public ReactiveCommand<Unit, Unit> Subscribe { get; }

    public ReactiveCommand<Unit, Unit> ApplyPolicy { get; }

    /// <summary>
    /// Applies a preset to the editable fields, filter included - <c>$SYS/#</c> is Mosquitto-only and
    /// EMQX refuses it, so the filter travels with the broker rather than being a global default.
    /// </summary>
    public void Choose(BrokerOptionDto option)
    {
        Uri = option.Uri;
        TopicFilter = option.DefaultTopicFilter;
    }

    /// <summary>Removes a topic filter.</summary>
    public async Task UnsubscribeAsync(string topic) => Error = await _client.UnsubscribeAsync(topic);

    /// <summary>Connects to the hub. Called once when the page first renders.</summary>
    public Task StartAsync() => _client.StartAsync();

    private ReconnectPolicyDto CurrentPolicy() =>
        new(InitialDelay, MaxDelay, BackoffFactor, JitterFactor, MaxAttempts: null);

    public void Dispose() => _cleanup.Dispose();
}
