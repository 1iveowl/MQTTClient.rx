using System.Collections.ObjectModel;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using DynamicData;
using DynamicData.Binding;
using ReactiveUI;
using ReactiveUI.SourceGenerators;
using Sample.Dashboard.Client.Models;
using Sample.Dashboard.Client.Services;

namespace Sample.Dashboard.Client.ViewModels;

/// <summary>
/// The message firehose.
/// </summary>
/// <remarks>
/// Rx end to end: <c>MqttClientRx</c> observables on the server, SignalR in the middle, DynamicData
/// and ReactiveUI here. One pipeline drives the table - filter, sort, bind - and the same changesets
/// bump <see cref="Revision"/>, whose property change is what re-renders the component. Adds,
/// evictions and filter changes all go through it.
/// </remarks>
public sealed partial class LiveViewModel : ReactiveObject, IDisposable
{
    private readonly CompositeDisposable _cleanup = [];
    private readonly MqttStreamClient _client;
    private readonly ReadOnlyObservableCollection<MessageDto> _messages;
    private readonly ObservableAsPropertyHelper<int> _count;
    private readonly ObservableAsPropertyHelper<long> _rate;
    private readonly ObservableAsPropertyHelper<long> _skipped;
    private readonly ObservableAsPropertyHelper<string> _hubState;
    private readonly ObservableAsPropertyHelper<string> _brokerState;
    private readonly ObservableAsPropertyHelper<string> _brokerName;

    /// <summary>Free-text filter over topic and payload.</summary>
    [Reactive]
    private string _filter = string.Empty;

    /// <summary>Whether new messages are being accepted into the table.</summary>
    [Reactive]
    private bool _paused;

    /// <summary>The row expanded in the detail panel.</summary>
    [Reactive]
    private MessageDto? _selected;

    /// <summary>
    /// Bumped by every changeset. Its property change is what re-renders the component, since a
    /// <see cref="ReadOnlyObservableCollection{T}"/> mutating in place raises nothing Blazor watches.
    /// </summary>
    [Reactive]
    private int _revision;

    public LiveViewModel(MqttStreamClient client)
    {
        _client = client;

        var predicate = this.WhenAnyValue(vm => vm.Filter)
            .Select<string, Func<MessageDto, bool>>(filter => message =>
                string.IsNullOrWhiteSpace(filter)
                || message.Topic.Contains(filter, StringComparison.OrdinalIgnoreCase)
                || message.Payload.Contains(filter, StringComparison.OrdinalIgnoreCase));

        // Newest first: on a live feed the interesting row is always the one that just arrived.
        //
        // Subscribed directly rather than through ToProperty. Hanging the binding off an
        // ObservableAsPropertyHelper makes the collection's updates depend on ReactiveUI's property
        // delivery, and when that stalls the cache keeps filling while the bound collection silently
        // stops - a table frozen at one row next to a counter climbing past a hundred. The bind is
        // the thing that must run, so it gets its own subscription.
        _cleanup.Add(client.Messages
            .Connect()
            .Filter(predicate)
            .SortAndBind(out _messages, SortExpressionComparer<MessageDto>.Descending(m => m.Sequence))
            .Subscribe(
                _ => Revision++,
                error => Console.Error.WriteLine($"Message binding failed: {error}")));

        _count = client.Messages.CountChanged.ToProperty(this, vm => vm.Count);
        _cleanup.Add(_count);

        // Messages per second, by differentiating the running total once a second. Sampling a
        // counter beats buffering the stream: the rate stays honest even while paused.
        //
        // Deliberately the default scheduler rather than RxSchedulers.MainThreadScheduler: on Blazor
        // that is CurrentThreadScheduler, which honours a due time by blocking the calling thread -
        // and in WebAssembly that thread is the only one there is. The default scheduler uses a
        // timer, so the UI keeps running between ticks.
        _rate = Observable.Interval(TimeSpan.FromSeconds(1))
            .Select(_ => client.Total)
            .Buffer(2, 1)
            .Where(pair => pair.Count == 2)
            .Select(pair => Math.Max(0, pair[1] - pair[0]))
            .ToProperty(this, vm => vm.Rate);
        _cleanup.Add(_rate);

        _skipped = client.Skipped.ToProperty(this, vm => vm.Skipped);
        _cleanup.Add(_skipped);

        _hubState = client.HubState.ToProperty(this, vm => vm.HubState, initialValue: "connecting…");
        _cleanup.Add(_hubState);

        _brokerState = client.Connection
            .Select(c => c?.State ?? "Unknown")
            .ToProperty(this, vm => vm.BrokerState, initialValue: "Unknown");
        _cleanup.Add(_brokerState);

        _brokerName = client.Profile
            .Select(p => p is null ? "—" : $"{p.Name} · {p.Uri}")
            .ToProperty(this, vm => vm.BrokerName, initialValue: "—");
        _cleanup.Add(_brokerName);

        // Pause is view-model state; pushing it into the stream client is what actually stops the
        // cache accepting, so the two cannot drift.
        _cleanup.Add(this.WhenAnyValue(vm => vm.Paused).Subscribe(client.SetPaused));

        TogglePause = ReactiveCommand.Create(() => { Paused = !Paused; });
        _cleanup.Add(TogglePause);

        Clear = ReactiveCommand.Create(client.ClearMessages);
        _cleanup.Add(Clear);

        ClearFilter = ReactiveCommand.Create(() => { Filter = string.Empty; });
        _cleanup.Add(ClearFilter);
    }

    /// <summary>Filtered, newest-first messages bound to the table.</summary>
    public ReadOnlyObservableCollection<MessageDto> Messages => _messages;

    /// <summary>Messages currently cached, before filtering.</summary>
    public int Count => _count.Value;

    /// <summary>Messages passing the current filter. Re-read on each render.</summary>
    public int Matching => _messages.Count;

    /// <summary>Messages received in the last second.</summary>
    public long Rate => _rate.Value;

    /// <summary>Messages that arrived while paused.</summary>
    public long Skipped => _skipped.Value;

    /// <summary>State of the browser-to-server link.</summary>
    public string HubState => _hubState.Value;

    /// <summary>State of the server's broker connection.</summary>
    public string BrokerState => _brokerState.Value;

    /// <summary>Which broker the server is pointed at.</summary>
    public string BrokerName => _brokerName.Value;

    /// <summary>Whether the broker connection is currently up.</summary>
    public bool IsConnected => BrokerState == "Connected";

    public ReactiveCommand<Unit, Unit> TogglePause { get; }

    public ReactiveCommand<Unit, Unit> Clear { get; }

    public ReactiveCommand<Unit, Unit> ClearFilter { get; }

    /// <summary>Connects to the hub. Called once when the page first renders.</summary>
    public Task StartAsync() => _client.StartAsync();

    public void Dispose() => _cleanup.Dispose();
}
