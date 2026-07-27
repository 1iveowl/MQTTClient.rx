using MQTTClientRx;
using Sample.Dashboard.Client.Models;

namespace Sample.Dashboard.Services;

/// <summary>What the user asked the server to connect to.</summary>
/// <param name="Uri">Broker address. Anything absolute with an mqtt/mqtts/ws/wss scheme.</param>
/// <param name="TopicFilter">Filter to subscribe on connect.</param>
/// <param name="Qos">Quality of service for that filter.</param>
/// <param name="ProtocolVersion">Protocol version to negotiate.</param>
/// <param name="Policy">Reconnect policy knobs from the Connection page.</param>
public sealed record BrokerRequest(
    string Uri,
    string TopicFilter,
    QoSLevel Qos,
    MqttProtocolVersion ProtocolVersion,
    ReconnectPolicyDto? Policy);

/// <summary>
/// The brokers offered in the picker.
/// </summary>
/// <remarks>
/// <para>
/// Public test brokers, all verified reachable when this list was written. They carry other people's
/// traffic, which is a feature here - the dashboard has something to show the moment it opens - and
/// a reason not to point automated tests at them.
/// </para>
/// <para>
/// Each preset carries its own default filter, because there is no universal one.
/// <c>$SYS/#</c> is Mosquitto's statistics tree; EMQX refuses it outright with
/// <c>NotAuthorized</c>, and HiveMQ's public broker does not publish it at all. Where there is no
/// broker-provided traffic, the dashboard's own heartbeat supplies some.
/// </para>
/// </remarks>
public static class BrokerCatalog
{
    /// <summary>Topics the dashboard publishes to itself, so every broker shows something.</summary>
    public const string OwnTraffic = "mqttclientrx/dashboard/#";

    /// <summary>
    /// The out-of-the-box choice. HiveMQ rather than Mosquitto: it is the most consistently
    /// available of the public brokers, and accepts wildcard subscriptions without an ACL fight.
    /// </summary>
    public const string DefaultUri = "mqtt://broker.hivemq.com:1883";

    /// <summary>Filter used when a broker has no suggestion of its own.</summary>
    public const string DefaultTopicFilter = OwnTraffic;

    /// <summary>The presets, in the order the picker shows them.</summary>
    public static IReadOnlyList<BrokerOptionDto> Options { get; } =
    [
        new("HiveMQ (public)", DefaultUri,
            "HiveMQ public broker. Full MQTT 5.0. Shows the dashboard\u0027s own heartbeat traffic.",
            RequiresTls: false, ConnectionType: nameof(MQTTClientRx.ConnectionType.Tcp),
            DefaultTopicFilter: OwnTraffic),

        new("Mosquitto (test)", "mqtt://test.mosquitto.org:1883",
            "Eclipse Mosquitto public test broker. Publishes $SYS statistics — but throttles clients that reconnect aggressively.",
            RequiresTls: false, ConnectionType: nameof(MQTTClientRx.ConnectionType.Tcp),
            DefaultTopicFilter: "$SYS/#"),

        new("Mosquitto (TLS)", "mqtts://test.mosquitto.org:8886",
            "The same broker over TLS on 8886, with a publicly trusted certificate.",
            RequiresTls: true, ConnectionType: nameof(MQTTClientRx.ConnectionType.Tcp),
            DefaultTopicFilter: "$SYS/#"),

        new("EMQX (public)", "mqtt://broker.emqx.io:1883",
            "EMQX public broker. Full MQTT 5.0 including shared subscriptions. Refuses $SYS.",
            RequiresTls: false, ConnectionType: nameof(MQTTClientRx.ConnectionType.Tcp),
            DefaultTopicFilter: OwnTraffic),

        new("Local broker", "mqtt://localhost:1883",
            "A broker on this machine — ./scripts/test-broker.sh start will run one.",
            RequiresTls: false, ConnectionType: nameof(MQTTClientRx.ConnectionType.Tcp),
            DefaultTopicFilter: "#")
    ];

    /// <summary>Resolves a URI to its preset name, or reports it as custom.</summary>
    public static string NameFor(string uri) =>
        Options.FirstOrDefault(option => string.Equals(option.Uri, uri, StringComparison.OrdinalIgnoreCase))?.Name
        ?? "Custom";
}
