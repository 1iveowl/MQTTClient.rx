using System.Reactive.Concurrency;
using Microsoft.Extensions.Logging;

namespace MQTTClientRx;

/// <summary>
/// Everything needed to establish and maintain a broker connection. Immutable - build one with
/// <c>with</c> expressions or object initialisers and hand it to <see cref="MqttClientRx"/>.
/// </summary>
public sealed record MqttClientRxOptions
{
    /// <summary>
    /// Broker address, e.g. <c>mqtt://test.mosquitto.org:1883</c>, <c>mqtts://host:8883</c>,
    /// <c>ws://host:8000/mqtt</c> or <c>wss://host:443/mqtt</c>.
    /// </summary>
    public required Uri Uri { get; init; }

    /// <summary>Transport to use. Must match the scheme in <see cref="Uri"/>.</summary>
    public ConnectionType ConnectionType { get; init; } = ConnectionType.Tcp;

    /// <summary>
    /// Protocol version to negotiate. Defaults to MQTT 5.0; drop to
    /// <see cref="MqttProtocolVersion.V311"/> for brokers that predate it, such as Azure IoT Hub.
    /// </summary>
    public MqttProtocolVersion ProtocolVersion { get; init; } = MqttProtocolVersion.V500;

    /// <summary>Client identifier. When null a random one is generated per connection.</summary>
    public string? ClientId { get; init; }

    /// <summary>Username and password, when the broker requires them.</summary>
    public MqttCredentials? Credentials { get; init; }

    /// <summary>Keep-alive interval. The broker may override this; see <see cref="MqttConnectResult.ServerKeepAlive"/>.</summary>
    public TimeSpan KeepAlivePeriod { get; init; } = TimeSpan.FromSeconds(15);

