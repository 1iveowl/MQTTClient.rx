namespace MQTTClientRx.Tests;

public class ReconnectPolicyTests
{
    private static readonly ReconnectPolicy NoJitter = new()
    {
        InitialDelay = TimeSpan.FromSeconds(1),
        MaxDelay = TimeSpan.FromSeconds(30),
        BackoffFactor = 2.0,
        JitterFactor = 0
    };

    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(3, 4)]
    [InlineData(4, 8)]
    [InlineData(5, 16)]
    [InlineData(6, 30)]  // clamped
    [InlineData(50, 30)] // still clamped, no overflow
    public void GetDelay_doubles_until_it_reaches_the_ceiling(int attempt, double expectedSeconds)
    {
        var delay = NoJitter.GetDelay(attempt);

        Assert.Equal(expectedSeconds, delay.TotalSeconds, precision: 3);
    }

    [Fact]
    public void GetDelay_stays_within_the_jitter_band()
    {
        var policy = NoJitter with { JitterFactor = 0.2 };

        // attempt 3 has a 4s base delay, so ±20% is [3.2s, 4.8s].
        Assert.Equal(3.2, policy.GetDelay(3, jitterSample: 0.0).TotalSeconds, precision: 3);
        Assert.Equal(4.0, policy.GetDelay(3, jitterSample: 0.5).TotalSeconds, precision: 3);
        Assert.Equal(4.8, policy.GetDelay(3, jitterSample: 1.0).TotalSeconds, precision: 3);
    }

    [Fact]
    public void GetDelay_never_exceeds_MaxDelay_even_with_jitter()
    {
        var policy = NoJitter with { JitterFactor = 0.5 };

        Assert.True(policy.GetDelay(20, jitterSample: 1.0) <= policy.MaxDelay);
    }

    [Fact]
    public void GetDelay_with_a_backoff_factor_of_one_is_constant()
    {
        var policy = NoJitter with { BackoffFactor = 1.0 };

        Assert.Equal(TimeSpan.FromSeconds(1), policy.GetDelay(1));
        Assert.Equal(TimeSpan.FromSeconds(1), policy.GetDelay(10));
    }

    [Fact]
    public void GetDelay_rejects_attempt_numbers_below_one()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => NoJitter.GetDelay(0));
    }

    [Fact]
    public void None_does_not_retry()
    {
        var context = new MqttReconnectContext(1, new MqttClientRxException("transient"));

        Assert.False(ReconnectPolicy.None.AllowsRetry(context));
    }

    [Fact]
    public void Default_retries_a_transient_failure()
    {
        var context = new MqttReconnectContext(1, new MqttClientRxException("socket closed"));

        Assert.True(ReconnectPolicy.Default.AllowsRetry(context));
    }

    [Fact]
    public void Default_stops_once_MaxAttempts_is_exceeded()
    {
        var policy = ReconnectPolicy.Default with { MaxAttempts = 3 };
        var failure = new MqttClientRxException("socket closed");

        Assert.True(policy.AllowsRetry(new MqttReconnectContext(3, failure)));
        Assert.False(policy.AllowsRetry(new MqttReconnectContext(4, failure)));
    }

    [Theory]
    [InlineData(MqttConnectResultCode.BadUserNameOrPassword)]
    [InlineData(MqttConnectResultCode.NotAuthorized)]
    [InlineData(MqttConnectResultCode.ClientIdentifierNotValid)]
    [InlineData(MqttConnectResultCode.Banned)]
    [InlineData(MqttConnectResultCode.UnsupportedProtocolVersion)]
    [InlineData(MqttConnectResultCode.BadAuthenticationMethod)]
    public void Default_gives_up_on_failures_that_will_never_resolve(MqttConnectResultCode code)
    {
        var context = ConnectFailure(code);

        Assert.False(ReconnectPolicy.Default.AllowsRetry(context));
    }

    [Theory]
    [InlineData(MqttConnectResultCode.ServerUnavailable)]
    [InlineData(MqttConnectResultCode.ServerBusy)]
    [InlineData(MqttConnectResultCode.ConnectionRateExceeded)]
    [InlineData(MqttConnectResultCode.QuotaExceeded)]
    public void Default_retries_failures_the_broker_may_recover_from(MqttConnectResultCode code)
    {
        var context = ConnectFailure(code);

        Assert.True(ReconnectPolicy.Default.AllowsRetry(context));
    }

    [Theory]
    [InlineData(MqttDisconnectReasonCode.NotAuthorized, false)]
    [InlineData(MqttDisconnectReasonCode.SessionTakenOver, false)]
    [InlineData(MqttDisconnectReasonCode.ServerMoved, false)]
    [InlineData(MqttDisconnectReasonCode.KeepAliveTimeout, true)]
    [InlineData(MqttDisconnectReasonCode.ServerShuttingDown, true)]
    [InlineData(MqttDisconnectReasonCode.NormalDisconnection, true)]
    public void Default_reads_the_disconnect_reason(MqttDisconnectReasonCode code, bool expectRetry)
    {
        var context = new MqttReconnectContext(
            1,
            new MqttDisconnectedException(new MqttDisconnect { ClientWasConnected = true, ReasonCode = code }));

        Assert.Equal(expectRetry, ReconnectPolicy.Default.AllowsRetry(context));
    }

    [Fact]
    public void A_custom_ShouldRetry_overrides_the_default()
    {
        var policy = ReconnectPolicy.Default with { ShouldRetry = _ => true };

        // NotAuthorized would normally stop the loop.
        Assert.True(policy.AllowsRetry(ConnectFailure(MqttConnectResultCode.NotAuthorized)));
    }

    private static MqttReconnectContext ConnectFailure(MqttConnectResultCode code) =>
        new(1, new MqttConnectFailedException(
            new MqttConnectResult { ResultCode = code },
            new Uri("mqtt://broker:1883")));
}
