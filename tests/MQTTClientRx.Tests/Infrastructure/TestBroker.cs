namespace MQTTClientRx.Tests.Infrastructure;

/// <summary>
/// Works out which broker the integration tests can talk to, and how much control they have over it.
/// </summary>
/// <remarks>
/// <para>Three modes, resolved once per test run:</para>
/// <list type="bullet">
/// <item>
/// <b>Managed</b> - a Docker daemon is reachable, so Testcontainers starts a disposable Mosquitto.
/// Everything runs, including the tests that stop and start the broker. This is what CI uses.
/// </item>
/// <item>
/// <b>External</b> - <c>MQTTCLIENTRX_TEST_BROKER</c> names a broker somebody else is running. Almost
/// everything runs; the two tests that need to restart the broker skip, because we do not own it.
/// </item>
/// <item>
/// <b>None</b> - neither is available, so the broker-backed tests skip rather than fail.
/// </item>
/// </list>
/// </remarks>
internal static class TestBroker
{
    private const string BrokerVariable = "MQTTCLIENTRX_TEST_BROKER";

    static TestBroker()
    {
        ExternalUri = ResolveExternalUri();
        DockerAvailable = ExternalUri is null && DetectDocker();
    }

    /// <summary>A broker supplied by the environment, if any.</summary>
    internal static Uri? ExternalUri { get; }

    /// <summary>Whether Testcontainers can start a broker of our own.</summary>
    internal static bool DockerAvailable { get; }

    /// <summary>Whether any broker is reachable at all.</summary>
    internal static bool IsAvailable => ExternalUri is not null || DockerAvailable;

    /// <summary>
    /// Whether the broker can be stopped and started. Only true when we started it ourselves -
    /// restarting somebody else's broker would be rude at best.
    /// </summary>
    internal static bool SupportsRestart => DockerAvailable;

    internal static string? SkipReason => IsAvailable
        ? null
        : $"No test broker. Start Docker, or point {BrokerVariable} at one — see tests/README.md.";

    internal static string? RestartSkipReason => SupportsRestart
        ? null
        : $"This test restarts the broker, which needs one it started itself. " +
          $"Unset {BrokerVariable} and enable Docker — see tests/README.md.";

    private static Uri? ResolveExternalUri()
    {
        var value = Environment.GetEnvironmentVariable(BrokerVariable);

        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri))
        {
            throw new InvalidOperationException(
                $"{BrokerVariable} is set to '{value}', which is not an absolute URI. " +
                "Expected something like mqtt://localhost:1883.");
        }

        return uri;
    }

    private static bool DetectDocker()
    {
        try
        {
            if (Environment.GetEnvironmentVariable("DOCKER_HOST") is { Length: > 0 })
            {
                return true;
            }

            return OperatingSystem.IsWindows()
                ? File.Exists(@"\\.\pipe\docker_engine")
                : File.Exists("/var/run/docker.sock");
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }
}

/// <summary>A <see cref="FactAttribute"/> that skips itself when no test broker is reachable.</summary>
public class BrokerFactAttribute : FactAttribute
{
    public BrokerFactAttribute()
    {
        if (TestBroker.SkipReason is { } reason)
        {
            Skip = reason;
        }
    }
}

/// <summary>A <see cref="TheoryAttribute"/> that skips itself when no test broker is reachable.</summary>
public class BrokerTheoryAttribute : TheoryAttribute
{
    public BrokerTheoryAttribute()
    {
        if (TestBroker.SkipReason is { } reason)
        {
            Skip = reason;
        }
    }
}

/// <summary>
/// For tests that stop and start the broker mid-run. Needs a broker this test run owns, so it skips
/// against an externally supplied one.
/// </summary>
public sealed class ControllableBrokerFactAttribute : BrokerFactAttribute
{
    public ControllableBrokerFactAttribute()
    {
        Skip ??= TestBroker.RestartSkipReason;
    }
}
