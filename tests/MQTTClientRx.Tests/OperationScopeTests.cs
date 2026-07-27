using Microsoft.Extensions.Time.Testing;
using MQTTClientRx.Internal;

namespace MQTTClientRx.Tests;

/// <summary>
/// Operation timeouts are driven by an injected <see cref="TimeProvider"/> rather than the system
/// timer, so they are assertable in virtual time - the same guarantee <c>IScheduler</c> gives the
/// observable pipeline, for the layer <c>IScheduler</c> cannot reach.
/// </summary>
public class OperationScopeTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    [Fact]
    public void Cancels_when_the_timeout_elapses()
    {
        var time = new FakeTimeProvider();
        using var lifetime = new CancellationTokenSource();
        using var scope = new OperationScope(CancellationToken.None, lifetime.Token, Timeout, time);

        time.Advance(Timeout - TimeSpan.FromMilliseconds(1));
        Assert.False(scope.Token.IsCancellationRequested);

        time.Advance(TimeSpan.FromMilliseconds(1));
        Assert.True(scope.Token.IsCancellationRequested);
    }

    [Fact]
    public void Cancels_when_the_caller_cancels()
    {
        var time = new FakeTimeProvider();
        using var caller = new CancellationTokenSource();
        using var lifetime = new CancellationTokenSource();
        using var scope = new OperationScope(caller.Token, lifetime.Token, Timeout, time);

        caller.Cancel();

        Assert.True(scope.Token.IsCancellationRequested);
    }

    [Fact]
    public void Cancels_when_the_client_lifetime_ends()
    {
        var time = new FakeTimeProvider();
        using var lifetime = new CancellationTokenSource();
        using var scope = new OperationScope(CancellationToken.None, lifetime.Token, Timeout, time);

        lifetime.Cancel();

        Assert.True(scope.Token.IsCancellationRequested);
    }

    [Fact]
    public void Stays_open_while_nothing_has_happened()
    {
        var time = new FakeTimeProvider();
        using var lifetime = new CancellationTokenSource();
        using var scope = new OperationScope(CancellationToken.None, lifetime.Token, Timeout, time);

        Assert.False(scope.Token.IsCancellationRequested);
    }

    [Fact]
    public void The_client_accepts_an_injected_time_provider()
    {
        var options = new MqttClientRxOptions
        {
            Uri = new Uri("mqtt://broker:1883"),
            TimeProvider = new FakeTimeProvider()
        };

        options.Validate();

        Assert.NotNull(options.TimeProvider);
    }

    [Fact]
    public void Both_clocks_default_to_the_real_ones()
    {
        var options = new MqttClientRxOptions { Uri = new Uri("mqtt://broker:1883") };

        Assert.Null(options.TimeProvider);
        Assert.Null(options.Scheduler);
    }
}
