namespace MQTTClientRx;

/// <summary>
/// Context handed to <see cref="ReconnectPolicy.ShouldRetry"/> so a policy can decide per-failure
/// whether reconnecting is worth attempting.
/// </summary>
/// <param name="Attempt">1-based number of the attempt that just failed.</param>
/// <param name="Exception">The failure that ended the connection.</param>
public sealed record MqttReconnectContext(int Attempt, Exception Exception)
{
    /// <summary>CONNACK reason code, when the failure was a rejected connection attempt.</summary>
    public MqttConnectResultCode? ConnectResultCode => (Exception as MqttConnectFailedException)?.ResultCode;

    /// <summary>DISCONNECT reason code, when an established connection was ended.</summary>
    public MqttDisconnectReasonCode? DisconnectReasonCode => (Exception as MqttDisconnectedException)?.Disconnect.ReasonCode;
}

/// <summary>
/// Reconnection strategy. MQTTnet 5 removed its <c>ManagedClient</c>, so reconnect, resubscribe and
/// backoff are this library's responsibility.
/// </summary>
/// <remarks>
/// Delays follow <c>InitialDelay * BackoffFactor^(attempt-1)</c>, clamped to <see cref="MaxDelay"/>
/// and spread by <see cref="JitterFactor"/> so a fleet of clients does not reconnect in lockstep
/// after a broker restart.
/// </remarks>
public sealed record ReconnectPolicy
{
    /// <summary>Reconnect forever: 1s initial delay doubling to a 30s ceiling, with 20% jitter.</summary>
    public static ReconnectPolicy Default { get; } = new();

    /// <summary>Never reconnect. The stream terminates with <see cref="IObserver{T}.OnError"/> on the first failure.</summary>
    public static ReconnectPolicy None { get; } = new() { Enabled = false };

    /// <summary>Whether reconnection is attempted at all.</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>Maximum number of consecutive attempts. <see langword="null"/> retries indefinitely.</summary>
    public int? MaxAttempts { get; init; }

    /// <summary>Delay before the first retry.</summary>
    public TimeSpan InitialDelay { get; init; } = TimeSpan.FromSeconds(1);

    /// <summary>Ceiling on the retry delay.</summary>
    public TimeSpan MaxDelay { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>Multiplier applied per attempt. 1.0 gives a constant delay.</summary>
    public double BackoffFactor { get; init; } = 2.0;

    /// <summary>
    /// Fraction of the computed delay to randomise by, in the range 0 to 1. 0.2 spreads the delay
    /// across ±20%.
    /// </summary>
    public double JitterFactor { get; init; } = 0.2;

    /// <summary>
    /// Decides whether a given failure is worth retrying. Defaults to
    /// <see cref="IsRetryableByDefault"/>, which gives up on failures that will never resolve by
    /// themselves - bad credentials, a rejected client identifier, an unsupported protocol version.
    /// </summary>
    public Func<MqttReconnectContext, bool>? ShouldRetry { get; init; }

    /// <summary>
    /// Computes the delay before the given attempt.
    /// </summary>
    /// <param name="attempt">1-based attempt number.</param>
    /// <param name="jitterSample">
    /// A value in [0,1) used to spread the delay. Supplied by the caller rather than drawn
    /// internally so the curve is deterministic under test.
    /// </param>
    public TimeSpan GetDelay(int attempt, double jitterSample = 0.5)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(attempt, 1);

        var exponent = Math.Max(0, attempt - 1);
        var baseMs = InitialDelay.TotalMilliseconds * Math.Pow(Math.Max(1.0, BackoffFactor), exponent);

        // Math.Pow overflows to Infinity for large attempt counts; clamp before jittering.
        if (double.IsNaN(baseMs) || baseMs > MaxDelay.TotalMilliseconds)
        {
            baseMs = MaxDelay.TotalMilliseconds;
        }

        if (JitterFactor > 0)
        {
            var spread = Math.Clamp(JitterFactor, 0, 1);
            baseMs *= 1.0 + (spread * ((2.0 * Math.Clamp(jitterSample, 0, 1)) - 1.0));
        }

        return TimeSpan.FromMilliseconds(Math.Clamp(baseMs, 0, MaxDelay.TotalMilliseconds));
    }

    /// <summary>
    /// Whether <paramref name="context"/> describes a failure that could plausibly succeed on retry.
    /// Used when <see cref="ShouldRetry"/> is not set.
    /// </summary>
    public static bool IsRetryableByDefault(MqttReconnectContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        // A broker that rejected our identity or protocol will keep rejecting it. Hammering it
        // achieves nothing and looks like an attack.
        if (context.ConnectResultCode is
            MqttConnectResultCode.BadUserNameOrPassword or
            MqttConnectResultCode.NotAuthorized or
            MqttConnectResultCode.ClientIdentifierNotValid or
            MqttConnectResultCode.Banned or
            MqttConnectResultCode.UnsupportedProtocolVersion or
            MqttConnectResultCode.BadAuthenticationMethod)
        {
            return false;
        }

        return context.DisconnectReasonCode is not (
            MqttDisconnectReasonCode.NotAuthorized or
            MqttDisconnectReasonCode.BadAuthenticationMethod or
            MqttDisconnectReasonCode.SessionTakenOver or
            MqttDisconnectReasonCode.ServerMoved);
    }

    internal bool AllowsRetry(MqttReconnectContext context) =>
        Enabled
        && (MaxAttempts is not { } max || context.Attempt <= max)
        && (ShouldRetry ?? IsRetryableByDefault)(context);
}
