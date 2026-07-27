using System.Net;
using System.Net.Sockets;
using System.Text;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;

namespace MQTTClientRx.Tests.Infrastructure;

/// <summary>
/// The broker the integration tests talk to, shared by every test in the collection.
/// </summary>
/// <remarks>
/// Starts a disposable Mosquitto through Testcontainers when Docker is available, and otherwise
/// defers to whatever <c>MQTTCLIENTRX_TEST_BROKER</c> points at. See
/// <see cref="TestBroker"/> and <c>tests/README.md</c>.
/// </remarks>
public sealed class MosquittoFixture : IAsyncLifetime
{
    // Mosquitto 2.x refuses anonymous connections unless told otherwise, so the configuration is
    // mapped in rather than relying on the image defaults.
    private const string Configuration = """
        listener 1883
        allow_anonymous true
        """;

    private IContainer? _container;
    private int _hostPort;

    /// <summary>Broker address, valid once the fixture has initialised. Stable across restarts.</summary>
    public Uri Uri { get; private set; } = new("mqtt://127.0.0.1:1883");

    public async Task InitializeAsync()
    {
        if (TestBroker.ExternalUri is { } external)
        {
            Uri = external;
            return;
        }

        if (!TestBroker.DockerAvailable)
        {
            return;
        }

        // Bind an explicit host port rather than letting Docker assign one. An auto-assigned port
        // is chosen afresh on every `docker start`, so the two tests that restart the broker would
        // move it out from under every test that runs after them - and the failure surfaces far
        // away from the cause, as a connect timeout in an unrelated test.
        _hostPort = FindFreePort();

        _container = new ContainerBuilder()
            .WithImage("eclipse-mosquitto:2")
            .WithPortBinding(_hostPort, 1883)
            .WithResourceMapping(Encoding.UTF8.GetBytes(Configuration), "/mosquitto/config/mosquitto.conf")
            .WithWaitStrategy(Wait.ForUnixContainer().UntilPortIsAvailable(1883))
            .Build();

        await _container.StartAsync();

        Uri = new Uri($"mqtt://{_container.Hostname}:{_hostPort}");
    }

    public async Task DisposeAsync()
    {
        if (_container is not null)
        {
            await _container.DisposeAsync();
        }
    }

    /// <summary>
    /// Stops and restarts the broker, to exercise reconnection against a real outage.
    /// </summary>
    /// <remarks>
    /// Only callable from <see cref="ControllableBrokerFactAttribute"/> tests, which is what
    /// guarantees <c>_container</c> is ours to restart.
    /// </remarks>
    public async Task RestartAsync()
    {
        if (_container is null)
        {
            throw new InvalidOperationException(
                "The broker was supplied externally and cannot be restarted. Tests that need to " +
                $"restart it must use [{nameof(ControllableBrokerFactAttribute)}].");
        }

        await _container.StopAsync();
        await _container.StartAsync();

        // The endpoint must survive the outage: the point of these tests is that the client
        // reconnects to the same broker, not that it chases a moving target.
        Assert.Equal(_hostPort, _container.GetMappedPublicPort(1883));
    }

    /// <summary>Options pointed at this broker, with test-friendly timings.</summary>
    public MqttClientRxOptions Options() => new()
    {
        Uri = Uri,
        ConnectionType = ConnectionType.Tcp,
        KeepAlivePeriod = TimeSpan.FromSeconds(5),
        OperationTimeout = TimeSpan.FromSeconds(10),
        ShareGracePeriod = TimeSpan.Zero,

        // Bounded on purpose. The production default retries forever, which in a test turns any
        // broken endpoint into a silent hang until the assertion timeout. Failing after a couple of
        // seconds points at the real problem instead. Tests that exercise reconnection override this.
        Reconnect = ReconnectPolicy.Default with
        {
            MaxAttempts = 3,
            InitialDelay = TimeSpan.FromMilliseconds(200),
            MaxDelay = TimeSpan.FromSeconds(1)
        }
    };

    /// <summary>
    /// Asks the OS for an unused port. Racy in principle, harmless in practice for a test fixture,
    /// and the only way to get a stable port that also survives a container restart.
    /// </summary>
    private static int FindFreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);

        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();

        return port;
    }
}

[CollectionDefinition(Name)]
public sealed class MosquittoCollection : ICollectionFixture<MosquittoFixture>
{
    public const string Name = "mosquitto";
}
