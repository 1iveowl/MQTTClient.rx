using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;

namespace Sample.Dashboard.Services;

/// <summary>
/// A transparent TCP forwarder sitting between the client and the real broker, so the dashboard can
/// sever a live connection on demand.
/// </summary>
/// <remarks>
/// <para>
/// Without this the "break the connection" button would be a lie. Disposing the subscription is a
/// <em>graceful</em> teardown - the library treats it as cancellation, not failure, and deliberately
/// does not reconnect. What the reconnect layer actually responds to is a socket dying underneath an
/// established session, and the only honest way to produce that against a broker we do not own is to
/// own the socket.
/// </para>
/// <para>
/// It forwards bytes and nothing else, so TLS still works end to end: the client negotiates with the
/// real broker through the tunnel and this class never sees the plaintext.
/// </para>
/// </remarks>
public sealed class BrokerProxy : IAsyncDisposable
{
    private readonly TcpListener _listener;
    private readonly string _targetHost;
    private readonly int _targetPort;
    private readonly ILogger _logger;
    private readonly ConcurrentDictionary<Guid, TcpClient> _live = new();
    private readonly CancellationTokenSource _stopping = new();

    private volatile bool _refusing;

    private BrokerProxy(TcpListener listener, string targetHost, int targetPort, ILogger logger)
    {
        _listener = listener;
        _targetHost = targetHost;
        _targetPort = targetPort;
        _logger = logger;

        Port = ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    /// <summary>Loopback port the MQTT client should connect to.</summary>
    public int Port { get; }

    /// <summary>Whether new connections are being refused, simulating a broker that is down.</summary>
    public bool IsRefusing => _refusing;

    /// <summary>Starts forwarding to <paramref name="targetHost"/>:<paramref name="targetPort"/>.</summary>
    public static BrokerProxy Start(string targetHost, int targetPort, ILogger logger)
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();

        var proxy = new BrokerProxy(listener, targetHost, targetPort, logger);
        _ = proxy.AcceptLoopAsync();

        return proxy;
    }

    /// <summary>
    /// Kills every forwarded socket. To the MQTT client this is indistinguishable from the network
    /// going away mid-session, which is exactly the point.
    /// </summary>
    public int Break()
    {
        var count = 0;

        foreach (var (key, client) in _live)
        {
            if (_live.TryRemove(key, out _))
            {
                Close(client);
                count++;
            }
        }

        return count;
    }

    /// <summary>Refuse (or resume accepting) new connections, so reconnect attempts visibly fail.</summary>
    public void SetRefusing(bool refusing) => _refusing = refusing;

    private async Task AcceptLoopAsync()
    {
        while (!_stopping.IsCancellationRequested)
        {
            TcpClient inbound;

            try
            {
                inbound = await _listener.AcceptTcpClientAsync(_stopping.Token);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (SocketException)
            {
                return;
            }
            catch (ObjectDisposedException)
            {
                return;
            }

            if (_refusing)
            {
                // Accept then immediately reset, so the client sees a refused connection rather than
                // a hang. A hang would exercise the timeout, not the reconnect policy.
                Close(inbound);
                continue;
            }

            _ = ForwardAsync(inbound);
        }
    }

    private async Task ForwardAsync(TcpClient inbound)
    {
        var inboundKey = Guid.NewGuid();
        var outboundKey = Guid.NewGuid();
        TcpClient? outbound = null;

        try
        {
            outbound = new TcpClient();
            await outbound.ConnectAsync(_targetHost, _targetPort, _stopping.Token);

            _live[inboundKey] = inbound;
            _live[outboundKey] = outbound;

            var inboundStream = inbound.GetStream();
            var outboundStream = outbound.GetStream();

            // Explicit pumps rather than Stream.CopyToAsync: this relays a request/response protocol,
            // so every read has to be flushed onward immediately instead of waiting for a buffer to
            // fill. Whichever direction ends first tears the pair down.
            await Task.WhenAny(
                PumpAsync(inboundStream, outboundStream),
                PumpAsync(outboundStream, inboundStream));
        }
        catch (Exception exception) when (exception is IOException or SocketException or OperationCanceledException or ObjectDisposedException)
        {
            // A severed tunnel is the normal case here, not an error worth shouting about.
            _logger.LogDebug(exception, "Proxy tunnel to {Host}:{Port} ended.", _targetHost, _targetPort);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Proxy tunnel to {Host}:{Port} failed.", _targetHost, _targetPort);
        }
        finally
        {
            _live.TryRemove(inboundKey, out _);
            _live.TryRemove(outboundKey, out _);

            Close(inbound);

            if (outbound is not null)
            {
                Close(outbound);
            }
        }
    }

    private async Task PumpAsync(Stream from, Stream to)
    {
        var buffer = new byte[8192];

        while (!_stopping.IsCancellationRequested)
        {
            var read = await from.ReadAsync(buffer, _stopping.Token);

            if (read == 0)
            {
                return;
            }

            await to.WriteAsync(buffer.AsMemory(0, read), _stopping.Token);
            await to.FlushAsync(_stopping.Token);
        }
    }

    private static void Close(TcpClient client)
    {
        try
        {
            // Linger 0 sends RST rather than FIN, so the peer sees an abrupt loss instead of an
            // orderly shutdown - closer to a real network failure.
            client.Client?.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.Linger, new LingerOption(true, 0));
            client.Close();
        }
        catch (Exception exception) when (exception is SocketException or ObjectDisposedException)
        {
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _stopping.CancelAsync();

        Break();

        try
        {
            _listener.Stop();
        }
        catch (SocketException)
        {
        }

        _stopping.Dispose();
    }
}