    /// <summary>
    /// Timeout applied to connect, subscribe, unsubscribe and publish when the caller supplies no
    /// <see cref="CancellationToken"/> of their own.
    /// </summary>
    public TimeSpan OperationTimeout { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Whether to discard any existing session on connect. Under MQTT 5.0 this pairs with
    /// <see cref="SessionExpiryInterval"/>; under 3.1.1 it is the whole of the clean-session flag.
    /// </summary>
    public bool CleanStart { get; init; } = true;

    /// <summary>How long the broker keeps session state after this client disconnects. MQTT 5.0 only.</summary>
    public TimeSpan? SessionExpiryInterval { get; init; }

    /// <summary>
    /// Maximum number of QoS &gt; 0 messages this client will accept without acknowledging - protocol
    /// level flow control. MQTT 5.0 only.
    /// </summary>
    public ushort? ReceiveMaximum { get; init; }

    /// <summary>Largest packet this client will accept. MQTT 5.0 only.</summary>
    public uint? MaximumPacketSize { get; init; }

    /// <summary>Number of topic aliases this client will accept. MQTT 5.0 only.</summary>
    public ushort? TopicAliasMaximum { get; init; }

    /// <summary>User properties sent on the CONNECT packet. MQTT 5.0 only.</summary>
    public IReadOnlyList<MqttUserProperty> UserProperties { get; init; } = [];

    /// <summary>TLS settings. Null means no TLS.</summary>
    public MqttTlsOptions? Tls { get; init; }

    /// <summary>Message the broker publishes if this client disappears without a clean disconnect.</summary>
    public MqttWillMessage? Will { get; init; }

    /// <summary>
    /// Filters subscribed automatically on connect, and re-subscribed after a reconnect that did not
    /// restore the session.
    /// </summary>
    public IReadOnlyList<MqttTopicFilter> TopicFilters { get; init; } = [];

    /// <summary>Reconnection strategy. Defaults to <see cref="ReconnectPolicy.Default"/>.</summary>
    public ReconnectPolicy Reconnect { get; init; } = ReconnectPolicy.Default;

    /// <summary>
    /// When QoS 1/2 messages are acknowledged. See <see cref="AcknowledgementMode"/> for the
    /// trade-off - the default acknowledges on delivery to the stream, not on processing.
    /// </summary>
    public AcknowledgementMode Acknowledgement { get; init; } = AcknowledgementMode.Automatic;

    /// <summary>How long to wait for a graceful DISCONNECT before dropping the socket.</summary>
    public TimeSpan DisconnectTimeout { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// How long the connection is held open after the last subscriber disposes, so that a subscriber
    /// arriving moments later reuses it instead of forcing a reconnect. Zero disconnects immediately.
    /// </summary>
    public TimeSpan ShareGracePeriod { get; init; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Scheduler for time-based operations in the observable pipeline - reconnect delays and the
    /// share grace period. Defaults to <see cref="Scheduler.Default"/>. Supply a
    /// <c>TestScheduler</c> to drive them in virtual time.
    /// </summary>
    public IScheduler? Scheduler { get; init; }

    /// <summary>
    /// Clock for the timeouts that live outside the observable pipeline -
    /// <see cref="OperationTimeout"/> and <see cref="DisconnectTimeout"/>. Defaults to
    /// <see cref="System.TimeProvider.System"/>. Supply a <c>FakeTimeProvider</c> to drive them in
    /// virtual time.
    /// </summary>
    /// <remarks>
    /// Two time abstractions, because they govern different layers and neither substitutes for the
    /// other: Rx operators take an <see cref="IScheduler"/>, while a
    /// <see cref="CancellationTokenSource"/> takes a <see cref="System.TimeProvider"/>. Rx 7.0 has
    /// no <see cref="System.TimeProvider"/> integration, so this cannot be collapsed into one knob.
    /// Leave both unset and everything runs on the real clock.
    /// </remarks>
    public TimeProvider? TimeProvider { get; init; }

    /// <summary>Optional logger. Defaults to no logging.</summary>
    public ILogger? Logger { get; init; }

    /// <summary>
    /// Creates options for a plain TCP connection.
    /// </summary>
    public static MqttClientRxOptions Tcp(string host, int port = 1883) =>
        new() { Uri = new Uri($"mqtt://{host}:{port}"), ConnectionType = ConnectionType.Tcp };

    /// <summary>
    /// Validates the options and throws <see cref="MqttOptionsException"/> if they are inconsistent
    /// or ask for MQTT 5.0 features under an earlier protocol version.
    /// </summary>
    /// <remarks>
    /// Called automatically on every connection attempt. Failing loudly here beats silently dropping
    /// properties the wire format cannot carry.
    /// </remarks>
    public void Validate()
    {
        if (!Uri.IsAbsoluteUri)
        {
            throw new MqttOptionsException($"{nameof(Uri)} must be absolute, e.g. mqtt://broker:1883.");
        }

        var scheme = Uri.Scheme.ToLowerInvariant();
        var schemeIsWebSocket = scheme is "ws" or "wss";
        var wantsWebSocket = ConnectionType == ConnectionType.WebSocket;

        if (schemeIsWebSocket != wantsWebSocket)
        {
            throw new MqttOptionsException(
                $"{nameof(ConnectionType)} is {ConnectionType} but the URI scheme is '{Uri.Scheme}'. " +
                "Use mqtt/mqtts with Tcp, or ws/wss with WebSocket.");
        }

        if (KeepAlivePeriod < TimeSpan.Zero)
        {
            throw new MqttOptionsException($"{nameof(KeepAlivePeriod)} cannot be negative.");
        }

        if (OperationTimeout <= TimeSpan.Zero)
        {
            throw new MqttOptionsException($"{nameof(OperationTimeout)} must be greater than zero.");
        }

        if (ProtocolVersion == MqttProtocolVersion.V500)
        {
            return;
        }

        foreach (var problem in DescribeVersion5OnlyUsage())
        {
            throw new MqttOptionsException(
                $"{problem} requires MQTT 5.0 but {nameof(ProtocolVersion)} is {ProtocolVersion}. " +
                $"Either set {nameof(ProtocolVersion)} to {nameof(MqttProtocolVersion.V500)} or remove the option.");
        }
    }

    private IEnumerable<string> DescribeVersion5OnlyUsage()
    {
        if (SessionExpiryInterval is not null)
        {
            yield return nameof(SessionExpiryInterval);
        }

        if (ReceiveMaximum is not null)
        {
            yield return nameof(ReceiveMaximum);
        }

        if (MaximumPacketSize is not null)
        {
            yield return nameof(MaximumPacketSize);
        }

        if (TopicAliasMaximum is not null)
        {
            yield return nameof(TopicAliasMaximum);
        }

        if (UserProperties.Count > 0)
        {
            yield return nameof(UserProperties);
        }

        if (Will?.DelayInterval is not null)
        {
            yield return $"{nameof(Will)}.{nameof(MqttWillMessage.DelayInterval)}";
        }

        if (Will?.UserProperties.Count > 0)
        {
            yield return $"{nameof(Will)}.{nameof(MqttWillMessage.UserProperties)}";
        }

        foreach (var filter in TopicFilters)
        {
            if (filter.NoLocal)
            {
                yield return $"{nameof(MqttTopicFilter)}.{nameof(MqttTopicFilter.NoLocal)} on '{filter.Topic}'";
            }

            if (filter.RetainAsPublished)
            {
                yield return $"{nameof(MqttTopicFilter)}.{nameof(MqttTopicFilter.RetainAsPublished)} on '{filter.Topic}'";
            }

            if (filter.RetainHandling != RetainHandling.SendAtSubscribe)
            {
                yield return $"{nameof(MqttTopicFilter)}.{nameof(MqttTopicFilter.RetainHandling)} on '{filter.Topic}'";
            }
        }
    }
}
