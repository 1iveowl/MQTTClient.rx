using Microsoft.AspNetCore.SignalR;
using MQTTClientRx;
using Sample.Dashboard.Client.Models;
using Sample.Dashboard.Services;

namespace Sample.Dashboard.Hubs;

/// <summary>
/// The browser's window onto the server's broker connection: a snapshot on join, live pushes after,
/// and the handful of commands the pages can issue.
/// </summary>
public sealed class MqttHub(BrokerConnection connection) : Hub
{
    /// <summary>
    /// Everything needed to render on connect, so a page opened mid-stream is not blank until the
    /// next message happens to arrive.
    /// </summary>
    public SnapshotDto GetSnapshot() => connection.Snapshot();

    /// <summary>Points the server at a broker. Also the way to apply a new reconnect policy.</summary>
    public Task Connect(string uri, string topicFilter, string qos, string protocolVersion, ReconnectPolicyDto? policy) =>
        connection.ConnectAsync(new BrokerRequest(
            uri,
            topicFilter,
            Enum.TryParse<QoSLevel>(qos, out var level) ? level : QoSLevel.AtMostOnce,
            Enum.TryParse<MqttProtocolVersion>(protocolVersion, out var version) ? version : MqttProtocolVersion.V500,
            policy));

    /// <summary>Closes the connection and leaves it closed.</summary>
    public Task Disconnect() => connection.DisconnectAsync();

    /// <summary>
    /// Severs the live socket. With <paramref name="keepBrokerUnreachable"/> the proxy also refuses
    /// reconnects, so the backoff curve keeps growing until <see cref="Restore"/>.
    /// </summary>
    public Task Break(bool keepBrokerUnreachable) => connection.BreakAsync(keepBrokerUnreachable);

    /// <summary>Lets a blocked reconnect through again.</summary>
    public Task Restore() => connection.RestoreAsync();

    /// <summary>Adds a topic filter; returns null on success or the broker's refusal.</summary>
    public Task<string?> Subscribe(string topic, string qos) =>
        connection.SubscribeAsync(topic, Enum.TryParse<QoSLevel>(qos, out var level) ? level : QoSLevel.AtMostOnce);

    /// <summary>Removes a topic filter.</summary>
    public Task<string?> Unsubscribe(string topic) => connection.UnsubscribeAsync(topic);
}
